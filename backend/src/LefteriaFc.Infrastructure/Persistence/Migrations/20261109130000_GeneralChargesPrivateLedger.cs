using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Private, migration-only port of the original eight charge categories and
/// partial-payment ledger. No API, card charging, SMS or recurring scheduler is
/// activated here. All settlement evidence is owner-only until separate review.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261109130000_GeneralChargesPrivateLedger")]
public sealed class GeneralChargesPrivateLedger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE SCHEMA IF NOT EXISTS finance;
      REVOKE ALL ON SCHEMA finance FROM PUBLIC;

      -- The original source: backend/models.py ChargeTemplate. Templates do
      -- not create future months or schedule debits; an owner must explicitly
      -- issue every selected charge in a later authenticated workflow.
      CREATE TABLE finance.general_charge_templates (
        id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL
          REFERENCES club.organizations(id) ON DELETE RESTRICT,
        name varchar(140) NOT NULL CHECK (length(btrim(name)) BETWEEN 1 AND 140
          AND name=btrim(name)),
        charge_type varchar(16) NOT NULL
          CHECK (charge_type IN ('training','event','grassroots',
            'registration','equipment','tournament','transport','other')),
        description varchar(500),
        default_amount_cents bigint NOT NULL
          CHECK (default_amount_cents BETWEEN 1 AND 100000000),
        currency char(3) NOT NULL DEFAULT 'EUR' CHECK (currency='EUR'),
        is_active boolean NOT NULL DEFAULT true,
        created_at timestamptz NOT NULL DEFAULT clock_timestamp()
      );

      -- Original Charge fields, but amounts are never represented by float.
      -- Historical legacy IDs are nullable and unique per organization.
      -- An unresolved legacy guardian is never presented in a parent's wallet.
      CREATE TABLE finance.general_charge_issues (
        id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL
          REFERENCES club.organizations(id) ON DELETE RESTRICT,
        player_id varchar(128) COLLATE "C" NOT NULL,
        charge_type varchar(16) NOT NULL
          CHECK (charge_type IN ('training','event','grassroots',
            'registration','equipment','tournament','transport','other')),
        description varchar(500) NOT NULL
          CHECK (length(btrim(description)) BETWEEN 1 AND 500
            AND description=btrim(description)),
        amount_cents bigint NOT NULL CHECK (amount_cents BETWEEN 1 AND 100000000),
        currency char(3) NOT NULL DEFAULT 'EUR' CHECK (currency='EUR'),
        due_date date,
        season varchar(60),
        period_label varchar(120),
        period_key varchar(7) COLLATE "C"
          CHECK (period_key IS NULL OR
            (period_key ~ '^[0-9]{4}-(0[1-9]|1[0-2])$')),
        notes varchar(2000),
        legacy_charge_id varchar(128) COLLATE "C",
        original_snapshot_digest char(64) COLLATE "C"
          CHECK (original_snapshot_digest IS NULL OR
            original_snapshot_digest ~ '^[0-9a-f]{64}$'),
        issued_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        created_by uuid REFERENCES identity.admin_accounts(id)
          ON DELETE RESTRICT,
        cancelled_at timestamptz,
        CONSTRAINT fk_general_charge_player_scope
          FOREIGN KEY (organization_id,player_id)
          REFERENCES club.players(organization_id,id) ON DELETE RESTRICT,
        UNIQUE (organization_id,id),
        CONSTRAINT ck_general_charge_cancellation
          CHECK (cancelled_at IS NULL OR cancelled_at>=issued_at)
      );
      CREATE UNIQUE INDEX uq_general_charge_legacy_source
        ON finance.general_charge_issues(organization_id,legacy_charge_id)
        WHERE legacy_charge_id IS NOT NULL;
      CREATE INDEX ix_general_charges_child_period
        ON finance.general_charge_issues
          (organization_id,player_id,period_key,issued_at DESC);
      CREATE INDEX ix_general_charges_due
        ON finance.general_charge_issues(organization_id,due_date)
        WHERE cancelled_at IS NULL;

      -- Guardian authorization must be verified separately from the
      -- historical charge snapshot: an unresolved legacy invoice can later
      -- be linked without altering any issued amount or source document.
      -- Still require a live canonical guardian/child check when reading.
      CREATE TABLE finance.general_charge_parent_verifications (
        charge_id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL,
        payer_identity_id uuid NOT NULL
          REFERENCES identity.mobile_identities(id) ON DELETE RESTRICT,
        verification_digest char(64) COLLATE "C" NOT NULL
          CHECK (verification_digest ~ '^[0-9a-f]{64}$'),
        verification_method varchar(20) NOT NULL
          CHECK (verification_method IN ('approved_guardian','reconciled_legacy')),
        verified_by uuid REFERENCES identity.admin_accounts(id)
          ON DELETE RESTRICT,
        verified_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        CONSTRAINT ck_general_payer_verification_authority CHECK (
          verification_method='reconciled_legacy' OR verified_by IS NOT NULL),
        CONSTRAINT fk_general_payer_charge_scope
          FOREIGN KEY (organization_id,charge_id)
          REFERENCES finance.general_charge_issues(organization_id,id)
          ON DELETE RESTRICT
      );
      CREATE INDEX ix_general_parent_verifications
        ON finance.general_charge_parent_verifications(
          organization_id,payer_identity_id,verified_at DESC);

      -- Positive payment and exact-full reversal events form an immutable
      -- accounting ledger; each request has an idempotency key. No refund
      -- endpoint or bank/card verification mechanism is activated by schema.
      CREATE TABLE finance.general_charge_events (
        id uuid PRIMARY KEY,
        charge_id uuid NOT NULL,
        organization_id varchar(32) NOT NULL,
        idempotency_id uuid NOT NULL UNIQUE,
        event_kind varchar(12) NOT NULL
          CHECK (event_kind IN ('payment','reversal')),
        payment_method varchar(12) NOT NULL
          CHECK (payment_method IN ('cash','bank','card','online','other')),
        evidence_type varchar(20) NOT NULL
          CHECK (evidence_type IN ('admin_cash','provider_capture',
            'bank_statement','verified_legacy')),
        amount_cents bigint NOT NULL CHECK (
          amount_cents BETWEEN -100000000 AND 100000000
          AND amount_cents<>0),
        currency char(3) NOT NULL DEFAULT 'EUR' CHECK (currency='EUR'),
        evidence_reference varchar(140),
        proof_digest char(64) COLLATE "C"
          CHECK (proof_digest IS NULL OR proof_digest ~ '^[0-9a-f]{64}$'),
        legacy_event_id varchar(128) COLLATE "C",
        actor_id uuid REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        reverses_event_id uuid REFERENCES finance.general_charge_events(id)
          ON DELETE RESTRICT,
        occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        CONSTRAINT fk_general_charge_event_scope
          FOREIGN KEY (organization_id,charge_id)
          REFERENCES finance.general_charge_issues(organization_id,id)
          ON DELETE RESTRICT,
        CONSTRAINT ck_general_charge_event_sign
          CHECK (
            (event_kind='payment' AND amount_cents>0 AND reverses_event_id IS NULL)
            OR (event_kind='reversal' AND amount_cents<0
              AND reverses_event_id IS NOT NULL)
          ),
        CONSTRAINT ck_general_charge_event_proof CHECK (
          (evidence_type<>'admin_cash'
            OR (actor_id IS NOT NULL AND payment_method='cash'))
          AND (evidence_type<>'provider_capture'
            OR (payment_method IN ('card','online')
              AND nullif(btrim(evidence_reference),'') IS NOT NULL))
          AND (evidence_type<>'bank_statement'
            OR (payment_method='bank'
              AND nullif(btrim(evidence_reference),'') IS NOT NULL))
          AND (evidence_type<>'verified_legacy' OR legacy_event_id IS NOT NULL)
        )
      );
      CREATE UNIQUE INDEX uq_general_charge_event_reversal
        ON finance.general_charge_events(reverses_event_id)
        WHERE reverses_event_id IS NOT NULL;
      CREATE UNIQUE INDEX uq_general_charge_legacy_event
        ON finance.general_charge_events(organization_id,legacy_event_id)
        WHERE legacy_event_id IS NOT NULL;
      CREATE INDEX ix_general_charge_events_ledger
        ON finance.general_charge_events(charge_id,occurred_at,id);

      -- Serializing on the charge row prevents two concurrent partial
      -- payments from exceeding the original amount. A duplicate replay
      -- must be handled by the future API using the idempotency_id unique
      -- key; this trigger never returns fake success for a replay.
      CREATE FUNCTION finance.guard_general_charge_event()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $guard_general_charge$
      DECLARE
        v_amount bigint;
        v_total bigint;
        v_organization text;
        v_cancelled timestamptz;
        v_original finance.general_charge_events%ROWTYPE;
      BEGIN
        SELECT amount_cents,organization_id,cancelled_at
          INTO v_amount,v_organization,v_cancelled
        FROM finance.general_charge_issues
          WHERE id=NEW.charge_id FOR UPDATE;
        IF NOT FOUND OR v_organization<>NEW.organization_id
          OR v_cancelled IS NOT NULL
        THEN RAISE EXCEPTION 'charge_event_outside_scope'
          USING ERRCODE='42501'; END IF;
        IF NEW.event_kind='reversal' THEN
          SELECT * INTO v_original FROM finance.general_charge_events
            WHERE id=NEW.reverses_event_id
              AND charge_id=NEW.charge_id
              AND organization_id=NEW.organization_id
              AND event_kind='payment';
          IF NOT FOUND
            OR NEW.amount_cents<>-v_original.amount_cents
            OR NEW.payment_method<>v_original.payment_method
          THEN RAISE EXCEPTION 'invalid_charge_reversal'
            USING ERRCODE='22023'; END IF;
        END IF;
        SELECT coalesce(sum(amount_cents),0) INTO v_total
        FROM finance.general_charge_events WHERE charge_id=NEW.charge_id;
        IF v_total+NEW.amount_cents NOT BETWEEN 0 AND v_amount THEN
          RAISE EXCEPTION 'charge_allocation_outside_balance'
            USING ERRCODE='23514';
        END IF;
        RETURN NEW;
      END $guard_general_charge$;

      CREATE TRIGGER trg_general_charge_event_insert
        BEFORE INSERT ON finance.general_charge_events
        FOR EACH ROW EXECUTE FUNCTION finance.guard_general_charge_event();

      CREATE FUNCTION finance.freeze_general_charge_event()
      RETURNS trigger LANGUAGE plpgsql VOLATILE
      SET search_path=pg_catalog,pg_temp AS $freeze_general_event$
      BEGIN
        RAISE EXCEPTION 'financial_events_are_immutable' USING ERRCODE='42501';
      END $freeze_general_event$;
      CREATE TRIGGER trg_general_charge_event_immutable
        BEFORE UPDATE OR DELETE ON finance.general_charge_events
        FOR EACH ROW EXECUTE FUNCTION finance.freeze_general_charge_event();
      CREATE TRIGGER trg_general_payer_verification_immutable
        BEFORE UPDATE OR DELETE ON finance.general_charge_parent_verifications
        FOR EACH ROW EXECUTE FUNCTION finance.freeze_general_charge_event();

      CREATE FUNCTION finance.freeze_general_charge_issue()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $freeze_general_issue$
      BEGIN
        IF TG_OP='DELETE' THEN
          RAISE EXCEPTION 'issued_charge_cannot_be_deleted'
            USING ERRCODE='42501';
        END IF;
        IF (to_jsonb(OLD)-'cancelled_at') IS DISTINCT FROM
             (to_jsonb(NEW)-'cancelled_at')
           OR OLD.cancelled_at IS NOT NULL OR NEW.cancelled_at IS NULL
           OR EXISTS(SELECT 1 FROM finance.general_charge_events
              WHERE charge_id=OLD.id)
        THEN RAISE EXCEPTION 'issued_charge_is_immutable'
          USING ERRCODE='42501'; END IF;
        RETURN NEW;
      END $freeze_general_issue$;
      CREATE TRIGGER trg_general_charge_issue_immutable
        BEFORE UPDATE OR DELETE ON finance.general_charge_issues
        FOR EACH ROW EXECUTE FUNCTION finance.freeze_general_charge_issue();

      -- Status follows the evidence ledger (not the latest payment redirect).
      -- Clock conversion uses Cyprus local date for due-date reporting.
      CREATE VIEW finance.general_charge_balances
      WITH (security_barrier=true) AS
        SELECT c.id,c.organization_id,c.player_id,link.payer_identity_id,
          (link.charge_id IS NOT NULL) AS ready_for_parent,
          c.charge_type,c.description,c.amount_cents,
          c.currency,c.due_date,c.period_key,c.period_label,c.season,
          c.issued_at,c.cancelled_at,
          coalesce(a.paid_cents,0)::bigint AS paid_cents,
          (c.amount_cents-coalesce(a.paid_cents,0))::bigint AS due_cents,
          CASE
            WHEN c.cancelled_at IS NOT NULL THEN 'cancelled'
            WHEN coalesce(a.paid_cents,0)=c.amount_cents THEN 'paid'
            WHEN c.due_date IS NOT NULL
              AND c.due_date<timezone('Asia/Nicosia',clock_timestamp())::date
              THEN 'overdue'
            ELSE 'pending'
          END::text AS status
        FROM finance.general_charge_issues c
        LEFT JOIN finance.general_charge_parent_verifications link
          ON link.charge_id=c.id AND link.organization_id=c.organization_id
        LEFT JOIN LATERAL (
          SELECT sum(e.amount_cents)::bigint AS paid_cents
          FROM finance.general_charge_events e WHERE e.charge_id=c.id
        ) a ON true;

      -- Default-deny row security is defense in depth in addition to
      -- absent table privileges. Future reader functions must independently
      -- recheck fresh parent or manager authority before accessing rows.
      ALTER TABLE finance.general_charge_templates ENABLE ROW LEVEL SECURITY;
      ALTER TABLE finance.general_charge_issues ENABLE ROW LEVEL SECURITY;
      ALTER TABLE finance.general_charge_parent_verifications ENABLE ROW LEVEL SECURITY;
      ALTER TABLE finance.general_charge_events ENABLE ROW LEVEL SECURITY;

      -- Tables/views and ledger functions are private by default; not one
      -- new grant to web-admin auth, mobile runtime or marketing role.
      REVOKE ALL ON finance.general_charge_templates,
        finance.general_charge_issues,finance.general_charge_parent_verifications,
        finance.general_charge_events,finance.general_charge_balances FROM PUBLIC;
      REVOKE ALL ON FUNCTION finance.guard_general_charge_event(),
        finance.freeze_general_charge_event(),
        finance.freeze_general_charge_issue() FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $general_charge_rollback$
      BEGIN
        IF EXISTS(SELECT 1 FROM finance.general_charge_issues LIMIT 1)
          OR EXISTS(SELECT 1 FROM finance.general_charge_parent_verifications LIMIT 1)
          OR EXISTS(SELECT 1 FROM finance.general_charge_events LIMIT 1)
          OR EXISTS(SELECT 1 FROM finance.general_charge_templates LIMIT 1)
        THEN RAISE EXCEPTION 'forward_recovery_required_for_finance';
        END IF;
      END $general_charge_rollback$;
      DROP VIEW finance.general_charge_balances;
      DROP TABLE finance.general_charge_events;
      DROP TABLE finance.general_charge_parent_verifications;
      DROP TABLE finance.general_charge_issues;
      DROP TABLE finance.general_charge_templates;
      DROP FUNCTION finance.guard_general_charge_event();
      DROP FUNCTION finance.freeze_general_charge_event();
      DROP FUNCTION finance.freeze_general_charge_issue();
    """);
}
