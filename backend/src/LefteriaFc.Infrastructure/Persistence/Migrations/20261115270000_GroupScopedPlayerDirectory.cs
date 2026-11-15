using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Exact, manager-permitted players of a single verified club team
/// or Academy group. Reuses the original locked roster read semantics but
/// counts and pages ONLY group members, never arbitrary organization players.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115270000_GroupScopedPlayerDirectory")]
public sealed class GroupScopedPlayerDirectory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
CREATE FUNCTION club.read_admin_group_players(
              _digest text,_owner text,_group text,_offset integer,_limit integer
            ) RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $admin_roster_players$
            DECLARE
              v_role text;
              v_total integer;
              v_rows jsonb;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                OR _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
                OR _group ~ '[[:cntrl:]]'
                OR _offset IS NULL OR _offset NOT BETWEEN 0 AND 20000
                OR _limit IS NULL OR _limit NOT BETWEEN 1 AND 50 THEN
                RAISE EXCEPTION 'roster_player_invalid_scope' USING ERRCODE='22023';
              END IF;

              -- Session, account and exact owner grant locked against
              -- revocation while deciding and reading youth roster data.
              PERFORM identity.lock_admin_club_authority(_digest,_owner);
              SELECT a.role INTO v_role
              FROM identity.admin_sessions s
              JOIN identity.admin_accounts a ON a.id=s.admin_account_id
              JOIN identity.admin_organization_grants g
                ON g.admin_account_id=a.id AND g.organization_id=_owner
              WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                AND s.expires_at>clock_timestamp()
                AND a.is_active AND NOT a.invite_pending
                AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL;
              IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
                RAISE EXCEPTION 'roster_player_permission_denied' USING ERRCODE='42501';
              END IF;
              IF NOT EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial') THEN
                RAISE EXCEPTION 'roster_player_not_ready' USING ERRCODE='55000';
              END IF;

              -- A group is a real, owner-bound roster record. A forged ID
              -- must never turn this endpoint into an organization directory.
              IF (_owner='lefteria_fc'
                  AND NOT EXISTS (SELECT 1 FROM club.teams g
                    WHERE g.id=_group AND g.organization_id=_owner))
                OR (_owner='lefteria_fc_academy'
                  AND NOT EXISTS (SELECT 1 FROM club.academy_groups g
                    WHERE g.id=_group AND g.organization_id=_owner))
              THEN RAISE EXCEPTION 'group_roster_access_denied' USING ERRCODE='42501'; END IF;

              -- Exact group membership is evaluated inside the locked
              -- security-definer query, BEFORE LIMIT/OFFSET. This prevents
              -- misleading counts and incomplete group player pages.
              -- Only active roster members; never a global directory or
              -- archived/transferred children. Bound the response and count.
              SELECT count(*) INTO v_total
              FROM club.players p
              WHERE p.organization_id=_owner AND p.is_active
                AND NOT p.is_archived AND NOT p.is_transferred
                AND p.team_type=CASE WHEN _owner='lefteria_fc'
                  THEN 'First Team' ELSE 'Academy' END
                AND ((_owner='lefteria_fc' AND p.team_id=_group)
                  OR (_owner='lefteria_fc_academy' AND
                    (p.primary_academy_group_id=_group OR EXISTS(
                      SELECT 1 FROM club.player_academy_groups membership
                      WHERE membership.organization_id=_owner
                        AND membership.player_id=p.id
                        AND membership.academy_group_id=_group))));
              IF v_total>25000 THEN
                RAISE EXCEPTION 'roster_player_too_large' USING ERRCODE='54000';
              END IF;

              SELECT COALESCE(jsonb_agg(
                jsonb_build_object(
                  'id',p.id,'name',p.name,
                  'team_id',CASE WHEN _owner='lefteria_fc' THEN p.team_id ELSE NULL END,
                  'academy_group_ids',CASE WHEN _owner='lefteria_fc_academy' THEN
                    COALESCE((
                      SELECT jsonb_agg(x.group_id ORDER BY x.group_id COLLATE "C")
                      FROM (
                        SELECT p.primary_academy_group_id AS group_id
                        WHERE p.primary_academy_group_id IS NOT NULL
                        UNION
                        SELECT m.academy_group_id AS group_id
                        FROM club.player_academy_groups m
                        WHERE m.organization_id=p.organization_id AND m.player_id=p.id
                      ) x
                    ),'[]'::jsonb)
                  ELSE '[]'::jsonb END)
                ORDER BY p.name COLLATE "C",p.id COLLATE "C"),'[]'::jsonb)
              INTO v_rows
              FROM (
                SELECT p.id,p.name,p.organization_id,p.team_id,p.primary_academy_group_id
                FROM club.players p
                WHERE p.organization_id=_owner AND p.is_active
                  AND NOT p.is_archived AND NOT p.is_transferred
                  AND p.team_type=CASE WHEN _owner='lefteria_fc'
                    THEN 'First Team' ELSE 'Academy' END
                  AND ((_owner='lefteria_fc' AND p.team_id=_group)
                    OR (_owner='lefteria_fc_academy' AND
                      (p.primary_academy_group_id=_group OR EXISTS(
                        SELECT 1 FROM club.player_academy_groups membership
                        WHERE membership.organization_id=_owner
                          AND membership.player_id=p.id
                          AND membership.academy_group_id=_group))))
                ORDER BY p.name COLLATE "C",p.id COLLATE "C"
                LIMIT _limit OFFSET _offset
              ) p;

              RETURN jsonb_build_object(
                'organization_id',_owner,'ready',true,
                'offset',_offset,'limit',_limit,'total',v_total,
                'players',v_rows);
            END $admin_roster_players$;

            REVOKE ALL ON FUNCTION club.read_admin_group_players(
              text,text,text,integer,integer) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION club.read_admin_group_players(text,text,text,integer,integer);
        """);
}
