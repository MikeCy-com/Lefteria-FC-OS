using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Include only currently valid direct phone-granted Parent children in
/// mobile group chat directory. The original directory remains available
/// on rollback; no new message visibility is inferred from a stale token.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110176000_DelegatedGuardianChatDirectory")]
public sealed class DelegatedGuardianChatDirectory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""

        CREATE OR REPLACE FUNCTION club.read_mobile_chat_groups(
          _identity uuid,_phone text,_role text,_expires timestamptz
        ) RETURNS jsonb LANGUAGE plpgsql STABLE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp
        AS $chat_groups$
        DECLARE
          v_available boolean;
          v_ready boolean;
          v_count integer;
          v_groups jsonb;
        BEGIN
          -- Only a JWT-confirmed identity is passed by ASP.NET. Repeat its
          -- full phone/role/status check and current canonical roster checks
          -- in the same SQL statement as group selection. No fan access.
          IF _identity IS NULL OR _phone IS NULL OR length(_phone)>32
             OR _role NOT IN ('parent','player','coach','management')
             OR _expires IS NULL THEN
            RAISE EXCEPTION 'chat_scope_denied' USING ERRCODE='42501';
          END IF;
          WITH actor AS (
            SELECT i.id,i.normalized_phone,i.role,i.legacy_id,
              i.management_admin_id
            FROM identity.mobile_identities i
            WHERE i.id=_identity AND i.normalized_phone=_phone
              AND i.role=_role AND i.is_active
              AND _expires>statement_timestamp()
          ), linked_players AS (
            SELECT DISTINCT p.id,p.organization_id,p.team_id,p.team_type,
              p.primary_academy_group_id
            FROM actor i JOIN identity.mobile_record_links l
              ON l.identity_id=i.id AND l.role=i.role
              AND l.normalized_phone=i.normalized_phone
            JOIN identity.canonical_contact_bindings b
              ON b.collection=l.collection AND b.kind=l.kind
              AND b.source_id=l.record_id AND b.normalized_phone=i.normalized_phone
            JOIN club.players p ON p.id=l.record_id
            WHERE i.role IN ('parent','player') AND l.collection='players'
              AND ((i.role='parent' AND l.kind='guardian')
                OR (i.role='player' AND l.kind='player_own'))
              AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
            UNION
            SELECT DISTINCT p.id,p.organization_id,p.team_id,p.team_type,
              p.primary_academy_group_id
            FROM actor i
            JOIN LATERAL identity.current_phone_granted_children(
              i.id,i.normalized_phone,i.role,_expires) guardian ON true
            JOIN club.players p ON p.id=guardian.player_id
              AND p.organization_id=guardian.organization_id
            WHERE i.role='parent'
              AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
          ), linked_coach AS (
            SELECT DISTINCT s.id
            FROM actor i JOIN identity.mobile_record_links l
              ON l.identity_id=i.id AND l.role=i.role
              AND l.normalized_phone=i.normalized_phone
            JOIN identity.canonical_contact_bindings b
              ON b.collection=l.collection AND b.kind=l.kind
              AND b.source_id=l.record_id AND b.normalized_phone=i.normalized_phone
            JOIN club.staff s ON s.id=l.record_id
            WHERE i.role='coach' AND l.collection='staff'
              AND l.kind='staff_own' AND s.is_active AND NOT s.is_archived
          ), selected_management AS (
            SELECT a.active_organization_id AS organization_id
            FROM actor i JOIN identity.admin_accounts a
              ON a.id=i.management_admin_id
            JOIN identity.admin_organization_grants g
              ON g.admin_account_id=a.id
              AND g.organization_id=a.active_organization_id
            LEFT JOIN identity.admin_staff_links link
              ON link.admin_account_id=a.id
            LEFT JOIN club.staff staff ON staff.id=link.staff_id
            WHERE i.role='management' AND a.is_active
              AND NOT a.invite_pending
              AND ((a.normalized_phone IS NULL AND i.legacy_id IS NULL)
                OR a.normalized_phone=i.normalized_phone)
              AND (link.admin_account_id IS NULL
                OR (staff.is_active AND NOT staff.is_archived))
          ), valid_actor AS (
            SELECT true AS valid FROM actor i
            WHERE (i.role='parent' AND EXISTS(SELECT 1 FROM linked_players))
              OR (i.role='player' AND (SELECT count(*) FROM linked_players)=1)
              OR (i.role='coach' AND EXISTS(SELECT 1 FROM linked_coach))
              OR (i.role='management' AND EXISTS(SELECT 1 FROM selected_management))
          ), ready AS (
            SELECT 1 FROM club.roster_import_state WHERE id='initial'
          ), eligible AS (
            SELECT t.id,t.name,t.organization_id,'team'::text AS kind
            FROM club.teams t
            WHERE t.organization_id='lefteria_fc' AND (
              EXISTS(SELECT 1 FROM linked_players p
                WHERE p.organization_id=t.organization_id
                  AND p.team_type='First Team' AND p.team_id=t.id)
              OR EXISTS(SELECT 1 FROM linked_coach c
                JOIN club.staff_teams st ON st.staff_id=c.id
                WHERE st.team_id=t.id)
              OR EXISTS(SELECT 1 FROM selected_management m
                WHERE m.organization_id=t.organization_id)
            )
            UNION ALL
            SELECT g.id,g.name,g.organization_id,'academy_group'::text AS kind
            FROM club.academy_groups g
            WHERE g.organization_id='lefteria_fc_academy' AND (
              EXISTS(SELECT 1 FROM linked_players p
                WHERE p.organization_id=g.organization_id
                  AND p.team_type='Academy'
                  AND (p.primary_academy_group_id=g.id OR EXISTS(
                    SELECT 1 FROM club.player_academy_groups membership
                    WHERE membership.player_id=p.id
                      AND membership.organization_id=p.organization_id
                      AND membership.academy_group_id=g.id)))
              OR EXISTS(SELECT 1 FROM linked_coach c
                JOIN club.staff_academy_groups sg ON sg.staff_id=c.id
                WHERE sg.academy_group_id=g.id
                  AND (sg.from_plural OR sg.from_singular OR
                    (sg.from_group_coach AND EXISTS(
                      SELECT 1 FROM club.academy_group_coaches actual
                      WHERE actual.academy_group_id=g.id AND actual.staff_id=c.id))))
              OR EXISTS(SELECT 1 FROM selected_management m
                WHERE m.organization_id=g.organization_id)
            )
          ), limited AS (
            SELECT * FROM eligible ORDER BY kind COLLATE "C",
              name COLLATE "C",id COLLATE "C" LIMIT 151
          )
          SELECT EXISTS(SELECT 1 FROM valid_actor),
            EXISTS(SELECT 1 FROM ready),
            (SELECT count(*)::integer FROM limited),
            coalesce((SELECT jsonb_agg(jsonb_build_object(
              'id',item.id,'name',item.name,'kind',item.kind,
              'organization_id',item.organization_id)
              ORDER BY item.kind COLLATE "C",item.name COLLATE "C",item.id COLLATE "C")
              FROM limited item),'[]'::jsonb)
          INTO v_available,v_ready,v_count,v_groups;
          IF NOT v_available THEN
            RAISE EXCEPTION 'chat_scope_denied' USING ERRCODE='42501';
          END IF;
          IF NOT v_ready THEN
            RAISE EXCEPTION 'chat_roster_unavailable' USING ERRCODE='55000';
          END IF;
          IF v_count>150 THEN
            RAISE EXCEPTION 'chat_group_limit' USING ERRCODE='54000';
          END IF;
          RETURN jsonb_build_object('identity_available',true,
            'roster_ready',true,'groups',v_groups);
        END;
        $chat_groups$;
        REVOKE ALL ON FUNCTION club.read_mobile_chat_groups(
          uuid,text,text,timestamptz) FROM PUBLIC;
        
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""

        CREATE OR REPLACE FUNCTION club.read_mobile_chat_groups(
          _identity uuid,_phone text,_role text,_expires timestamptz
        ) RETURNS jsonb LANGUAGE plpgsql STABLE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp
        AS $chat_groups$
        DECLARE
          v_available boolean;
          v_ready boolean;
          v_count integer;
          v_groups jsonb;
        BEGIN
          -- Only a JWT-confirmed identity is passed by ASP.NET. Repeat its
          -- full phone/role/status check and current canonical roster checks
          -- in the same SQL statement as group selection. No fan access.
          IF _identity IS NULL OR _phone IS NULL OR length(_phone)>32
             OR _role NOT IN ('parent','player','coach','management')
             OR _expires IS NULL THEN
            RAISE EXCEPTION 'chat_scope_denied' USING ERRCODE='42501';
          END IF;
          WITH actor AS (
            SELECT i.id,i.normalized_phone,i.role,i.legacy_id,
              i.management_admin_id
            FROM identity.mobile_identities i
            WHERE i.id=_identity AND i.normalized_phone=_phone
              AND i.role=_role AND i.is_active
              AND _expires>statement_timestamp()
          ), linked_players AS (
            SELECT DISTINCT p.id,p.organization_id,p.team_id,p.team_type,
              p.primary_academy_group_id
            FROM actor i JOIN identity.mobile_record_links l
              ON l.identity_id=i.id AND l.role=i.role
              AND l.normalized_phone=i.normalized_phone
            JOIN identity.canonical_contact_bindings b
              ON b.collection=l.collection AND b.kind=l.kind
              AND b.source_id=l.record_id AND b.normalized_phone=i.normalized_phone
            JOIN club.players p ON p.id=l.record_id
            WHERE i.role IN ('parent','player') AND l.collection='players'
              AND ((i.role='parent' AND l.kind='guardian')
                OR (i.role='player' AND l.kind='player_own'))
              AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
          ), linked_coach AS (
            SELECT DISTINCT s.id
            FROM actor i JOIN identity.mobile_record_links l
              ON l.identity_id=i.id AND l.role=i.role
              AND l.normalized_phone=i.normalized_phone
            JOIN identity.canonical_contact_bindings b
              ON b.collection=l.collection AND b.kind=l.kind
              AND b.source_id=l.record_id AND b.normalized_phone=i.normalized_phone
            JOIN club.staff s ON s.id=l.record_id
            WHERE i.role='coach' AND l.collection='staff'
              AND l.kind='staff_own' AND s.is_active AND NOT s.is_archived
          ), selected_management AS (
            SELECT a.active_organization_id AS organization_id
            FROM actor i JOIN identity.admin_accounts a
              ON a.id=i.management_admin_id
            JOIN identity.admin_organization_grants g
              ON g.admin_account_id=a.id
              AND g.organization_id=a.active_organization_id
            LEFT JOIN identity.admin_staff_links link
              ON link.admin_account_id=a.id
            LEFT JOIN club.staff staff ON staff.id=link.staff_id
            WHERE i.role='management' AND a.is_active
              AND NOT a.invite_pending
              AND ((a.normalized_phone IS NULL AND i.legacy_id IS NULL)
                OR a.normalized_phone=i.normalized_phone)
              AND (link.admin_account_id IS NULL
                OR (staff.is_active AND NOT staff.is_archived))
          ), valid_actor AS (
            SELECT true AS valid FROM actor i
            WHERE (i.role='parent' AND EXISTS(SELECT 1 FROM linked_players))
              OR (i.role='player' AND (SELECT count(*) FROM linked_players)=1)
              OR (i.role='coach' AND EXISTS(SELECT 1 FROM linked_coach))
              OR (i.role='management' AND EXISTS(SELECT 1 FROM selected_management))
          ), ready AS (
            SELECT 1 FROM club.roster_import_state WHERE id='initial'
          ), eligible AS (
            SELECT t.id,t.name,t.organization_id,'team'::text AS kind
            FROM club.teams t
            WHERE t.organization_id='lefteria_fc' AND (
              EXISTS(SELECT 1 FROM linked_players p
                WHERE p.organization_id=t.organization_id
                  AND p.team_type='First Team' AND p.team_id=t.id)
              OR EXISTS(SELECT 1 FROM linked_coach c
                JOIN club.staff_teams st ON st.staff_id=c.id
                WHERE st.team_id=t.id)
              OR EXISTS(SELECT 1 FROM selected_management m
                WHERE m.organization_id=t.organization_id)
            )
            UNION ALL
            SELECT g.id,g.name,g.organization_id,'academy_group'::text AS kind
            FROM club.academy_groups g
            WHERE g.organization_id='lefteria_fc_academy' AND (
              EXISTS(SELECT 1 FROM linked_players p
                WHERE p.organization_id=g.organization_id
                  AND p.team_type='Academy'
                  AND (p.primary_academy_group_id=g.id OR EXISTS(
                    SELECT 1 FROM club.player_academy_groups membership
                    WHERE membership.player_id=p.id
                      AND membership.organization_id=p.organization_id
                      AND membership.academy_group_id=g.id)))
              OR EXISTS(SELECT 1 FROM linked_coach c
                JOIN club.staff_academy_groups sg ON sg.staff_id=c.id
                WHERE sg.academy_group_id=g.id
                  AND (sg.from_plural OR sg.from_singular OR
                    (sg.from_group_coach AND EXISTS(
                      SELECT 1 FROM club.academy_group_coaches actual
                      WHERE actual.academy_group_id=g.id AND actual.staff_id=c.id))))
              OR EXISTS(SELECT 1 FROM selected_management m
                WHERE m.organization_id=g.organization_id)
            )
          ), limited AS (
            SELECT * FROM eligible ORDER BY kind COLLATE "C",
              name COLLATE "C",id COLLATE "C" LIMIT 151
          )
          SELECT EXISTS(SELECT 1 FROM valid_actor),
            EXISTS(SELECT 1 FROM ready),
            (SELECT count(*)::integer FROM limited),
            coalesce((SELECT jsonb_agg(jsonb_build_object(
              'id',item.id,'name',item.name,'kind',item.kind,
              'organization_id',item.organization_id)
              ORDER BY item.kind COLLATE "C",item.name COLLATE "C",item.id COLLATE "C")
              FROM limited item),'[]'::jsonb)
          INTO v_available,v_ready,v_count,v_groups;
          IF NOT v_available THEN
            RAISE EXCEPTION 'chat_scope_denied' USING ERRCODE='42501';
          END IF;
          IF NOT v_ready THEN
            RAISE EXCEPTION 'chat_roster_unavailable' USING ERRCODE='55000';
          END IF;
          IF v_count>150 THEN
            RAISE EXCEPTION 'chat_group_limit' USING ERRCODE='54000';
          END IF;
          RETURN jsonb_build_object('identity_available',true,
            'roster_ready',true,'groups',v_groups);
        END;
        $chat_groups$;
        REVOKE ALL ON FUNCTION club.read_mobile_chat_groups(
          uuid,text,text,timestamptz) FROM PUBLIC;
        
        """);
}