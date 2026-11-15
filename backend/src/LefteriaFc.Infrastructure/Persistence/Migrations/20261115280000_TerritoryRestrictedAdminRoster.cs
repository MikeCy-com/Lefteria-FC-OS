using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Closes the legacy organization-wide roster/assignment authorization gap:
/// delegated managers can only read complete player memberships and replace
/// group memberships within verified First Team/Academy location grants.
/// Super Admin retains trusted organization-wide authority.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115280000_TerritoryRestrictedAdminRoster")]
public sealed class TerritoryRestrictedAdminRoster : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""

            -- These helpers are invoked only by the existing owner-checked
            -- security-definer roster functions. They have NO public,
            -- mobile-runtime or admin-runtime EXECUTE grant of their own.
            CREATE FUNCTION club.admin_roster_player_visible(
              _actor uuid,_role text,_owner text,_player text,
              _team text,_primary text
            ) RETURNS boolean
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $player_visible$
            DECLARE v_scope uuid; v_valid boolean;
            BEGIN
              IF _actor IS NULL OR _owner NOT IN
                  ('lefteria_fc','lefteria_fc_academy')
                OR _player IS NULL THEN RETURN false; END IF;
              IF _role='super_admin' THEN RETURN true; END IF;
              IF _role<>'manager' THEN RETURN false; END IF;
              IF _owner='lefteria_fc' THEN
                IF _team IS NULL THEN RETURN false; END IF;
                SELECT s.id INTO v_scope
                  FROM club.football_operational_scopes s
                  WHERE s.organization_id='lefteria_fc' AND s.team_id=_team;
                RETURN v_scope IS NOT NULL AND
                  club.admin_football_allowed(
                    _actor,_role,_owner,v_scope,NULL,false);
              END IF;
              -- Current Academy membership must be complete and inside
              -- assigned locations. Mixed-location memberships MUST NOT
              -- be enumerated through the broad assignment picker.
              WITH memberships AS (
                SELECT _primary AS group_id WHERE _primary IS NOT NULL
                UNION
                SELECT m.academy_group_id FROM club.player_academy_groups m
                  WHERE m.organization_id=_owner AND m.player_id=_player
              )
              SELECT EXISTS(SELECT 1 FROM memberships) AND
                NOT EXISTS(
                  SELECT 1 FROM memberships m
                  LEFT JOIN club.academy_group_location_assignments g
                    ON g.group_id=m.group_id AND g.organization_id=_owner
                  WHERE g.scope_id IS NULL OR g.location_id IS NULL
                    OR NOT club.admin_football_allowed(
                      _actor,_role,_owner,g.scope_id,g.location_id,false)
                ) INTO v_valid;
              RETURN COALESCE(v_valid,false);
            END $player_visible$;

            CREATE FUNCTION club.admin_roster_replace_allowed(
              _actor uuid,_role text,_owner text,
              _current jsonb,_desired jsonb
            ) RETURNS boolean
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $replace_allowed$
            DECLARE v_group text; v_scope uuid; v_location uuid;
            BEGIN
              IF _actor IS NULL OR _owner NOT IN
                  ('lefteria_fc','lefteria_fc_academy')
                OR jsonb_typeof(_current) IS DISTINCT FROM 'array'
                OR jsonb_typeof(_desired) IS DISTINCT FROM 'array'
              THEN RETURN false; END IF;
              IF _role='super_admin' THEN RETURN true; END IF;
              IF _role<>'manager' OR jsonb_array_length(_current)=0
              THEN RETURN false; END IF;
              FOR v_group IN
                SELECT value FROM jsonb_array_elements_text(_current)
                UNION
                SELECT value FROM jsonb_array_elements_text(_desired)
              LOOP
                v_scope:=NULL; v_location:=NULL;
                IF _owner='lefteria_fc' THEN
                  SELECT s.id INTO v_scope
                    FROM club.football_operational_scopes s
                    WHERE s.organization_id='lefteria_fc'
                      AND s.team_id=v_group FOR SHARE;
                ELSE
                  SELECT g.scope_id,g.location_id INTO v_scope,v_location
                    FROM club.academy_group_location_assignments g
                    WHERE g.organization_id='lefteria_fc_academy'
                      AND g.group_id=v_group FOR SHARE;
                END IF;
                IF v_scope IS NULL OR NOT club.admin_football_allowed(
                  _actor,_role,_owner,v_scope,v_location,true)
                THEN RETURN false; END IF;
              END LOOP;
              RETURN true;
            END $replace_allowed$;

            REVOKE ALL ON FUNCTION
              club.admin_roster_player_visible(uuid,text,text,text,text,text),
              club.admin_roster_replace_allowed(uuid,text,text,jsonb,jsonb)
              FROM PUBLIC;


            CREATE OR REPLACE FUNCTION club.read_admin_roster_players(
              _digest text,_owner text,_offset integer,_limit integer
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
                OR _limit IS NULL OR _limit NOT BETWEEN 1 AND 50 THEN
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

            REVOKE ALL ON FUNCTION club.read_admin_roster_players(
              text,text,integer,integer) FROM PUBLIC;
            

            CREATE OR REPLACE FUNCTION club.write_admin_roster_assignment(
              _digest text,_owner text,_player text,_expected jsonb,_desired jsonb
            ) RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $assignment$
            DECLARE
              v_actor uuid;
              v_session uuid;
              v_role text;
              v_team_type text;
              v_team_id text;
              v_primary text;
              v_current jsonb;
              v_expected jsonb;
              v_desired jsonb;
              v_id text;
              v_count integer;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                 OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                 OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
                 OR _player !~ '^[A-Za-z0-9_-]+$'
                 OR jsonb_typeof(_expected) IS DISTINCT FROM 'array'
                 OR jsonb_typeof(_desired) IS DISTINCT FROM 'array'
                 OR jsonb_array_length(_expected)>20 OR jsonb_array_length(_desired)>20
                 OR (_owner='lefteria_fc' AND
                     (jsonb_array_length(_expected)>1 OR jsonb_array_length(_desired)>1))
              THEN
                RAISE EXCEPTION 'assignment_invalid' USING ERRCODE='22023';
              END IF;
              FOR v_id IN SELECT value FROM jsonb_array_elements_text(_expected)
                UNION ALL SELECT value FROM jsonb_array_elements_text(_desired)
              LOOP
                IF v_id IS NULL OR length(v_id) NOT BETWEEN 1 AND 128
                   OR v_id !~ '^[A-Za-z0-9_-]+$' THEN
                  RAISE EXCEPTION 'assignment_invalid_group' USING ERRCODE='22023';
                END IF;
              END LOOP;
              SELECT count(DISTINCT value) INTO v_count
              FROM jsonb_array_elements_text(_expected);
              IF v_count<>jsonb_array_length(_expected) THEN
                RAISE EXCEPTION 'assignment_duplicate_expected' USING ERRCODE='22023';
              END IF;
              SELECT count(DISTINCT value) INTO v_count
              FROM jsonb_array_elements_text(_desired);
              IF v_count<>jsonb_array_length(_desired) THEN
                RAISE EXCEPTION 'assignment_duplicate_desired' USING ERRCODE='22023';
              END IF;

              -- Locks the authenticated session, account and exact organization
              -- grant before the row lock, preventing revocation races.
              PERFORM identity.lock_admin_club_authority(_digest,_owner);
              SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
              FROM identity.admin_sessions s
              JOIN identity.admin_accounts a ON a.id=s.admin_account_id
              JOIN identity.admin_organization_grants g
                ON g.admin_account_id=a.id AND g.organization_id=_owner
              WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                AND s.expires_at>clock_timestamp() AND a.is_active
                AND NOT a.invite_pending AND NOT a.otp2fa_enabled
                AND a.password_hash IS NOT NULL;
              IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
                RAISE EXCEPTION 'assignment_permission_denied' USING ERRCODE='42501';
              END IF;
              IF NOT EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial') THEN
                RAISE EXCEPTION 'assignment_roster_not_ready' USING ERRCODE='55000';
              END IF;

              SELECT p.team_type,p.team_id,p.primary_academy_group_id
                INTO v_team_type,v_team_id,v_primary
              FROM club.players p
              WHERE p.id=_player AND p.organization_id=_owner
                AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                AND p.team_type=CASE WHEN _owner='lefteria_fc'
                  THEN 'First Team' ELSE 'Academy' END
              FOR UPDATE;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'assignment_player_not_found' USING ERRCODE='22023';
              END IF;

              IF _owner='lefteria_fc' THEN
                v_current := CASE WHEN v_team_id IS NULL THEN '[]'::jsonb
                   ELSE jsonb_build_array(v_team_id) END;
                SELECT count(*) INTO v_count FROM club.teams t
                  WHERE t.organization_id=_owner
                    AND t.id IN (SELECT value FROM jsonb_array_elements_text(_desired));
              ELSE
                SELECT coalesce(jsonb_agg(group_id ORDER BY group_id),'[]'::jsonb)
                  INTO v_current
                FROM (
                  SELECT v_primary AS group_id WHERE v_primary IS NOT NULL
                  UNION
                  SELECT pg.academy_group_id FROM club.player_academy_groups pg
                    WHERE pg.organization_id=_owner AND pg.player_id=_player
                ) membership;
                SELECT count(*) INTO v_count FROM club.academy_groups g
                  WHERE g.organization_id=_owner
                    AND g.id IN (SELECT value FROM jsonb_array_elements_text(_desired));
              END IF;
              IF v_count<>jsonb_array_length(_desired) THEN
                RAISE EXCEPTION 'assignment_unknown_group' USING ERRCODE='22023';
              END IF;
              SELECT coalesce(jsonb_agg(value ORDER BY value),'[]'::jsonb)
                INTO v_expected FROM jsonb_array_elements_text(_expected);
              SELECT coalesce(jsonb_agg(value ORDER BY value),'[]'::jsonb)
                INTO v_desired FROM jsonb_array_elements_text(_desired);
              IF v_current<>v_expected THEN
                RAISE EXCEPTION 'assignment_conflict' USING ERRCODE='40001';
              END IF;
              -- The actor may not replace memberships in another city,
              -- location or team even when the org-level role is manager.
              -- Current and desired full group sets are both checked,
              -- under delegated grant locks, before any mutation.
              IF NOT club.admin_roster_replace_allowed(
                v_actor,v_role,_owner,v_current,v_desired) THEN
                RAISE EXCEPTION 'assignment_territory_denied' USING ERRCODE='42501';
              END IF;

              IF _owner='lefteria_fc' THEN
                UPDATE club.players SET team_id=v_desired->>0
                  WHERE id=_player AND organization_id=_owner;
              ELSE
                -- Keep an existing primary group if retained. Otherwise pick
                -- the first canonical sorted group, or NULL when removing all.
                SELECT CASE WHEN v_primary IS NOT NULL AND v_desired ? v_primary
                    THEN v_primary ELSE v_desired->>0 END INTO v_primary;
                UPDATE club.players SET primary_academy_group_id=v_primary
                  WHERE id=_player AND organization_id=_owner;
                DELETE FROM club.player_academy_groups
                  WHERE organization_id=_owner AND player_id=_player;
                INSERT INTO club.player_academy_groups(
                  organization_id,player_id,academy_group_id)
                SELECT _owner,_player,value FROM jsonb_array_elements_text(v_desired);
              END IF;

              INSERT INTO audit.security_events(
                id,actor_realm,occurred_at,actor_identity_id,
                device_session_id,action,outcome,organization_id,reason_code)
              VALUES (gen_random_uuid(),'admin',clock_timestamp(),v_actor,
                v_session,'admin.roster.membership.replace','succeeded',
                _owner,'roster_membership');
              RETURN jsonb_build_object(
                'organization_id',_owner,'player_id',_player,
                'team_id',CASE WHEN _owner='lefteria_fc'
                  THEN v_desired->>0 ELSE NULL END,
                'academy_group_ids',CASE WHEN _owner='lefteria_fc_academy'
                  THEN v_desired ELSE '[]'::jsonb END
              );
            END
            $assignment$;
            REVOKE ALL ON FUNCTION club.write_admin_roster_assignment(
              text,text,text,jsonb,jsonb) FROM PUBLIC;
            
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Restore the exact previous implementations if this migration alone
        // rolls back; the earlier migrations still own the original routines.
        migrationBuilder.Sql("""

            CREATE OR REPLACE FUNCTION club.read_admin_roster_players(
              _digest text,_owner text,_offset integer,_limit integer
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

              -- Only active roster members; never a global directory or
              -- archived/transferred children. Bound the response and count.
              SELECT count(*) INTO v_total
              FROM club.players p
              WHERE p.organization_id=_owner AND p.is_active
                AND NOT p.is_archived AND NOT p.is_transferred
                AND p.team_type=CASE WHEN _owner='lefteria_fc'
                  THEN 'First Team' ELSE 'Academy' END;
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
                ORDER BY p.name COLLATE "C",p.id COLLATE "C"
                LIMIT _limit OFFSET _offset
              ) p;

              RETURN jsonb_build_object(
                'organization_id',_owner,'ready',true,
                'offset',_offset,'limit',_limit,'total',v_total,
                'players',v_rows);
            END $admin_roster_players$;

            REVOKE ALL ON FUNCTION club.read_admin_roster_players(
              text,text,integer,integer) FROM PUBLIC;
            

            CREATE OR REPLACE FUNCTION club.write_admin_roster_assignment(
              _digest text,_owner text,_player text,_expected jsonb,_desired jsonb
            ) RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $assignment$
            DECLARE
              v_actor uuid;
              v_session uuid;
              v_role text;
              v_team_type text;
              v_team_id text;
              v_primary text;
              v_current jsonb;
              v_expected jsonb;
              v_desired jsonb;
              v_id text;
              v_count integer;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                 OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                 OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
                 OR _player !~ '^[A-Za-z0-9_-]+$'
                 OR jsonb_typeof(_expected) IS DISTINCT FROM 'array'
                 OR jsonb_typeof(_desired) IS DISTINCT FROM 'array'
                 OR jsonb_array_length(_expected)>20 OR jsonb_array_length(_desired)>20
                 OR (_owner='lefteria_fc' AND
                     (jsonb_array_length(_expected)>1 OR jsonb_array_length(_desired)>1))
              THEN
                RAISE EXCEPTION 'assignment_invalid' USING ERRCODE='22023';
              END IF;
              FOR v_id IN SELECT value FROM jsonb_array_elements_text(_expected)
                UNION ALL SELECT value FROM jsonb_array_elements_text(_desired)
              LOOP
                IF v_id IS NULL OR length(v_id) NOT BETWEEN 1 AND 128
                   OR v_id !~ '^[A-Za-z0-9_-]+$' THEN
                  RAISE EXCEPTION 'assignment_invalid_group' USING ERRCODE='22023';
                END IF;
              END LOOP;
              SELECT count(DISTINCT value) INTO v_count
              FROM jsonb_array_elements_text(_expected);
              IF v_count<>jsonb_array_length(_expected) THEN
                RAISE EXCEPTION 'assignment_duplicate_expected' USING ERRCODE='22023';
              END IF;
              SELECT count(DISTINCT value) INTO v_count
              FROM jsonb_array_elements_text(_desired);
              IF v_count<>jsonb_array_length(_desired) THEN
                RAISE EXCEPTION 'assignment_duplicate_desired' USING ERRCODE='22023';
              END IF;

              -- Locks the authenticated session, account and exact organization
              -- grant before the row lock, preventing revocation races.
              PERFORM identity.lock_admin_club_authority(_digest,_owner);
              SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
              FROM identity.admin_sessions s
              JOIN identity.admin_accounts a ON a.id=s.admin_account_id
              JOIN identity.admin_organization_grants g
                ON g.admin_account_id=a.id AND g.organization_id=_owner
              WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                AND s.expires_at>clock_timestamp() AND a.is_active
                AND NOT a.invite_pending AND NOT a.otp2fa_enabled
                AND a.password_hash IS NOT NULL;
              IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
                RAISE EXCEPTION 'assignment_permission_denied' USING ERRCODE='42501';
              END IF;
              IF NOT EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial') THEN
                RAISE EXCEPTION 'assignment_roster_not_ready' USING ERRCODE='55000';
              END IF;

              SELECT p.team_type,p.team_id,p.primary_academy_group_id
                INTO v_team_type,v_team_id,v_primary
              FROM club.players p
              WHERE p.id=_player AND p.organization_id=_owner
                AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                AND p.team_type=CASE WHEN _owner='lefteria_fc'
                  THEN 'First Team' ELSE 'Academy' END
              FOR UPDATE;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'assignment_player_not_found' USING ERRCODE='22023';
              END IF;

              IF _owner='lefteria_fc' THEN
                v_current := CASE WHEN v_team_id IS NULL THEN '[]'::jsonb
                   ELSE jsonb_build_array(v_team_id) END;
                SELECT count(*) INTO v_count FROM club.teams t
                  WHERE t.organization_id=_owner
                    AND t.id IN (SELECT value FROM jsonb_array_elements_text(_desired));
              ELSE
                SELECT coalesce(jsonb_agg(group_id ORDER BY group_id),'[]'::jsonb)
                  INTO v_current
                FROM (
                  SELECT v_primary AS group_id WHERE v_primary IS NOT NULL
                  UNION
                  SELECT pg.academy_group_id FROM club.player_academy_groups pg
                    WHERE pg.organization_id=_owner AND pg.player_id=_player
                ) membership;
                SELECT count(*) INTO v_count FROM club.academy_groups g
                  WHERE g.organization_id=_owner
                    AND g.id IN (SELECT value FROM jsonb_array_elements_text(_desired));
              END IF;
              IF v_count<>jsonb_array_length(_desired) THEN
                RAISE EXCEPTION 'assignment_unknown_group' USING ERRCODE='22023';
              END IF;
              SELECT coalesce(jsonb_agg(value ORDER BY value),'[]'::jsonb)
                INTO v_expected FROM jsonb_array_elements_text(_expected);
              SELECT coalesce(jsonb_agg(value ORDER BY value),'[]'::jsonb)
                INTO v_desired FROM jsonb_array_elements_text(_desired);
              IF v_current<>v_expected THEN
                RAISE EXCEPTION 'assignment_conflict' USING ERRCODE='40001';
              END IF;

              IF _owner='lefteria_fc' THEN
                UPDATE club.players SET team_id=v_desired->>0
                  WHERE id=_player AND organization_id=_owner;
              ELSE
                -- Keep an existing primary group if retained. Otherwise pick
                -- the first canonical sorted group, or NULL when removing all.
                SELECT CASE WHEN v_primary IS NOT NULL AND v_desired ? v_primary
                    THEN v_primary ELSE v_desired->>0 END INTO v_primary;
                UPDATE club.players SET primary_academy_group_id=v_primary
                  WHERE id=_player AND organization_id=_owner;
                DELETE FROM club.player_academy_groups
                  WHERE organization_id=_owner AND player_id=_player;
                INSERT INTO club.player_academy_groups(
                  organization_id,player_id,academy_group_id)
                SELECT _owner,_player,value FROM jsonb_array_elements_text(v_desired);
              END IF;

              INSERT INTO audit.security_events(
                id,actor_realm,occurred_at,actor_identity_id,
                device_session_id,action,outcome,organization_id,reason_code)
              VALUES (gen_random_uuid(),'admin',clock_timestamp(),v_actor,
                v_session,'admin.roster.membership.replace','succeeded',
                _owner,'roster_membership');
              RETURN jsonb_build_object(
                'organization_id',_owner,'player_id',_player,
                'team_id',CASE WHEN _owner='lefteria_fc'
                  THEN v_desired->>0 ELSE NULL END,
                'academy_group_ids',CASE WHEN _owner='lefteria_fc_academy'
                  THEN v_desired ELSE '[]'::jsonb END
              );
            END
            $assignment$;
            REVOKE ALL ON FUNCTION club.write_admin_roster_assignment(
              text,text,text,jsonb,jsonb) FROM PUBLIC;
            
            DROP FUNCTION club.admin_roster_replace_allowed(uuid,text,text,jsonb,jsonb);
            DROP FUNCTION club.admin_roster_player_visible(uuid,text,text,text,text,text);
            """);
    }
}
