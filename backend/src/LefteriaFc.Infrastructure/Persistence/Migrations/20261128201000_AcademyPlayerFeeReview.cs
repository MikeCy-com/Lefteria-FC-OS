using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Exact-child finance review includes all retained monthly invoices
/// rather than filtering the latest 100 organization-wide records in the UI.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261128201000_AcademyPlayerFeeReview")]
public sealed class AcademyPlayerFeeReview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION finance.read_admin_player_training_fees(_digest text,_owner text,_player text)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $fee_admin_read$
      DECLARE v_role text; v_fees jsonb; v_name text; v_active boolean; v_month date;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner IS DISTINCT FROM 'lefteria_fc_academy'
          OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128 OR _player ~ '[[:cntrl:]]'
        THEN RAISE EXCEPTION 'fee_scope_invalid' USING ERRCODE='22023'; END IF;
        PERFORM identity.lock_admin_club_authority(_digest,_owner);
        SELECT a.role INTO v_role FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id
            AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role<>'super_admin' THEN
          RAISE EXCEPTION 'fee_admin_read_denied' USING ERRCODE='42501'; END IF;
        SELECT p.name,p.is_active AND NOT p.is_archived AND NOT p.is_transferred
          INTO v_name,v_active FROM club.players p
          WHERE p.organization_id=_owner AND p.id=_player AND p.team_type='Academy';
        IF NOT FOUND THEN RAISE EXCEPTION 'fee_player_not_authorized' USING ERRCODE='42501'; END IF;
        v_month:=date_trunc('month',clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date;
        SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',f.id,'player_id',f.player_id,'child_name',v_name,
            'fee_month',f.fee_month,'amount_cents',f.amount_cents,
            'currency',f.currency,'status',f.status,
            'payer_email',f.payer_email,'payer_first_name',f.payer_first_name,'payer_last_name',f.payer_last_name,
            'cancelled_at',f.cancelled_at,'cancel_reason',f.cancel_reason,
            'cancel_eligible',f.fee_month>v_month AND f.status='issued'
              AND f.payment_request_id IS NULL AND f.paid_at IS NULL
              AND NOT EXISTS(SELECT 1 FROM finance.training_fee_cash_receipts cr WHERE cr.fee_issue_id=f.id)
              AND NOT EXISTS(SELECT 1 FROM finance.training_fee_sms_outbox sms WHERE sms.fee_issue_id=f.id),
            'payment_request_id',f.payment_request_id,'payment_status',COALESCE(r.status,'not_created'),
            'checkout_url',CASE WHEN r.status='ready' THEN r.checkout_url ELSE NULL END,
            'sms_status',(SELECT o.state FROM finance.training_fee_sms_outbox o
                WHERE o.fee_issue_id=f.id ORDER BY o.created_at DESC LIMIT 1))
            ORDER BY f.fee_month DESC,f.issued_at DESC,f.id DESC),'[]'::jsonb)
          INTO v_fees
          FROM (SELECT * FROM finance.training_fee_issues
            WHERE organization_id=_owner AND player_id=_player
            ORDER BY fee_month DESC,issued_at DESC,id DESC LIMIT 1001) f
          LEFT JOIN finance.payment_requests r ON r.id=f.payment_request_id;
        IF jsonb_array_length(v_fees)>1000
        THEN RAISE EXCEPTION 'fee_player_history_too_large' USING ERRCODE='54000'; END IF;
        RETURN jsonb_build_object('organization_id',_owner,'player_id',_player,'player_name',v_name,
          'player_active',v_active,'current_month',v_month,'fees',v_fees);
      END $fee_admin_read$;
      REVOKE ALL ON FUNCTION finance.read_admin_player_training_fees(text,text,text) FROM PUBLIC;
      """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION finance.read_admin_player_training_fees(text,text,text);
      """);
}
