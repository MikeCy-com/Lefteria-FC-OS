using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Manual supplier bill register, distinct from cash payments, VAT accounting,
/// and a posted double-entry general ledger. First Team vs Academy and the
/// Academy's issue-time city/location are immutable.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115264000_FinanceSupplierBillRegister")]
public sealed class FinanceSupplierBillRegister : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE finance.supplier_bills (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        organization_id varchar(32) NOT NULL
          CHECK(organization_id IN ('lefteria_fc','lefteria_fc_academy')),
        client_request_id uuid NOT NULL,
        supplier_name varchar(140) NOT NULL,
        invoice_reference varchar(80) NOT NULL,
        category varchar(32) NOT NULL CHECK(category IN (
          'facilities','equipment','travel','matchday',
          'staff_services','administration','other')),
        description varchar(300) NOT NULL,
        bill_date date NOT NULL
          CHECK(bill_date BETWEEN DATE '2020-01-01' AND DATE '2100-12-31'),
        gross_amount_cents bigint NOT NULL
          CHECK(gross_amount_cents BETWEEN 1 AND 1000000000),
        city_id uuid,
        location_id uuid,
        recorded_by uuid NOT NULL
          REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        UNIQUE(organization_id,client_request_id),
        FOREIGN KEY(city_id,location_id)
          REFERENCES club.football_locations(city_id,id) ON DELETE RESTRICT,
        CHECK((organization_id='lefteria_fc' AND city_id IS NULL
          AND location_id IS NULL) OR
          (organization_id='lefteria_fc_academy' AND city_id IS NOT NULL
          AND location_id IS NOT NULL)),
        CHECK(supplier_name=btrim(supplier_name) AND length(supplier_name)>=2
          AND supplier_name !~ '[[:cntrl:]]'),
        CHECK(invoice_reference=btrim(invoice_reference)
          AND length(invoice_reference)>0
          AND invoice_reference !~ '[[:cntrl:]]'),
        CHECK(description=btrim(description) AND length(description)>=4
          AND description !~ '[[:cntrl:]]')
      );
      CREATE UNIQUE INDEX ux_supplier_bills_reference
        ON finance.supplier_bills(
          organization_id,lower(supplier_name),lower(invoice_reference));
      CREATE INDEX ix_supplier_bills_month
        ON finance.supplier_bills(organization_id,bill_date,id);
      CREATE INDEX ix_supplier_bills_academy_location_month
        ON finance.supplier_bills(location_id,bill_date)
        WHERE organization_id='lefteria_fc_academy';
      REVOKE ALL ON finance.supplier_bills FROM PUBLIC;
      ALTER TABLE finance.supplier_bills ENABLE ROW LEVEL SECURITY;
      -- No silent edits/deletions: corrections are a separate reviewed
      -- reversal workflow, not UPDATE of the source supplier invoice.
      CREATE FUNCTION finance.reject_supplier_bill_mutation()
      RETURNS trigger LANGUAGE plpgsql VOLATILE
      SET search_path=pg_catalog,pg_temp AS $immutable$
      BEGIN
        RAISE EXCEPTION 'supplier_bill_immutable'
          USING ERRCODE='42501';
      END $immutable$;
      REVOKE ALL ON FUNCTION finance.reject_supplier_bill_mutation() FROM PUBLIC;
      CREATE TRIGGER trg_supplier_bills_immutable
        BEFORE UPDATE OR DELETE ON finance.supplier_bills
        FOR EACH ROW EXECUTE FUNCTION finance.reject_supplier_bill_mutation();


      CREATE FUNCTION finance.authorize_admin_supplier_bills(
        _digest text,_owner text
      ) RETURNS uuid LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $verify$
      DECLARE v_actor uuid; v_role text;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
        THEN RAISE EXCEPTION 'supplier_bill_access_denied'
          USING ERRCODE='42501'; END IF;
        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'supplier_bill_access_denied'
            USING ERRCODE='42501';
        END;
        SELECT a.id,a.role INTO v_actor,v_role
        FROM identity.admin_sessions s
        JOIN identity.admin_accounts a ON a.id=s.admin_account_id
        JOIN identity.admin_organization_grants g
          ON g.admin_account_id=a.id AND g.organization_id=_owner
        WHERE s.token_digest=_digest AND s.revoked_at IS NULL
          AND s.expires_at>clock_timestamp()
          AND a.is_active AND NOT a.invite_pending AND NOT a.otp2fa_enabled
          AND a.password_hash IS NOT NULL;
        IF v_actor IS NULL OR v_role NOT IN ('super_admin','accountant')
        THEN RAISE EXCEPTION 'supplier_bill_access_denied'
          USING ERRCODE='42501'; END IF;
        RETURN v_actor;
      END $verify$;
      REVOKE ALL ON FUNCTION finance.authorize_admin_supplier_bills(text,text)
        FROM PUBLIC;

      -- A recorded supplier invoice is an obligation, not a cash expense,
      -- provider confirmation, VAT deduction or balanced ledger entry.
      -- Idempotent request IDs cannot silently modify a previously saved bill.
      CREATE FUNCTION finance.create_admin_supplier_bill(
        _digest text,_owner text,_request uuid,_supplier text,_reference text,
        _category text,_description text,_bill_date date,_cents bigint,
        _location uuid
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $create$
      DECLARE
        v_actor uuid; v_city uuid; v_id uuid; v_replay boolean:=false;
      BEGIN
        v_actor:=finance.authorize_admin_supplier_bills(_digest,_owner);
        IF _request IS NULL OR _request='00000000-0000-0000-0000-000000000000'::uuid
          OR _supplier IS NULL OR length(_supplier) NOT BETWEEN 2 AND 140
          OR _supplier<>btrim(_supplier) OR _supplier ~ '[[:cntrl:]]'
          OR _reference IS NULL OR length(_reference) NOT BETWEEN 1 AND 80
          OR _reference<>btrim(_reference) OR _reference ~ '[[:cntrl:]]'
          OR _description IS NULL OR length(_description) NOT BETWEEN 4 AND 300
          OR _description<>btrim(_description) OR _description ~ '[[:cntrl:]]'
          OR _category IS NULL OR _category NOT IN (
            'facilities','equipment','travel','matchday',
            'staff_services','administration','other')
          OR _bill_date IS NULL OR _bill_date NOT BETWEEN
            DATE '2020-01-01' AND DATE '2100-12-31'
          OR _cents IS NULL OR _cents NOT BETWEEN 1 AND 1000000000
        THEN RAISE EXCEPTION 'supplier_bill_invalid' USING ERRCODE='22023'; END IF;

        IF _owner='lefteria_fc_academy' THEN
          SELECT l.city_id INTO v_city
          FROM club.football_locations l
          JOIN club.football_cities c ON c.id=l.city_id
          JOIN club.football_operational_scopes s
            ON s.academy_city_id=l.city_id
            AND s.organization_id='lefteria_fc_academy'
          WHERE l.id=_location AND l.is_active AND c.is_active
          FOR SHARE OF l,c,s;
          IF v_city IS NULL THEN RAISE EXCEPTION 'supplier_bill_site_invalid'
            USING ERRCODE='22023'; END IF;
        ELSIF _location IS NOT NULL THEN
          RAISE EXCEPTION 'first_team_location_forbidden' USING ERRCODE='22023';
        END IF;

        INSERT INTO finance.supplier_bills(
          organization_id,client_request_id,supplier_name,invoice_reference,
          category,description,bill_date,gross_amount_cents,
          city_id,location_id,recorded_by)
        VALUES(_owner,_request,_supplier,_reference,_category,_description,
          _bill_date,_cents,v_city,_location,v_actor)
        ON CONFLICT (organization_id,client_request_id) DO NOTHING
        RETURNING id INTO v_id;

        IF v_id IS NULL THEN
          SELECT b.id INTO v_id FROM finance.supplier_bills b
          WHERE b.organization_id=_owner AND b.client_request_id=_request
            AND b.supplier_name=_supplier AND b.invoice_reference=_reference
            AND b.category=_category AND b.description=_description
            AND b.bill_date=_bill_date AND b.gross_amount_cents=_cents
            AND b.location_id IS NOT DISTINCT FROM _location
            AND b.recorded_by=v_actor;
          IF v_id IS NULL THEN
            RAISE EXCEPTION 'supplier_bill_request_conflict'
              USING ERRCODE='23505';
          END IF;
          v_replay:=true;
        ELSE
          INSERT INTO audit.security_events(
            id,actor_realm,occurred_at,actor_identity_id,action,outcome,
            organization_id,reason_code,subject_digest)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
            'admin.finance.supplier_bill.record','succeeded',_owner,
            'manual_unpaid_supplier_bill',
            encode(sha256(convert_to(_owner||'|'||v_id::text,'UTF8')),'hex'));
        END IF;
        RETURN jsonb_build_object('id',v_id,'organization_id',_owner,
          'recorded',true,'replay',v_replay,'cash_paid',false,
          'posted_to_general_ledger',false);
      END $create$;
      REVOKE ALL ON FUNCTION finance.create_admin_supplier_bill(
        text,text,uuid,text,text,text,text,date,bigint,uuid) FROM PUBLIC;

      CREATE FUNCTION finance.read_admin_supplier_bills(
        _digest text,_owner text,_month date
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $read$
      DECLARE v_actor uuid;
      BEGIN
        v_actor:=finance.authorize_admin_supplier_bills(_digest,_owner);
        IF _month IS NULL OR _month NOT BETWEEN
          DATE '2020-01-01' AND DATE '2100-12-01'
          OR extract(day FROM _month)<>1
        THEN RAISE EXCEPTION 'supplier_bill_month_invalid' USING ERRCODE='22023'; END IF;
        RETURN jsonb_build_object(
          'organization_id',_owner,'month',_month,'currency','EUR',
          'is_cash_payment',false,'is_general_ledger',false,
          'summary',(SELECT jsonb_build_object(
            'bill_count',count(*)::int,
            'gross_amount_cents',coalesce(sum(b.gross_amount_cents),0)::bigint)
            FROM finance.supplier_bills b
            WHERE b.organization_id=_owner
              AND b.bill_date>=_month
              AND b.bill_date<(_month+interval '1 month')::date),
          'bills',(SELECT coalesce(jsonb_agg(to_jsonb(rows)
            ORDER BY rows.bill_date DESC,rows.id),'[]'::jsonb)
            FROM (SELECT b.id,b.supplier_name,b.invoice_reference,b.category,
              b.description,b.bill_date,b.gross_amount_cents,
              b.city_id,b.location_id,b.recorded_at
              FROM finance.supplier_bills b
              WHERE b.organization_id=_owner
                AND b.bill_date>=_month
                AND b.bill_date<(_month+interval '1 month')::date
              ORDER BY b.bill_date DESC,b.id LIMIT 100) rows),
          'sites',CASE WHEN _owner='lefteria_fc_academy' THEN (
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'city_id',c.id,'city_name',c.name,
              'location_id',l.id,'location_name',l.name)
              ORDER BY c.name,l.name,l.id),'[]'::jsonb)
            FROM club.football_locations l
            JOIN club.football_cities c ON c.id=l.city_id
            JOIN club.football_operational_scopes s
              ON s.academy_city_id=c.id
                AND s.organization_id='lefteria_fc_academy'
            WHERE c.is_active AND l.is_active
          ) ELSE '[]'::jsonb END);
      END $read$;
      REVOKE ALL ON FUNCTION finance.read_admin_supplier_bills(text,text,date)
        FROM PUBLIC;

      -- Managers can only read aggregate supplier obligations at their own
      -- approved Academy sites; no vendor names, invoice references or notes.
      CREATE FUNCTION finance.read_admin_supplier_site_totals(
        _digest text,_owner text,_month date
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $sites$
      DECLARE v_actor uuid; v_role text;
      BEGIN
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);
        IF _owner<>'lefteria_fc_academy'
          OR _month IS NULL OR _month NOT BETWEEN
            DATE '2020-01-01' AND DATE '2100-12-01'
          OR extract(day FROM _month)<>1
        THEN RAISE EXCEPTION 'supplier_site_month_invalid' USING ERRCODE='22023'; END IF;
        RETURN jsonb_build_object('organization_id',_owner,'month',_month,
          'currency','EUR','is_cash_payment',false,
          'locations',(
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'city_id',l.city_id,'location_id',l.id,
              'bill_count',m.bill_count,
              'gross_amount_cents',m.gross_amount_cents)
              ORDER BY l.city_id,l.id),'[]'::jsonb)
            FROM club.football_locations l
            JOIN club.football_operational_scopes s
              ON s.academy_city_id=l.city_id
              AND s.organization_id='lefteria_fc_academy'
            CROSS JOIN LATERAL (
              SELECT count(*)::int AS bill_count,
                coalesce(sum(b.gross_amount_cents),0)::bigint
                  AS gross_amount_cents
              FROM finance.supplier_bills b
              WHERE b.organization_id='lefteria_fc_academy'
                AND b.location_id=l.id AND b.city_id=l.city_id
                AND b.bill_date>=_month
                AND b.bill_date<(_month+interval '1 month')::date
            ) m
            WHERE club.admin_football_allowed(
              v_actor,v_role,_owner,s.id,l.id,false)));
      END $sites$;
      REVOKE ALL ON FUNCTION finance.read_admin_supplier_site_totals(
        text,text,date) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          DROP FUNCTION finance.read_admin_supplier_site_totals(text,text,date);
          DROP FUNCTION finance.read_admin_supplier_bills(text,text,date);
          DROP FUNCTION finance.create_admin_supplier_bill(
            text,text,uuid,text,text,text,text,date,bigint,uuid);
          DROP FUNCTION finance.authorize_admin_supplier_bills(text,text);
          DROP TABLE finance.supplier_bills;
          DROP FUNCTION finance.reject_supplier_bill_mutation();
          """);
}
