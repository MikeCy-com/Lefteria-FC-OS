using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Encrypted, permanently private quarantine for manager-uploaded player files.
/// No mobile/public file delivery, scanner override, or download is permitted.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110200000_PrivatePlayerDocumentQuarantine")]
public sealed class PrivatePlayerDocumentQuarantine : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE academy_private.encrypted_document_objects (
        id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL
          REFERENCES club.organizations(id) ON DELETE RESTRICT
          CHECK (organization_id IN ('lefteria_fc','lefteria_fc_academy')),
        player_id varchar(128) COLLATE "C" NOT NULL,
        key_id varchar(120) NOT NULL CHECK(length(btrim(key_id)) BETWEEN 1 AND 120),
        ciphertext bytea NOT NULL
          CHECK(octet_length(ciphertext) BETWEEN 1 AND 10485760),
        nonce bytea NOT NULL CHECK(octet_length(nonce)=12),
        auth_tag bytea NOT NULL CHECK(octet_length(auth_tag)=16),
        sha256 char(64) COLLATE "C" NOT NULL
          CHECK(sha256 ~ '^[0-9a-f]{64}$'),
        scan_status varchar(16) NOT NULL DEFAULT 'quarantined'
          CHECK(scan_status IN ('quarantined','approved','rejected')),
        stored_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        FOREIGN KEY (organization_id,player_id)
          REFERENCES club.players(organization_id,id) ON DELETE RESTRICT,
        UNIQUE(id,organization_id,player_id)
      );
      ALTER TABLE academy_private.player_documents
        ADD CONSTRAINT fk_private_document_encrypted_object
          FOREIGN KEY(storage_object_id,organization_id,player_id)
          REFERENCES academy_private.encrypted_document_objects(
            id,organization_id,player_id) ON DELETE RESTRICT;
      REVOKE ALL ON academy_private.encrypted_document_objects FROM PUBLIC;
      ALTER TABLE academy_private.encrypted_document_objects
        ENABLE ROW LEVEL SECURITY;

      CREATE FUNCTION academy_private.quarantine_admin_player_document(
        _digest text,_owner text,_player text,_request uuid,
        _type text,_title text,_issued date,_expires date,
        _mime text,_size bigint,_sha text,_key text,
        _object uuid,_cipher bytea,_nonce bytea,_tag bytea
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $private_upload$
      DECLARE v_actor uuid;v_session uuid;v_role text;v_id uuid:=gen_random_uuid();
        v_existing uuid;v_existing_sha text;v_existing_actor uuid;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
          OR _player ~ '[[:cntrl:]]'
          OR _request IS NULL OR _object IS NULL
          OR _type NOT IN (
            'id_passport','birth_certificate','photo','registration_form',
            'kop_registration','transfer_certificate','medical_cert',
            'vaccination_record','consent_form','gdpr_consent','insurance',
            'school_confirmation','image_rights','other')
          OR _type IS NULL
          OR _title IS NULL OR length(btrim(_title)) NOT BETWEEN 1 AND 200
          OR _title<>btrim(_title) OR _title ~ '[[:cntrl:]]'
          OR _mime NOT IN ('application/pdf','image/jpeg','image/png','image/webp')
          OR _mime IS NULL
          OR _size IS NULL OR _size NOT BETWEEN 1 AND 10485760
          OR _cipher IS NULL OR octet_length(_cipher)<>_size
          OR _nonce IS NULL OR octet_length(_nonce)<>12
          OR _tag IS NULL OR octet_length(_tag)<>16
          OR _sha IS NULL OR _sha !~ '^[0-9a-f]{64}$'
          OR _key IS NULL OR length(btrim(_key)) NOT BETWEEN 1 AND 120
          OR (_issued IS NOT NULL AND _expires IS NOT NULL AND _expires<_issued)
          OR (_type IN ('id_passport','kop_registration','medical_cert',
                 'insurance','school_confirmation') AND _expires IS NULL)
        THEN RAISE EXCEPTION 'private_document_upload_invalid' USING ERRCODE='22023';
        END IF;
        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'private_document_authority_revoked' USING ERRCODE='42501';
        END;
        SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
          RAISE EXCEPTION 'private_document_upload_denied' USING ERRCODE='42501';
        END IF;
        PERFORM 1 FROM club.players p WHERE p.organization_id=_owner
          AND p.id=_player AND p.is_active
          AND NOT p.is_archived AND NOT p.is_transferred
          AND (
            (_owner='lefteria_fc' AND p.team_type='First Team'
              AND EXISTS(SELECT 1 FROM club.teams t
                WHERE t.organization_id=_owner AND t.id=p.team_id))
            OR (_owner='lefteria_fc_academy' AND p.team_type='Academy'
              AND (EXISTS(SELECT 1 FROM club.academy_groups g
                     WHERE g.organization_id=_owner AND g.id=p.primary_academy_group_id)
                OR EXISTS(SELECT 1 FROM club.player_academy_groups m
                     JOIN club.academy_groups g
                       ON g.organization_id=m.organization_id AND g.id=m.academy_group_id
                     WHERE m.organization_id=_owner AND m.player_id=p.id)))
          ) FOR SHARE;
        IF NOT FOUND THEN RAISE EXCEPTION 'private_document_player_unavailable'
          USING ERRCODE='42501'; END IF;

        SELECT d.id,o.sha256,e.actor_id INTO v_existing,v_existing_sha,v_existing_actor
          FROM academy_private.document_audit_events e
          JOIN academy_private.player_documents d ON d.id=e.document_id
          JOIN academy_private.encrypted_document_objects o
            ON o.id=d.storage_object_id
          WHERE e.request_id=_request AND e.action='upload';
        IF FOUND THEN
          IF v_existing_actor<>v_actor OR v_existing_sha<>_sha
            OR NOT EXISTS (SELECT 1 FROM academy_private.player_documents d
              WHERE d.id=v_existing AND d.organization_id=_owner
                AND d.player_id=_player AND d.document_type=_type
                AND d.title=_title AND d.issue_date IS NOT DISTINCT FROM _issued
                AND d.expiry_date IS NOT DISTINCT FROM _expires) THEN
            RAISE EXCEPTION 'private_document_request_conflict' USING ERRCODE='23505';
          END IF;
          RETURN jsonb_build_object('id',v_existing,'organization_id',_owner,
            'player_id',_player,'scan_status','quarantined','replay',true);
        END IF;

        INSERT INTO academy_private.encrypted_document_objects(
          id,organization_id,player_id,key_id,ciphertext,nonce,auth_tag,sha256)
        VALUES(_object,_owner,_player,_key,_cipher,_nonce,_tag,_sha);
        INSERT INTO academy_private.player_documents(
          id,organization_id,player_id,document_type,title,issue_date,
          expiry_date,storage_object_id,encryption_key_id,sha256,
          mime_type,size_bytes,uploaded_by)
        VALUES(v_id,_owner,_player,_type,_title,_issued,_expires,_object,_key,
          _sha,_mime,_size,v_actor);
        INSERT INTO academy_private.document_audit_events(
          id,document_id,actor_id,action,request_id)
        VALUES(gen_random_uuid(),v_id,v_actor,'upload',_request);
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
          'admin.player.document.quarantine','succeeded',_owner,'encrypted');
        RETURN jsonb_build_object('id',v_id,'organization_id',_owner,
          'player_id',_player,'scan_status','quarantined','replay',false);
      END $private_upload$;
      REVOKE ALL ON FUNCTION academy_private.quarantine_admin_player_document(
        text,text,text,uuid,text,text,date,date,text,bigint,text,text,
        uuid,bytea,bytea,bytea) FROM PUBLIC;
    """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$
      BEGIN
        IF EXISTS(SELECT 1 FROM academy_private.encrypted_document_objects) THEN
          RAISE EXCEPTION 'private_encrypted_document_data_requires_forward_recovery';
        END IF;
      END $guard$;
      DROP FUNCTION academy_private.quarantine_admin_player_document(
        text,text,text,uuid,text,text,date,date,text,bigint,text,text,
        uuid,bytea,bytea,bytea);
      ALTER TABLE academy_private.player_documents
        DROP CONSTRAINT fk_private_document_encrypted_object;
      DROP TABLE academy_private.encrypted_document_objects;
    """);
}