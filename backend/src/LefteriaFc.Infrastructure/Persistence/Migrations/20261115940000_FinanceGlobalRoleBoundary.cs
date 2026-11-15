using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Organization-wide Payabl, Academy fees, SMS and invoice operations are
/// Super Admin only for legacy payment functions. Accountant access remains limited to distinct accounting APIs. City/location managers use verified site
/// financial reporting instead. Existing finance function privileges remain
/// restricted; no provider side-effects are enabled by this migration.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115940000_FinanceGlobalRoleBoundary")]
public sealed class FinanceGlobalRoleBoundary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- Reviewed copies of ten existing definer functions. Keep their
      -- validation, sessions, organization grants and accounting behavior
      -- intact; only narrow the global-finance role predicate.
      CREATE OR REPLACE FUNCTION finance.create_admin_payment_request(
                _digest text,_owner text,_id uuid,_client uuid,_nonce_digest text,
                _purpose text,_cents bigint,_email text,_first text,_last text
              ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
              SET search_path=pg_catalog,pg_temp
              AS $payment_create$
              DECLARE v_actor uuid; v_role text; v_session uuid;
                v_row finance.payment_requests%ROWTYPE; v_created boolean:=false;
              BEGIN
                IF _digest !~ '^[0-9a-f]{64}$' OR _owner NOT IN
                  ('lefteria_fc','lefteria_fc_academy') OR _id IS NULL OR _client IS NULL
                  OR _nonce_digest !~ '^[0-9a-f]{64}$'
                  OR length(_purpose) NOT BETWEEN 3 AND 140
                  OR _purpose<>btrim(_purpose) OR _purpose ~ '[[:cntrl:]]'
                  OR _cents NOT BETWEEN 1 AND 100000000
                  OR length(_email) NOT BETWEEN 3 AND 50 OR _email !~ '^[ -~]+$'
                  OR position('@' in _email)=0
                  OR length(_first) NOT BETWEEN 1 AND 50 OR _first !~ '^[ -~]+$'
                  OR length(_last) NOT BETWEEN 1 AND 50 OR _last !~ '^[ -~]+$'
                THEN RAISE EXCEPTION 'payment_request_invalid' USING ERRCODE='22023'; END IF;
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
                IF NOT FOUND OR v_role<>'super_admin'
                  THEN RAISE EXCEPTION 'payment_access_denied' USING ERRCODE='42501'; END IF;
                INSERT INTO finance.payment_requests(
                  id,organization_id,actor_id,client_request_id,order_id,purpose,
                  amount_cents,payer_email,payer_first_name,payer_last_name,callback_digest)
                VALUES(_id,_owner,v_actor,_client,'LFC'||left(replace(_id::text,'-',''),27),
                  _purpose,_cents,_email,_first,_last,_nonce_digest)
                ON CONFLICT (organization_id,actor_id,client_request_id) DO NOTHING
                RETURNING * INTO v_row;
                IF FOUND THEN v_created:=true; END IF;
                IF NOT v_created THEN
                  SELECT * INTO v_row FROM finance.payment_requests
                  WHERE organization_id=_owner AND actor_id=v_actor
                    AND client_request_id=_client;
                  IF NOT FOUND OR v_row.purpose<>_purpose OR v_row.amount_cents<>_cents
                    OR v_row.payer_email<>_email OR v_row.payer_first_name<>_first
                    OR v_row.payer_last_name<>_last THEN
                    RAISE EXCEPTION 'payment_idempotency_conflict' USING ERRCODE='23505';
                  END IF;
                END IF;
                IF v_created THEN
                  INSERT INTO audit.security_events(
                    id,actor_realm,occurred_at,actor_identity_id,device_session_id,
                    action,outcome,organization_id,reason_code)
                  VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
                    'admin.finance.payabl.request','succeeded',_owner,'payment_request');
                END IF;
                RETURN jsonb_build_object('id',v_row.id,'created',v_created,
                  'status',v_row.status,'checkout_url',v_row.checkout_url,
                  'amount_cents',v_row.amount_cents,'currency',v_row.currency);
              END $payment_create$;

      CREATE OR REPLACE FUNCTION finance.ready_admin_payment_request(
                _digest text,_owner text,_id uuid,_checkout text,_transaction text
              ) RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
              SET search_path=pg_catalog,pg_temp AS $payment_ready$
              DECLARE v_actor uuid; v_role text;
              BEGIN
                IF _digest !~ '^[0-9a-f]{64}$' OR _owner NOT IN
                   ('lefteria_fc','lefteria_fc_academy') OR _checkout IS NULL
                   OR length(_checkout)>2048 OR _checkout !~ '^https://'
                   OR _transaction !~ '^[0-9]{1,40}$' THEN
                  RAISE EXCEPTION 'payment_ready_invalid' USING ERRCODE='22023';
                END IF;
                PERFORM identity.lock_admin_club_authority(_digest,_owner);
                SELECT a.id,a.role INTO v_actor,v_role
                FROM identity.admin_sessions s JOIN identity.admin_accounts a
                  ON a.id=s.admin_account_id
                JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id
                  AND g.organization_id=_owner
                WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                  AND s.expires_at>clock_timestamp() AND a.is_active
                  AND NOT a.invite_pending AND NOT a.otp2fa_enabled
                  AND a.password_hash IS NOT NULL;
                IF NOT FOUND OR v_role<>'super_admin' THEN
                  RAISE EXCEPTION 'payment_ready_denied' USING ERRCODE='42501';
                END IF;
                UPDATE finance.payment_requests SET status='ready',
                  checkout_url=_checkout,initial_transaction_id=_transaction,
                  updated_at=clock_timestamp()
                WHERE id=_id AND organization_id=_owner AND actor_id=v_actor
                  AND status='initiating';
                IF NOT FOUND THEN
                  RAISE EXCEPTION 'payment_state_conflict' USING ERRCODE='P0001';
                END IF;
                RETURN true;
              END $payment_ready$;

      CREATE OR REPLACE FUNCTION finance.read_admin_payment_requests(
                _digest text,_owner text
              ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
              SET search_path=pg_catalog,pg_temp AS $payment_read$
              DECLARE v_role text; v_items jsonb;
              BEGIN
                IF _digest !~ '^[0-9a-f]{64}$' OR _owner NOT IN
                  ('lefteria_fc','lefteria_fc_academy') THEN
                  RAISE EXCEPTION 'payment_scope_invalid' USING ERRCODE='22023';
                END IF;
                PERFORM identity.lock_admin_club_authority(_digest,_owner);
                SELECT a.role INTO v_role FROM identity.admin_sessions s
                JOIN identity.admin_accounts a ON a.id=s.admin_account_id
                JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id
                  AND g.organization_id=_owner
                WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                  AND s.expires_at>clock_timestamp() AND a.is_active
                  AND NOT a.invite_pending AND NOT a.otp2fa_enabled
                  AND a.password_hash IS NOT NULL;
                IF NOT FOUND OR v_role<>'super_admin'
                  THEN RAISE EXCEPTION 'payment_read_denied' USING ERRCODE='42501'; END IF;
                SELECT coalesce(jsonb_agg(jsonb_build_object(
                  'id',p.id,'purpose',p.purpose,'amount_cents',p.amount_cents,
                  'currency',p.currency,'status',p.status,'checkout_url',p.checkout_url,
                  'payer_email',p.payer_email,'created_at',p.created_at,
                  'last_provider_event',(
                    SELECT jsonb_build_object(
                      'event_type',n.event_type,'payment_state',n.payment_state,
                      'error_code',n.error_code,'reported_amount_cents',n.amount_cents,
                      'reported_currency',n.currency,'recorded_at',n.recorded_at)
                    FROM finance.payment_notifications n
                    WHERE n.request_id=p.id
                    ORDER BY n.recorded_at DESC,n.provider_timestamp DESC LIMIT 1
                  ))
                  ORDER BY p.created_at DESC,p.id DESC),'[]'::jsonb)
                INTO v_items FROM (
                  SELECT * FROM finance.payment_requests
                  WHERE organization_id=_owner ORDER BY created_at DESC,id DESC LIMIT 50
                ) p;
                RETURN jsonb_build_object('organization_id',_owner,'payments',v_items);
              END $payment_read$;

      CREATE OR REPLACE FUNCTION finance.issue_admin_training_fee(
                _digest text,_owner text,_id uuid,_player text,_month date,
                _cents bigint,_email text,_first text,_last text
              ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
              SET search_path=pg_catalog,pg_temp AS $fee_issue$
              DECLARE
                v_actor uuid; v_session uuid; v_role text;
                v_payer uuid; v_payer_phone varchar(15);
                v_row finance.training_fee_issues%ROWTYPE;
                v_created boolean:=false;
              BEGIN
                IF _digest !~ '^[0-9a-f]{64}$'
                  OR _owner<>'lefteria_fc_academy' OR _id IS NULL
                  OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
                  OR _month IS NULL OR EXTRACT(day FROM _month)<>1
                  OR _month < DATE '2020-01-01' OR _month > DATE '2100-12-01'
                  OR _cents NOT BETWEEN 1 AND 100000000
                  OR length(_email) NOT BETWEEN 3 AND 50
                  OR _email !~ '^[ -~]+$' OR position('@' IN _email)=0
                  OR length(_first) NOT BETWEEN 1 AND 50 OR _first !~ '^[ -~]+$'
                  OR length(_last) NOT BETWEEN 1 AND 50 OR _last !~ '^[ -~]+$' THEN
                  RAISE EXCEPTION 'invalid_training_fee_issue' USING ERRCODE='22023';
                END IF;
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
                IF NOT FOUND OR v_role<>'super_admin' THEN
                  RAISE EXCEPTION 'fee_access_denied' USING ERRCODE='42501';
                END IF;
      
                SELECT i.id,i.normalized_phone INTO v_payer,v_payer_phone
                FROM club.players p
                JOIN identity.mobile_record_links l
                  ON l.collection='players' AND l.record_id=p.id
                  AND l.kind='guardian' AND l.role='parent'
                JOIN identity.mobile_identities i
                  ON i.id=l.identity_id AND i.normalized_phone=l.normalized_phone
                  AND i.role='parent' AND i.is_active
                JOIN identity.canonical_contact_bindings b
                  ON b.collection=l.collection AND b.source_id=l.record_id
                  AND b.kind='guardian' AND b.normalized_phone=i.normalized_phone
                WHERE p.id=_player AND p.organization_id=_owner
                  AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                ORDER BY l.is_primary DESC,i.id LIMIT 1;
                IF NOT FOUND THEN
                  RAISE EXCEPTION 'verified_parent_and_child_required' USING ERRCODE='42501';
                END IF;
      
                INSERT INTO finance.training_fee_issues(
                  id,organization_id,player_id,payer_identity_id,fee_month,
                  amount_cents,payer_phone,payer_email,payer_first_name,payer_last_name,issued_by)
                VALUES(_id,_owner,_player,v_payer,_month,_cents,'+'||v_payer_phone,_email,_first,_last,v_actor)
                ON CONFLICT (organization_id,player_id,fee_month) DO NOTHING
                RETURNING * INTO v_row;
                IF FOUND THEN v_created:=true; END IF;
                IF NOT v_created THEN
                  SELECT * INTO v_row FROM finance.training_fee_issues
                    WHERE organization_id=_owner AND player_id=_player
                      AND fee_month=_month FOR UPDATE;
                  IF NOT FOUND OR v_row.amount_cents<>_cents
                    OR v_row.payer_identity_id<>v_payer
                    OR v_row.payer_email<>_email OR v_row.payer_first_name<>_first
                    OR v_row.payer_last_name<>_last THEN
                    RAISE EXCEPTION 'fee_month_already_issued_differently'
                      USING ERRCODE='23505';
                  END IF;
                END IF;
                IF v_created THEN
                  INSERT INTO audit.security_events(
                    id,actor_realm,occurred_at,actor_identity_id,device_session_id,
                    action,outcome,organization_id,reason_code)
                  VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
                    'admin.finance.training_fee.issue','succeeded',_owner,'explicit_month');
                END IF;
                RETURN jsonb_build_object('id',v_row.id,'created',v_created,
                  'status',v_row.status,'fee_month',v_row.fee_month,
                  'amount_cents',v_row.amount_cents,
                  'payer_email',v_row.payer_email,
                  'payer_first_name',v_row.payer_first_name,
                  'payer_last_name',v_row.payer_last_name,
                  'player_id',v_row.player_id,
                  'payment_request_id',v_row.payment_request_id);
              END $fee_issue$;

      CREATE OR REPLACE FUNCTION finance.attach_admin_training_fee_payment(
                _digest text,_owner text,_issue uuid,_request uuid
              ) RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
              SET search_path=pg_catalog,pg_temp AS $fee_attach$
              DECLARE v_actor uuid; v_role text; v_fee finance.training_fee_issues%ROWTYPE;
                      v_payment finance.payment_requests%ROWTYPE;
              BEGIN
                IF _digest !~ '^[0-9a-f]{64}$'
                  OR _owner<>'lefteria_fc_academy' OR _issue IS NULL OR _request IS NULL
                  THEN RAISE EXCEPTION 'fee_attach_invalid' USING ERRCODE='22023'; END IF;
                PERFORM identity.lock_admin_club_authority(_digest,_owner);
                SELECT a.id,a.role INTO v_actor,v_role FROM identity.admin_sessions s
                  JOIN identity.admin_accounts a ON a.id=s.admin_account_id
                  JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id
                    AND g.organization_id=_owner
                  WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                    AND s.expires_at>clock_timestamp() AND a.is_active
                    AND NOT a.invite_pending AND NOT a.otp2fa_enabled
                    AND a.password_hash IS NOT NULL;
                IF NOT FOUND OR v_role<>'super_admin' THEN
                  RAISE EXCEPTION 'fee_attach_denied' USING ERRCODE='42501'; END IF;
                SELECT * INTO v_fee FROM finance.training_fee_issues
                  WHERE id=_issue AND organization_id=_owner FOR UPDATE;
                SELECT * INTO v_payment FROM finance.payment_requests
                  WHERE id=_request AND organization_id=_owner AND actor_id=v_actor FOR UPDATE;
                IF v_fee.id IS NULL OR v_payment.id IS NULL
                  OR v_fee.status<>'issued'
                  OR (v_fee.payment_request_id IS NOT NULL
                    AND v_fee.payment_request_id<>_request)
                  OR v_fee.amount_cents<>v_payment.amount_cents
                  OR v_fee.payer_email<>v_payment.payer_email
                  OR v_fee.payer_first_name<>v_payment.payer_first_name
                  OR v_fee.payer_last_name<>v_payment.payer_last_name
                  OR v_payment.currency<>'EUR'
                  OR v_payment.purpose<>('Academy training '||to_char(v_fee.fee_month,'YYYY-MM'))
                THEN RAISE EXCEPTION 'fee_payment_mismatch' USING ERRCODE='23505'; END IF;
                UPDATE finance.training_fee_issues
                  SET payment_request_id=_request WHERE id=_issue;
                RETURN true;
              END $fee_attach$;

      CREATE OR REPLACE FUNCTION finance.read_admin_training_fees(_digest text,_owner text)
              RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
              SET search_path=pg_catalog,pg_temp AS $fee_admin_read$
              DECLARE v_role text; v_fees jsonb;
              BEGIN
                IF _digest !~ '^[0-9a-f]{64}$' OR _owner<>'lefteria_fc_academy'
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
                SELECT coalesce(jsonb_agg(jsonb_build_object(
                    'id',f.id,'player_id',f.player_id,'child_name',p.name,
                    'fee_month',f.fee_month,'amount_cents',f.amount_cents,
                    'currency',f.currency,'status',f.status,
                    'payer_email',f.payer_email,
                    'payer_first_name',f.payer_first_name,
                    'payer_last_name',f.payer_last_name,
                    'payment_request_id',f.payment_request_id,
                    'payment_status',COALESCE(r.status,'not_created'),
                    'checkout_url',CASE WHEN r.status='ready' THEN r.checkout_url ELSE NULL END,
                    'sms_status',(SELECT o.state FROM finance.training_fee_sms_outbox o
                        WHERE o.fee_issue_id=f.id ORDER BY o.created_at DESC LIMIT 1))
                    ORDER BY f.fee_month DESC,f.issued_at DESC,f.id DESC),'[]'::jsonb)
                  INTO v_fees
                  FROM (SELECT * FROM finance.training_fee_issues
                    WHERE organization_id=_owner
                    ORDER BY fee_month DESC,issued_at DESC,id DESC LIMIT 100) f
                  JOIN club.players p ON p.id=f.player_id
                  LEFT JOIN finance.payment_requests r ON r.id=f.payment_request_id;
                RETURN jsonb_build_object('organization_id',_owner,'fees',v_fees);
              END $fee_admin_read$;

      CREATE OR REPLACE FUNCTION finance.reserve_admin_training_fee_sms(
                _digest text,_owner text,_fee uuid,_request uuid
              ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
              SET search_path=pg_catalog,pg_temp AS $sms_reserve$
              DECLARE v_actor uuid; v_session uuid; v_role text;
                v_fee finance.training_fee_issues%ROWTYPE;
                v_payment finance.payment_requests%ROWTYPE;
                v_phone text; v_row finance.training_fee_sms_outbox%ROWTYPE;
                v_created boolean:=false;
              BEGIN
                IF _digest !~ '^[0-9a-f]{64}$'
                  OR _owner<>'lefteria_fc_academy' OR _fee IS NULL OR _request IS NULL
                  THEN RAISE EXCEPTION 'fee_sms_invalid' USING ERRCODE='22023'; END IF;
                PERFORM identity.lock_admin_club_authority(_digest,_owner);
                SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
                  FROM identity.admin_sessions s
                  JOIN identity.admin_accounts a ON a.id=s.admin_account_id
                  JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id
                    AND g.organization_id=_owner
                  WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                    AND s.expires_at>clock_timestamp() AND a.is_active
                    AND NOT a.invite_pending AND NOT a.otp2fa_enabled
                    AND a.password_hash IS NOT NULL;
                IF NOT FOUND OR v_role<>'super_admin' THEN
                  RAISE EXCEPTION 'fee_sms_denied' USING ERRCODE='42501'; END IF;
                SELECT * INTO v_fee FROM finance.training_fee_issues
                  WHERE id=_fee AND organization_id=_owner FOR UPDATE;
                SELECT * INTO v_payment FROM finance.payment_requests
                  WHERE id=v_fee.payment_request_id;
                SELECT i.normalized_phone INTO v_phone
                  FROM identity.mobile_identities i
                  JOIN identity.mobile_record_links l ON l.identity_id=i.id
                    AND l.normalized_phone=i.normalized_phone
                    AND l.role='parent' AND l.kind='guardian'
                    AND l.collection='players' AND l.record_id=v_fee.player_id
                  JOIN identity.canonical_contact_bindings b ON b.collection=l.collection
                    AND b.source_id=l.record_id AND b.kind='guardian'
                    AND b.normalized_phone=i.normalized_phone
                  WHERE i.id=v_fee.payer_identity_id AND i.role='parent' AND i.is_active;
                IF v_fee.id IS NULL OR v_fee.status<>'issued'
                  OR v_payment.status<>'ready'
                  OR v_payment.checkout_url IS NULL
                  OR v_payment.amount_cents<>v_fee.amount_cents
                  OR v_payment.organization_id<>_owner
                  OR v_phone !~ '^[1-9][0-9]{7,14}$'
                  THEN RAISE EXCEPTION 'fee_sms_unavailable' USING ERRCODE='42501'; END IF;
                INSERT INTO finance.training_fee_sms_outbox(
                  id,fee_issue_id,client_request_id,organization_id,actor_id)
                VALUES(gen_random_uuid(),_fee,_request,_owner,v_actor)
                ON CONFLICT(fee_issue_id,client_request_id) DO NOTHING
                RETURNING * INTO v_row;
                IF FOUND THEN v_created:=true; END IF;
                IF NOT v_created THEN
                  SELECT * INTO v_row FROM finance.training_fee_sms_outbox
                    WHERE fee_issue_id=_fee AND client_request_id=_request;
                ELSE
                  INSERT INTO audit.security_events(
                    id,actor_realm,occurred_at,actor_identity_id,device_session_id,
                    action,outcome,organization_id,reason_code)
                  VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
                    'admin.finance.training_fee.sms','succeeded',_owner,'reserved_only');
                END IF;
                RETURN jsonb_build_object('id',v_row.id,'new',v_created,
                  'state',v_row.state,'phone','+'||v_phone,
                  'checkout_url',v_payment.checkout_url,
                  'amount_cents',v_fee.amount_cents,'fee_month',v_fee.fee_month);
              END $sms_reserve$;

      CREATE OR REPLACE FUNCTION finance.finish_admin_training_fee_sms(
                _digest text,_owner text,_id uuid,_accepted boolean,_sid text
              ) RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
              SET search_path=pg_catalog,pg_temp AS $sms_finish$
              DECLARE v_actor uuid; v_role text;
              BEGIN
                IF _digest !~ '^[0-9a-f]{64}$'
                  OR _owner<>'lefteria_fc_academy' OR _id IS NULL
                  OR _accepted IS NULL
                  OR (_accepted AND (_sid IS NULL OR _sid !~ '^SM[0-9a-fA-F]{32}$'))
                THEN RAISE EXCEPTION 'fee_sms_finish_invalid' USING ERRCODE='22023'; END IF;
                PERFORM identity.lock_admin_club_authority(_digest,_owner);
                SELECT a.id,a.role INTO v_actor,v_role FROM identity.admin_sessions s
                  JOIN identity.admin_accounts a ON a.id=s.admin_account_id
                  JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id
                    AND g.organization_id=_owner
                  WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                    AND s.expires_at>clock_timestamp() AND a.is_active
                    AND NOT a.invite_pending AND NOT a.otp2fa_enabled
                    AND a.password_hash IS NOT NULL;
                IF NOT FOUND OR v_role<>'super_admin' THEN
                  RAISE EXCEPTION 'fee_sms_finish_denied' USING ERRCODE='42501'; END IF;
                UPDATE finance.training_fee_sms_outbox
                  SET state=CASE WHEN _accepted THEN 'accepted' ELSE 'unknown' END,
                    provider_sid=CASE WHEN _accepted THEN _sid ELSE NULL END,
                    updated_at=clock_timestamp()
                  WHERE id=_id AND organization_id=_owner
                    AND actor_id=v_actor AND state='reserved';
                RETURN FOUND;
              END $sms_finish$;

      CREATE OR REPLACE FUNCTION finance.record_admin_training_fee_cash(
              _digest text,_owner text,_issue uuid,_receipt uuid,_notes text
            ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp AS $fee_cash$
            DECLARE v_actor uuid; v_session uuid; v_role text;
                    v_fee finance.training_fee_issues%ROWTYPE;
                    v_existing finance.training_fee_cash_receipts%ROWTYPE;
            BEGIN
              IF _digest !~ '^[0-9a-f]{64}$' OR _owner<>'lefteria_fc_academy'
                OR _issue IS NULL OR _receipt IS NULL OR
                _notes IS NULL OR length(_notes)>280 OR _notes ~ '[[:cntrl:]]'
              THEN RAISE EXCEPTION 'invalid_cash_receipt' USING ERRCODE='22023'; END IF;
              PERFORM identity.lock_admin_club_authority(_digest,_owner);
              SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
              FROM identity.admin_sessions s
              JOIN identity.admin_accounts a ON a.id=s.admin_account_id
              JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id
                AND g.organization_id=_owner
              WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                AND s.expires_at>clock_timestamp() AND a.is_active
                AND NOT a.invite_pending AND NOT a.otp2fa_enabled
                AND a.password_hash IS NOT NULL;
              IF NOT FOUND OR v_role<>'super_admin' THEN
                RAISE EXCEPTION 'cash_finance_access_denied' USING ERRCODE='42501';
              END IF;
              SELECT * INTO v_fee FROM finance.training_fee_issues
                WHERE id=_issue AND organization_id=_owner FOR UPDATE;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'cash_invoice_missing' USING ERRCODE='42501';
              END IF;
              SELECT * INTO v_existing FROM finance.training_fee_cash_receipts
                WHERE fee_issue_id=_issue;
              IF FOUND THEN
                IF v_existing.id<>_receipt OR v_existing.actor_id<>v_actor
                  OR v_existing.notes<>_notes THEN
                  RAISE EXCEPTION 'cash_receipt_already_exists' USING ERRCODE='23505';
                END IF;
                RETURN jsonb_build_object('id',v_existing.id,'fee_issue_id',_issue,
                  'payment_method','cash','status','paid','replay',true);
              END IF;
              -- Do not accept cash while a card checkout may still complete.
              -- Staff must first reconcile/void that Payabl attempt out of band.
              IF v_fee.status<>'issued' OR v_fee.payment_request_id IS NOT NULL
                THEN RAISE EXCEPTION 'cash_conflicts_with_card_attempt'
                  USING ERRCODE='23505'; END IF;
              INSERT INTO finance.training_fee_cash_receipts(
                id,fee_issue_id,organization_id,actor_id,amount_cents,notes)
              VALUES(_receipt,_issue,_owner,v_actor,v_fee.amount_cents,_notes);
              UPDATE finance.training_fee_issues SET status='paid',
                paid_at=clock_timestamp() WHERE id=_issue AND status='issued';
              INSERT INTO audit.security_events(
                id,actor_realm,occurred_at,actor_identity_id,device_session_id,
                action,outcome,organization_id,reason_code)
              VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
                'admin.finance.training_fee.cash_payment','succeeded',_owner,'exact_invoice');
              RETURN jsonb_build_object('id',_receipt,'fee_issue_id',_issue,
                'payment_method','cash','status','paid','replay',false);
            END $fee_cash$;

      CREATE OR REPLACE FUNCTION finance.read_admin_invoice_pdf(
              _digest text,_owner text,_ids uuid[]
            ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp AS $invoice_admin$
            DECLARE v_role text; v_result jsonb; v_count integer; v_payers integer;
            BEGIN
              IF _digest !~ '^[0-9a-f]{64}$' OR _owner<>'lefteria_fc_academy'
                OR _ids IS NULL OR cardinality(_ids) NOT BETWEEN 1 AND 20
                OR EXISTS(SELECT 1 FROM unnest(_ids) x WHERE x IS NULL)
                OR (SELECT count(DISTINCT x) FROM unnest(_ids) x)<>cardinality(_ids)
              THEN RAISE EXCEPTION 'invalid_invoice_selection' USING ERRCODE='22023'; END IF;
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
                RAISE EXCEPTION 'invoice_access_denied' USING ERRCODE='42501'; END IF;
              SELECT count(*),count(DISTINCT f.payer_identity_id),
                jsonb_agg(jsonb_build_object(
                'id',f.id,'organization_id',f.organization_id,
                'payer_identity_id',f.payer_identity_id,
                'player_id',f.player_id,'player_name',p.name,
                'group_name',coalesce(g.name,'Academy'),
                'payer_name',f.payer_first_name||' '||f.payer_last_name,
                'payer_email',f.payer_email,'payer_phone',f.payer_phone,
                'fee_month',f.fee_month,'due_date',f.invoice_due_date,
                'issued_at',f.issued_at,'paid_at',f.paid_at,'amount_cents',f.amount_cents,
                'description','Training '||to_char(f.fee_month,'YYYY-MM'),
                'currency',f.currency,'status',f.status,
                'payment_method',CASE WHEN cr.id IS NOT NULL THEN 'cash'
                  WHEN r.status='paid' THEN 'card' ELSE 'unpaid' END,
                'transaction_reference',CASE WHEN cr.id IS NOT NULL THEN cr.id::text
                  WHEN r.status='paid' THEN coalesce(r.captured_transaction_id,'')
                  ELSE '' END,
                'payment_status',coalesce(r.status,'not_created'))
                ORDER BY f.fee_month ASC,f.player_id COLLATE "C",f.id)
              INTO v_count,v_payers,v_result
              FROM finance.training_fee_issues f
                JOIN club.players p ON p.id=f.player_id AND p.organization_id=f.organization_id
                LEFT JOIN club.academy_groups g ON g.id=p.primary_academy_group_id
                  AND g.organization_id=p.organization_id
                LEFT JOIN finance.training_fee_cash_receipts cr ON cr.fee_issue_id=f.id
                LEFT JOIN finance.payment_requests r ON r.id=f.payment_request_id
              WHERE f.id=ANY(_ids) AND f.organization_id=_owner;
              IF v_count<>cardinality(_ids) OR v_payers<>1 THEN
                -- Never put fees from different guardians on a document with
                -- the first guardian's payer heading.
                RAISE EXCEPTION 'invoice_outside_scope' USING ERRCODE='42501';
              END IF;
              RETURN jsonb_build_object('items',v_result,'organization_id',_owner);
            END $invoice_admin$;
    """);

    // Downgrading a populated finance database would reopen cross-location
    // manager access. Only an empty disposable migration fixture may revert.
    // No privilege-widening reverse migration. Older migration Downs own the
    // underlying functions/tables on a complete rollback. A targeted rollback
    // keeps the narrowed predicates until a separately reviewed forward change.
    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("SELECT 1; -- finance_global_role_requires_reviewed_forward_recovery");
}
