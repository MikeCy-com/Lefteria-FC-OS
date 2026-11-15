using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Human-reviewed, append-only approval corrections. An effective reversal
/// removes only the manual accounting effect of the original approval;
/// no bank, card or cash movement occurs, and no general-ledger entry posts.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115268000_FinanceSupplierApprovalReversals")]
public sealed class FinanceSupplierApprovalReversals : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""

      CREATE TABLE finance.supplier_review_reversal_requests (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        organization_id varchar(32) NOT NULL
          CHECK(organization_id IN ('lefteria_fc','lefteria_fc_academy')),
        client_request_id uuid NOT NULL
          CHECK(client_request_id<>'00000000-0000-0000-0000-000000000000'::uuid),
        target_kind varchar(10) NOT NULL CHECK(target_kind IN ('payment','credit')),
        target_id uuid NOT NULL
          CHECK(target_id<>'00000000-0000-0000-0000-000000000000'::uuid),
        bill_id uuid NOT NULL REFERENCES finance.supplier_bills(id) ON DELETE RESTRICT,
        reason varchar(180) NOT NULL CHECK(
          reason=btrim(reason) AND length(reason) BETWEEN 12 AND 180
          AND reason !~ '[[:cntrl:]]'),
        requested_by uuid NOT NULL
          REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        requested_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        UNIQUE(organization_id,client_request_id)
      );
      CREATE INDEX ix_supplier_review_reversal_target
        ON finance.supplier_review_reversal_requests(target_kind,target_id,bill_id);
      CREATE INDEX ix_supplier_review_reversal_bill
        ON finance.supplier_review_reversal_requests(bill_id,requested_at,id);
      CREATE TABLE finance.supplier_review_reversal_decisions (
        request_id uuid PRIMARY KEY
          REFERENCES finance.supplier_review_reversal_requests(id)
          ON DELETE RESTRICT,
        decision varchar(24) NOT NULL
          CHECK(decision IN ('approved_manual','rejected')),
        review_note varchar(180) NOT NULL CHECK(
          review_note=btrim(review_note) AND length(review_note) BETWEEN 12 AND 180
          AND review_note !~ '[[:cntrl:]]'),
        reviewed_by uuid NOT NULL
          REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        reviewed_at timestamptz NOT NULL DEFAULT clock_timestamp()
      );
      REVOKE ALL ON finance.supplier_review_reversal_requests,
        finance.supplier_review_reversal_decisions FROM PUBLIC;
      ALTER TABLE finance.supplier_review_reversal_requests ENABLE ROW LEVEL SECURITY;
      ALTER TABLE finance.supplier_review_reversal_decisions ENABLE ROW LEVEL SECURITY;
      CREATE TRIGGER trg_supplier_review_reversal_request_immutable
        BEFORE UPDATE OR DELETE ON finance.supplier_review_reversal_requests
        FOR EACH ROW EXECUTE FUNCTION finance.reject_supplier_payment_mutation();
      CREATE TRIGGER trg_supplier_review_reversal_decision_immutable
        BEFORE UPDATE OR DELETE ON finance.supplier_review_reversal_decisions
        FOR EACH ROW EXECUTE FUNCTION finance.reject_supplier_payment_mutation();

      CREATE FUNCTION finance.create_admin_supplier_approval_reversal(
        _digest text,_owner text,_client_request uuid,_kind text,_target uuid,
        _reason text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $create$
      DECLARE v_actor uuid; v_bill uuid; v_id uuid; v_existing boolean:=false;
        v_status text;
      BEGIN
        v_actor:=finance.authorize_admin_supplier_bills(_digest,_owner);
        IF _client_request IS NULL
          OR _client_request='00000000-0000-0000-0000-000000000000'::uuid
          OR _target IS NULL OR _target='00000000-0000-0000-0000-000000000000'::uuid
          OR _kind IS NULL OR _kind NOT IN ('payment','credit')
          OR _reason IS NULL OR length(_reason) NOT BETWEEN 12 AND 180
          OR _reason<>btrim(_reason) OR _reason ~ '[[:cntrl:]]'
        THEN RAISE EXCEPTION 'supplier_approval_reversal_invalid'
          USING ERRCODE='22023'; END IF;

        -- An idempotent replay must retain its current status even after
        -- final approval; a changed payload or changed actor is rejected.
        SELECT r.id,r.bill_id INTO v_id,v_bill
        FROM finance.supplier_review_reversal_requests r
        WHERE r.organization_id=_owner AND r.client_request_id=_client_request;
        IF v_id IS NOT NULL THEN
          IF NOT EXISTS(
            SELECT 1 FROM finance.supplier_review_reversal_requests r
            WHERE r.id=v_id AND r.target_kind=_kind AND r.target_id=_target
              AND r.reason=_reason AND r.requested_by=v_actor)
          THEN RAISE EXCEPTION 'supplier_reversal_request_id_conflict'
            USING ERRCODE='23505'; END IF;
          v_existing:=true;
        ELSE
          IF _kind='payment' THEN
            SELECT r.bill_id INTO v_bill
            FROM finance.supplier_payment_requests r
            JOIN finance.supplier_payment_decisions d
              ON d.request_id=r.id AND d.decision='approved_manual'
            WHERE r.id=_target AND r.organization_id=_owner;
          ELSE
            SELECT c.bill_id INTO v_bill
            FROM finance.supplier_credit_requests c
            JOIN finance.supplier_credit_decisions d
              ON d.request_id=c.id AND d.decision='approved_manual'
            WHERE c.id=_target AND c.organization_id=_owner;
          END IF;
          IF v_bill IS NULL
          THEN RAISE EXCEPTION 'supplier_reversal_requires_approved_record'
            USING ERRCODE='22023'; END IF;

          IF EXISTS(
            SELECT 1 FROM finance.supplier_review_reversal_requests r
            JOIN finance.supplier_review_reversal_decisions d
              ON d.request_id=r.id AND d.decision='approved_manual'
            WHERE r.organization_id=_owner AND r.target_kind=_kind
              AND r.target_id=_target)
          THEN RAISE EXCEPTION 'supplier_approval_already_reversed'
            USING ERRCODE='23514'; END IF;
          INSERT INTO finance.supplier_review_reversal_requests(
            organization_id,client_request_id,target_kind,target_id,bill_id,
            reason,requested_by)
          VALUES(_owner,_client_request,_kind,_target,v_bill,_reason,v_actor)
          ON CONFLICT(organization_id,client_request_id) DO NOTHING
          RETURNING id INTO v_id;
          IF v_id IS NULL THEN
            SELECT r.id INTO v_id
            FROM finance.supplier_review_reversal_requests r
            WHERE r.organization_id=_owner AND r.client_request_id=_client_request
              AND r.target_kind=_kind AND r.target_id=_target
              AND r.reason=_reason AND r.requested_by=v_actor;
            IF v_id IS NULL
            THEN RAISE EXCEPTION 'supplier_reversal_request_id_conflict'
              USING ERRCODE='23505'; END IF;
            v_existing:=true;
          ELSE
            INSERT INTO audit.security_events(
              id,actor_realm,occurred_at,actor_identity_id,action,outcome,
              organization_id,reason_code,subject_digest)
            VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
              'admin.finance.supplier_reversal.request','succeeded',_owner,
              _kind,
              encode(sha256(convert_to(_owner||'|'||v_id::text,'UTF8')),'hex'));
          END IF;
        END IF;
        SELECT coalesce(d.decision,'pending') INTO v_status
        FROM finance.supplier_review_reversal_requests r
        LEFT JOIN finance.supplier_review_reversal_decisions d
          ON d.request_id=r.id
        WHERE r.id=v_id;
        RETURN jsonb_build_object('id',v_id,'bill_id',v_bill,
          'target_kind',_kind,'target_id',_target,'organization_id',_owner,
          'status',v_status,'replay',v_existing,
          'bank_verified',false,'bank_refund_recorded',false,
          'posted_to_general_ledger',false);
      END $create$;
      REVOKE ALL ON FUNCTION finance.create_admin_supplier_approval_reversal(
        text,text,uuid,text,uuid,text) FROM PUBLIC;

      CREATE FUNCTION finance.review_admin_supplier_approval_reversal(
        _digest text,_owner text,_reversal uuid,_approve boolean,_note text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $review$
      DECLARE v_actor uuid; v_role text; v_original_reviewer uuid;
        v_reversal finance.supplier_review_reversal_requests%ROWTYPE;
        v_prior finance.supplier_review_reversal_decisions%ROWTYPE;
        v_bill uuid; v_decision text; v_replay boolean:=false;
      BEGIN
        v_actor:=finance.authorize_admin_supplier_bills(_digest,_owner);
        SELECT a.role INTO v_role
        FROM identity.admin_sessions s JOIN identity.admin_accounts a
          ON a.id=s.admin_account_id
        WHERE s.token_digest=_digest AND s.admin_account_id=v_actor
          AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp();
        IF v_role<>'super_admin' OR _reversal IS NULL OR _approve IS NULL
          OR _note IS NULL OR length(_note) NOT BETWEEN 12 AND 180
          OR _note<>btrim(_note) OR _note ~ '[[:cntrl:]]'
        THEN RAISE EXCEPTION 'supplier_reversal_review_denied'
          USING ERRCODE='42501'; END IF;
        SELECT r.* INTO v_reversal FROM finance.supplier_review_reversal_requests r
        WHERE r.id=_reversal AND r.organization_id=_owner FOR SHARE;
        IF NOT FOUND OR v_reversal.requested_by=v_actor
        THEN RAISE EXCEPTION 'supplier_reversal_requester_cannot_review'
          USING ERRCODE='42501'; END IF;
        IF v_reversal.target_kind='payment' THEN
          SELECT d.reviewed_by INTO v_original_reviewer
          FROM finance.supplier_payment_requests r
          JOIN finance.supplier_payment_decisions d
            ON d.request_id=r.id AND d.decision='approved_manual'
          WHERE r.id=v_reversal.target_id AND r.organization_id=_owner
            AND r.bill_id=v_reversal.bill_id;
        ELSE
          SELECT d.reviewed_by INTO v_original_reviewer
          FROM finance.supplier_credit_requests c
          JOIN finance.supplier_credit_decisions d
            ON d.request_id=c.id AND d.decision='approved_manual'
          WHERE c.id=v_reversal.target_id AND c.organization_id=_owner
            AND c.bill_id=v_reversal.bill_id;
        END IF;
        IF v_original_reviewer IS NULL OR v_original_reviewer=v_actor
        THEN RAISE EXCEPTION 'supplier_reversal_requires_independent_reviewer'
          USING ERRCODE='42501'; END IF;
        -- All approved obligations, credits and payment reviews serialize
        -- on the same immutable original supplier invoice row.
        SELECT b.id INTO v_bill FROM finance.supplier_bills b
        WHERE b.id=v_reversal.bill_id AND b.organization_id=_owner FOR UPDATE;
        IF NOT FOUND
        THEN RAISE EXCEPTION 'supplier_reversal_bill_missing'
          USING ERRCODE='42501'; END IF;

        v_decision:=CASE WHEN _approve THEN 'approved_manual' ELSE 'rejected' END;
        SELECT d.* INTO v_prior FROM finance.supplier_review_reversal_decisions d
        WHERE d.request_id=_reversal FOR SHARE;
        IF FOUND THEN
          IF v_prior.decision<>v_decision OR v_prior.review_note<>_note
            OR v_prior.reviewed_by<>v_actor
          THEN RAISE EXCEPTION 'supplier_reversal_decision_conflict'
            USING ERRCODE='23505'; END IF;
          v_replay:=true;
        ELSE
          IF _approve AND EXISTS(
            SELECT 1 FROM finance.supplier_review_reversal_requests r
            JOIN finance.supplier_review_reversal_decisions d
              ON d.request_id=r.id AND d.decision='approved_manual'
            WHERE r.organization_id=_owner
              AND r.target_kind=v_reversal.target_kind
              AND r.target_id=v_reversal.target_id)
          THEN RAISE EXCEPTION 'supplier_reversal_duplicate_approval'
            USING ERRCODE='23514'; END IF;
          INSERT INTO finance.supplier_review_reversal_decisions(
            request_id,decision,review_note,reviewed_by)
          VALUES(_reversal,v_decision,_note,v_actor);
          INSERT INTO audit.security_events(
            id,actor_realm,occurred_at,actor_identity_id,action,outcome,
            organization_id,reason_code,subject_digest)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
            'admin.finance.supplier_reversal.review','succeeded',_owner,
            v_reversal.target_kind||'_'||v_decision,
            encode(sha256(convert_to(_owner||'|'||_reversal::text,'UTF8')),'hex'));
        END IF;
        RETURN jsonb_build_object('id',_reversal,
          'bill_id',v_reversal.bill_id,
          'target_kind',v_reversal.target_kind,
          'target_id',v_reversal.target_id,
          'organization_id',_owner,'status',v_decision,
          'replay',v_replay,'bank_verified',false,
          'bank_refund_recorded',false,'posted_to_general_ledger',false);
      END $review$;
      REVOKE ALL ON FUNCTION finance.review_admin_supplier_approval_reversal(
        text,text,uuid,boolean,text) FROM PUBLIC;

      CREATE OR REPLACE FUNCTION finance.review_admin_supplier_payment_request(
        _digest text,_owner text,_request uuid,_approve boolean,_note text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $review$
      DECLARE
        v_actor uuid; v_role text;
        v_payment finance.supplier_payment_requests%ROWTYPE;
        v_previous finance.supplier_payment_decisions%ROWTYPE;
        v_bill_amount bigint; v_approved bigint;
        v_decision text; v_replay boolean:=false;
      BEGIN
        v_actor:=finance.authorize_admin_supplier_bills(_digest,_owner);
        SELECT a.role INTO v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          WHERE s.token_digest=_digest AND s.admin_account_id=v_actor
            AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp();
        IF v_role<>'super_admin'
          OR _request IS NULL OR _approve IS NULL OR _note IS NULL
          OR length(_note) NOT BETWEEN 8 AND 180
          OR _note<>btrim(_note) OR _note ~ '[[:cntrl:]]'
        THEN RAISE EXCEPTION 'supplier_payment_review_denied'
          USING ERRCODE='42501'; END IF;

        SELECT r.* INTO v_payment FROM finance.supplier_payment_requests r
        WHERE r.id=_request AND r.organization_id=_owner FOR SHARE;
        IF NOT FOUND OR v_payment.requested_by=v_actor
        THEN RAISE EXCEPTION 'supplier_payment_four_eyes_denied'
          USING ERRCODE='42501'; END IF;
        -- A lock on the permanent bill row serializes all approvals for it.
        SELECT b.gross_amount_cents INTO v_bill_amount
          FROM finance.supplier_bills b
          WHERE b.id=v_payment.bill_id AND b.organization_id=_owner
          FOR UPDATE;
        IF NOT FOUND THEN RAISE EXCEPTION 'supplier_payment_bill_missing'
          USING ERRCODE='42501'; END IF;

        v_decision:=CASE WHEN _approve THEN 'approved_manual' ELSE 'rejected' END;
        SELECT d.* INTO v_previous FROM finance.supplier_payment_decisions d
        WHERE d.request_id=_request FOR SHARE;
        IF FOUND THEN
          IF v_previous.decision<>v_decision OR v_previous.review_note<>_note
            OR v_previous.reviewed_by<>v_actor
          THEN RAISE EXCEPTION 'supplier_payment_decision_conflict'
            USING ERRCODE='23505'; END IF;
          v_replay:=true;
        ELSE
          IF _approve THEN
            SELECT coalesce(sum(r.amount_cents),0)::bigint INTO v_approved
            FROM finance.supplier_payment_requests r
            JOIN finance.supplier_payment_decisions d
              ON d.request_id=r.id AND d.decision='approved_manual' AND NOT EXISTS (
                SELECT 1 FROM finance.supplier_review_reversal_requests rev
                JOIN finance.supplier_review_reversal_decisions revd
                  ON revd.request_id=rev.id AND revd.decision='approved_manual'
                WHERE rev.target_kind='payment' AND rev.target_id=r.id
              )
            WHERE r.bill_id=v_payment.bill_id AND r.organization_id=_owner;
            IF v_payment.amount_cents>v_bill_amount-v_approved
            THEN RAISE EXCEPTION 'supplier_payment_over_budget'
              USING ERRCODE='23514'; END IF;
          END IF;
          INSERT INTO finance.supplier_payment_decisions(
            request_id,decision,review_note,reviewed_by)
          VALUES(_request,v_decision,_note,v_actor);
          INSERT INTO audit.security_events(
            id,actor_realm,occurred_at,actor_identity_id,action,outcome,
            organization_id,reason_code,subject_digest)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
            'admin.finance.supplier_payment.review','succeeded',_owner,
            v_decision,
            encode(sha256(convert_to(_owner||'|'||_request::text,'UTF8')),'hex'));
        END IF;
        RETURN jsonb_build_object('id',_request,'organization_id',_owner,
          'bill_id',v_payment.bill_id,'status',v_decision,
          'replay',v_replay,'bank_verified',false,
          'posted_to_general_ledger',false);
      END $review$;

      CREATE OR REPLACE FUNCTION finance.read_admin_supplier_payment_register(
        _digest text,_owner text,_month date
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $read$
      DECLARE v_actor uuid;
      BEGIN
        v_actor:=finance.authorize_admin_supplier_bills(_digest,_owner);
        IF _month IS NULL OR _month NOT BETWEEN
          DATE '2020-01-01' AND DATE '2100-12-01'
          OR extract(day FROM _month)<>1
        THEN RAISE EXCEPTION 'supplier_payment_month_invalid'
          USING ERRCODE='22023'; END IF;

        RETURN jsonb_build_object(
          'organization_id',_owner,'month',_month,'currency','EUR',
          'bank_verified',false,'is_general_ledger',false,
          'summary',(
            SELECT jsonb_build_object(
              'bill_count',count(*)::int,
              'obligations_cents',coalesce(sum(b.gross_amount_cents),0)::bigint,
              'approved_manual_cents',coalesce(sum(p.approved_cents),0)::bigint,
              'remaining_cents',coalesce(sum(
                b.gross_amount_cents-p.approved_cents),0)::bigint,
              'pending_evidence_cents',coalesce(sum(p.pending_cents),0)::bigint)
            FROM finance.supplier_bills b
            CROSS JOIN LATERAL (
              SELECT
                coalesce(sum(r.amount_cents) FILTER(
                  WHERE d.decision='approved_manual' AND NOT EXISTS (
                SELECT 1 FROM finance.supplier_review_reversal_requests rev
                JOIN finance.supplier_review_reversal_decisions revd
                  ON revd.request_id=rev.id AND revd.decision='approved_manual'
                WHERE rev.target_kind='payment' AND rev.target_id=r.id
              )),0)::bigint AS approved_cents,
                coalesce(sum(r.amount_cents) FILTER(
                  WHERE d.request_id IS NULL),0)::bigint AS pending_cents
              FROM finance.supplier_payment_requests r
              LEFT JOIN finance.supplier_payment_decisions d
                ON d.request_id=r.id
              WHERE r.bill_id=b.id AND r.organization_id=_owner
            ) p
            WHERE b.organization_id=_owner
              AND b.bill_date>=_month
              AND b.bill_date<(_month+interval '1 month')::date
          ),
          'bills',(
            SELECT coalesce(jsonb_agg(to_jsonb(rows)
              ORDER BY rows.bill_date DESC,rows.id),'[]'::jsonb)
            FROM (
              SELECT b.id,b.supplier_name,b.invoice_reference,b.bill_date,
                b.gross_amount_cents,b.city_id,b.location_id,
                p.approved_cents AS approved_manual_cents,
                b.gross_amount_cents-p.approved_cents AS remaining_cents,
                p.pending_cents AS pending_evidence_cents
              FROM finance.supplier_bills b
              CROSS JOIN LATERAL (
                SELECT
                  coalesce(sum(r.amount_cents) FILTER(
                    WHERE d.decision='approved_manual' AND NOT EXISTS (
                SELECT 1 FROM finance.supplier_review_reversal_requests rev
                JOIN finance.supplier_review_reversal_decisions revd
                  ON revd.request_id=rev.id AND revd.decision='approved_manual'
                WHERE rev.target_kind='payment' AND rev.target_id=r.id
              )),0)::bigint AS approved_cents,
                  coalesce(sum(r.amount_cents) FILTER(
                    WHERE d.request_id IS NULL),0)::bigint AS pending_cents
                FROM finance.supplier_payment_requests r
                LEFT JOIN finance.supplier_payment_decisions d
                  ON d.request_id=r.id
                WHERE r.bill_id=b.id AND r.organization_id=_owner
              ) p
              WHERE b.organization_id=_owner
                AND b.bill_date>=_month
                AND b.bill_date<(_month+interval '1 month')::date
              ORDER BY b.bill_date DESC,b.id LIMIT 100
            ) rows
          ),
          'requests',(
            SELECT coalesce(jsonb_agg(to_jsonb(rows)
              ORDER BY rows.requested_at DESC,rows.id),'[]'::jsonb)
            FROM (
              SELECT r.id,r.bill_id,r.amount_cents,r.payment_date,
                r.payment_method,r.evidence_reference,r.requested_at,
                r.requested_by,coalesce(d.decision,'pending') AS status,
                d.reviewed_at,d.review_note,d.reviewed_by,
                EXISTS (
                  SELECT 1 FROM finance.supplier_review_reversal_requests rev
                  JOIN finance.supplier_review_reversal_decisions revd
                    ON revd.request_id=rev.id AND revd.decision='approved_manual'
                  WHERE rev.target_kind='payment' AND rev.target_id=r.id
                ) AS reversed_manual
              FROM finance.supplier_payment_requests r
              JOIN finance.supplier_bills b ON b.id=r.bill_id
                AND b.organization_id=_owner
              LEFT JOIN finance.supplier_payment_decisions d
                ON d.request_id=r.id
              WHERE r.organization_id=_owner
                AND b.bill_date>=_month
                AND b.bill_date<(_month+interval '1 month')::date
              ORDER BY r.requested_at DESC,r.id LIMIT 200
            ) rows
          )
        );
      END $read$;

      CREATE OR REPLACE FUNCTION finance.check_supplier_payment_credit_ceiling()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $check$
      DECLARE v_bill uuid; v_gross bigint; v_existing bigint; v_credits bigint;
        v_amount bigint;
      BEGIN
        IF NEW.decision<>'approved_manual' THEN RETURN NEW; END IF;
        SELECT r.bill_id,r.amount_cents,b.gross_amount_cents
          INTO v_bill,v_amount,v_gross
        FROM finance.supplier_payment_requests r
        JOIN finance.supplier_bills b ON b.id=r.bill_id
          AND b.organization_id=r.organization_id
        WHERE r.id=NEW.request_id FOR UPDATE OF b;
        IF v_bill IS NULL THEN RAISE EXCEPTION 'supplier_payment_bill_missing'
          USING ERRCODE='23514'; END IF;
        SELECT coalesce(sum(c.amount_cents),0)::bigint INTO v_credits
          FROM finance.supplier_credit_requests c
          JOIN finance.supplier_credit_decisions d
            ON d.request_id=c.id AND d.decision='approved_manual' AND NOT EXISTS (
                SELECT 1 FROM finance.supplier_review_reversal_requests rev
                JOIN finance.supplier_review_reversal_decisions revd
                  ON revd.request_id=rev.id AND revd.decision='approved_manual'
                WHERE rev.target_kind='credit' AND rev.target_id=c.id
              )
          WHERE c.bill_id=v_bill;
        SELECT coalesce(sum(r.amount_cents),0)::bigint INTO v_existing
          FROM finance.supplier_payment_requests r
          JOIN finance.supplier_payment_decisions d
            ON d.request_id=r.id AND d.decision='approved_manual' AND NOT EXISTS (
                SELECT 1 FROM finance.supplier_review_reversal_requests rev
                JOIN finance.supplier_review_reversal_decisions revd
                  ON revd.request_id=rev.id AND revd.decision='approved_manual'
                WHERE rev.target_kind='payment' AND rev.target_id=r.id
              )
          WHERE r.bill_id=v_bill;
        IF v_amount>v_gross-v_existing-v_credits
        THEN RAISE EXCEPTION 'supplier_payment_exceeds_net_bill'
          USING ERRCODE='23514'; END IF;
        RETURN NEW;
      END $check$;

      CREATE OR REPLACE FUNCTION finance.review_admin_supplier_credit_request(
        _digest text,_owner text,_request uuid,_approve boolean,_note text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $review$
      DECLARE v_actor uuid; v_role text;
        v_credit finance.supplier_credit_requests%ROWTYPE;
        v_previous finance.supplier_credit_decisions%ROWTYPE;
        v_gross bigint; v_paid bigint; v_credits bigint;
        v_decision text; v_replay boolean:=false;
      BEGIN
        v_actor:=finance.authorize_admin_supplier_bills(_digest,_owner);
        SELECT a.role INTO v_role FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          WHERE s.token_digest=_digest AND s.admin_account_id=v_actor
            AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp();
        IF v_role<>'super_admin' OR _request IS NULL OR _approve IS NULL
          OR _note IS NULL OR length(_note) NOT BETWEEN 8 AND 180
          OR _note<>btrim(_note) OR _note ~ '[[:cntrl:]]'
        THEN RAISE EXCEPTION 'supplier_credit_review_denied'
          USING ERRCODE='42501'; END IF;
        SELECT c.* INTO v_credit FROM finance.supplier_credit_requests c
        WHERE c.id=_request AND c.organization_id=_owner FOR SHARE;
        IF NOT FOUND OR v_credit.requested_by=v_actor
        THEN RAISE EXCEPTION 'supplier_credit_four_eyes_denied'
          USING ERRCODE='42501'; END IF;
        -- Serializes with all supplier-payment and credit approvals.
        SELECT b.gross_amount_cents INTO v_gross FROM finance.supplier_bills b
          WHERE b.id=v_credit.bill_id AND b.organization_id=_owner FOR UPDATE;
        IF NOT FOUND THEN RAISE EXCEPTION 'supplier_credit_bill_missing'
          USING ERRCODE='42501'; END IF;
        v_decision:=CASE WHEN _approve THEN 'approved_manual' ELSE 'rejected' END;
        SELECT d.* INTO v_previous FROM finance.supplier_credit_decisions d
          WHERE d.request_id=_request FOR SHARE;
        IF FOUND THEN
          IF v_previous.decision<>v_decision OR v_previous.review_note<>_note
            OR v_previous.reviewed_by<>v_actor
          THEN RAISE EXCEPTION 'supplier_credit_decision_conflict'
            USING ERRCODE='23505'; END IF;
          v_replay:=true;
        ELSE
          IF _approve THEN
            SELECT coalesce(sum(r.amount_cents),0)::bigint INTO v_paid
              FROM finance.supplier_payment_requests r
              JOIN finance.supplier_payment_decisions d
                ON d.request_id=r.id AND d.decision='approved_manual' AND NOT EXISTS (
                SELECT 1 FROM finance.supplier_review_reversal_requests rev
                JOIN finance.supplier_review_reversal_decisions revd
                  ON revd.request_id=rev.id AND revd.decision='approved_manual'
                WHERE rev.target_kind='payment' AND rev.target_id=r.id
              )
              WHERE r.bill_id=v_credit.bill_id AND r.organization_id=_owner;
            SELECT coalesce(sum(c.amount_cents),0)::bigint INTO v_credits
              FROM finance.supplier_credit_requests c
              JOIN finance.supplier_credit_decisions d
                ON d.request_id=c.id AND d.decision='approved_manual' AND NOT EXISTS (
                SELECT 1 FROM finance.supplier_review_reversal_requests rev
                JOIN finance.supplier_review_reversal_decisions revd
                  ON revd.request_id=rev.id AND revd.decision='approved_manual'
                WHERE rev.target_kind='credit' AND rev.target_id=c.id
              )
              WHERE c.bill_id=v_credit.bill_id AND c.organization_id=_owner;
            IF v_credit.amount_cents>v_gross-v_paid-v_credits
            THEN RAISE EXCEPTION 'supplier_credit_exceeds_unpaid_bill'
              USING ERRCODE='23514'; END IF;
          END IF;
          INSERT INTO finance.supplier_credit_decisions(
            request_id,decision,review_note,reviewed_by)
          VALUES(_request,v_decision,_note,v_actor);
          INSERT INTO audit.security_events(
            id,actor_realm,occurred_at,actor_identity_id,action,outcome,
            organization_id,reason_code,subject_digest)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
            'admin.finance.supplier_credit.review','succeeded',_owner,
            v_decision,
            encode(sha256(convert_to(_owner||'|'||_request::text,'UTF8')),'hex'));
        END IF;
        RETURN jsonb_build_object('id',_request,'bill_id',v_credit.bill_id,
          'organization_id',_owner,'status',v_decision,
          'replay',v_replay,'bank_refund_recorded',false,
          'posted_to_general_ledger',false);
      END $review$;

      CREATE OR REPLACE FUNCTION finance.read_admin_supplier_payable_position(
        _digest text,_owner text,_month date
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $read$
      DECLARE v_report jsonb; v_bills jsonb; v_credits jsonb; v_reversals jsonb;
        v_gross bigint; v_paid bigint; v_credit_sum bigint; v_pending bigint;
      BEGIN
        v_report:=finance.read_admin_supplier_payment_register(
          _digest,_owner,_month);
        v_gross:=(v_report#>>'{summary,obligations_cents}')::bigint;
        v_paid:=(v_report#>>'{summary,approved_manual_cents}')::bigint;
        SELECT coalesce(sum(c.amount_cents) FILTER(
            WHERE d.decision='approved_manual' AND NOT EXISTS (
                SELECT 1 FROM finance.supplier_review_reversal_requests rev
                JOIN finance.supplier_review_reversal_decisions revd
                  ON revd.request_id=rev.id AND revd.decision='approved_manual'
                WHERE rev.target_kind='credit' AND rev.target_id=c.id
              )),0)::bigint,
          coalesce(sum(c.amount_cents) FILTER(
            WHERE d.request_id IS NULL),0)::bigint
          INTO v_credit_sum,v_pending
        FROM finance.supplier_credit_requests c
        JOIN finance.supplier_bills b ON b.id=c.bill_id
          AND b.organization_id=_owner
        LEFT JOIN finance.supplier_credit_decisions d ON d.request_id=c.id
        WHERE c.organization_id=_owner AND b.bill_date>=_month
          AND b.bill_date<(_month+interval '1 month')::date;
        SELECT coalesce(jsonb_agg(
          item.value||jsonb_build_object(
            'approved_credit_cents',totals.approved,
            'net_payable_cents',(item.value->>'gross_amount_cents')::bigint-totals.approved,
            'remaining_cents',(item.value->>'remaining_cents')::bigint-totals.approved)
          ORDER BY item.ordinality),'[]'::jsonb)
          INTO v_bills
        FROM jsonb_array_elements(v_report->'bills')
          WITH ORDINALITY AS item(value,ordinality)
        CROSS JOIN LATERAL (
          SELECT coalesce(sum(c.amount_cents),0)::bigint AS approved
          FROM finance.supplier_credit_requests c
          JOIN finance.supplier_credit_decisions d
            ON d.request_id=c.id AND d.decision='approved_manual' AND NOT EXISTS (
                SELECT 1 FROM finance.supplier_review_reversal_requests rev
                JOIN finance.supplier_review_reversal_decisions revd
                  ON revd.request_id=rev.id AND revd.decision='approved_manual'
                WHERE rev.target_kind='credit' AND rev.target_id=c.id
              )
          WHERE c.bill_id=(item.value->>'id')::uuid
            AND c.organization_id=_owner
        ) totals;
        SELECT coalesce(jsonb_agg(to_jsonb(rows)
          ORDER BY rows.requested_at DESC,rows.id),'[]'::jsonb)
          INTO v_credits
        FROM (
          SELECT c.id,c.bill_id,c.credit_reference,c.credit_date,
            c.amount_cents,c.reason,c.requested_by,c.requested_at,
            coalesce(d.decision,'pending') AS status,
            d.reviewed_at,d.review_note,d.reviewed_by,
            EXISTS (
              SELECT 1 FROM finance.supplier_review_reversal_requests rev
              JOIN finance.supplier_review_reversal_decisions revd
                ON revd.request_id=rev.id AND revd.decision='approved_manual'
              WHERE rev.target_kind='credit' AND rev.target_id=c.id
            ) AS reversed_manual
          FROM finance.supplier_credit_requests c
          JOIN finance.supplier_bills b
            ON b.id=c.bill_id AND b.organization_id=_owner
          LEFT JOIN finance.supplier_credit_decisions d ON d.request_id=c.id
          WHERE c.organization_id=_owner
            AND b.bill_date>=_month
            AND b.bill_date<(_month+interval '1 month')::date
          ORDER BY c.requested_at DESC,c.id LIMIT 200
        ) rows;
        SELECT coalesce(jsonb_agg(to_jsonb(rows)
          ORDER BY rows.requested_at DESC,rows.id),'[]'::jsonb)
          INTO v_reversals
        FROM (
          SELECT rev.id,rev.target_kind,rev.target_id,rev.reason,
            rev.requested_by,rev.requested_at,
            coalesce(revd.decision,'pending') AS status,
            revd.reviewed_by,revd.reviewed_at,revd.review_note
          FROM finance.supplier_review_reversal_requests rev
          JOIN finance.supplier_bills b ON b.id=rev.bill_id
            AND b.organization_id=_owner
          LEFT JOIN finance.supplier_review_reversal_decisions revd
            ON revd.request_id=rev.id
          WHERE rev.organization_id=_owner
            AND b.bill_date>=_month
            AND b.bill_date<(_month+interval '1 month')::date
          ORDER BY rev.requested_at DESC,rev.id LIMIT 200
        ) rows;
        IF v_credit_sum>v_gross-v_paid
        THEN RAISE EXCEPTION 'supplier_payable_register_inconsistent'
          USING ERRCODE='23514'; END IF;
        RETURN v_report||jsonb_build_object(
          'summary',(v_report->'summary')||jsonb_build_object(
            'approved_credit_cents',v_credit_sum,
            'pending_credit_cents',v_pending,
            'net_payables_cents',v_gross-v_credit_sum,
            'remaining_cents',v_gross-v_credit_sum-v_paid),
          'bills',v_bills,'credit_notes',v_credits,
          'reversals',v_reversals,
          'bank_refund_recorded',false);
      END $read$;

      """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
      CREATE OR REPLACE FUNCTION finance.review_admin_supplier_payment_request(
        _digest text,_owner text,_request uuid,_approve boolean,_note text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $review$
      DECLARE
        v_actor uuid; v_role text;
        v_payment finance.supplier_payment_requests%ROWTYPE;
        v_previous finance.supplier_payment_decisions%ROWTYPE;
        v_bill_amount bigint; v_approved bigint;
        v_decision text; v_replay boolean:=false;
      BEGIN
        v_actor:=finance.authorize_admin_supplier_bills(_digest,_owner);
        SELECT a.role INTO v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          WHERE s.token_digest=_digest AND s.admin_account_id=v_actor
            AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp();
        IF v_role<>'super_admin'
          OR _request IS NULL OR _approve IS NULL OR _note IS NULL
          OR length(_note) NOT BETWEEN 8 AND 180
          OR _note<>btrim(_note) OR _note ~ '[[:cntrl:]]'
        THEN RAISE EXCEPTION 'supplier_payment_review_denied'
          USING ERRCODE='42501'; END IF;

        SELECT r.* INTO v_payment FROM finance.supplier_payment_requests r
        WHERE r.id=_request AND r.organization_id=_owner FOR SHARE;
        IF NOT FOUND OR v_payment.requested_by=v_actor
        THEN RAISE EXCEPTION 'supplier_payment_four_eyes_denied'
          USING ERRCODE='42501'; END IF;
        -- A lock on the permanent bill row serializes all approvals for it.
        SELECT b.gross_amount_cents INTO v_bill_amount
          FROM finance.supplier_bills b
          WHERE b.id=v_payment.bill_id AND b.organization_id=_owner
          FOR UPDATE;
        IF NOT FOUND THEN RAISE EXCEPTION 'supplier_payment_bill_missing'
          USING ERRCODE='42501'; END IF;

        v_decision:=CASE WHEN _approve THEN 'approved_manual' ELSE 'rejected' END;
        SELECT d.* INTO v_previous FROM finance.supplier_payment_decisions d
        WHERE d.request_id=_request FOR SHARE;
        IF FOUND THEN
          IF v_previous.decision<>v_decision OR v_previous.review_note<>_note
            OR v_previous.reviewed_by<>v_actor
          THEN RAISE EXCEPTION 'supplier_payment_decision_conflict'
            USING ERRCODE='23505'; END IF;
          v_replay:=true;
        ELSE
          IF _approve THEN
            SELECT coalesce(sum(r.amount_cents),0)::bigint INTO v_approved
            FROM finance.supplier_payment_requests r
            JOIN finance.supplier_payment_decisions d
              ON d.request_id=r.id AND d.decision='approved_manual'
            WHERE r.bill_id=v_payment.bill_id AND r.organization_id=_owner;
            IF v_payment.amount_cents>v_bill_amount-v_approved
            THEN RAISE EXCEPTION 'supplier_payment_over_budget'
              USING ERRCODE='23514'; END IF;
          END IF;
          INSERT INTO finance.supplier_payment_decisions(
            request_id,decision,review_note,reviewed_by)
          VALUES(_request,v_decision,_note,v_actor);
          INSERT INTO audit.security_events(
            id,actor_realm,occurred_at,actor_identity_id,action,outcome,
            organization_id,reason_code,subject_digest)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
            'admin.finance.supplier_payment.review','succeeded',_owner,
            v_decision,
            encode(sha256(convert_to(_owner||'|'||_request::text,'UTF8')),'hex'));
        END IF;
        RETURN jsonb_build_object('id',_request,'organization_id',_owner,
          'bill_id',v_payment.bill_id,'status',v_decision,
          'replay',v_replay,'bank_verified',false,
          'posted_to_general_ledger',false);
      END $review$;

      CREATE OR REPLACE FUNCTION finance.read_admin_supplier_payment_register(
        _digest text,_owner text,_month date
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $read$
      DECLARE v_actor uuid;
      BEGIN
        v_actor:=finance.authorize_admin_supplier_bills(_digest,_owner);
        IF _month IS NULL OR _month NOT BETWEEN
          DATE '2020-01-01' AND DATE '2100-12-01'
          OR extract(day FROM _month)<>1
        THEN RAISE EXCEPTION 'supplier_payment_month_invalid'
          USING ERRCODE='22023'; END IF;

        RETURN jsonb_build_object(
          'organization_id',_owner,'month',_month,'currency','EUR',
          'bank_verified',false,'is_general_ledger',false,
          'summary',(
            SELECT jsonb_build_object(
              'bill_count',count(*)::int,
              'obligations_cents',coalesce(sum(b.gross_amount_cents),0)::bigint,
              'approved_manual_cents',coalesce(sum(p.approved_cents),0)::bigint,
              'remaining_cents',coalesce(sum(
                b.gross_amount_cents-p.approved_cents),0)::bigint,
              'pending_evidence_cents',coalesce(sum(p.pending_cents),0)::bigint)
            FROM finance.supplier_bills b
            CROSS JOIN LATERAL (
              SELECT
                coalesce(sum(r.amount_cents) FILTER(
                  WHERE d.decision='approved_manual'),0)::bigint AS approved_cents,
                coalesce(sum(r.amount_cents) FILTER(
                  WHERE d.request_id IS NULL),0)::bigint AS pending_cents
              FROM finance.supplier_payment_requests r
              LEFT JOIN finance.supplier_payment_decisions d
                ON d.request_id=r.id
              WHERE r.bill_id=b.id AND r.organization_id=_owner
            ) p
            WHERE b.organization_id=_owner
              AND b.bill_date>=_month
              AND b.bill_date<(_month+interval '1 month')::date
          ),
          'bills',(
            SELECT coalesce(jsonb_agg(to_jsonb(rows)
              ORDER BY rows.bill_date DESC,rows.id),'[]'::jsonb)
            FROM (
              SELECT b.id,b.supplier_name,b.invoice_reference,b.bill_date,
                b.gross_amount_cents,b.city_id,b.location_id,
                p.approved_cents AS approved_manual_cents,
                b.gross_amount_cents-p.approved_cents AS remaining_cents,
                p.pending_cents AS pending_evidence_cents
              FROM finance.supplier_bills b
              CROSS JOIN LATERAL (
                SELECT
                  coalesce(sum(r.amount_cents) FILTER(
                    WHERE d.decision='approved_manual'),0)::bigint AS approved_cents,
                  coalesce(sum(r.amount_cents) FILTER(
                    WHERE d.request_id IS NULL),0)::bigint AS pending_cents
                FROM finance.supplier_payment_requests r
                LEFT JOIN finance.supplier_payment_decisions d
                  ON d.request_id=r.id
                WHERE r.bill_id=b.id AND r.organization_id=_owner
              ) p
              WHERE b.organization_id=_owner
                AND b.bill_date>=_month
                AND b.bill_date<(_month+interval '1 month')::date
              ORDER BY b.bill_date DESC,b.id LIMIT 100
            ) rows
          ),
          'requests',(
            SELECT coalesce(jsonb_agg(to_jsonb(rows)
              ORDER BY rows.requested_at DESC,rows.id),'[]'::jsonb)
            FROM (
              SELECT r.id,r.bill_id,r.amount_cents,r.payment_date,
                r.payment_method,r.evidence_reference,r.requested_at,
                r.requested_by,coalesce(d.decision,'pending') AS status,
                d.reviewed_at,d.review_note
              FROM finance.supplier_payment_requests r
              JOIN finance.supplier_bills b ON b.id=r.bill_id
                AND b.organization_id=_owner
              LEFT JOIN finance.supplier_payment_decisions d
                ON d.request_id=r.id
              WHERE r.organization_id=_owner
                AND b.bill_date>=_month
                AND b.bill_date<(_month+interval '1 month')::date
              ORDER BY r.requested_at DESC,r.id LIMIT 200
            ) rows
          )
        );
      END $read$;

      CREATE OR REPLACE FUNCTION finance.check_supplier_payment_credit_ceiling()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $check$
      DECLARE v_bill uuid; v_gross bigint; v_existing bigint; v_credits bigint;
        v_amount bigint;
      BEGIN
        IF NEW.decision<>'approved_manual' THEN RETURN NEW; END IF;
        SELECT r.bill_id,r.amount_cents,b.gross_amount_cents
          INTO v_bill,v_amount,v_gross
        FROM finance.supplier_payment_requests r
        JOIN finance.supplier_bills b ON b.id=r.bill_id
          AND b.organization_id=r.organization_id
        WHERE r.id=NEW.request_id FOR UPDATE OF b;
        IF v_bill IS NULL THEN RAISE EXCEPTION 'supplier_payment_bill_missing'
          USING ERRCODE='23514'; END IF;
        SELECT coalesce(sum(c.amount_cents),0)::bigint INTO v_credits
          FROM finance.supplier_credit_requests c
          JOIN finance.supplier_credit_decisions d
            ON d.request_id=c.id AND d.decision='approved_manual'
          WHERE c.bill_id=v_bill;
        SELECT coalesce(sum(r.amount_cents),0)::bigint INTO v_existing
          FROM finance.supplier_payment_requests r
          JOIN finance.supplier_payment_decisions d
            ON d.request_id=r.id AND d.decision='approved_manual'
          WHERE r.bill_id=v_bill;
        IF v_amount>v_gross-v_existing-v_credits
        THEN RAISE EXCEPTION 'supplier_payment_exceeds_net_bill'
          USING ERRCODE='23514'; END IF;
        RETURN NEW;
      END $check$;

      CREATE OR REPLACE FUNCTION finance.review_admin_supplier_credit_request(
        _digest text,_owner text,_request uuid,_approve boolean,_note text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $review$
      DECLARE v_actor uuid; v_role text;
        v_credit finance.supplier_credit_requests%ROWTYPE;
        v_previous finance.supplier_credit_decisions%ROWTYPE;
        v_gross bigint; v_paid bigint; v_credits bigint;
        v_decision text; v_replay boolean:=false;
      BEGIN
        v_actor:=finance.authorize_admin_supplier_bills(_digest,_owner);
        SELECT a.role INTO v_role FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          WHERE s.token_digest=_digest AND s.admin_account_id=v_actor
            AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp();
        IF v_role<>'super_admin' OR _request IS NULL OR _approve IS NULL
          OR _note IS NULL OR length(_note) NOT BETWEEN 8 AND 180
          OR _note<>btrim(_note) OR _note ~ '[[:cntrl:]]'
        THEN RAISE EXCEPTION 'supplier_credit_review_denied'
          USING ERRCODE='42501'; END IF;
        SELECT c.* INTO v_credit FROM finance.supplier_credit_requests c
        WHERE c.id=_request AND c.organization_id=_owner FOR SHARE;
        IF NOT FOUND OR v_credit.requested_by=v_actor
        THEN RAISE EXCEPTION 'supplier_credit_four_eyes_denied'
          USING ERRCODE='42501'; END IF;
        -- Serializes with all supplier-payment and credit approvals.
        SELECT b.gross_amount_cents INTO v_gross FROM finance.supplier_bills b
          WHERE b.id=v_credit.bill_id AND b.organization_id=_owner FOR UPDATE;
        IF NOT FOUND THEN RAISE EXCEPTION 'supplier_credit_bill_missing'
          USING ERRCODE='42501'; END IF;
        v_decision:=CASE WHEN _approve THEN 'approved_manual' ELSE 'rejected' END;
        SELECT d.* INTO v_previous FROM finance.supplier_credit_decisions d
          WHERE d.request_id=_request FOR SHARE;
        IF FOUND THEN
          IF v_previous.decision<>v_decision OR v_previous.review_note<>_note
            OR v_previous.reviewed_by<>v_actor
          THEN RAISE EXCEPTION 'supplier_credit_decision_conflict'
            USING ERRCODE='23505'; END IF;
          v_replay:=true;
        ELSE
          IF _approve THEN
            SELECT coalesce(sum(r.amount_cents),0)::bigint INTO v_paid
              FROM finance.supplier_payment_requests r
              JOIN finance.supplier_payment_decisions d
                ON d.request_id=r.id AND d.decision='approved_manual'
              WHERE r.bill_id=v_credit.bill_id AND r.organization_id=_owner;
            SELECT coalesce(sum(c.amount_cents),0)::bigint INTO v_credits
              FROM finance.supplier_credit_requests c
              JOIN finance.supplier_credit_decisions d
                ON d.request_id=c.id AND d.decision='approved_manual'
              WHERE c.bill_id=v_credit.bill_id AND c.organization_id=_owner;
            IF v_credit.amount_cents>v_gross-v_paid-v_credits
            THEN RAISE EXCEPTION 'supplier_credit_exceeds_unpaid_bill'
              USING ERRCODE='23514'; END IF;
          END IF;
          INSERT INTO finance.supplier_credit_decisions(
            request_id,decision,review_note,reviewed_by)
          VALUES(_request,v_decision,_note,v_actor);
          INSERT INTO audit.security_events(
            id,actor_realm,occurred_at,actor_identity_id,action,outcome,
            organization_id,reason_code,subject_digest)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
            'admin.finance.supplier_credit.review','succeeded',_owner,
            v_decision,
            encode(sha256(convert_to(_owner||'|'||_request::text,'UTF8')),'hex'));
        END IF;
        RETURN jsonb_build_object('id',_request,'bill_id',v_credit.bill_id,
          'organization_id',_owner,'status',v_decision,
          'replay',v_replay,'bank_refund_recorded',false,
          'posted_to_general_ledger',false);
      END $review$;

      CREATE OR REPLACE FUNCTION finance.read_admin_supplier_payable_position(
        _digest text,_owner text,_month date
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $read$
      DECLARE v_report jsonb; v_bills jsonb; v_credits jsonb;
        v_gross bigint; v_paid bigint; v_credit_sum bigint; v_pending bigint;
      BEGIN
        v_report:=finance.read_admin_supplier_payment_register(
          _digest,_owner,_month);
        v_gross:=(v_report#>>'{summary,obligations_cents}')::bigint;
        v_paid:=(v_report#>>'{summary,approved_manual_cents}')::bigint;
        SELECT coalesce(sum(c.amount_cents) FILTER(
            WHERE d.decision='approved_manual'),0)::bigint,
          coalesce(sum(c.amount_cents) FILTER(
            WHERE d.request_id IS NULL),0)::bigint
          INTO v_credit_sum,v_pending
        FROM finance.supplier_credit_requests c
        JOIN finance.supplier_bills b ON b.id=c.bill_id
          AND b.organization_id=_owner
        LEFT JOIN finance.supplier_credit_decisions d ON d.request_id=c.id
        WHERE c.organization_id=_owner AND b.bill_date>=_month
          AND b.bill_date<(_month+interval '1 month')::date;
        SELECT coalesce(jsonb_agg(
          item.value||jsonb_build_object(
            'approved_credit_cents',totals.approved,
            'net_payable_cents',(item.value->>'gross_amount_cents')::bigint-totals.approved,
            'remaining_cents',(item.value->>'remaining_cents')::bigint-totals.approved)
          ORDER BY item.ordinality),'[]'::jsonb)
          INTO v_bills
        FROM jsonb_array_elements(v_report->'bills')
          WITH ORDINALITY AS item(value,ordinality)
        CROSS JOIN LATERAL (
          SELECT coalesce(sum(c.amount_cents),0)::bigint AS approved
          FROM finance.supplier_credit_requests c
          JOIN finance.supplier_credit_decisions d
            ON d.request_id=c.id AND d.decision='approved_manual'
          WHERE c.bill_id=(item.value->>'id')::uuid
            AND c.organization_id=_owner
        ) totals;
        SELECT coalesce(jsonb_agg(to_jsonb(rows)
          ORDER BY rows.requested_at DESC,rows.id),'[]'::jsonb)
          INTO v_credits
        FROM (
          SELECT c.id,c.bill_id,c.credit_reference,c.credit_date,
            c.amount_cents,c.reason,c.requested_by,c.requested_at,
            coalesce(d.decision,'pending') AS status,
            d.reviewed_at,d.review_note
          FROM finance.supplier_credit_requests c
          JOIN finance.supplier_bills b
            ON b.id=c.bill_id AND b.organization_id=_owner
          LEFT JOIN finance.supplier_credit_decisions d ON d.request_id=c.id
          WHERE c.organization_id=_owner
            AND b.bill_date>=_month
            AND b.bill_date<(_month+interval '1 month')::date
          ORDER BY c.requested_at DESC,c.id LIMIT 200
        ) rows;
        IF v_credit_sum>v_gross-v_paid
        THEN RAISE EXCEPTION 'supplier_payable_register_inconsistent'
          USING ERRCODE='23514'; END IF;
        RETURN v_report||jsonb_build_object(
          'summary',(v_report->'summary')||jsonb_build_object(
            'approved_credit_cents',v_credit_sum,
            'pending_credit_cents',v_pending,
            'net_payables_cents',v_gross-v_credit_sum,
            'remaining_cents',v_gross-v_credit_sum-v_paid),
          'bills',v_bills,'credit_notes',v_credits,
          'bank_refund_recorded',false);
      END $read$;

          DROP FUNCTION finance.review_admin_supplier_approval_reversal(
            text,text,uuid,boolean,text);
          DROP FUNCTION finance.create_admin_supplier_approval_reversal(
            text,text,uuid,text,uuid,text);
          DROP TABLE finance.supplier_review_reversal_decisions;
          DROP TABLE finance.supplier_review_reversal_requests;
          """);
}
