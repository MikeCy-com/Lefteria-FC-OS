using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Scoped register discovery and cash shift lifecycle. This migration creates
/// no grants, enrollments, locations, registers, payments or cashier assignments.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110153000_PosCashierShiftOperations")]
public sealed class PosCashierShiftOperations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      ALTER TABLE pos.shifts ADD COLUMN open_request_id uuid;
      ALTER TABLE pos.shifts ADD COLUMN close_request_id uuid;
      ALTER TABLE pos.shifts ADD COLUMN expected_cash_cents bigint;
      ALTER TABLE pos.shifts ADD COLUMN variance_cents bigint;
      ALTER TABLE pos.shifts ADD CONSTRAINT ck_pos_shift_closure_values
        CHECK (
          (closed_at IS NULL AND close_request_id IS NULL
            AND expected_cash_cents IS NULL AND variance_cents IS NULL)
          OR
          (closed_at IS NOT NULL AND close_request_id IS NOT NULL
            AND expected_cash_cents IS NOT NULL AND variance_cents IS NOT NULL
            AND variance_cents=counted_cash_cents-expected_cash_cents)
        );
      CREATE UNIQUE INDEX uq_pos_shift_open_request
        ON pos.shifts(organization_id,open_request_id)
        WHERE open_request_id IS NOT NULL;
      CREATE UNIQUE INDEX uq_pos_shift_close_request
        ON pos.shifts(organization_id,close_request_id)
        WHERE close_request_id IS NOT NULL;

      CREATE FUNCTION pos.current_cashier_authority(
        _digest text,_owner text,_register text
      ) RETURNS uuid LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $auth$
      DECLARE v_actor uuid; v_role text;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _register IS NULL OR _register !~ '^[A-Za-z0-9_-]{1,80}$'
        THEN RAISE EXCEPTION 'cashier_scope_invalid' USING ERRCODE='22023';
        END IF;
        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'cashier_authority_revoked' USING ERRCODE='42501';
        END;
        SELECT a.id,a.role INTO v_actor,v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          JOIN pos.cashier_assignments ca
            ON ca.organization_id=_owner AND ca.register_id=_register
              AND ca.admin_account_id=a.id AND ca.is_active
          JOIN pos.registers r
            ON r.organization_id=_owner AND r.id=_register AND r.is_active
          JOIN pos.locations l
            ON l.organization_id=_owner AND l.id=r.location_id AND l.is_active
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL
          FOR SHARE OF ca,r,l;
        IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
          RAISE EXCEPTION 'cashier_not_assigned_or_not_active' USING ERRCODE='42501';
        END IF;
        RETURN v_actor;
      END $auth$;

      CREATE FUNCTION pos.read_admin_cashier_registers(
        _digest text,_owner text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $read$
      DECLARE v_actor uuid; v_role text; v_rows jsonb; v_total integer;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
        THEN RAISE EXCEPTION 'cashier_scope_invalid' USING ERRCODE='22023';
        END IF;
        PERFORM identity.lock_admin_club_authority(_digest,_owner);
        SELECT a.id,a.role INTO v_actor,v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
          RAISE EXCEPTION 'cashier_read_denied' USING ERRCODE='42501';
        END IF;
        SELECT count(*) INTO v_total FROM pos.registers r
          JOIN pos.locations l ON l.organization_id=r.organization_id
            AND l.id=r.location_id
          JOIN pos.cashier_assignments ca
            ON ca.organization_id=r.organization_id AND ca.register_id=r.id
              AND ca.admin_account_id=v_actor AND ca.is_active
          WHERE r.organization_id=_owner AND r.is_active AND l.is_active;
        IF v_total>100 THEN
          RAISE EXCEPTION 'cashier_registers_too_many' USING ERRCODE='54000';
        END IF;
        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'id',r.id,'label',r.label,'location_id',r.location_id,
          'location_name',l.name,'shift_id',sh.id,
          'opened_at',sh.opened_at,'opening_cash_cents',sh.opening_cash_cents)
          ORDER BY r.id),'[]'::jsonb) INTO v_rows
          FROM pos.registers r
          JOIN pos.locations l
            ON l.organization_id=r.organization_id AND l.id=r.location_id
          JOIN pos.cashier_assignments ca
            ON ca.organization_id=r.organization_id AND ca.register_id=r.id
              AND ca.admin_account_id=v_actor AND ca.is_active
          LEFT JOIN pos.shifts sh
            ON sh.organization_id=r.organization_id
              AND sh.register_id=r.id AND sh.closed_at IS NULL
          WHERE r.organization_id=_owner AND r.is_active AND l.is_active;
        RETURN jsonb_build_object('organization_id',_owner,
          'registers',v_rows,'total',v_total);
      END $read$;

      CREATE FUNCTION pos.open_admin_cashier_shift(
        _digest text,_owner text,_register text,_opening bigint,_request uuid
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $open$
      DECLARE v_actor uuid; v_id uuid; v_existing pos.shifts%ROWTYPE;
      BEGIN
        IF _opening IS NULL OR _opening NOT BETWEEN 0 AND 100000000
          OR _request IS NULL OR _request='00000000-0000-0000-0000-000000000000'::uuid
        THEN RAISE EXCEPTION 'cashier_open_invalid' USING ERRCODE='22023';
        END IF;
        v_actor:=pos.current_cashier_authority(_digest,_owner,_register);
        SELECT * INTO v_existing FROM pos.shifts
          WHERE organization_id=_owner AND open_request_id=_request FOR UPDATE;
        IF FOUND THEN
          IF v_existing.register_id<>_register OR v_existing.cashier_id<>v_actor
             OR v_existing.opening_cash_cents<>_opening THEN
            RAISE EXCEPTION 'cashier_open_replay_mismatch' USING ERRCODE='23505';
          END IF;
          RETURN jsonb_build_object('organization_id',_owner,
            'register_id',_register,'shift_id',v_existing.id,
            'opening_cash_cents',v_existing.opening_cash_cents,'replay',true);
        END IF;
        v_id:=gen_random_uuid();
        INSERT INTO pos.shifts(id,organization_id,register_id,cashier_id,
          opening_cash_cents,open_request_id)
        VALUES(v_id,_owner,_register,v_actor,_opening,_request);
        INSERT INTO pos.transaction_audit(id,organization_id,actor_id,action,request_id)
        VALUES(gen_random_uuid(),_owner,v_actor,'shift_open',_request);
        RETURN jsonb_build_object('organization_id',_owner,
          'register_id',_register,'shift_id',v_id,
          'opening_cash_cents',_opening,'replay',false);
      END $open$;

      CREATE FUNCTION pos.close_admin_cashier_shift(
        _digest text,_owner text,_shift uuid,_counted bigint,_request uuid
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $close$
      DECLARE v_actor uuid; v_shift pos.shifts%ROWTYPE;
        v_cash_in bigint; v_cash_out bigint; v_expected bigint;
      BEGIN
        IF _shift IS NULL OR _counted IS NULL
          OR _counted NOT BETWEEN 0 AND 100000000
          OR _request IS NULL OR _request='00000000-0000-0000-0000-000000000000'::uuid
        THEN RAISE EXCEPTION 'cashier_close_invalid' USING ERRCODE='22023';
        END IF;
        -- Obtain current authorization before the shift row lock, matching
        -- the checkout lock order and avoiding an auth/shift deadlock.
        SELECT * INTO v_shift FROM pos.shifts
          WHERE id=_shift AND organization_id=_owner;
        IF NOT FOUND THEN RAISE EXCEPTION 'cashier_shift_missing' USING ERRCODE='42501'; END IF;
        v_actor:=pos.current_cashier_authority(_digest,_owner,v_shift.register_id);
        SELECT * INTO v_shift FROM pos.shifts
          WHERE id=_shift AND organization_id=_owner FOR UPDATE;
        IF NOT FOUND OR v_actor<>v_shift.cashier_id THEN
          RAISE EXCEPTION 'cashier_shift_other_actor' USING ERRCODE='42501';
        END IF;
        IF v_shift.closed_at IS NOT NULL THEN
          IF v_shift.close_request_id<>_request OR v_shift.counted_cash_cents<>_counted THEN
            RAISE EXCEPTION 'cashier_shift_already_closed' USING ERRCODE='23505';
          END IF;
          RETURN jsonb_build_object('organization_id',_owner,
            'shift_id',_shift,'expected_cash_cents',v_shift.expected_cash_cents,
            'counted_cash_cents',v_shift.counted_cash_cents,
            'variance_cents',v_shift.variance_cents,'replay',true);
        END IF;
        -- No closing with unresolved payments/returns: settlement and
        -- pending refunds must be resolved first by approved handlers.
        IF EXISTS(SELECT 1 FROM pos.sales WHERE organization_id=_owner
          AND shift_id=_shift AND status NOT IN ('paid','voided'))
          OR EXISTS(SELECT 1 FROM pos.refund_events f
            JOIN pos.returns r ON r.organization_id=f.organization_id
              AND r.id=f.return_id
            JOIN pos.sales s ON s.organization_id=r.organization_id
              AND s.id=r.sale_id
            WHERE s.organization_id=_owner AND s.shift_id=_shift
              AND f.status='pending') THEN
          RAISE EXCEPTION 'cashier_shift_has_unsettled_transactions'
            USING ERRCODE='55000';
        END IF;
        SELECT coalesce(sum(p.amount_cents),0) INTO v_cash_in
          FROM pos.payment_events p JOIN pos.sales s
            ON s.organization_id=p.organization_id AND s.id=p.sale_id
          WHERE s.organization_id=_owner AND s.shift_id=_shift
            AND p.status='verified' AND p.payment_kind='cash';
        SELECT coalesce(sum(f.amount_cents),0) INTO v_cash_out
          FROM pos.refund_events f JOIN pos.returns r
            ON r.organization_id=f.organization_id AND r.id=f.return_id
          JOIN pos.sales s ON s.organization_id=r.organization_id AND s.id=r.sale_id
          WHERE s.organization_id=_owner AND s.shift_id=_shift
            AND f.status='verified' AND f.refund_kind='cash';
        v_expected:=v_shift.opening_cash_cents+v_cash_in-v_cash_out;
        IF v_expected<0 OR v_expected>100000000 THEN
          RAISE EXCEPTION 'cashier_expected_cash_out_of_range' USING ERRCODE='23514';
        END IF;
        UPDATE pos.shifts SET closed_at=clock_timestamp(),
          counted_cash_cents=_counted,expected_cash_cents=v_expected,
          variance_cents=_counted-v_expected,close_request_id=_request
          WHERE id=_shift;
        INSERT INTO pos.transaction_audit(id,organization_id,actor_id,action,request_id)
        VALUES(gen_random_uuid(),_owner,v_actor,'shift_close',_request);
        RETURN jsonb_build_object('organization_id',_owner,
          'shift_id',_shift,'counted_cash_cents',_counted,
          'expected_cash_cents',v_expected,
          'variance_cents',_counted-v_expected,'replay',false);
      END $close$;

      -- Sensitive operations remain default-deny until independently
      -- reviewed, explicitly granted to a dedicated admin database role.
      REVOKE ALL ON FUNCTION pos.current_cashier_authority(text,text,text) FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos.read_admin_cashier_registers(text,text) FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos.open_admin_cashier_shift(text,text,text,bigint,uuid) FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos.close_admin_cashier_shift(text,text,uuid,bigint,uuid) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$ BEGIN
        IF EXISTS(SELECT 1 FROM pos.shifts) THEN
          RAISE EXCEPTION 'cashier_shift_data_requires_forward_recovery';
        END IF;
      END $guard$;
      DROP FUNCTION pos.close_admin_cashier_shift(text,text,uuid,bigint,uuid);
      DROP FUNCTION pos.open_admin_cashier_shift(text,text,text,bigint,uuid);
      DROP FUNCTION pos.read_admin_cashier_registers(text,text);
      DROP FUNCTION pos.current_cashier_authority(text,text,text);
      DROP INDEX pos.uq_pos_shift_close_request;
      DROP INDEX pos.uq_pos_shift_open_request;
      ALTER TABLE pos.shifts DROP CONSTRAINT ck_pos_shift_closure_values;
      ALTER TABLE pos.shifts DROP COLUMN variance_cents;
      ALTER TABLE pos.shifts DROP COLUMN expected_cash_cents;
      ALTER TABLE pos.shifts DROP COLUMN close_request_id;
      ALTER TABLE pos.shifts DROP COLUMN open_request_id;
    """);
}
