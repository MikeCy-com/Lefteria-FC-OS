using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// A private, zero-grant holding vault for registration-time child identity
/// evidence. Each source document is assigned to exactly one registration,
/// then moved into THAT player's private Documents when the authorized
/// Academy approval transaction creates a roster placement.
/// The public upload API is deliberately NOT enabled by this migration.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261122173000_AcademyRegistrationDocumentPromotion")]
public sealed class AcademyRegistrationDocumentPromotion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE academy_intake.registration_document_escrow (
        id uuid PRIMARY KEY CHECK(id<>'00000000-0000-0000-0000-000000000000'::uuid),
        registration_id uuid NOT NULL
          REFERENCES academy_intake.registrations(id) ON DELETE RESTRICT,
        organization_id varchar(32) NOT NULL DEFAULT 'lefteria_fc_academy'
          CHECK(organization_id='lefteria_fc_academy'),
        document_slot varchar(24) NOT NULL CHECK(document_slot IN
          ('id_front','id_back','birth_certificate','passport_photo')),
        document_type varchar(32) NOT NULL CHECK(
          (document_slot IN ('id_front','id_back') AND document_type='id_passport')
          OR (document_slot='birth_certificate' AND document_type='birth_certificate')
          OR (document_slot='passport_photo' AND document_type='photo')),
        expiry_date date,
        mime_type varchar(40) NOT NULL CHECK(mime_type IN
          ('application/pdf','image/jpeg','image/png','image/webp')),
        size_bytes bigint NOT NULL CHECK(size_bytes BETWEEN 1 AND 10485760),
        encryption_key_id varchar(120) NOT NULL
          CHECK(length(btrim(encryption_key_id)) BETWEEN 1 AND 120),
        sha256 char(64) COLLATE "C" NOT NULL
          CHECK(sha256 ~ '^[0-9a-f]{64}$'),
        ciphertext bytea,
        nonce bytea,
        auth_tag bytea,
        created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        promoted_at timestamptz,
        promoted_player_id varchar(128) COLLATE "C",
        promoted_document_id uuid UNIQUE,
        UNIQUE(registration_id,document_slot),
        CHECK(document_type<>'id_passport' OR expiry_date IS NOT NULL),
        CHECK(
          (promoted_at IS NULL AND promoted_player_id IS NULL
            AND promoted_document_id IS NULL AND ciphertext IS NOT NULL
            AND octet_length(ciphertext)=size_bytes
            AND nonce IS NOT NULL AND auth_tag IS NOT NULL
            AND octet_length(nonce)=12 AND octet_length(auth_tag)=16)
          OR
          (promoted_at IS NOT NULL AND promoted_player_id IS NOT NULL
            AND promoted_document_id IS NOT NULL AND ciphertext IS NULL
            AND nonce IS NULL AND auth_tag IS NULL)),
        FOREIGN KEY(organization_id,promoted_player_id)
          REFERENCES club.players(organization_id,id) ON DELETE RESTRICT,
        FOREIGN KEY(promoted_document_id)
          REFERENCES academy_private.player_documents(id) ON DELETE RESTRICT
      );
      CREATE INDEX ix_registration_document_escrow_unpromoted
        ON academy_intake.registration_document_escrow(registration_id)
        WHERE promoted_at IS NULL;
      REVOKE ALL ON academy_intake.registration_document_escrow FROM PUBLIC;
      ALTER TABLE academy_intake.registration_document_escrow ENABLE ROW LEVEL SECURITY;

      CREATE TABLE academy_intake.registration_document_promotions (
        escrow_id uuid PRIMARY KEY
          REFERENCES academy_intake.registration_document_escrow(id) ON DELETE RESTRICT,
        registration_id uuid NOT NULL
          REFERENCES academy_intake.registrations(id) ON DELETE RESTRICT,
        player_document_id uuid NOT NULL UNIQUE
          REFERENCES academy_private.player_documents(id) ON DELETE RESTRICT,
        player_id varchar(128) COLLATE "C" NOT NULL,
        organization_id varchar(32) NOT NULL DEFAULT 'lefteria_fc_academy'
          CHECK(organization_id='lefteria_fc_academy'),
        approved_by uuid NOT NULL
          REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        transferred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        FOREIGN KEY(organization_id,player_id)
          REFERENCES club.players(organization_id,id) ON DELETE RESTRICT
      );
      REVOKE ALL ON academy_intake.registration_document_promotions FROM PUBLIC;
      ALTER TABLE academy_intake.registration_document_promotions ENABLE ROW LEVEL SECURITY;

      -- Only the real, authorized placement insert is allowed to attach
      -- evidence to a player. No client chooses destination player_id.
      CREATE FUNCTION academy_intake.promote_documents_on_enrollment()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $promote$
      DECLARE v_doc record; v_id uuid; v_title text;
      BEGIN
        IF TG_TABLE_SCHEMA<>'academy_intake'
          OR TG_TABLE_NAME<>'registration_placements'
          OR NEW.organization_id<>'lefteria_fc_academy'
          OR NOT EXISTS (
            SELECT 1 FROM academy_intake.registrations r
            WHERE r.id=NEW.registration_id
              AND r.organization_id=NEW.organization_id)
        THEN RAISE EXCEPTION 'registration_document_placement_invalid'
          USING ERRCODE='42501'; END IF;

        FOR v_doc IN
          SELECT * FROM academy_intake.registration_document_escrow e
          WHERE e.registration_id=NEW.registration_id
            AND e.organization_id=NEW.organization_id
            AND e.promoted_at IS NULL
          ORDER BY e.document_slot FOR UPDATE
        LOOP
          v_id:=gen_random_uuid();
          v_title:=CASE v_doc.document_slot
            WHEN 'id_front' THEN 'Ταυτότητα - εμπρόσθια όψη'
            WHEN 'id_back' THEN 'Ταυτότητα - οπίσθια όψη'
            WHEN 'birth_certificate' THEN 'Πιστοποιητικό γέννησης'
            ELSE 'Φωτογραφία τύπου διαβατηρίου' END;
          -- The same ciphertext is moved into the existing player vault;
          -- quarantine remains in effect, no scanner bypass/download grant.
          INSERT INTO academy_private.encrypted_document_objects(
            id,organization_id,player_id,key_id,ciphertext,
            nonce,auth_tag,sha256,scan_status)
          VALUES(v_doc.id,NEW.organization_id,NEW.player_id,
            v_doc.encryption_key_id,v_doc.ciphertext,
            v_doc.nonce,v_doc.auth_tag,v_doc.sha256,'quarantined');
          INSERT INTO academy_private.player_documents(
            id,organization_id,player_id,document_type,title,issue_date,
            expiry_date,storage_object_id,encryption_key_id,sha256,
            mime_type,size_bytes,uploaded_by)
          VALUES(v_id,NEW.organization_id,NEW.player_id,v_doc.document_type,
            v_title,NULL,v_doc.expiry_date,v_doc.id,v_doc.encryption_key_id,
            v_doc.sha256,v_doc.mime_type,v_doc.size_bytes,NEW.enrolled_by);
          INSERT INTO academy_private.document_audit_events(
            id,document_id,actor_id,action,request_id)
          VALUES(gen_random_uuid(),v_id,NEW.enrolled_by,'upload',gen_random_uuid());
          INSERT INTO academy_intake.registration_document_promotions(
            escrow_id,registration_id,player_document_id,player_id,approved_by)
          VALUES(v_doc.id,NEW.registration_id,v_id,NEW.player_id,NEW.enrolled_by);
          -- Delete the pre-approval encrypted copy; its single durable
          -- encrypted object now belongs to the exact approved player.
          UPDATE academy_intake.registration_document_escrow SET
            ciphertext=NULL,nonce=NULL,auth_tag=NULL,promoted_at=clock_timestamp(),
            promoted_player_id=NEW.player_id,promoted_document_id=v_id
          WHERE id=v_doc.id AND registration_id=NEW.registration_id
            AND promoted_at IS NULL;
        END LOOP;
        RETURN NEW;
      END $promote$;
      REVOKE ALL ON FUNCTION academy_intake.promote_documents_on_enrollment()
        FROM PUBLIC;
      CREATE TRIGGER promote_registration_documents_at_placement
        AFTER INSERT ON academy_intake.registration_placements
        FOR EACH ROW EXECUTE FUNCTION academy_intake.promote_documents_on_enrollment();
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $safety$
      BEGIN
        IF EXISTS(SELECT 1 FROM academy_intake.registration_document_escrow)
          OR EXISTS(SELECT 1 FROM academy_intake.registration_document_promotions)
        THEN RAISE EXCEPTION 'registration_document_evidence_requires_forward_recovery'
          USING ERRCODE='23505'; END IF;
      END $safety$;
      DROP TRIGGER promote_registration_documents_at_placement
        ON academy_intake.registration_placements;
      DROP FUNCTION academy_intake.promote_documents_on_enrollment();
      DROP TABLE academy_intake.registration_document_promotions;
      DROP TABLE academy_intake.registration_document_escrow;
    """);
}
