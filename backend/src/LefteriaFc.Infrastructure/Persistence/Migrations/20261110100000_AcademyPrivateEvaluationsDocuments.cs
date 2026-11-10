using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Club-wide private evaluation and document vault staging. Deliberately
/// no runtime grants, anonymous links, upload endpoints or player-app access.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110100000_AcademyPrivateEvaluationsDocuments")]
public sealed class AcademyPrivateEvaluationsDocuments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE SCHEMA IF NOT EXISTS academy_private;
      REVOKE ALL ON SCHEMA academy_private FROM PUBLIC;

      -- Dated source-style evaluations. The revision ledger makes corrections
      -- traceable instead of replacing a coach's historical assessment.
      CREATE TABLE academy_private.evaluations (
        id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL
          REFERENCES club.organizations(id) ON DELETE RESTRICT
          CHECK (organization_id IN ('lefteria_fc','lefteria_fc_academy')),
        player_id varchar(128) COLLATE "C" NOT NULL,
        legacy_evaluation_id varchar(128) COLLATE "C",
        author_id uuid NOT NULL REFERENCES identity.admin_accounts(id)
          ON DELETE RESTRICT,
        created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        edit_until timestamptz NOT NULL,
        current_revision integer NOT NULL DEFAULT 1
          CHECK (current_revision BETWEEN 1 AND 10000),
        withdrawn_at timestamptz,
        withdrawn_by uuid REFERENCES identity.admin_accounts(id)
          ON DELETE RESTRICT,
        FOREIGN KEY (organization_id,player_id)
          REFERENCES club.players(organization_id,id) ON DELETE RESTRICT,
        UNIQUE (organization_id,id),
        CHECK (edit_until=created_at+interval '24 hours'),
        CHECK ((withdrawn_at IS NULL)=(withdrawn_by IS NULL))
      );
      CREATE UNIQUE INDEX uq_academy_evaluation_legacy
        ON academy_private.evaluations(organization_id,legacy_evaluation_id)
        WHERE legacy_evaluation_id IS NOT NULL;
      CREATE INDEX ix_academy_evaluation_player
        ON academy_private.evaluations
        (organization_id,player_id,created_at DESC,id);

      -- Source categories: technical, tactical, physical, character, overall.
      -- JSON objects are bounded and private; first-team-only medical/body
      -- metrics are intentionally absent from this Academy schema.
      CREATE TABLE academy_private.evaluation_revisions (
        evaluation_id uuid NOT NULL REFERENCES academy_private.evaluations(id)
          ON DELETE RESTRICT,
        revision integer NOT NULL CHECK (revision BETWEEN 1 AND 10000),
        editor_id uuid NOT NULL REFERENCES identity.admin_accounts(id)
          ON DELETE RESTRICT,
        edited_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        summary varchar(500),
        notes varchar(4000),
        technical jsonb NOT NULL DEFAULT '{}'::jsonb
          CHECK (jsonb_typeof(technical)='object' AND pg_column_size(technical)<=4096),
        tactical jsonb NOT NULL DEFAULT '{}'::jsonb
          CHECK (jsonb_typeof(tactical)='object' AND pg_column_size(tactical)<=4096),
        physical jsonb NOT NULL DEFAULT '{}'::jsonb
          CHECK (jsonb_typeof(physical)='object' AND pg_column_size(physical)<=4096),
        character jsonb NOT NULL DEFAULT '{}'::jsonb
          CHECK (jsonb_typeof(character)='object' AND pg_column_size(character)<=4096),
        overall jsonb NOT NULL DEFAULT '{}'::jsonb
          CHECK (jsonb_typeof(overall)='object' AND pg_column_size(overall)<=4096),
        PRIMARY KEY(evaluation_id,revision)
      );

      -- Metadata only. File bytes belong in separately encrypted, private,
      -- key-managed storage: never in public /uploads or a public URL.
      CREATE TABLE academy_private.player_documents (
        id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL
          REFERENCES club.organizations(id) ON DELETE RESTRICT
          CHECK (organization_id IN ('lefteria_fc','lefteria_fc_academy')),
        player_id varchar(128) COLLATE "C" NOT NULL,
        legacy_document_id varchar(128) COLLATE "C",
        document_type varchar(32) NOT NULL CHECK (document_type IN (
          'id_passport','birth_certificate','photo','registration_form',
          'kop_registration','transfer_certificate','medical_cert',
          'vaccination_record','consent_form','gdpr_consent','insurance',
          'school_confirmation','image_rights','other')),
        title varchar(200) NOT NULL CHECK (length(btrim(title)) BETWEEN 1 AND 200),
        issue_date date,
        expiry_date date,
        storage_object_id uuid NOT NULL UNIQUE,
        encryption_key_id varchar(120) NOT NULL
          CHECK (length(btrim(encryption_key_id)) BETWEEN 1 AND 120),
        sha256 char(64) COLLATE "C" NOT NULL
          CHECK (sha256 ~ '^[0-9a-f]{64}$'),
        mime_type varchar(100) NOT NULL CHECK (mime_type IN (
          'application/pdf','image/jpeg','image/png','image/webp')),
        size_bytes bigint NOT NULL CHECK (size_bytes BETWEEN 1 AND 10485760),
        uploaded_by uuid NOT NULL REFERENCES identity.admin_accounts(id)
          ON DELETE RESTRICT,
        created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        withdrawn_at timestamptz,
        withdrawn_by uuid REFERENCES identity.admin_accounts(id)
          ON DELETE RESTRICT,
        FOREIGN KEY (organization_id,player_id)
          REFERENCES club.players(organization_id,id) ON DELETE RESTRICT,
        UNIQUE (organization_id,id),
        CHECK (expiry_date IS NULL OR issue_date IS NULL OR expiry_date>=issue_date),
        CHECK (document_type NOT IN (
          'id_passport','kop_registration','medical_cert','insurance',
          'school_confirmation') OR expiry_date IS NOT NULL),
        CHECK ((withdrawn_at IS NULL)=(withdrawn_by IS NULL))
      );
      CREATE UNIQUE INDEX uq_academy_document_legacy
        ON academy_private.player_documents(organization_id,legacy_document_id)
        WHERE legacy_document_id IS NOT NULL;
      CREATE INDEX ix_academy_document_expiry
        ON academy_private.player_documents
        (organization_id,expiry_date,id)
        WHERE withdrawn_at IS NULL AND expiry_date IS NOT NULL;
      CREATE INDEX ix_academy_document_player
        ON academy_private.player_documents
        (organization_id,player_id,document_type,id);

      CREATE TABLE academy_private.document_audit_events (
        id uuid PRIMARY KEY,
        document_id uuid NOT NULL REFERENCES academy_private.player_documents(id)
          ON DELETE RESTRICT,
        actor_id uuid NOT NULL REFERENCES identity.admin_accounts(id)
          ON DELETE RESTRICT,
        action varchar(16) NOT NULL CHECK (action IN (
          'upload','view','download','metadata_update','withdraw')),
        occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        request_id uuid NOT NULL UNIQUE
      );

      -- Fail closed: these tables have no runtime role access or RLS policies.
      REVOKE ALL ON ALL TABLES IN SCHEMA academy_private FROM PUBLIC;
      ALTER TABLE academy_private.evaluations ENABLE ROW LEVEL SECURITY;
      ALTER TABLE academy_private.evaluation_revisions ENABLE ROW LEVEL SECURITY;
      ALTER TABLE academy_private.player_documents ENABLE ROW LEVEL SECURITY;
      ALTER TABLE academy_private.document_audit_events ENABLE ROW LEVEL SECURITY;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP TABLE academy_private.document_audit_events;
      DROP TABLE academy_private.player_documents;
      DROP TABLE academy_private.evaluation_revisions;
      DROP TABLE academy_private.evaluations;
      DROP SCHEMA academy_private;
    """);
}
