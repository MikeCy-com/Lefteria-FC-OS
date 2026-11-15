using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Auditable issue-time Academy location attribution and exact manager-scoped
/// financial aggregates without changing existing invoices, payment events,
/// Payabl captures or historical issue amounts. Ambiguity remains unassigned.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115250000_AcademyLocationFinanceAttribution")]
public sealed class AcademyLocationFinanceAttribution : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- Issue-time attribution is separate from immutable billing documents.
      -- Existing invoices are never rewritten/backfilled by current roster
      -- membership. A cancelled/refunded invoice does not silently alter its
      -- documented site; balances are computed from the original ledger.
      CREATE TABLE finance.academy_training_fee_locations (
        issue_id uuid PRIMARY KEY
          REFERENCES finance.training_fee_issues(id) ON DELETE CASCADE,
        city_id uuid NOT NULL,
        location_id uuid NOT NULL,
        method varchar(24) NOT NULL
          CHECK(method IN ('unique_site_at_issue','manually_verified')),
        evidence_digest char(64),
        recorded_by uuid REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        FOREIGN KEY(city_id,location_id)
          REFERENCES club.football_locations(city_id,id) ON DELETE RESTRICT,
        CHECK((method='unique_site_at_issue' AND evidence_digest IS NULL
          AND recorded_by IS NULL)
          OR (method='manually_verified'
            AND evidence_digest ~ '^[0-9a-f]{64}$' AND recorded_by IS NOT NULL))
      );
      CREATE INDEX ix_academy_fee_locations_site
        ON finance.academy_training_fee_locations(location_id,issue_id);

      CREATE TABLE finance.academy_general_charge_locations (
        issue_id uuid PRIMARY KEY
          REFERENCES finance.general_charge_issues(id) ON DELETE CASCADE,
        city_id uuid NOT NULL,
        location_id uuid NOT NULL,
        method varchar(24) NOT NULL DEFAULT 'manually_verified'
          CHECK(method='manually_verified'),
        evidence_digest char(64) NOT NULL
          CHECK(evidence_digest ~ '^[0-9a-f]{64}$'),
        recorded_by uuid NOT NULL
          REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        FOREIGN KEY(city_id,location_id)
          REFERENCES club.football_locations(city_id,id) ON DELETE RESTRICT
      );
      CREATE INDEX ix_academy_general_locations_site
        ON finance.academy_general_charge_locations(location_id,issue_id);
      REVOKE ALL ON TABLE finance.academy_training_fee_locations,
        finance.academy_general_charge_locations FROM PUBLIC;
      ALTER TABLE finance.academy_training_fee_locations ENABLE ROW LEVEL SECURITY;
      ALTER TABLE finance.academy_general_charge_locations ENABLE ROW LEVEL SECURITY;

      -- A current-month Academy training invoice may be assigned at the exact
      -- issue instant ONLY when every canonical group membership is mapped
      -- and all memberships agree on ONE active location. This is an
      -- issue-time snapshot, not a historical period rewrite. Backdated/
      -- advance issues and ambiguous or legacy memberships remain
      -- unallocated for human verification. No invoice amount/status changes.
      CREATE FUNCTION finance.capture_new_academy_training_issue_site()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $capture$
      DECLARE
        v_all bigint; v_verified bigint; v_sites bigint;
        v_city uuid; v_location uuid;
      BEGIN
        IF TG_TABLE_SCHEMA<>'finance' OR TG_TABLE_NAME<>'training_fee_issues' THEN
          RAISE EXCEPTION 'finance_attribution_trigger_invalid'
            USING ERRCODE='42501';
        END IF;
        IF NEW.organization_id<>'lefteria_fc_academy'
          OR NEW.fee_month<>
            date_trunc('month',NEW.issued_at AT TIME ZONE 'Asia/Nicosia')::date
          OR NEW.issued_at NOT BETWEEN
            clock_timestamp()-interval '2 minutes'
            AND clock_timestamp()+interval '2 minutes'
          OR NOT EXISTS(SELECT 1 FROM club.players p
            WHERE p.id=NEW.player_id AND p.organization_id='lefteria_fc_academy')
        THEN RETURN NEW; END IF;

        SELECT count(*),count(a.location_id),count(DISTINCT a.location_id),
          min(a.city_id::text)::uuid,min(a.location_id::text)::uuid
        INTO v_all,v_verified,v_sites,v_city,v_location
        FROM club.player_academy_groups pag
        LEFT JOIN club.academy_group_location_assignments a
          ON a.organization_id=pag.organization_id
          AND a.group_id=pag.academy_group_id
        LEFT JOIN club.football_locations l
          ON l.id=a.location_id AND l.city_id=a.city_id AND l.is_active
        LEFT JOIN club.football_cities c
          ON c.id=a.city_id AND c.is_active
        WHERE pag.organization_id='lefteria_fc_academy'
          AND pag.player_id=NEW.player_id;

        -- A group with an inactive assigned site must not be silently
        -- dropped from the denominator. Check all memberships separately.
        IF v_all=0 OR v_all<>v_verified OR v_sites<>1 OR NOT EXISTS (
          SELECT 1 FROM club.football_locations l
          JOIN club.football_cities c ON c.id=l.city_id
          WHERE l.id=v_location AND l.city_id=v_city
            AND l.is_active AND c.is_active
        ) OR EXISTS (
          SELECT 1 FROM club.player_academy_groups pag
          LEFT JOIN club.academy_group_location_assignments a
            ON a.group_id=pag.academy_group_id
              AND a.organization_id=pag.organization_id
          LEFT JOIN club.football_locations l
            ON l.id=a.location_id AND l.city_id=a.city_id AND l.is_active
          LEFT JOIN club.football_cities c
            ON c.id=a.city_id AND c.is_active
          WHERE pag.organization_id='lefteria_fc_academy'
            AND pag.player_id=NEW.player_id
            AND (a.location_id IS NULL OR l.id IS NULL OR c.id IS NULL)
        ) THEN RETURN NEW; END IF;

        INSERT INTO finance.academy_training_fee_locations(
          issue_id,city_id,location_id,method)
        VALUES(NEW.id,v_city,v_location,'unique_site_at_issue');
        RETURN NEW;
      END $capture$;

      CREATE TRIGGER trg_academy_fee_site_at_issue
        AFTER INSERT ON finance.training_fee_issues
        FOR EACH ROW EXECUTE FUNCTION finance.capture_new_academy_training_issue_site();

      -- Once attributed, neither owner nor a delegated manager may silently
      -- edit the metadata. The only operational mutation is a reviewed
      -- allocation of a previously UNALLOCATED issue.
      CREATE FUNCTION finance.guard_academy_issue_attribution_update()
      RETURNS trigger LANGUAGE plpgsql VOLATILE
      SET search_path=pg_catalog,pg_temp AS $immutable$
      BEGIN
        RAISE EXCEPTION 'academy_issue_site_immutable' USING ERRCODE='42501';
      END $immutable$;
      CREATE TRIGGER trg_academy_fee_site_immutable
        BEFORE UPDATE ON finance.academy_training_fee_locations
        FOR EACH ROW EXECUTE FUNCTION finance.guard_academy_issue_attribution_update();
      CREATE TRIGGER trg_academy_general_site_immutable
        BEFORE UPDATE ON finance.academy_general_charge_locations
        FOR EACH ROW EXECUTE FUNCTION finance.guard_academy_issue_attribution_update();

      CREATE FUNCTION finance.assign_admin_academy_issue_site(
        _digest text,_owner text,_kind text,_issue uuid,
        _location uuid,_evidence text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $allocate$
      DECLARE
        v_actor uuid; v_role text;
        v_city uuid; v_count int;
        v_proof text;
      BEGIN
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);
        IF _owner<>'lefteria_fc_academy' OR v_role<>'super_admin'
          OR _kind NOT IN ('training_fee','general_charge')
          OR _kind IS NULL OR _issue IS NULL OR _location IS NULL
          OR _evidence IS NULL OR length(btrim(_evidence)) NOT BETWEEN 5 AND 160
          OR _evidence<>btrim(_evidence) OR _evidence ~ '[[:cntrl:]]'
        THEN RAISE EXCEPTION 'academy_finance_allocation_denied'
          USING ERRCODE='42501'; END IF;

        SELECT l.city_id INTO v_city FROM club.football_locations l
          JOIN club.football_cities c ON c.id=l.city_id
          JOIN club.football_operational_scopes s ON s.academy_city_id=l.city_id
          WHERE l.id=_location AND l.is_active AND c.is_active
            AND s.organization_id='lefteria_fc_academy' FOR SHARE OF l,c,s;
        IF v_city IS NULL THEN RAISE EXCEPTION 'academy_finance_site_invalid'
          USING ERRCODE='42501'; END IF;
        v_proof:=encode(sha256(convert_to(
          _kind||'|'||_issue::text||'|'||_location::text||'|'||_evidence,'UTF8')),'hex');

        IF _kind='training_fee' THEN
          PERFORM 1 FROM finance.training_fee_issues
            WHERE id=_issue AND organization_id='lefteria_fc_academy'
            FOR SHARE;
          IF NOT FOUND THEN RAISE EXCEPTION 'academy_fee_not_found'
            USING ERRCODE='42501'; END IF;
          INSERT INTO finance.academy_training_fee_locations(
            issue_id,city_id,location_id,method,evidence_digest,recorded_by)
          VALUES(_issue,v_city,_location,'manually_verified',v_proof,v_actor)
          ON CONFLICT DO NOTHING;
        ELSE
          PERFORM 1 FROM finance.general_charge_issues
            WHERE id=_issue AND organization_id='lefteria_fc_academy'
              AND cancelled_at IS NULL FOR SHARE;
          IF NOT FOUND THEN RAISE EXCEPTION 'academy_charge_not_found'
            USING ERRCODE='42501'; END IF;
          INSERT INTO finance.academy_general_charge_locations(
            issue_id,city_id,location_id,evidence_digest,recorded_by)
          VALUES(_issue,v_city,_location,v_proof,v_actor)
          ON CONFLICT DO NOTHING;
        END IF;
        GET DIAGNOSTICS v_count=ROW_COUNT;
        IF v_count<>1 THEN
          RAISE EXCEPTION 'academy_finance_issue_already_attributed'
            USING ERRCODE='23505'; END IF;

        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,action,outcome,
          organization_id,reason_code,subject_digest)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
          'admin.academy.finance.site.allocate','succeeded',_owner,_kind,v_proof);
        RETURN jsonb_build_object('organization_id',_owner,'kind',_kind,
          'issue_id',_issue,'city_id',v_city,'location_id',_location,
          'method','manually_verified');
      END $allocate$;

      -- Location managers only see the issue-time or manually VERIFIED
      -- location allocation. All figures remain denominated in euro cents,
      -- and monthly fees / general charges are deliberately separate so
      -- overlapping legacy sources are not double-counted as one revenue.
      CREATE FUNCTION finance.read_admin_academy_location_finance(
        _digest text,_owner text,_month date
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $read_finance$
      DECLARE v_actor uuid; v_role text;
      BEGIN
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);
        IF _owner<>'lefteria_fc_academy' OR _month IS NULL
          OR _month NOT BETWEEN DATE '2020-01-01' AND DATE '2100-12-01'
          OR EXTRACT(day FROM _month)<>1
        THEN RAISE EXCEPTION 'academy_finance_month_invalid'
          USING ERRCODE='22023'; END IF;

        RETURN jsonb_build_object(
          'organization_id',_owner,'month',_month,'currency','EUR',
          'locations',(
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'city_id',l.city_id,'location_id',l.id,
              'training_issued_count',t.issued_count,
              'training_issued_cents',t.issued_cents,
              'training_paid_cents',t.paid_cents,
              'training_review_count',t.review_count,
              'general_issued_count',g.issued_count,
              'general_issued_cents',g.issued_cents,
              'general_paid_cents',g.paid_cents,
              'general_overdue_count',g.overdue_count
            ) ORDER BY l.city_id,l.id),'[]'::jsonb)
            FROM club.football_locations l
            JOIN club.football_operational_scopes s
              ON s.academy_city_id=l.city_id
              AND s.organization_id='lefteria_fc_academy'
            CROSS JOIN LATERAL (
              SELECT count(*)::int AS issued_count,
                coalesce(sum(i.amount_cents),0)::bigint AS issued_cents,
                coalesce(sum(i.amount_cents)
                  FILTER(WHERE i.status='paid'),0)::bigint AS paid_cents,
                count(*) FILTER(WHERE i.status='review_required')::int AS review_count
              FROM finance.academy_training_fee_locations a
              JOIN finance.training_fee_issues i ON i.id=a.issue_id
              WHERE a.location_id=l.id AND a.city_id=l.city_id
                AND i.organization_id='lefteria_fc_academy'
                AND i.fee_month=_month
            ) t
            CROSS JOIN LATERAL (
              SELECT count(*)::int AS issued_count,
                coalesce(sum(b.amount_cents),0)::bigint AS issued_cents,
                coalesce(sum(b.paid_cents),0)::bigint AS paid_cents,
                count(*) FILTER(WHERE b.status='overdue')::int AS overdue_count
              FROM finance.academy_general_charge_locations a
              JOIN finance.general_charge_balances b ON b.id=a.issue_id
              WHERE a.location_id=l.id AND a.city_id=l.city_id
                AND b.organization_id='lefteria_fc_academy'
                AND b.cancelled_at IS NULL
                AND b.issued_at>=_month::timestamp AT TIME ZONE 'Asia/Nicosia'
                AND b.issued_at<(_month+interval '1 month')::timestamp
                  AT TIME ZONE 'Asia/Nicosia'
            ) g
            WHERE club.admin_football_allowed(
              v_actor,v_role,_owner,s.id,l.id,false)
          )
        );
      END $read_finance$;

      -- Only a superadmin can inspect the organization-wide backlog of
      -- fees without proven location; no parent contacts, phone numbers,
      -- payment tokens or financial events are exposed by this allowlist.
      CREATE FUNCTION finance.read_admin_academy_unallocated_issues(
        _digest text,_owner text,_month date
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $unallocated$
      DECLARE v_actor uuid; v_role text;
      BEGIN
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);
        IF _owner<>'lefteria_fc_academy' OR v_role<>'super_admin'
          OR _month IS NULL
          OR _month NOT BETWEEN DATE '2020-01-01' AND DATE '2100-12-01'
          OR EXTRACT(day FROM _month)<>1
        THEN RAISE EXCEPTION 'academy_unallocated_denied'
          USING ERRCODE='42501'; END IF;
        RETURN jsonb_build_object(
          'organization_id',_owner,'month',_month,
          'total_count',(
            SELECT count(*)::int FROM (
              SELECT i.id FROM finance.training_fee_issues i
              WHERE i.organization_id='lefteria_fc_academy'
                AND i.fee_month=_month
                AND NOT EXISTS(SELECT 1 FROM finance.academy_training_fee_locations a
                  WHERE a.issue_id=i.id)
              UNION ALL
              SELECT g.id FROM finance.general_charge_issues g
              WHERE g.organization_id='lefteria_fc_academy'
                AND g.cancelled_at IS NULL
                AND g.issued_at>=_month::timestamp AT TIME ZONE 'Asia/Nicosia'
                AND g.issued_at<(_month+interval '1 month')::timestamp
                  AT TIME ZONE 'Asia/Nicosia'
                AND NOT EXISTS(SELECT 1 FROM finance.academy_general_charge_locations a
                  WHERE a.issue_id=g.id)
            ) q
          ),
          'issues',(
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'kind',q.kind,'id',q.id,'player_id',q.player_id,
              'player_name',q.player_name,'amount_cents',q.amount_cents,
              'month',q.month
            ) ORDER BY q.kind,q.player_name,q.id),'[]'::jsonb)
            FROM (
              SELECT 'training_fee'::text AS kind,i.id,i.player_id,
                p.name AS player_name,i.amount_cents,
                i.fee_month AS month
              FROM finance.training_fee_issues i
              JOIN club.players p ON p.id=i.player_id
                AND p.organization_id='lefteria_fc_academy'
              WHERE i.organization_id='lefteria_fc_academy'
                AND i.fee_month=_month
                AND NOT EXISTS(SELECT 1 FROM finance.academy_training_fee_locations a
                  WHERE a.issue_id=i.id)
              UNION ALL
              SELECT 'general_charge'::text AS kind,g.id,g.player_id,
                p.name AS player_name,g.amount_cents,
                date_trunc('month',g.issued_at AT TIME ZONE 'Asia/Nicosia')::date
                  AS month
              FROM finance.general_charge_issues g
              JOIN club.players p ON p.id=g.player_id
                AND p.organization_id='lefteria_fc_academy'
              WHERE g.organization_id='lefteria_fc_academy'
                AND g.cancelled_at IS NULL
                AND g.issued_at>=_month::timestamp AT TIME ZONE 'Asia/Nicosia'
                AND g.issued_at<(_month+interval '1 month')::timestamp
                  AT TIME ZONE 'Asia/Nicosia'
                AND NOT EXISTS(SELECT 1 FROM finance.academy_general_charge_locations a
                  WHERE a.issue_id=g.id)
              ORDER BY kind,player_name,id LIMIT 100
            ) q
          )
        );
      END $unallocated$;

      REVOKE ALL ON FUNCTION finance.capture_new_academy_training_issue_site(),
        finance.guard_academy_issue_attribution_update(),
        finance.assign_admin_academy_issue_site(text,text,text,uuid,uuid,text),
        finance.read_admin_academy_location_finance(text,text,date),
        finance.read_admin_academy_unallocated_issues(text,text,date)
      FROM PUBLIC;

    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- Destructive rollback of verified financial attribution is forbidden
      -- after any location allocation has been recorded. Use forward recovery.
      DO $guard$
      BEGIN
        IF EXISTS(SELECT 1 FROM finance.academy_training_fee_locations LIMIT 1)
          OR EXISTS(SELECT 1 FROM finance.academy_general_charge_locations LIMIT 1)
        THEN RAISE EXCEPTION 'finance_attribution_forward_recovery_required'; END IF;
      END $guard$;
      DROP FUNCTION finance.read_admin_academy_unallocated_issues(text,text,date);
      DROP FUNCTION finance.read_admin_academy_location_finance(text,text,date);
      DROP FUNCTION finance.assign_admin_academy_issue_site(text,text,text,uuid,uuid,text);
      DROP TRIGGER trg_academy_fee_site_at_issue ON finance.training_fee_issues;
      DROP FUNCTION finance.capture_new_academy_training_issue_site();
      DROP TRIGGER trg_academy_fee_site_immutable ON finance.academy_training_fee_locations;
      DROP TRIGGER trg_academy_general_site_immutable ON finance.academy_general_charge_locations;
      DROP FUNCTION finance.guard_academy_issue_attribution_update();
      DROP TABLE finance.academy_general_charge_locations;
      DROP TABLE finance.academy_training_fee_locations;
    """);
}
