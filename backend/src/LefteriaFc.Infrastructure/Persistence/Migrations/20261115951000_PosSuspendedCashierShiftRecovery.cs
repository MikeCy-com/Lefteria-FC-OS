using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// A Super Admin can record a physically counted, idempotent emergency
/// closure for a suspended cashier with no unsettled or inconsistent sales.
/// Original cashier identity and financial history are immutable.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115951000_PosSuspendedCashierShiftRecovery")]
public sealed class PosSuspendedCashierShiftRecovery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      ALTER TABLE pos_cashier.shifts
        ADD COLUMN recovery_actor_id uuid REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT;
      ALTER TABLE pos_cashier.shifts
        ADD COLUMN recovery_reason varchar(32);
      ALTER TABLE pos_cashier.shifts ADD CONSTRAINT ck_pos_shift_recovery_evidence
        CHECK (
          (recovery_actor_id IS NULL AND recovery_reason IS NULL)
          OR (recovery_actor_id IS NOT NULL AND recovery_reason IS NOT NULL
            AND closed_at IS NOT NULL AND close_request_id IS NOT NULL)
        );
      ALTER TABLE pos_cashier.transaction_audit DROP CONSTRAINT transaction_audit_action_check;
      ALTER TABLE pos_cashier.transaction_audit ADD CONSTRAINT transaction_audit_action_check
        CHECK(action IN ('shift_open','shift_close','shift_recovery_close','sale_create','cash_verify',
                'card_capture_verify','sale_paid','receipt_issue',
                'return_request','return_approve','return_reject','refund_verify'));
      CREATE OR REPLACE FUNCTION pos_cashier.read_admin_terminal_setup(
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
                      'open_shift',(SELECT jsonb_build_object(
                        'id',sh.id,'cashier_id',sh.cashier_id,
                        'cashier_name',subject.display_name,
                        'cashier_active',subject.is_active,
                        'opened_at',sh.opened_at,
                        'opening_cash_cents',sh.opening_cash_cents)
                        FROM pos_cashier.shifts sh
                        JOIN identity.admin_accounts subject ON subject.id=sh.cashier_id
                        WHERE sh.organization_id=r.organization_id
                          AND sh.register_id=r.id AND sh.closed_at IS NULL
                        LIMIT 1),
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
                      WHERE a.is_active AND a.role IN ('manager','super_admin','cashier')
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
      CREATE FUNCTION pos_cashier.recover_suspended_admin_cashier_shift(
        _digest text,_owner text,_shift uuid,_counted bigint,_request uuid,_reason text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $recover_shift$
      DECLARE
        v_actor uuid; v_role text; v_session uuid;
        v_register text; v_location text; v_subject uuid;
        v_target_role text; v_target_active boolean;
        v_shift pos_cashier.shifts%ROWTYPE;
        v_cash_in bigint; v_cash_out bigint; v_expected bigint;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _shift IS NULL OR _shift='00000000-0000-0000-0000-000000000000'::uuid
          OR _request IS NULL OR _request='00000000-0000-0000-0000-000000000000'::uuid
          OR _counted IS NULL OR _counted NOT BETWEEN 0 AND 100000000
          OR _reason NOT IN ('account_suspended','security_incident','operator_unavailable')
          OR _reason IS NULL
        THEN RAISE EXCEPTION 'pos_shift_recovery_invalid' USING ERRCODE='22023'; END IF;
      
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);
        IF v_role<>'super_admin'
        THEN RAISE EXCEPTION 'pos_shift_recovery_super_admin_required'
          USING ERRCODE='42501'; END IF;
        -- Territory authority exposes only actor and role; independently
        -- bind the same live administrator session for the audit event.
        SELECT s.id INTO v_session FROM identity.admin_sessions s
          WHERE s.token_digest=_digest AND s.admin_account_id=v_actor
            AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp();
        IF v_session IS NULL THEN
          RAISE EXCEPTION 'pos_shift_recovery_session_revoked'
            USING ERRCODE='42501'; END IF;
      
        -- Never permit a shift id from another organization to disclose a site.
        SELECT sh.cashier_id,sh.register_id,r.location_id
          INTO v_subject,v_register,v_location
          FROM pos_cashier.shifts sh
          JOIN pos_cashier.registers r ON r.organization_id=sh.organization_id
            AND r.id=sh.register_id
          WHERE sh.id=_shift AND sh.organization_id=_owner;
        IF v_location IS NULL
        THEN RAISE EXCEPTION 'pos_shift_recovery_not_found' USING ERRCODE='42501'; END IF;
        -- Also rechecks the canonical active Academy city/location.
        PERFORM pos_cashier.authorize_site_finances(_digest,_owner,v_location);
      
        -- Lock target before shift, consistent with cashier auth account->shift
        -- and Super Admin terminal assignment account->shift ordering.
        SELECT role,is_active INTO v_target_role,v_target_active
          FROM identity.admin_accounts WHERE id=v_subject FOR SHARE;
        SELECT * INTO v_shift FROM pos_cashier.shifts
          WHERE id=_shift AND organization_id=_owner FOR UPDATE;
        IF NOT FOUND OR v_shift.cashier_id<>v_subject OR v_shift.register_id<>v_register
        THEN RAISE EXCEPTION 'pos_shift_recovery_changed' USING ERRCODE='40001'; END IF;
        IF v_shift.closed_at IS NOT NULL THEN
          IF v_shift.recovery_actor_id IS DISTINCT FROM v_actor
             OR v_shift.close_request_id IS DISTINCT FROM _request
             OR v_shift.counted_cash_cents IS DISTINCT FROM _counted
             OR v_shift.recovery_reason IS DISTINCT FROM _reason
          THEN RAISE EXCEPTION 'pos_shift_recovery_already_closed'
            USING ERRCODE='23505'; END IF;
          RETURN jsonb_build_object('organization_id',_owner,
            'register_id',v_register,'shift_id',_shift,'cashier_id',v_subject,
            'closed_by',v_actor,'reason',v_shift.recovery_reason,
            'counted_cash_cents',v_shift.counted_cash_cents,
            'expected_cash_cents',v_shift.expected_cash_cents,
            'variance_cents',v_shift.variance_cents,
            'requires_review',v_shift.variance_cents<>0,'replay',true);
        END IF;
        IF v_target_role IS DISTINCT FROM 'cashier' OR v_target_active IS DISTINCT FROM false
        THEN RAISE EXCEPTION 'pos_shift_recovery_cashier_must_be_suspended'
          USING ERRCODE='42501'; END IF;
        -- All unresolved or inconsistent ledger events must be reviewed separately.
        -- Never repair, void or mark a sale paid as part of emergency closure.
        IF EXISTS(
          SELECT 1 FROM pos_cashier.sales sale
          WHERE sale.organization_id=_owner AND sale.shift_id=_shift AND (
            sale.status NOT IN ('paid','voided')
            OR (sale.status='paid' AND NOT EXISTS(
              SELECT 1 FROM pos_cashier.receipts receipt
              WHERE receipt.organization_id=sale.organization_id AND receipt.sale_id=sale.id))
            OR (sale.status<>'paid' AND EXISTS(
              SELECT 1 FROM pos_cashier.receipts receipt
              WHERE receipt.organization_id=sale.organization_id AND receipt.sale_id=sale.id))
            OR EXISTS(
              SELECT 1 FROM pos_cashier.sale_reservations rs
              WHERE rs.organization_id=sale.organization_id AND rs.sale_id=sale.id
                AND rs.released_at IS NULL AND rs.consumed_at IS NULL)
            OR EXISTS(
              SELECT 1 FROM pos_cashier.payment_events payment
              WHERE payment.organization_id=sale.organization_id AND payment.sale_id=sale.id
                AND payment.status='pending')
            OR EXISTS(
              SELECT 1 FROM pos_cashier.refund_events refund
              JOIN pos_cashier.returns returned ON returned.organization_id=refund.organization_id
                AND returned.id=refund.return_id
              WHERE returned.organization_id=sale.organization_id
                AND returned.sale_id=sale.id AND refund.status='pending')
          )
        ) THEN RAISE EXCEPTION 'pos_shift_recovery_ledger_unresolved' USING ERRCODE='55000'; END IF;
        SELECT coalesce(sum(payment.amount_cents),0) INTO v_cash_in
          FROM pos_cashier.payment_events payment
          JOIN pos_cashier.sales sale ON sale.organization_id=payment.organization_id
            AND sale.id=payment.sale_id
          WHERE sale.organization_id=_owner AND sale.shift_id=_shift
            AND payment.status='verified' AND payment.payment_kind='cash';
        SELECT coalesce(sum(refund.amount_cents),0) INTO v_cash_out
          FROM pos_cashier.refund_events refund
          JOIN pos_cashier.returns returned ON returned.organization_id=refund.organization_id
            AND returned.id=refund.return_id
          JOIN pos_cashier.sales sale ON sale.organization_id=returned.organization_id
            AND sale.id=returned.sale_id
          WHERE sale.organization_id=_owner AND sale.shift_id=_shift
            AND refund.status='verified' AND refund.refund_kind='cash';
        v_expected:=v_shift.opening_cash_cents+v_cash_in-v_cash_out;
        IF v_expected NOT BETWEEN 0 AND 100000000
        THEN RAISE EXCEPTION 'pos_shift_recovery_expected_out_of_range'
          USING ERRCODE='23514'; END IF;
      
        UPDATE pos_cashier.shifts SET closed_at=clock_timestamp(),
          counted_cash_cents=_counted,expected_cash_cents=v_expected,
          variance_cents=_counted-v_expected,close_request_id=_request,
          recovery_actor_id=v_actor,recovery_reason=_reason
          WHERE id=_shift AND organization_id=_owner;
        INSERT INTO pos_cashier.transaction_audit
          (id,organization_id,actor_id,action,request_id)
          VALUES(gen_random_uuid(),_owner,v_actor,'shift_recovery_close',_request);
        INSERT INTO audit.security_events
          (id,actor_realm,occurred_at,actor_identity_id,device_session_id,
           action,outcome,organization_id,subject_digest,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
            'admin.pos.cashier.recovery_close','succeeded',_owner,md5(_shift::text),_reason);
        RETURN jsonb_build_object('organization_id',_owner,
          'register_id',v_register,'shift_id',_shift,'cashier_id',v_subject,
          'closed_by',v_actor,'reason',_reason,
          'counted_cash_cents',_counted,'expected_cash_cents',v_expected,
          'variance_cents',_counted-v_expected,
          'requires_review',_counted<>v_expected,'replay',false);
      END $recover_shift$;
      REVOKE ALL ON FUNCTION pos_cashier.recover_suspended_admin_cashier_shift(
        text,text,uuid,bigint,uuid,text) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $rollback_guard$ BEGIN
        IF EXISTS(SELECT 1 FROM pos_cashier.shifts WHERE recovery_actor_id IS NOT NULL)
          OR EXISTS(SELECT 1 FROM pos_cashier.transaction_audit WHERE action='shift_recovery_close')
        THEN RAISE EXCEPTION 'pos_shift_recovery_requires_forward_recovery'
          USING ERRCODE='23514'; END IF;
      END $rollback_guard$;
      DROP FUNCTION pos_cashier.recover_suspended_admin_cashier_shift(text,text,uuid,bigint,uuid,text);
      ALTER TABLE pos_cashier.transaction_audit DROP CONSTRAINT transaction_audit_action_check;
      ALTER TABLE pos_cashier.transaction_audit ADD CONSTRAINT transaction_audit_action_check
        CHECK(action IN ('shift_open','shift_close','sale_create','cash_verify',
                'card_capture_verify','sale_paid','receipt_issue',
                'return_request','return_approve','return_reject','refund_verify'));
      ALTER TABLE pos_cashier.shifts DROP CONSTRAINT ck_pos_shift_recovery_evidence;
      ALTER TABLE pos_cashier.shifts DROP COLUMN recovery_reason;
      ALTER TABLE pos_cashier.shifts DROP COLUMN recovery_actor_id;
      CREATE OR REPLACE FUNCTION pos_cashier.read_admin_terminal_setup(
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
                      WHERE a.is_active AND a.role IN ('manager','super_admin','cashier')
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
    """);
}
