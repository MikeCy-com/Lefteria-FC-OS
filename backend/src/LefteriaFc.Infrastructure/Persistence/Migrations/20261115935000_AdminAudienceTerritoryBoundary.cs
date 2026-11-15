using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Restrict the legacy live-audience directory to verified canonical territories.
/// No unassigned youth totals or neighboring Academy locations for managers.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115935000_AdminAudienceTerritoryBoundary")]
public sealed class AdminAudienceTerritoryBoundary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION club.read_admin_audiences(_digest text, _owner text)
                        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
                        SET search_path=pg_catalog,pg_temp
                        AS $admin_audience$
                        DECLARE
                          v_role text;
                          v_actor uuid;
                          v_groups integer;
                          v_unassigned integer;
                          v_items jsonb;
                        BEGIN
                          IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                             OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy') THEN
                            RAISE EXCEPTION 'audience_invalid_scope' USING ERRCODE='22023';
                          END IF;

                          -- A lock on the current session, account and exact owner grant
                          -- serializes with revocation/downgrades. A coach's assigned
                          -- group read is NOT a global read and is not enabled here.
                          PERFORM identity.lock_admin_club_authority(_digest,_owner);
                          SELECT a.id,a.role INTO v_actor,v_role
                          FROM identity.admin_sessions s
                          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
                          JOIN identity.admin_organization_grants g
                            ON g.admin_account_id=a.id AND g.organization_id=_owner
                          WHERE s.token_digest=_digest
                            AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp()
                            AND a.is_active AND NOT a.invite_pending
                            AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL;
                          -- Editors and matchday staff have no territorial grant model;
                          -- never fall back to organization-wide youth counts.
                          IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
                            RAISE EXCEPTION 'audience_scope_denied' USING ERRCODE='42501';
                          END IF;

                          -- An unimported roster is unavailable, never an empty success.
                          IF NOT EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial') THEN
                            RAISE EXCEPTION 'audience_roster_not_ready' USING ERRCODE='55000';
                          END IF;

                          IF _owner='lefteria_fc' THEN
                            SELECT count(*) INTO v_groups FROM club.teams
                            WHERE organization_id=_owner AND (
                              v_role='super_admin'
                              OR club.admin_app_notice_group_allowed(v_actor,_owner,id));
                            IF v_groups>150 THEN
                              RAISE EXCEPTION 'audience_directory_too_large' USING ERRCODE='54000';
                            END IF;
                            SELECT count(*) INTO v_unassigned FROM club.players p
                            WHERE p.organization_id=_owner AND p.team_type='First Team'
                              AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                              AND p.team_id IS NULL AND v_role='super_admin';
                            SELECT COALESCE(jsonb_agg(jsonb_build_object(
                                'id',t.id,'name',t.name,'kind','team',
                                'active_players',(SELECT count(*)::integer FROM club.players p
                                  WHERE p.organization_id=t.organization_id AND p.team_id=t.id
                                    AND p.team_type='First Team' AND p.is_active
                                    AND NOT p.is_archived AND NOT p.is_transferred))
                                ORDER BY t.name COLLATE "C",t.id COLLATE "C"),'[]'::jsonb)
                              INTO v_items FROM club.teams t
                              WHERE t.organization_id=_owner AND (
                                v_role='super_admin'
                                OR club.admin_app_notice_group_allowed(v_actor,_owner,t.id));
                          ELSE
                            SELECT count(*) INTO v_groups FROM club.academy_groups
                            WHERE organization_id=_owner AND (
                              v_role='super_admin'
                              OR club.admin_app_notice_group_allowed(v_actor,_owner,id));
                            IF v_groups>150 THEN
                              RAISE EXCEPTION 'audience_directory_too_large' USING ERRCODE='54000';
                            END IF;
                            SELECT count(*) INTO v_unassigned FROM club.players p
                            WHERE p.organization_id=_owner AND p.team_type='Academy'
                              AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                              AND p.primary_academy_group_id IS NULL
                              AND NOT EXISTS (
                                SELECT 1 FROM club.player_academy_groups m
                                WHERE m.organization_id=p.organization_id AND m.player_id=p.id
                              ) AND v_role='super_admin';

                            -- A player may have both primary and plural group references.
                            -- UNION (not UNION ALL) prevents duplicate audience counting.
                            WITH memberships AS (
                              SELECT p.organization_id,p.id AS player_id,
                                p.primary_academy_group_id AS group_id
                              FROM club.players p
                              WHERE p.organization_id=_owner AND p.team_type='Academy'
                                AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                                AND p.primary_academy_group_id IS NOT NULL
                              UNION
                              SELECT p.organization_id,p.id AS player_id,m.academy_group_id AS group_id
                              FROM club.player_academy_groups m
                              JOIN club.players p ON p.organization_id=m.organization_id AND p.id=m.player_id
                              WHERE p.organization_id=_owner AND p.team_type='Academy'
                                AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                            )
                            SELECT COALESCE(jsonb_agg(jsonb_build_object(
                                'id',g.id,'name',g.name,'kind','academy_group',
                                'active_players',(SELECT count(*)::integer
                                  FROM memberships m WHERE m.organization_id=g.organization_id
                                    AND m.group_id=g.id))
                                ORDER BY g.name COLLATE "C",g.id COLLATE "C"),'[]'::jsonb)
                              INTO v_items FROM club.academy_groups g
                              WHERE g.organization_id=_owner AND (
                                v_role='super_admin'
                                OR club.admin_app_notice_group_allowed(v_actor,_owner,g.id));
                          END IF;

                          RETURN jsonb_build_object('organization_id',_owner,'ready',true,
                            'unassigned_players',v_unassigned,'groups',v_items);
                        END $admin_audience$;
                        -- CREATE OR REPLACE retains the reviewed executable-role ACL.
                        REVOKE ALL ON FUNCTION club.read_admin_audiences(text,text) FROM PUBLIC;
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION club.read_admin_audiences(_digest text, _owner text)
                        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
                        SET search_path=pg_catalog,pg_temp
                        AS $admin_audience$
                        DECLARE
                          v_role text;
                          v_groups integer;
                          v_unassigned integer;
                          v_items jsonb;
                        BEGIN
                          IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                             OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy') THEN
                            RAISE EXCEPTION 'audience_invalid_scope' USING ERRCODE='22023';
                          END IF;

                          -- A lock on the current session, account and exact owner grant
                          -- serializes with revocation/downgrades. A coach's assigned
                          -- group read is NOT a global read and is not enabled here.
                          PERFORM identity.lock_admin_club_authority(_digest,_owner);
                          SELECT a.role INTO v_role
                          FROM identity.admin_sessions s
                          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
                          JOIN identity.admin_organization_grants g
                            ON g.admin_account_id=a.id AND g.organization_id=_owner
                          WHERE s.token_digest=_digest
                            AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp()
                            AND a.is_active AND NOT a.invite_pending
                            AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL;
                          IF NOT FOUND OR
                            (_owner='lefteria_fc' AND v_role NOT IN
                              ('manager','super_admin','editor','matchday_editor')) OR
                            (_owner='lefteria_fc_academy' AND v_role NOT IN
                              ('manager','super_admin','editor')) THEN
                            RAISE EXCEPTION 'audience_scope_denied' USING ERRCODE='42501';
                          END IF;

                          -- An unimported roster is unavailable, never an empty success.
                          IF NOT EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial') THEN
                            RAISE EXCEPTION 'audience_roster_not_ready' USING ERRCODE='55000';
                          END IF;

                          IF _owner='lefteria_fc' THEN
                            SELECT count(*) INTO v_groups FROM club.teams
                            WHERE organization_id=_owner;
                            IF v_groups>150 THEN
                              RAISE EXCEPTION 'audience_directory_too_large' USING ERRCODE='54000';
                            END IF;
                            SELECT count(*) INTO v_unassigned FROM club.players p
                            WHERE p.organization_id=_owner AND p.team_type='First Team'
                              AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                              AND p.team_id IS NULL;
                            SELECT COALESCE(jsonb_agg(jsonb_build_object(
                                'id',t.id,'name',t.name,'kind','team',
                                'active_players',(SELECT count(*)::integer FROM club.players p
                                  WHERE p.organization_id=t.organization_id AND p.team_id=t.id
                                    AND p.team_type='First Team' AND p.is_active
                                    AND NOT p.is_archived AND NOT p.is_transferred))
                                ORDER BY t.name COLLATE "C",t.id COLLATE "C"),'[]'::jsonb)
                              INTO v_items FROM club.teams t WHERE t.organization_id=_owner;
                          ELSE
                            SELECT count(*) INTO v_groups FROM club.academy_groups
                            WHERE organization_id=_owner;
                            IF v_groups>150 THEN
                              RAISE EXCEPTION 'audience_directory_too_large' USING ERRCODE='54000';
                            END IF;
                            SELECT count(*) INTO v_unassigned FROM club.players p
                            WHERE p.organization_id=_owner AND p.team_type='Academy'
                              AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                              AND p.primary_academy_group_id IS NULL
                              AND NOT EXISTS (
                                SELECT 1 FROM club.player_academy_groups m
                                WHERE m.organization_id=p.organization_id AND m.player_id=p.id
                              );

                            -- A player may have both primary and plural group references.
                            -- UNION (not UNION ALL) prevents duplicate audience counting.
                            WITH memberships AS (
                              SELECT p.organization_id,p.id AS player_id,
                                p.primary_academy_group_id AS group_id
                              FROM club.players p
                              WHERE p.organization_id=_owner AND p.team_type='Academy'
                                AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                                AND p.primary_academy_group_id IS NOT NULL
                              UNION
                              SELECT p.organization_id,p.id AS player_id,m.academy_group_id AS group_id
                              FROM club.player_academy_groups m
                              JOIN club.players p ON p.organization_id=m.organization_id AND p.id=m.player_id
                              WHERE p.organization_id=_owner AND p.team_type='Academy'
                                AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                            )
                            SELECT COALESCE(jsonb_agg(jsonb_build_object(
                                'id',g.id,'name',g.name,'kind','academy_group',
                                'active_players',(SELECT count(*)::integer
                                  FROM memberships m WHERE m.organization_id=g.organization_id
                                    AND m.group_id=g.id))
                                ORDER BY g.name COLLATE "C",g.id COLLATE "C"),'[]'::jsonb)
                              INTO v_items FROM club.academy_groups g WHERE g.organization_id=_owner;
                          END IF;

                          RETURN jsonb_build_object('organization_id',_owner,'ready',true,
                            'unassigned_players',v_unassigned,'groups',v_items);
                        END $admin_audience$;
            """);
}
