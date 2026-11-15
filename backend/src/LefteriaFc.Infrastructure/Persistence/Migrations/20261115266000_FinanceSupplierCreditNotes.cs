using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Supplier-issued credit notes, with immutable approval decisions and
/// adjusted unpaid liabilities. Does not move money or post tax/GL entries.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115266000_FinanceSupplierCreditNotes")]
public sealed class FinanceSupplierCreditNotes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE finance.supplier_credit_requests (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        organization_id varchar(32) NOT NULL
          CHECK(organization_id IN ('lefteria_fc','lefteria_fc_academy')),
        client_request_id uuid NOT NULL
          CHECK(client_request_id<>'00000000-0000-0000-0000-000000000000'::uuid),
        bill_id uuid NOT NULL REFERENCES finance.supplier_bills(id) ON DELETE RESTRICT,
        credit_reference varchar(80) NOT NULL CHECK(
          credit_reference=btrim(credit_reference)
          AND length(credit_reference) BETWEEN 4 AND 80
          AND credit_reference !~ '[[:cntrl:]]'),
        credit_date date NOT NULL CHECK(
          credit_date BETWEEN DATE '2020-01-01' AND DATE '2100-12-31'),
        amount_cents bigint NOT NULL CHECK(amount_cents BETWEEN 1 AND 1000000000),
        reason varchar(180) NOT NULL CHECK(
          reason=btrim(reason) AND length(reason) BETWEEN 8 AND 180
          AND reason !~ '[[:cntrl:]]'),
        requested_by uuid NOT NULL REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        requested_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        UNIQUE(organization_id,client_request_id)
      );
      CREATE UNIQUE INDEX ux_supplier_credit_bill_reference
        ON finance.supplier_credit_requests(bill_id,lower(credit_reference));
      CREATE INDEX ix_supplier_credit_requests_bill
        ON finance.supplier_credit_requests(bill_id,requested_at,id);

      CREATE TABLE finance.supplier_credit_decisions (
        request_id uuid PRIMARY KEY
          REFERENCES finance.supplier_credit_requests(id) ON DELETE RESTRICT,
        decision varchar(24) NOT NULL CHECK(decision IN ('approved_manual','rejected')),
        review_note varchar(180) NOT NULL CHECK(
          review_note=btrim(review_note) AND length(review_note) BETWEEN 8 AND 180
          AND review_note !~ '[[:cntrl:]]'),
        reviewed_by uuid NOT NULL REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        reviewed_at timestamptz NOT NULL DEFAULT clock_timestamp()
      );
      REVOKE ALL ON finance.supplier_credit_requests,finance.supplier_credit_decisions FROM PUBLIC;
      ALTER TABLE finance.supplier_credit_requests ENABLE ROW LEVEL SECURITY;
      ALTER TABLE finance.supplier_credit_decisions ENABLE ROW LEVEL SECURITY;
      CREATE TRIGGER trg_supplier_credit_request_immutable
        BEFORE UPDATE OR DELETE ON finance.supplier_credit_requests
        FOR EACH ROW EXECUTE FUNCTION finance.reject_supplier_payment_mutation();
      CREATE TRIGGER trg_supplier_credit_decision_immutable
        BEFORE UPDATE OR DELETE ON finance.supplier_credit_decisions
        FOR EACH ROW EXECUTE FUNCTION finance.reject_supplier_payment_mutation();

      -- The existing approval function locks the original bill row.
      -- This insert guard also protects the cross-register invariant if another
      -- privileged server path attempts to approve a payment.
      CREATE FUNCTION finance.check_supplier_payment_credit_ceiling()
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
      REVOKE ALL ON FUNCTION finance.check_supplier_payment_credit_ceiling() FROM PUBLIC;
      CREATE TRIGGER trg_supplier_payment_credit_ceiling
        BEFORE INSERT ON finance.supplier_payment_decisions
        FOR EACH ROW EXECUTE FUNCTION finance.check_supplier_payment_credit_ceiling();

      CREATE FUNCTION finance.create_admin_supplier_credit_request(
        _digest text,_owner text,_request uuid,_bill uuid,
        _reference text,_date date,_cents bigint,_reason text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $create$
      DECLARE v_actor uuid; v_id uuid; v_replay boolean:=false;
        v_amount bigint; v_bill_date date;
      BEGIN
        v_actor:=finance.authorize_admin_supplier_bills(_digest,_owner);
        IF _request IS NULL OR _request='00000000-0000-0000-0000-000000000000'::uuid
          OR _bill IS NULL OR _bill='00000000-0000-0000-0000-000000000000'::uuid
          OR _reference IS NULL OR length(_reference) NOT BETWEEN 4 AND 80
          OR _reference<>btrim(_reference) OR _reference ~ '[[:cntrl:]]'
          OR _reason IS NULL OR length(_reason) NOT BETWEEN 8 AND 180
          OR _reason<>btrim(_reason) OR _reason ~ '[[:cntrl:]]'
          OR _date IS NULL OR _date NOT BETWEEN DATE '2020-01-01'
            AND (clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date
          OR _cents IS NULL OR _cents NOT BETWEEN 1 AND 1000000000
        THEN RAISE EXCEPTION 'supplier_credit_invalid'
          USING ERRCODE='22023'; END IF;
        SELECT b.gross_amount_cents,b.bill_date INTO v_amount,v_bill_date
          FROM finance.supplier_bills b WHERE b.id=_bill
            AND b.organization_id=_owner FOR SHARE;
        IF v_amount IS NULL OR _cents>v_amount OR _date<v_bill_date
        THEN RAISE EXCEPTION 'supplier_credit_bill_invalid'
          USING ERRCODE='22023'; END IF;
        INSERT INTO finance.supplier_credit_requests(
          organization_id,client_request_id,bill_id,credit_reference,
          credit_date,amount_cents,reason,requested_by)
        VALUES(_owner,_request,_bill,_reference,_date,_cents,_reason,v_actor)
        ON CONFLICT(organization_id,client_request_id) DO NOTHING
        RETURNING id INTO v_id;
        IF v_id IS NULL THEN
          SELECT c.id INTO v_id FROM finance.supplier_credit_requests c
          WHERE c.organization_id=_owner AND c.client_request_id=_request
            AND c.bill_id=_bill AND c.credit_reference=_reference
            AND c.credit_date=_date AND c.amount_cents=_cents
            AND c.reason=_reason AND c.requested_by=v_actor;
          IF v_id IS NULL THEN RAISE EXCEPTION 'supplier_credit_request_conflict'
            USING ERRCODE='23505'; END IF;
          v_replay:=true;
        ELSE
          INSERT INTO audit.security_events(
            id,actor_realm,occurred_at,actor_identity_id,action,outcome,
            organization_id,reason_code,subject_digest)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
            'admin.finance.supplier_credit.request','succeeded',_owner,
            'supplier_credit_unapproved',
            encode(sha256(convert_to(_owner||'|'||v_id::text,'UTF8')),'hex'));
        END IF;
        RETURN jsonb_build_object('id',v_id,'bill_id',_bill,
          'organization_id',_owner,'status',
          coalesce((SELECT d.decision FROM finance.supplier_credit_decisions d
            WHERE d.request_id=v_id),'pending'),
          'replay',v_replay,'bank_refund_recorded',false,
          'posted_to_general_ledger',false);
      END $create$;
      REVOKE ALL ON FUNCTION finance.create_admin_supplier_credit_request(
        text,text,uuid,uuid,text,date,bigint,text) FROM PUBLIC;

      CREATE FUNCTION finance.review_admin_supplier_credit_request(
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
      REVOKE ALL ON FUNCTION finance.review_admin_supplier_credit_request(
        text,text,uuid,boolean,text) FROM PUBLIC;

      -- Composes the already authorized payment register. The old register
      -- remains unchanged for migration rollback compatibility.
      CREATE FUNCTION finance.read_admin_supplier_payable_position(
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
          row.value||jsonb_build_object(
            'approved_credit_cents',totals.approved,
            'net_payable_cents',(row.value->>'gross_amount_cents')::bigint-totals.approved,
            'remaining_cents',(row.value->>'remaining_cents')::bigint-totals.approved)
          ORDER BY row.ordinality),'[]'::jsonb)
          INTO v_bills
        FROM jsonb_array_elements(v_report->'bills')
          WITH ORDINALITY AS row(value,ordinality)
        CROSS JOIN LATERAL (
          SELECT coalesce(sum(c.amount_cents),0)::bigint AS approved
          FROM finance.supplier_credit_requests c
          JOIN finance.supplier_credit_decisions d
            ON d.request_id=c.id AND d.decision='approved_manual'
          WHERE c.bill_id=(row.value->>'id')::uuid
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
      REVOKE ALL ON FUNCTION finance.read_admin_supplier_payable_position(
        text,text,date) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          DROP FUNCTION finance.read_admin_supplier_payable_position(text,text,date);
          DROP FUNCTION finance.review_admin_supplier_credit_request(
            text,text,uuid,boolean,text);
          DROP FUNCTION finance.create_admin_supplier_credit_request(
            text,text,uuid,uuid,text,date,bigint,text);
          DROP TRIGGER trg_supplier_payment_credit_ceiling
            ON finance.supplier_payment_decisions;
          DROP FUNCTION finance.check_supplier_payment_credit_ceiling();
          DROP TABLE finance.supplier_credit_decisions;
          DROP TABLE finance.supplier_credit_requests;
          """);
}
