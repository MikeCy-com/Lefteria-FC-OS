using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Create an audited pending refund instruction from an approved return,
/// bounded by the original verified payment and returned-line value. No
/// transfer, cash payout or gateway refund is executed by this function.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110163000_PosReviewedRefundRequests")]
public sealed class PosReviewedRefundRequests : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      ALTER TABLE pos.refund_events ADD COLUMN request_id uuid;
      CREATE UNIQUE INDEX uq_pos_refund_request
        ON pos.refund_events(organization_id,request_id)
        WHERE request_id IS NOT NULL;

      ALTER TABLE pos.transaction_audit
        DROP CONSTRAINT transaction_audit_action_check;
      ALTER TABLE pos.transaction_audit
        ADD CONSTRAINT transaction_audit_action_check CHECK(action IN (
          'shift_open','shift_close','sale_create','sale_void',
          'cash_verify','card_capture_verify','sale_paid','receipt_issue',
          'return_request','return_approve','return_reject',
          'refund_request','refund_verify'));

      CREATE FUNCTION pos.request_approved_return_refund(
        _digest text,_owner text,_return uuid,_payment uuid,
        _request uuid,_amount bigint
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $refund$
      DECLARE
        v_actor uuid; v_role text; v_return pos.returns%ROWTYPE;
        v_payment pos.payment_events%ROWTYPE;
        v_existing pos.refund_events%ROWTYPE; v_new uuid;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _return IS NULL OR _payment IS NULL OR _request IS NULL
          OR _request='00000000-0000-0000-0000-000000000000'::uuid
          OR _amount IS NULL OR _amount NOT BETWEEN 1 AND 100000000
        THEN RAISE EXCEPTION 'pos_refund_request_invalid'
          USING ERRCODE='22023'; END IF;

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
          RAISE EXCEPTION 'pos_refund_not_authorized' USING ERRCODE='42501'; END IF;

        SELECT * INTO v_return FROM pos.returns
          WHERE organization_id=_owner AND id=_return FOR UPDATE;
        IF NOT FOUND OR v_return.status<>'approved'
          OR v_return.approved_by IS NULL
          OR v_actor=v_return.created_by
          OR v_actor=v_return.approved_by THEN
          RAISE EXCEPTION 'pos_refund_separation_of_duties'
            USING ERRCODE='42501'; END IF;

        SELECT * INTO v_existing FROM pos.refund_events
          WHERE organization_id=_owner AND request_id=_request FOR UPDATE;
        IF FOUND THEN
          IF v_existing.return_id<>_return
            OR v_existing.payment_event_id<>_payment
            OR v_existing.amount_cents<>_amount
            OR v_existing.recorded_by<>v_actor THEN
            RAISE EXCEPTION 'pos_refund_request_replay_mismatch'
              USING ERRCODE='23505';
          END IF;
          RETURN jsonb_build_object(
            'organization_id',_owner,'return_id',_return,
            'refund_id',v_existing.id,'status',v_existing.status,
            'amount_cents',v_existing.amount_cents,'replay',true);
        END IF;

        SELECT * INTO v_payment FROM pos.payment_events
          WHERE id=_payment AND organization_id=_owner
            AND sale_id=v_return.sale_id AND status='verified' FOR UPDATE;
        IF NOT FOUND THEN
          RAISE EXCEPTION 'pos_refund_original_payment_unverified'
            USING ERRCODE='42501'; END IF;
        v_new:=gen_random_uuid();
        INSERT INTO pos.refund_events(
          id,organization_id,return_id,payment_event_id,
          refund_kind,amount_cents,status,recorded_by,request_id)
        VALUES(v_new,_owner,_return,_payment,v_payment.payment_kind,
          _amount,'pending',v_actor,_request);

        -- The existing pos.guard_refund_amount trigger validates both the
        -- original payment ceiling and the total value of returned units.
        INSERT INTO pos.transaction_audit(
          id,organization_id,actor_id,sale_id,action,request_id)
        VALUES(gen_random_uuid(),_owner,v_actor,
          v_return.sale_id,'refund_request',_request);
        RETURN jsonb_build_object(
          'organization_id',_owner,'return_id',_return,
          'refund_id',v_new,'status','pending',
          'amount_cents',_amount,'replay',false);
      END $refund$;
      REVOKE ALL ON FUNCTION pos.request_approved_return_refund(
        text,text,uuid,uuid,uuid,bigint) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$ BEGIN
        IF EXISTS(SELECT 1 FROM pos.refund_events WHERE request_id IS NOT NULL)
        THEN RAISE EXCEPTION 'pos_refund_history_requires_forward_recovery'; END IF;
      END $guard$;
      DROP FUNCTION pos.request_approved_return_refund(
        text,text,uuid,uuid,uuid,bigint);
      ALTER TABLE pos.transaction_audit
        DROP CONSTRAINT transaction_audit_action_check;
      ALTER TABLE pos.transaction_audit
        ADD CONSTRAINT transaction_audit_action_check CHECK(action IN (
          'shift_open','shift_close','sale_create','sale_void',
          'cash_verify','card_capture_verify','sale_paid','receipt_issue',
          'return_request','return_approve','return_reject','refund_verify'));
      DROP INDEX pos.uq_pos_refund_request;
      ALTER TABLE pos.refund_events DROP COLUMN request_id;
    """);
}