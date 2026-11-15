using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// A supplier payment is a requested manual-evidence entry until a different,
/// authorized Super Admin reviews it. No provider call, automatic cash movement,
/// VAT posting, or general-ledger posting is performed by this migration.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115265000_FinanceSupplierPaymentReview")]
public sealed class FinanceSupplierPaymentReview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE finance.supplier_payment_requests (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        organization_id varchar(32) NOT NULL
          CHECK(organization_id IN ('lefteria_fc','lefteria_fc_academy')),
        client_request_id uuid NOT NULL
          CHECK(client_request_id<>'00000000-0000-0000-0000-000000000000'::uuid),
        bill_id uuid NOT NULL
          REFERENCES finance.supplier_bills(id) ON DELETE RESTRICT,
        amount_cents bigint NOT NULL
          CHECK(amount_cents BETWEEN 1 AND 1000000000),
        payment_date date NOT NULL
          CHECK(payment_date BETWEEN DATE '2020-01-01' AND DATE '2100-12-31'),
        payment_method varchar(24) NOT NULL CHECK(payment_method IN
          ('bank_transfer','cash','external_card')),
        evidence_reference varchar(140) NOT NULL
          CHECK(evidence_reference=btrim(evidence_reference)
            AND length(evidence_reference) BETWEEN 6 AND 140
            AND evidence_reference !~ '[[:cntrl:]]'),
        requested_by uuid NOT NULL
          REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        requested_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        UNIQUE(organization_id,client_request_id)
      );
      CREATE INDEX ix_supplier_payment_requests_bill
        ON finance.supplier_payment_requests(bill_id,requested_at,id);
      CREATE INDEX ix_supplier_payment_requests_owner
        ON finance.supplier_payment_requests(organization_id,requested_at,id);

      CREATE TABLE finance.supplier_payment_decisions (
        request_id uuid PRIMARY KEY
          REFERENCES finance.supplier_payment_requests(id) ON DELETE RESTRICT,
        decision varchar(24) NOT NULL
          CHECK(decision IN ('approved_manual','rejected')),
        review_note varchar(180) NOT NULL
          CHECK(review_note=btrim(review_note)
            AND length(review_note) BETWEEN 8 AND 180
            AND review_note !~ '[[:cntrl:]]'),
        reviewed_by uuid NOT NULL
          REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        reviewed_at timestamptz NOT NULL DEFAULT clock_timestamp()
      );
      REVOKE ALL ON finance.supplier_payment_requests,
        finance.supplier_payment_decisions FROM PUBLIC;
      ALTER TABLE finance.supplier_payment_requests ENABLE ROW LEVEL SECURITY;
      ALTER TABLE finance.supplier_payment_decisions ENABLE ROW LEVEL SECURITY;

      CREATE FUNCTION finance.reject_supplier_payment_mutation()
      RETURNS trigger LANGUAGE plpgsql VOLATILE
      SET search_path=pg_catalog,pg_temp AS $immutable$
      BEGIN
        RAISE EXCEPTION 'supplier_payment_immutable'
          USING ERRCODE='42501';
      END $immutable$;
      REVOKE ALL ON FUNCTION finance.reject_supplier_payment_mutation() FROM PUBLIC;
      CREATE TRIGGER trg_supplier_payment_request_immutable
        BEFORE UPDATE OR DELETE ON finance.supplier_payment_requests
        FOR EACH ROW EXECUTE FUNCTION finance.reject_supplier_payment_mutation();
      CREATE TRIGGER trg_supplier_payment_decision_immutable
        BEFORE UPDATE OR DELETE ON finance.supplier_payment_decisions
        FOR EACH ROW EXECUTE FUNCTION finance.reject_supplier_payment_mutation();

      -- This is an evidence request, NOT a paid transaction. Original invoice
      -- and its issue-time organization / Academy site can never be rewritten.
      CREATE FUNCTION finance.create_admin_supplier_payment_request(
        _digest text,_owner text,_request uuid,_bill uuid,_cents bigint,
        _date date,_method text,_evidence text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $create$
      DECLARE
        v_actor uuid; v_id uuid; v_replay boolean:=false;
        v_bill_amount bigint; v_bill_date date;
      BEGIN
        v_actor:=finance.authorize_admin_supplier_bills(_digest,_owner);
        IF _request IS NULL OR _request='00000000-0000-0000-0000-000000000000'::uuid
          OR _bill IS NULL OR _bill='00000000-0000-0000-0000-000000000000'::uuid
          OR _cents IS NULL OR _cents NOT BETWEEN 1 AND 1000000000
          OR _date IS NULL OR _date NOT BETWEEN DATE '2020-01-01' AND
            (clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date
          OR _method IS NULL OR _method NOT IN
            ('bank_transfer','cash','external_card')
          OR _evidence IS NULL OR length(_evidence) NOT BETWEEN 6 AND 140
          OR _evidence<>btrim(_evidence) OR _evidence ~ '[[:cntrl:]]'
        THEN RAISE EXCEPTION 'supplier_payment_request_invalid'
          USING ERRCODE='22023'; END IF;

        SELECT b.gross_amount_cents,b.bill_date
          INTO v_bill_amount,v_bill_date
        FROM finance.supplier_bills b
        WHERE b.id=_bill AND b.organization_id=_owner
        FOR SHARE;
        IF v_bill_amount IS NULL OR _cents>v_bill_amount OR _date<v_bill_date
        THEN RAISE EXCEPTION 'supplier_payment_wrong_bill_or_amount'
          USING ERRCODE='22023'; END IF;

        INSERT INTO finance.supplier_payment_requests(
          organization_id,client_request_id,bill_id,amount_cents,
          payment_date,payment_method,evidence_reference,requested_by)
        VALUES(_owner,_request,_bill,_cents,_date,_method,_evidence,v_actor)
        ON CONFLICT(organization_id,client_request_id) DO NOTHING
        RETURNING id INTO v_id;
        IF v_id IS NULL THEN
          SELECT r.id INTO v_id FROM finance.supplier_payment_requests r
          WHERE r.organization_id=_owner AND r.client_request_id=_request
            AND r.bill_id=_bill AND r.amount_cents=_cents
            AND r.payment_date=_date AND r.payment_method=_method
            AND r.evidence_reference=_evidence AND r.requested_by=v_actor;
          IF v_id IS NULL THEN
            RAISE EXCEPTION 'supplier_payment_request_conflict'
              USING ERRCODE='23505'; END IF;
          v_replay:=true;
        ELSE
          INSERT INTO audit.security_events(
            id,actor_realm,occurred_at,actor_identity_id,action,outcome,
            organization_id,reason_code,subject_digest)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
            'admin.finance.supplier_payment.request','succeeded',_owner,
            'unverified_manual_evidence',
            encode(sha256(convert_to(_owner||'|'||v_id::text,'UTF8')),'hex'));
        END IF;
        RETURN jsonb_build_object('id',v_id,'organization_id',_owner,
          'bill_id',_bill,'status',coalesce((SELECT d.decision
            FROM finance.supplier_payment_decisions d
            WHERE d.request_id=v_id),'pending'),'replay',v_replay,
          'bank_verified',false,'posted_to_general_ledger',false);
      END $create$;
      REVOKE ALL ON FUNCTION finance.create_admin_supplier_payment_request(
        text,text,uuid,uuid,bigint,date,text,text) FROM PUBLIC;

      -- Four-eyes: approval only by a different Super Admin after checking
      -- independent off-system payment evidence. Manual approval does not
      -- assert that a bank feed, provider, or payment processor verified it.
      -- Bill row lock serializes concurrent reviews, preventing overpayment.
      CREATE FUNCTION finance.review_admin_supplier_payment_request(
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
      REVOKE ALL ON FUNCTION finance.review_admin_supplier_payment_request(
        text,text,uuid,boolean,text) FROM PUBLIC;

      CREATE FUNCTION finance.read_admin_supplier_payment_register(
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
      REVOKE ALL ON FUNCTION finance.read_admin_supplier_payment_register(
        text,text,date) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          DROP FUNCTION finance.read_admin_supplier_payment_register(text,text,date);
          DROP FUNCTION finance.review_admin_supplier_payment_request(
            text,text,uuid,boolean,text);
          DROP FUNCTION finance.create_admin_supplier_payment_request(
            text,text,uuid,uuid,bigint,date,text,text);
          DROP TABLE finance.supplier_payment_decisions;
          DROP TABLE finance.supplier_payment_requests;
          DROP FUNCTION finance.reject_supplier_payment_mutation();
          """);
}
