using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Search before paging, with the same exact organization and
/// membership boundaries as the existing roster readers.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261128195000_AdminRosterSearch")]
public sealed class AdminRosterSearch : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
            CREATE FUNCTION club.search_admin_roster_players(
              _digest text,_owner text,_offset integer,_limit integer,_search text,_filter_group text
            ) RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $admin_roster_players$
            DECLARE
              v_role text;
              v_actor uuid;
              v_total integer;
              v_rows jsonb;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                OR _offset IS NULL OR _offset NOT BETWEEN 0 AND 20000
                OR _limit IS NULL OR _limit NOT BETWEEN 1 AND 50
                OR _search IS NULL OR length(_search)>100 OR _search ~ '[[:cntrl:]]' OR _search<>btrim(_search)
                OR (_filter_group IS NOT NULL AND (length(_filter_group) NOT BETWEEN 1 AND 128 OR _filter_group ~ '[[:cntrl:]]')) THEN
                RAISE EXCEPTION 'roster_player_invalid_scope' USING ERRCODE='22023';
              END IF;

              -- Session, account and exact owner grant locked against
              -- revocation while deciding and reading youth roster data.
              PERFORM identity.lock_admin_club_authority(_digest,_owner);
              SELECT a.id,a.role INTO v_actor,v_role
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

              -- Only active roster members; never a global directory or
              -- archived/transferred children. Bound the response and count.
              SELECT count(*) INTO v_total
              FROM club.players p
              WHERE p.organization_id=_owner AND p.is_active
                AND NOT p.is_archived AND NOT p.is_transferred
                AND p.team_type=CASE WHEN _owner='lefteria_fc'
                  THEN 'First Team' ELSE 'Academy' END
                AND (_search='' OR strpos(lower(p.name),lower(_search))>0 OR strpos(lower(p.id),lower(_search))>0)
                AND (_filter_group IS NULL OR
                  (_owner='lefteria_fc' AND p.team_id=_filter_group) OR
                  (_owner='lefteria_fc_academy' AND (p.primary_academy_group_id=_filter_group OR EXISTS(
                    SELECT 1 FROM club.player_academy_groups m WHERE m.organization_id=_owner
                      AND m.player_id=p.id AND m.academy_group_id=_filter_group))))
                AND club.admin_roster_player_visible(v_actor,v_role,_owner,
                  p.id,p.team_id,p.primary_academy_group_id);
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
                  AND (_search='' OR strpos(lower(p.name),lower(_search))>0 OR strpos(lower(p.id),lower(_search))>0)
                AND (_filter_group IS NULL OR
                  (_owner='lefteria_fc' AND p.team_id=_filter_group) OR
                  (_owner='lefteria_fc_academy' AND (p.primary_academy_group_id=_filter_group OR EXISTS(
                    SELECT 1 FROM club.player_academy_groups m WHERE m.organization_id=_owner
                      AND m.player_id=p.id AND m.academy_group_id=_filter_group))))
                AND club.admin_roster_player_visible(v_actor,v_role,_owner,
                    p.id,p.team_id,p.primary_academy_group_id)
                ORDER BY p.name COLLATE "C",p.id COLLATE "C"
                LIMIT _limit OFFSET _offset
              ) p;

              RETURN jsonb_build_object(
                'organization_id',_owner,'ready',true,
                'offset',_offset,'limit',_limit,'total',v_total,
                'players',v_rows);
            END $admin_roster_players$;
            CREATE FUNCTION club.search_admin_group_roster(
              _digest text,_owner text,_group text,_offset integer,_limit integer,_search text
            ) RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $admin_roster_players$
            DECLARE
              v_role text;
              v_actor uuid;
              v_scope uuid;
              v_location uuid;
              v_total integer;
              v_rows jsonb;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                OR _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
                OR _group ~ '[[:cntrl:]]'
                OR _offset IS NULL OR _offset NOT BETWEEN 0 AND 20000
                OR _limit IS NULL OR _limit NOT BETWEEN 1 AND 50
                OR _search IS NULL OR length(_search)>100 OR _search ~ '[[:cntrl:]]' OR _search<>btrim(_search) THEN
                RAISE EXCEPTION 'group_roster_invalid_scope' USING ERRCODE='22023';
              END IF;

              -- Session, account and exact owner grant locked against
              -- revocation while deciding and reading youth roster data.
              PERFORM identity.lock_admin_club_authority(_digest,_owner);
              SELECT a.id,a.role INTO v_actor,v_role
              FROM identity.admin_sessions s
              JOIN identity.admin_accounts a ON a.id=s.admin_account_id
              JOIN identity.admin_organization_grants g
                ON g.admin_account_id=a.id AND g.organization_id=_owner
              WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                AND s.expires_at>clock_timestamp()
                AND a.is_active AND NOT a.invite_pending
                AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL;
              IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
                RAISE EXCEPTION 'group_roster_permission_denied' USING ERRCODE='42501';
              END IF;
              IF NOT EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial') THEN
                RAISE EXCEPTION 'group_roster_not_ready' USING ERRCODE='55000';
              END IF;

              -- Reject foreign/unpublished group identities before touching
              -- any player record. Academy and First Team catalogs never mix.
              IF _owner='lefteria_fc' THEN
                -- The selected team must have its own verified First Team
                -- operational scope and an explicit team-manager grant.
                SELECT s.id INTO v_scope FROM club.football_operational_scopes s
                  JOIN club.teams t ON t.id=s.team_id
                    AND t.organization_id=s.organization_id
                  WHERE s.organization_id=_owner AND s.team_id=_group;
                v_location:=NULL;
              ELSE
                -- An Academy group is tied to one physical location; a
                -- manager of another location in the same city is denied.
                SELECT g.scope_id,g.location_id INTO v_scope,v_location
                  FROM club.academy_group_location_assignments g
                  JOIN club.academy_groups a ON a.id=g.group_id
                    AND a.organization_id=g.organization_id
                  WHERE g.organization_id=_owner AND g.group_id=_group;
              END IF;
              -- Organization grants alone are never sufficient to read a
              -- child's group roster. Unassigned or foreign scopes fail
              -- closed until Super Admin assigns their team or location.
              IF v_scope IS NULL
                OR NOT club.admin_football_allowed(
                    v_actor,v_role,_owner,v_scope,v_location,false) THEN
                RAISE EXCEPTION 'group_roster_scope_denied' USING ERRCODE='42501';
              END IF;

              -- Only active roster members; never a global directory or
              -- archived/transferred children. Bound the response and count.
              SELECT count(*) INTO v_total
              FROM club.players p
              WHERE p.organization_id=_owner AND p.is_active
                AND NOT p.is_archived AND NOT p.is_transferred
                AND p.team_type=CASE WHEN _owner='lefteria_fc'
                  THEN 'First Team' ELSE 'Academy' END
                AND (_search='' OR strpos(lower(p.name),lower(_search))>0 OR strpos(lower(p.id),lower(_search))>0)
                AND ((_owner='lefteria_fc' AND p.team_id=_group)
                   OR (_owner='lefteria_fc_academy' AND
                     (p.primary_academy_group_id=_group OR EXISTS(
                       SELECT 1 FROM club.player_academy_groups m
                        WHERE m.organization_id=_owner AND m.player_id=p.id
                          AND m.academy_group_id=_group))));
              IF v_total>25000 THEN
                RAISE EXCEPTION 'group_roster_too_large' USING ERRCODE='54000';
              END IF;

              SELECT COALESCE(jsonb_agg(
                jsonb_build_object(
                  'id',p.id,'name',p.name,
                  'team_id',CASE WHEN _owner='lefteria_fc' THEN p.team_id ELSE NULL END,
                  -- Never disclose other locations' group memberships;
                  -- assignments must use the separately authorized org picker.
                  'academy_group_ids',CASE WHEN _owner='lefteria_fc_academy'
                    THEN jsonb_build_array(_group) ELSE '[]'::jsonb END)
                ORDER BY p.name COLLATE "C",p.id COLLATE "C"),'[]'::jsonb)
              INTO v_rows
              FROM (
                SELECT p.id,p.name,p.organization_id,p.team_id,p.primary_academy_group_id
                FROM club.players p
                WHERE p.organization_id=_owner AND p.is_active
                  AND NOT p.is_archived AND NOT p.is_transferred
                  AND p.team_type=CASE WHEN _owner='lefteria_fc'
                    THEN 'First Team' ELSE 'Academy' END
                  AND (_search='' OR strpos(lower(p.name),lower(_search))>0 OR strpos(lower(p.id),lower(_search))>0)
                AND ((_owner='lefteria_fc' AND p.team_id=_group)
                   OR (_owner='lefteria_fc_academy' AND
                     (p.primary_academy_group_id=_group OR EXISTS(
                       SELECT 1 FROM club.player_academy_groups m
                        WHERE m.organization_id=_owner AND m.player_id=p.id
                          AND m.academy_group_id=_group))))
                ORDER BY p.name COLLATE "C",p.id COLLATE "C"
                LIMIT _limit OFFSET _offset
              ) p;

              RETURN jsonb_build_object(
                'organization_id',_owner,'group_id',_group,'ready',true,
                'offset',_offset,'limit',_limit,'total',v_total,
                'players',v_rows);
            END $admin_roster_players$;
            REVOKE ALL ON FUNCTION club.search_admin_roster_players(text,text,integer,integer,text,text),
              club.search_admin_group_roster(text,text,text,integer,integer,text) FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION club.search_admin_group_roster(text,text,text,integer,integer,text);
        DROP FUNCTION club.search_admin_roster_players(text,text,integer,integer,text,text);
        """);
}
