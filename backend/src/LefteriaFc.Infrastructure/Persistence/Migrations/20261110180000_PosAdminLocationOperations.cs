using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Admin-only POS city/location management and scoped location catalog reads.
/// No operator, terminal checkout, payment or public shop grants.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110180000_PosAdminLocationOperations")]
public sealed class PosAdminLocationOperations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION pos.require_admin_location_owner(
        _digest text,_owner text
      ) RETURNS TABLE(actor_id uuid,session_id uuid)
      LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $pos_manager$
      DECLARE v_role text;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy') THEN
          RAISE EXCEPTION 'pos_location_scope_invalid' USING ERRCODE='22023';
        END IF;
        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'pos_manager_authority_revoked' USING ERRCODE='42501';
        END;
        SELECT a.id,s.id,a.role INTO actor_id,session_id,v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
          RAISE EXCEPTION 'pos_manager_not_authorized' USING ERRCODE='42501';
        END IF;
        RETURN NEXT;
      END $pos_manager$;

      CREATE FUNCTION pos.read_admin_locations(
        _digest text,_owner text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $pos_locations_read$
      DECLARE v_cities integer; v_locations integer; v_assignments integer;
      BEGIN
        PERFORM 1 FROM pos.require_admin_location_owner(_digest,_owner);
        SELECT count(*) INTO v_cities FROM pos.cities WHERE organization_id=_owner;
        SELECT count(*) INTO v_locations FROM pos.locations WHERE organization_id=_owner;
        SELECT count(*) INTO v_assignments FROM pos.location_products WHERE organization_id=_owner;
        IF v_cities>100 OR v_locations>80 OR v_assignments>5000 THEN
          RAISE EXCEPTION 'pos_location_directory_too_large' USING ERRCODE='54000';
        END IF;
        RETURN jsonb_build_object(
          'organization_id',_owner,
          'cities',coalesce((
            SELECT jsonb_agg(jsonb_build_object(
              'id',c.id,'name',c.name,'active',c.active,'version',c.version)
              ORDER BY c.id)
            FROM pos.cities c WHERE c.organization_id=_owner),'[]'::jsonb),
          'locations',coalesce((
            SELECT jsonb_agg(jsonb_build_object(
              'id',l.id,'city_id',l.city_id,'name',l.name,'code',l.code,
              'active',l.active,'version',l.version,
              'products',coalesce((
                SELECT jsonb_agg(jsonb_build_object(
                  'product_id',lp.product_id,'enabled',lp.enabled,
                  'version',lp.version,'available_units',coalesce((
                    SELECT sum(st.on_hand) FROM pos.location_stock st
                    WHERE st.organization_id=lp.organization_id
                      AND st.location_id=lp.location_id
                      AND st.product_id=lp.product_id),0)) ORDER BY lp.product_id)
                FROM pos.location_products lp
                WHERE lp.organization_id=l.organization_id AND lp.location_id=l.id
              ),'[]'::jsonb))
              ORDER BY l.id)
            FROM pos.locations l WHERE l.organization_id=_owner),'[]'::jsonb)
        );
      END $pos_locations_read$;

      CREATE FUNCTION pos.create_admin_city(
        _digest text,_owner text,_name text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $pos_city_create$
      DECLARE v_actor uuid; v_session uuid; v_id text;
      BEGIN
        IF _name IS NULL OR length(_name) NOT BETWEEN 1 AND 100
          OR _name<>btrim(_name) OR _name ~ '[[:cntrl:]]' THEN
          RAISE EXCEPTION 'pos_city_name_invalid' USING ERRCODE='22023';
        END IF;
        SELECT actor_id,session_id INTO v_actor,v_session
          FROM pos.require_admin_location_owner(_digest,_owner);
        IF (SELECT count(*) FROM pos.cities WHERE organization_id=_owner)>=100 THEN
          RAISE EXCEPTION 'pos_city_limit_reached' USING ERRCODE='54000';
        END IF;
        v_id:='city_'||replace(gen_random_uuid()::text,'-','');
        INSERT INTO pos.cities(organization_id,id,name)
        VALUES(_owner,v_id,_name);
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
          'admin.pos.city.create','succeeded',_owner,'created');
        RETURN jsonb_build_object('id',v_id,'name',_name,'active',true,
          'version',0,'organization_id',_owner);
      END $pos_city_create$;

      CREATE FUNCTION pos.create_admin_location(
        _digest text,_owner text,_city text,_name text,_code text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $pos_location_create$
      DECLARE v_actor uuid; v_session uuid; v_id text;
      BEGIN
        IF _city IS NULL OR _city !~ '^[A-Za-z0-9_-]{1,80}$'
          OR _name IS NULL OR length(_name) NOT BETWEEN 1 AND 100
          OR _name<>btrim(_name) OR _name ~ '[[:cntrl:]]'
          OR _code IS NULL OR _code !~ '^[a-z0-9][a-z0-9-]{1,63}$' THEN
          RAISE EXCEPTION 'pos_location_invalid' USING ERRCODE='22023';
        END IF;
        SELECT actor_id,session_id INTO v_actor,v_session
          FROM pos.require_admin_location_owner(_digest,_owner);
        IF NOT EXISTS(SELECT 1 FROM pos.cities
          WHERE organization_id=_owner AND id=_city AND active) THEN
          RAISE EXCEPTION 'pos_city_not_available' USING ERRCODE='22023';
        END IF;
        IF (SELECT count(*) FROM pos.locations WHERE organization_id=_owner)>=80 THEN
          RAISE EXCEPTION 'pos_location_limit_reached' USING ERRCODE='54000';
        END IF;
        v_id:='loc_'||replace(gen_random_uuid()::text,'-','');
        INSERT INTO pos.locations(organization_id,id,city_id,code,name)
        VALUES(_owner,v_id,_city,_code,_name);
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
          'admin.pos.location.create','succeeded',_owner,'created');
        RETURN jsonb_build_object('organization_id',_owner,'id',v_id,
          'city_id',_city,'name',_name,'code',_code,'active',true,'version',0);
      END $pos_location_create$;

      REVOKE ALL ON FUNCTION pos.require_admin_location_owner(text,text) FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos.read_admin_locations(text,text) FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos.create_admin_city(text,text,text) FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos.create_admin_location(text,text,text,text,text) FROM PUBLIC;
    """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION pos.create_admin_location(text,text,text,text,text);
      DROP FUNCTION pos.create_admin_city(text,text,text);
      DROP FUNCTION pos.read_admin_locations(text,text);
      DROP FUNCTION pos.require_admin_location_owner(text,text);
    """);
}