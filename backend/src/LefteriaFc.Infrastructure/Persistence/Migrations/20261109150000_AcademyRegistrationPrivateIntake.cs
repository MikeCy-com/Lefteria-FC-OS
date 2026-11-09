using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Private Academy registration intake staging. There is deliberately no
/// public POST route, runtime table grant, payment charge or player activation.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261109150000_AcademyRegistrationPrivateIntake")]
public sealed class AcademyRegistrationPrivateIntake : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE SCHEMA IF NOT EXISTS academy_intake;
      REVOKE ALL ON SCHEMA academy_intake FROM PUBLIC;

      -- A registration is an application, never an active player, invoice or
      -- guardian account. The original five steps are a UI workflow, not five
      -- automatically trusted records.
      CREATE TABLE academy_intake.registrations (
        id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL
          REFERENCES club.organizations(id) ON DELETE RESTRICT
          CHECK (organization_id='lefteria_fc_academy'),
        legacy_registration_id varchar(128) COLLATE "C",
        applicant_first_name varchar(100) NOT NULL
          CHECK (length(btrim(applicant_first_name)) BETWEEN 1 AND 100),
        applicant_last_name varchar(100) NOT NULL
          CHECK (length(btrim(applicant_last_name)) BETWEEN 1 AND 100),
        applicant_birth_date date NOT NULL
          CHECK (applicant_birth_date >= DATE '2000-01-01'),
        guardian_name varchar(200) NOT NULL
          CHECK (length(btrim(guardian_name)) BETWEEN 1 AND 200),
        guardian_relationship varchar(80) NOT NULL,
        guardian_phone varchar(32) NOT NULL,
        guardian_email varchar(254) NOT NULL,
        status varchar(20) NOT NULL DEFAULT 'pending'
          CHECK (status IN ('pending','under_review','needs_information',
                            'approved','rejected','withdrawn')),
        status_version integer NOT NULL DEFAULT 1
          CHECK (status_version BETWEEN 1 AND 100000),
        submitted_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        decided_at timestamptz,
        decided_by uuid REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        -- Do not create a roster entry here. An approved registration only
        -- becomes an active player through separate verified onboarding.
        onboarded_player_id varchar(128) COLLATE "C",
        CONSTRAINT fk_registration_onboarded_player
          FOREIGN KEY (organization_id,onboarded_player_id)
          REFERENCES club.players(organization_id,id) ON DELETE RESTRICT,
        CONSTRAINT ck_registration_decision CHECK (
          (status IN ('approved','rejected') AND decided_at IS NOT NULL
             AND decided_by IS NOT NULL)
          OR (status NOT IN ('approved','rejected') AND decided_at IS NULL
             AND decided_by IS NULL)),
        CONSTRAINT ck_registration_onboarding CHECK (
          onboarded_player_id IS NULL OR status='approved'),
        UNIQUE (organization_id,id)
      );
      CREATE UNIQUE INDEX uq_academy_registration_legacy
        ON academy_intake.registrations(organization_id,legacy_registration_id)
        WHERE legacy_registration_id IS NOT NULL;
      CREATE INDEX ix_academy_registration_review
        ON academy_intake.registrations(organization_id,status,submitted_at,id);

      -- Preserve independent evidence for required/optional guardian choices.
      -- Media consent must allow refusal, not default to approval.
      CREATE TABLE academy_intake.registration_consents (
        registration_id uuid NOT NULL REFERENCES academy_intake.registrations(id)
          ON DELETE RESTRICT,
        consent_kind varchar(32) NOT NULL CHECK (consent_kind IN (
          'participation','medical_authorization','privacy_notice',
          'media','communications','liability','financial')),
        granted boolean NOT NULL,
        policy_version varchar(80) NOT NULL
          CHECK (length(btrim(policy_version)) BETWEEN 1 AND 80),
        recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        evidence_digest char(64) COLLATE "C" NOT NULL
          CHECK (evidence_digest ~ '^[0-9a-f]{64}$'),
        PRIMARY KEY (registration_id,consent_kind)
      );

      -- Medical responses and signatures must never appear in ordinary
      -- registration listings. Ciphertext only, with external key management;
      -- no application role can read this table after migration.
      CREATE TABLE academy_intake.registration_sealed_evidence (
        registration_id uuid PRIMARY KEY
          REFERENCES academy_intake.registrations(id) ON DELETE RESTRICT,
        encryption_key_id varchar(120) NOT NULL,
        evidence_ciphertext bytea NOT NULL
          CHECK (octet_length(evidence_ciphertext) BETWEEN 32 AND 65536),
        evidence_sha256 char(64) COLLATE "C" NOT NULL
          CHECK (evidence_sha256 ~ '^[0-9a-f]{64}$'),
        stored_at timestamptz NOT NULL DEFAULT clock_timestamp()
      );

      CREATE TABLE academy_intake.registration_review_events (
        id uuid PRIMARY KEY,
        registration_id uuid NOT NULL
          REFERENCES academy_intake.registrations(id) ON DELETE RESTRICT,
        actor_id uuid NOT NULL
          REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        previous_status varchar(20),
        next_status varchar(20) NOT NULL,
        status_version integer NOT NULL CHECK (status_version>0),
        occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        reason_code varchar(80) NOT NULL,
        UNIQUE (registration_id,status_version)
      );

      REVOKE ALL ON ALL TABLES IN SCHEMA academy_intake FROM PUBLIC;
      ALTER TABLE academy_intake.registrations ENABLE ROW LEVEL SECURITY;
      ALTER TABLE academy_intake.registration_consents ENABLE ROW LEVEL SECURITY;
      ALTER TABLE academy_intake.registration_sealed_evidence ENABLE ROW LEVEL SECURITY;
      ALTER TABLE academy_intake.registration_review_events ENABLE ROW LEVEL SECURITY;
      -- With no policies and no runtime grants the tables fail closed.
      -- No anonymous submission, auto-approval, billing, or notification exists.
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP TABLE academy_intake.registration_review_events;
      DROP TABLE academy_intake.registration_sealed_evidence;
      DROP TABLE academy_intake.registration_consents;
      DROP TABLE academy_intake.registrations;
      DROP SCHEMA academy_intake;
    """);
}
