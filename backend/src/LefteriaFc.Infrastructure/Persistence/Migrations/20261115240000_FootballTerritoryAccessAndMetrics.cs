using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Exact delegated geography grants with protected roster counts. Historical
/// payment issues cannot be attributed to a location without an immutable,
/// verified issue-time snapshot: this migration makes no financial edits.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115240000_FootballTerritoryAccessAndMetrics")]
public sealed class FootballTerritoryAccessAndMetrics : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- Managers receive only verified city/location/team access; the only
      -- entity that can issue/revoke these grants is an active super admin
      -- assigned to the same exact organization.
      CREATE FUNCTION club.read_admin_football_access(_digest text,_owner text)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $read_access$
      DECLARE v_actor uuid; v_role text;
      BEGIN
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);
        IF v_role<>'super_admin' THEN
          RAISE EXCEPTION 'football_grants_super_admin_only' USING ERRCODE='42501';
        END IF;
        RETURN jsonb_build_object(
          'organization_id',_owner,
          'accounts',(
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'id',a.id,'display_name',a.display_name,'role',a.role)
              ORDER BY a.display_name,a.id),'[]'::jsonb)
            FROM identity.admin_accounts a
            JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id
            WHERE g.organization_id=_owner AND a.is_active
              AND NOT a.invite_pending AND a.password_hash IS NOT NULL
              AND a.role IN ('manager','coach','editor','matchday_editor')
          ),
          'city_grants',CASE WHEN _owner='lefteria_fc_academy' THEN (
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'admin_account_id',g.admin_account_id,
              'scope_id',g.city_id,'role',g.role)
              ORDER BY g.city_id,g.admin_account_id),'[]'::jsonb)
            FROM club.football_city_admin_grants g
          ) ELSE '[]'::jsonb END,
          'location_grants',CASE WHEN _owner='lefteria_fc_academy' THEN (
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'admin_account_id',g.admin_account_id,
              'scope_id',g.location_id,'role',g.role)
              ORDER BY g.location_id,g.admin_account_id,g.role),'[]'::jsonb)
            FROM club.football_location_admin_grants g
          ) ELSE '[]'::jsonb END,
          'team_grants',CASE WHEN _owner='lefteria_fc' THEN (
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'admin_account_id',g.admin_account_id,
              'scope_id',g.scope_id,'role',g.role)
              ORDER BY g.scope_id,g.admin_account_id,g.role),'[]'::jsonb)
            FROM club.football_team_admin_grants g
            JOIN club.football_operational_scopes s ON s.id=g.scope_id
            WHERE s.organization_id='lefteria_fc'
          ) ELSE '[]'::jsonb END
        );
      END $read_access$;

      CREATE FUNCTION club.write_admin_football_access(
        _digest text,_owner text,_subject uuid,_kind text,
        _scope uuid,_role text,_enabled boolean
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $write_access$
      DECLARE v_actor uuid; v_role text; v_subject_role text; v_changed int:=0;
      BEGIN
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);
        IF v_role<>'super_admin' OR _subject IS NULL
          OR _scope IS NULL OR _enabled IS NULL OR _kind IS NULL OR _role IS NULL
          OR _kind NOT IN ('city','location','team')
          OR _subject=v_actor
        THEN
          RAISE EXCEPTION 'football_grant_invalid_actor' USING ERRCODE='42501';
        END IF;
        -- Lock target account and its org membership in the same transaction
        -- to serialize with revocation or deactivation. No organizer may
        -- silently grant a user access to an unrelated organization.
        SELECT a.role INTO v_subject_role
          FROM identity.admin_accounts a
          WHERE a.id=_subject AND a.is_active AND NOT a.invite_pending
            AND a.password_hash IS NOT NULL FOR SHARE;
        IF v_subject_role IS NULL THEN
          RAISE EXCEPTION 'football_grant_unknown_subject' USING ERRCODE='42501';
        END IF;
        PERFORM 1 FROM identity.admin_organization_grants g
          WHERE g.admin_account_id=_subject AND g.organization_id=_owner FOR SHARE;
        IF NOT FOUND THEN
          RAISE EXCEPTION 'football_grant_foreign_organization' USING ERRCODE='42501';
        END IF;
        IF (_role IN ('city_manager','location_manager','team_manager')
              AND v_subject_role<>'manager')
          OR (_role='coach' AND v_subject_role<>'coach')
          OR (_role='staff' AND v_subject_role NOT IN ('editor','matchday_editor'))
        THEN
          RAISE EXCEPTION 'football_grant_role_mismatch' USING ERRCODE='42501';
        END IF;

        IF _kind='city' THEN
          IF _owner<>'lefteria_fc_academy' OR _role<>'city_manager'
            OR NOT EXISTS(SELECT 1 FROM club.football_cities WHERE id=_scope AND is_active)
          THEN RAISE EXCEPTION 'football_city_grant_denied' USING ERRCODE='42501'; END IF;
          IF _enabled THEN
            INSERT INTO club.football_city_admin_grants(admin_account_id,city_id,role)
            VALUES(_subject,_scope,'city_manager') ON CONFLICT DO NOTHING;
          ELSE
            DELETE FROM club.football_city_admin_grants
              WHERE admin_account_id=_subject AND city_id=_scope AND role='city_manager';
          END IF;
        ELSIF _kind='location' THEN
          IF _owner<>'lefteria_fc_academy'
            OR _role NOT IN ('location_manager','coach','staff')
            OR NOT EXISTS(SELECT 1 FROM club.football_locations l
              JOIN club.football_cities c ON c.id=l.city_id
              WHERE l.id=_scope AND l.is_active AND c.is_active)
          THEN RAISE EXCEPTION 'football_location_grant_denied' USING ERRCODE='42501'; END IF;
          IF _enabled THEN
            INSERT INTO club.football_location_admin_grants(admin_account_id,location_id,role)
            VALUES(_subject,_scope,_role) ON CONFLICT DO NOTHING;
          ELSE
            DELETE FROM club.football_location_admin_grants
              WHERE admin_account_id=_subject AND location_id=_scope AND role=_role;
          END IF;
        ELSE
          IF _owner<>'lefteria_fc'
            OR _role NOT IN ('team_manager','coach','staff')
            OR NOT EXISTS(SELECT 1 FROM club.football_operational_scopes
              WHERE id=_scope AND organization_id='lefteria_fc' AND team_id IS NOT NULL)
          THEN RAISE EXCEPTION 'football_team_grant_denied' USING ERRCODE='42501'; END IF;
          IF _enabled THEN
            INSERT INTO club.football_team_admin_grants(
              admin_account_id,scope_id,organization_id,role)
            VALUES(_subject,_scope,'lefteria_fc',_role) ON CONFLICT DO NOTHING;
          ELSE
            DELETE FROM club.football_team_admin_grants
              WHERE admin_account_id=_subject AND scope_id=_scope AND role=_role;
          END IF;
        END IF;
        GET DIAGNOSTICS v_changed = ROW_COUNT;
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,action,outcome,
          organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
          CASE WHEN _enabled THEN 'admin.football.grant.create'
            ELSE 'admin.football.grant.revoke' END,
          'succeeded',_owner,_kind||':'||_role);
        RETURN jsonb_build_object(
          'organization_id',_owner,'admin_account_id',_subject,
          'scope_id',_scope,'kind',_kind,'role',_role,
          'enabled',_enabled,'changed',v_changed=1);
      END $write_access$;

      -- No personal financial data, payment history, medical details,
      -- identities or private coaches' notes. Real roster/group counts come
      -- from canonical memberships rather than legacy imported site labels.
      CREATE FUNCTION club.read_admin_football_location_metrics(
        _digest text,_owner text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $metrics$
      DECLARE v_actor uuid; v_role text;
      BEGIN
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);
        IF _owner<>'lefteria_fc_academy' THEN RETURN
          jsonb_build_object('organization_id',_owner,'locations','[]'::jsonb); END IF;
        RETURN jsonb_build_object(
          'organization_id',_owner,'locations',(
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'city_id',l.city_id,'location_id',l.id,
              'active_players',coalesce(m.active_players,0),
              'groups',coalesce(m.groups,0)
            ) ORDER BY l.city_id,l.id),'[]'::jsonb)
            FROM club.football_locations l
            JOIN club.football_operational_scopes s
              ON s.academy_city_id=l.city_id
              AND s.organization_id='lefteria_fc_academy'
            LEFT JOIN club.private_academy_location_roster_counts m
              ON m.city_id=l.city_id AND m.location_id=l.id
            WHERE club.admin_football_allowed(
              v_actor,v_role,'lefteria_fc_academy',s.id,l.id,false)
          )
        );
      END $metrics$;

      REVOKE ALL ON FUNCTION club.read_admin_football_access(text,text),
        club.write_admin_football_access(text,text,uuid,text,uuid,text,boolean),
        club.read_admin_football_location_metrics(text,text) FROM PUBLIC;

    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION club.read_admin_football_location_metrics(text,text);
      DROP FUNCTION club.write_admin_football_access(text,text,uuid,text,uuid,text,boolean);
      DROP FUNCTION club.read_admin_football_access(text,text);
    """);
}
