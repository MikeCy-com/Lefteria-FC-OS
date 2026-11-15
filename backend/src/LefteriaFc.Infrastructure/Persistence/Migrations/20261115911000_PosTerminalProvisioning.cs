using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Super-admin terminal provisioning and named admin cashier assignments.
/// Cash settlement, card capture and fiscal issuing remain separately gated.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115911000_PosTerminalProvisioning")]
public sealed class PosTerminalProvisioning : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- This list is not public and never includes customer/player information.
      CREATE FUNCTION pos_cashier.read_admin_terminal_setup(
        _digest text,_owner text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $terminal_list$
      DECLARE v_actor uuid;v_role text;v_rows jsonb;v_users jsonb;v_canonical jsonb;
      BEGIN
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);
        IF v_role<>'super_admin'
        THEN RAISE EXCEPTION 'pos_terminal_super_admin_only' USING ERRCODE='42501'; END IF;
        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'id',r.id,'label',r.label,'location_id',r.location_id,
          'location_name',l.name,'active',r.is_active,
          'operators',coalesce((
            SELECT jsonb_agg(jsonb_build_object(
              'admin_account_id',ca.admin_account_id,
              'name',a.display_name,'active',ca.is_active)
              ORDER BY a.display_name,a.id)
            FROM pos_cashier.cashier_assignments ca
            JOIN identity.admin_accounts a ON a.id=ca.admin_account_id
            WHERE ca.organization_id=r.organization_id AND ca.register_id=r.id
          ),'[]'::jsonb)
        ) ORDER BY l.name,r.label,r.id),'[]'::jsonb) INTO v_rows
        FROM pos_cashier.registers r
        JOIN pos.locations l
          ON l.organization_id=r.organization_id AND l.id=r.location_id
        WHERE r.organization_id=_owner;
        IF jsonb_array_length(v_rows)>200 THEN
          RAISE EXCEPTION 'pos_terminal_limit_exceeded' USING ERRCODE='54000'; END IF;
        SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',a.id,'name',a.display_name,'role',a.role)
            ORDER BY a.display_name,a.id),'[]'::jsonb) INTO v_users
          FROM identity.admin_accounts a
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE a.is_active AND a.role IN ('manager','super_admin')
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
        IF jsonb_array_length(v_users)>500
        THEN RAISE EXCEPTION 'pos_operator_limit_exceeded' USING ERRCODE='54000'; END IF;
        IF _owner='lefteria_fc_academy' THEN
          SELECT coalesce(jsonb_agg(jsonb_build_object(
            'academy_location_id',l.id,'academy_city_id',c.id,
            'location_name',l.name,'city_name',c.name,
            'pos_location_id',link.pos_location_id)
            ORDER BY c.name,l.name,l.id),'[]'::jsonb) INTO v_canonical
          FROM club.football_locations l
          JOIN club.football_cities c ON c.id=l.city_id
          LEFT JOIN pos.academy_football_location_links link
            ON link.academy_location_id=l.id
          WHERE c.is_active AND l.is_active;
        ELSE v_canonical:='[]'::jsonb; END IF;
        RETURN jsonb_build_object('organization_id',_owner,
          'terminals',v_rows,'operator_accounts',v_users,
          'academy_locations',v_canonical);
      END $terminal_list$;

      CREATE FUNCTION pos_cashier.create_admin_terminal(
        _digest text,_owner text,_location text,_label text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $terminal_create$
      DECLARE v_actor uuid;v_role text;v_id text;v_count int;
      BEGIN
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);
        IF v_role<>'super_admin' OR _label IS NULL
          OR _label<>btrim(_label) OR length(_label) NOT BETWEEN 1 AND 140
          OR _label ~ '[[:cntrl:]]'
        THEN RAISE EXCEPTION 'pos_terminal_create_denied' USING ERRCODE='42501'; END IF;
        PERFORM pos_cashier.authorize_site_finances(_digest,_owner,_location);
        SELECT count(*) INTO v_count FROM pos_cashier.registers
          WHERE organization_id=_owner AND location_id=_location;
        IF v_count>=20 THEN
          RAISE EXCEPTION 'pos_terminal_site_limit' USING ERRCODE='54000'; END IF;
        v_id:='register_'||replace(gen_random_uuid()::text,'-','');
        INSERT INTO pos_cashier.registers(
          organization_id,location_id,id,label,is_active)
          VALUES(_owner,_location,v_id,_label,true);
        INSERT INTO pos_cashier.receipt_sequences(organization_id,register_id,next_number)
          VALUES(_owner,v_id,1) ON CONFLICT DO NOTHING;
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,
          action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
            'admin.pos.terminal.create','succeeded',_owner,'registered');
        RETURN jsonb_build_object('organization_id',_owner,
          'register_id',v_id,'location_id',_location,
          'label',_label,'active',true);
      END $terminal_create$;

      CREATE FUNCTION pos_cashier.set_admin_terminal_operator(
        _digest text,_owner text,_register text,_subject uuid,_enabled boolean
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $operator_assign$
      DECLARE
        v_actor uuid;v_role text;v_subject_role text;v_location text;
        v_academy_location uuid;v_scope uuid;
      BEGIN
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);
        IF v_role<>'super_admin' OR _register IS NULL
          OR _register !~ '^[A-Za-z0-9_-]{1,80}$'
          OR _subject IS NULL OR _enabled IS NULL
        THEN RAISE EXCEPTION 'pos_operator_assignment_denied' USING ERRCODE='42501'; END IF;
        SELECT r.location_id INTO v_location FROM pos_cashier.registers r
          JOIN pos.locations l
            ON l.organization_id=r.organization_id AND l.id=r.location_id
          WHERE r.organization_id=_owner AND r.id=_register
            AND r.is_active AND l.active FOR SHARE OF r,l;
        IF v_location IS NULL
        THEN RAISE EXCEPTION 'pos_register_not_available' USING ERRCODE='42501'; END IF;
        PERFORM pos_cashier.authorize_site_finances(_digest,_owner,v_location);
        SELECT a.role INTO v_subject_role
          FROM identity.admin_accounts a
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE a.id=_subject AND a.is_active AND NOT a.invite_pending
            AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL FOR SHARE OF a,g;
        IF v_subject_role NOT IN ('manager','super_admin') OR v_subject_role IS NULL
        THEN RAISE EXCEPTION 'pos_operator_account_unavailable' USING ERRCODE='42501'; END IF;
        IF _owner='lefteria_fc_academy' THEN
          SELECT link.academy_location_id,s.id
            INTO v_academy_location,v_scope
            FROM pos.academy_football_location_links link
            JOIN club.football_operational_scopes s
              ON s.academy_city_id=link.academy_city_id
              AND s.organization_id=_owner
            WHERE link.organization_id=_owner AND link.pos_location_id=v_location;
          IF v_scope IS NULL OR NOT club.admin_football_allowed(
            _subject,v_subject_role,_owner,v_scope,v_academy_location,true)
          THEN RAISE EXCEPTION 'pos_operator_location_grant_required'
             USING ERRCODE='42501'; END IF;
        END IF;
        -- Never strand an open shift by revoking its active operator.
        -- Close and reconcile the assigned shift before withdrawing access.
        IF NOT _enabled AND EXISTS (
          SELECT 1 FROM pos_cashier.shifts sh
          WHERE sh.organization_id=_owner AND sh.register_id=_register
            AND sh.cashier_id=_subject AND sh.closed_at IS NULL
        ) THEN RAISE EXCEPTION 'pos_operator_open_shift_must_close'
            USING ERRCODE='23514'; END IF;
        INSERT INTO pos_cashier.cashier_assignments(
          organization_id,register_id,admin_account_id,is_active)
          VALUES(_owner,_register,_subject,_enabled)
          ON CONFLICT(organization_id,register_id,admin_account_id)
          DO UPDATE SET is_active=EXCLUDED.is_active;
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,
          action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
            'admin.pos.terminal.operator','succeeded',_owner,
            CASE WHEN _enabled THEN 'assigned' ELSE 'revoked' END);
        RETURN jsonb_build_object('organization_id',_owner,
          'register_id',_register,'admin_account_id',_subject,
          'active',_enabled);
      END $operator_assign$;

      REVOKE ALL ON FUNCTION pos_cashier.read_admin_terminal_setup(text,text) FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos_cashier.create_admin_terminal(text,text,text,text) FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos_cashier.set_admin_terminal_operator(
        text,text,text,uuid,boolean) FROM PUBLIC;
    """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$ BEGIN
        IF EXISTS(SELECT 1 FROM pos_cashier.registers)
        THEN RAISE EXCEPTION 'pos_register_history_requires_forward_recovery'; END IF;
      END $guard$;
      DROP FUNCTION pos_cashier.set_admin_terminal_operator(text,text,text,uuid,boolean);
      DROP FUNCTION pos_cashier.create_admin_terminal(text,text,text,text);
      DROP FUNCTION pos_cashier.read_admin_terminal_setup(text,text);
    """);
}
