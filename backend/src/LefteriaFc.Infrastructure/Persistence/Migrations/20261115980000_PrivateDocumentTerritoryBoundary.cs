using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Enforce immutable creation-location provenance for private player document
/// metadata and encrypted upload. Managers see only currently authorized
/// records from their current site; legacy and prior-site data stay hidden.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115980000_PrivateDocumentTerritoryBoundary")]
public sealed class PrivateDocumentTerritoryBoundary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""

      -- Legacy document metadata has no proven origin. Never backfill from
      -- today's roster or infer where a sensitive file was uploaded.
      ALTER TABLE academy_private.player_documents
        ADD COLUMN origin_scope_id uuid,
        ADD COLUMN origin_city_id uuid,
        ADD COLUMN origin_location_id uuid,
        ADD COLUMN origin_recorded_at timestamptz,
        ADD CONSTRAINT ck_document_origin_complete CHECK(
          (origin_scope_id IS NULL AND origin_city_id IS NULL
            AND origin_location_id IS NULL AND origin_recorded_at IS NULL)
          OR (origin_scope_id IS NOT NULL AND origin_recorded_at IS NOT NULL
            AND ((organization_id='lefteria_fc' AND origin_city_id IS NULL
                    AND origin_location_id IS NULL)
              OR (organization_id='lefteria_fc_academy'
                    AND origin_city_id IS NOT NULL
                    AND origin_location_id IS NOT NULL)))
        ),
        ADD CONSTRAINT fk_document_origin_scope
          FOREIGN KEY (organization_id,origin_scope_id)
            REFERENCES club.football_operational_scopes(organization_id,id)
            ON DELETE RESTRICT,
        ADD CONSTRAINT fk_document_origin_city_scope
          FOREIGN KEY (origin_city_id,origin_scope_id)
            REFERENCES club.football_operational_scopes(academy_city_id,id)
            ON DELETE RESTRICT,
        ADD CONSTRAINT fk_document_origin_city_location
          FOREIGN KEY (origin_city_id,origin_location_id)
            REFERENCES club.football_locations(city_id,id) ON DELETE RESTRICT;
      CREATE INDEX ix_document_origin_territory
        ON academy_private.player_documents
          (organization_id,player_id,origin_scope_id,origin_location_id)
        WHERE origin_scope_id IS NOT NULL;

      -- Reuse private evaluation's proven shared location resolver, exact
      -- manager grant checks and immediate revocation lock. No new grants.
      CREATE FUNCTION academy_private.capture_document_origin()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $capture_document$
      DECLARE v_role text;v_scope uuid;v_city uuid;v_location uuid;
      BEGIN
        IF TG_TABLE_SCHEMA<>'academy_private'
          OR TG_TABLE_NAME<>'player_documents'
        THEN RAISE EXCEPTION 'document_origin_invalid_trigger'
          USING ERRCODE='42501'; END IF;
        IF TG_OP='UPDATE' THEN
          IF ROW(NEW.origin_scope_id,NEW.origin_city_id,
                 NEW.origin_location_id,NEW.origin_recorded_at)
             IS DISTINCT FROM ROW(OLD.origin_scope_id,OLD.origin_city_id,
                 OLD.origin_location_id,OLD.origin_recorded_at)
          THEN RAISE EXCEPTION 'document_origin_immutable'
            USING ERRCODE='42501'; END IF;
          RETURN NEW;
        END IF;
        IF NEW.origin_scope_id IS NOT NULL OR NEW.origin_city_id IS NOT NULL
          OR NEW.origin_location_id IS NOT NULL
          OR NEW.origin_recorded_at IS NOT NULL
        THEN RAISE EXCEPTION 'document_origin_server_only'
          USING ERRCODE='42501'; END IF;
        SELECT a.role INTO v_role FROM identity.admin_accounts a
          WHERE a.id=NEW.uploaded_by;
        IF v_role='manager' THEN
          SELECT t.scope_id,t.city_id,t.location_id
            INTO v_scope,v_city,v_location
          FROM academy_private.authorize_private_player_manager(
            NEW.uploaded_by,NEW.organization_id,NEW.player_id) t;
        ELSIF v_role='super_admin' THEN
          SELECT t.scope_id,t.city_id,t.location_id
            INTO v_scope,v_city,v_location
          FROM academy_private.resolve_private_player_territory(
            NEW.organization_id,NEW.player_id) t;
        ELSE RAISE EXCEPTION 'document_origin_invalid_actor'
          USING ERRCODE='42501';
        END IF;
        IF v_scope IS NOT NULL THEN
          NEW.origin_scope_id:=v_scope;
          NEW.origin_city_id:=v_city;
          NEW.origin_location_id:=v_location;
          NEW.origin_recorded_at:=clock_timestamp();
        END IF;
        RETURN NEW;
      END $capture_document$;
      REVOKE ALL ON FUNCTION academy_private.capture_document_origin() FROM PUBLIC;
      CREATE TRIGGER tr_document_origin_capture
        BEFORE INSERT ON academy_private.player_documents
        FOR EACH ROW EXECUTE FUNCTION academy_private.capture_document_origin();
      CREATE TRIGGER tr_document_origin_immutable
        BEFORE UPDATE ON academy_private.player_documents
        FOR EACH ROW EXECUTE FUNCTION academy_private.capture_document_origin();

      CREATE OR REPLACE FUNCTION academy_private.read_admin_player_document_metadata(
        _digest text,_owner text,_player text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $academy_document_read$
      DECLARE
        v_role text; v_actor uuid; v_session uuid;
        v_count integer; v_rows jsonb;
        v_scope uuid;v_city uuid;v_location uuid;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
           OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
           OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
           OR _player ~ '[[:cntrl:]]' THEN
          RAISE EXCEPTION 'document_scope_invalid' USING ERRCODE='22023';
        END IF;
        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'document_authority_revoked' USING ERRCODE='42501';
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
          RAISE EXCEPTION 'document_access_denied' USING ERRCODE='42501';
        END IF;
        IF NOT EXISTS (
          SELECT 1 FROM club.players p WHERE p.organization_id=_owner
            AND p.id=_player
            AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
            AND (
              (_owner='lefteria_fc' AND p.team_type='First Team'
                AND p.team_id IS NOT NULL
                AND EXISTS (SELECT 1 FROM club.teams t
                  WHERE t.organization_id=_owner AND t.id=p.team_id))
              OR
              (_owner='lefteria_fc_academy' AND p.team_type='Academy'
                AND (
                  EXISTS (SELECT 1 FROM club.academy_groups g
                    WHERE g.organization_id=_owner AND g.id=p.primary_academy_group_id)
                  OR EXISTS (SELECT 1 FROM club.player_academy_groups m
                    JOIN club.academy_groups g
                      ON g.organization_id=m.organization_id
                        AND g.id=m.academy_group_id
                    WHERE m.organization_id=_owner AND m.player_id=p.id)
                ))
            )
        ) THEN
          RAISE EXCEPTION 'document_player_not_available' USING ERRCODE='42501';
        END IF;
        IF v_role='manager' THEN
          SELECT t.scope_id,t.city_id,t.location_id
            INTO v_scope,v_city,v_location
          FROM academy_private.authorize_private_player_manager(
            v_actor,_owner,_player) t;
        END IF;
        SELECT count(*) INTO v_count FROM academy_private.player_documents d
          WHERE d.organization_id=_owner AND d.player_id=_player
            AND d.withdrawn_at IS NULL
          AND (v_role='super_admin' OR
            (d.origin_scope_id=v_scope AND
             (_owner='lefteria_fc' OR
              (d.origin_city_id=v_city AND d.origin_location_id=v_location))));
        IF v_count>100 THEN
          RAISE EXCEPTION 'document_list_too_large' USING ERRCODE='54000';
        END IF;
        -- Audit every disclosed document before returning any metadata.
        -- An error rolls back both audit and disclosure.
        INSERT INTO academy_private.document_audit_events (
          id,document_id,actor_id,action,request_id)
        SELECT gen_random_uuid(),d.id,v_actor,'view',gen_random_uuid()
        FROM academy_private.player_documents d
        WHERE d.organization_id=_owner AND d.player_id=_player
          AND d.withdrawn_at IS NULL
          AND (v_role='super_admin' OR
            (d.origin_scope_id=v_scope AND
             (_owner='lefteria_fc' OR
              (d.origin_city_id=v_city AND d.origin_location_id=v_location))));
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
          'admin.academy.documents.list','succeeded',_owner,'metadata_only');

        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'id',d.id,'document_type',d.document_type,'title',d.title,
          'issue_date',d.issue_date,'expiry_date',d.expiry_date,
          'created_at',d.created_at,
          'expires_in_days',CASE WHEN d.expiry_date IS NULL THEN NULL
            ELSE d.expiry_date-(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date END,
          'status',CASE
            WHEN d.expiry_date IS NULL THEN 'no_expiry'
            WHEN d.expiry_date<(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date THEN 'expired'
            WHEN d.expiry_date<=(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date+7 THEN 'critical'
            WHEN d.expiry_date<=(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date+30 THEN 'warning'
            WHEN d.expiry_date<=(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date+60 THEN 'notice'
            WHEN d.expiry_date<=(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date+90 THEN 'watch'
            ELSE 'valid' END)
          ORDER BY d.created_at DESC,d.id),'[]'::jsonb)
        INTO v_rows FROM academy_private.player_documents d
        WHERE d.organization_id=_owner AND d.player_id=_player
          AND d.withdrawn_at IS NULL
          AND (v_role='super_admin' OR
            (d.origin_scope_id=v_scope AND
             (_owner='lefteria_fc' OR
              (d.origin_city_id=v_city AND d.origin_location_id=v_location))));
        RETURN jsonb_build_object('organization_id',_owner,
          'player_id',_player,'total',v_count,'documents',v_rows);
      END $academy_document_read$;
      CREATE OR REPLACE FUNCTION academy_private.quarantine_admin_player_document(
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

        -- Check manager grants before any idempotent replay or encrypted
        -- object is stored; location transfers cannot resurrect old uploads.
        IF v_role='manager' THEN
          PERFORM 1 FROM academy_private.authorize_private_player_manager(
            v_actor,_owner,_player);
        END IF;
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

      REVOKE ALL ON FUNCTION academy_private.read_admin_player_document_metadata(
        text,text,text) FROM PUBLIC;
      REVOKE ALL ON FUNCTION academy_private.quarantine_admin_player_document(
        text,text,text,uuid,text,text,date,date,text,bigint,text,text,
        uuid,bytea,bytea,bytea) FROM PUBLIC;

    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP TRIGGER tr_document_origin_immutable ON academy_private.player_documents;
      DROP TRIGGER tr_document_origin_capture ON academy_private.player_documents;
      DROP FUNCTION academy_private.capture_document_origin();
      CREATE OR REPLACE FUNCTION academy_private.read_admin_player_document_metadata(
        _digest text,_owner text,_player text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $academy_document_read$
      DECLARE
        v_role text; v_actor uuid; v_session uuid;
        v_count integer; v_rows jsonb;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
           OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
           OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
           OR _player ~ '[[:cntrl:]]' THEN
          RAISE EXCEPTION 'document_scope_invalid' USING ERRCODE='22023';
        END IF;
        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'document_authority_revoked' USING ERRCODE='42501';
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
          RAISE EXCEPTION 'document_access_denied' USING ERRCODE='42501';
        END IF;
        IF NOT EXISTS (
          SELECT 1 FROM club.players p WHERE p.organization_id=_owner
            AND p.id=_player
            AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
            AND (
              (_owner='lefteria_fc' AND p.team_type='First Team'
                AND p.team_id IS NOT NULL
                AND EXISTS (SELECT 1 FROM club.teams t
                  WHERE t.organization_id=_owner AND t.id=p.team_id))
              OR
              (_owner='lefteria_fc_academy' AND p.team_type='Academy'
                AND (
                  EXISTS (SELECT 1 FROM club.academy_groups g
                    WHERE g.organization_id=_owner AND g.id=p.primary_academy_group_id)
                  OR EXISTS (SELECT 1 FROM club.player_academy_groups m
                    JOIN club.academy_groups g
                      ON g.organization_id=m.organization_id
                        AND g.id=m.academy_group_id
                    WHERE m.organization_id=_owner AND m.player_id=p.id)
                ))
            )
        ) THEN
          RAISE EXCEPTION 'document_player_not_available' USING ERRCODE='42501';
        END IF;
        SELECT count(*) INTO v_count FROM academy_private.player_documents d
          WHERE d.organization_id=_owner AND d.player_id=_player
            AND d.withdrawn_at IS NULL;
        IF v_count>100 THEN
          RAISE EXCEPTION 'document_list_too_large' USING ERRCODE='54000';
        END IF;
        -- Audit every disclosed document before returning any metadata.
        -- An error rolls back both audit and disclosure.
        INSERT INTO academy_private.document_audit_events (
          id,document_id,actor_id,action,request_id)
        SELECT gen_random_uuid(),d.id,v_actor,'view',gen_random_uuid()
        FROM academy_private.player_documents d
        WHERE d.organization_id=_owner AND d.player_id=_player
          AND d.withdrawn_at IS NULL;
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
          'admin.academy.documents.list','succeeded',_owner,'metadata_only');

        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'id',d.id,'document_type',d.document_type,'title',d.title,
          'issue_date',d.issue_date,'expiry_date',d.expiry_date,
          'created_at',d.created_at,
          'expires_in_days',CASE WHEN d.expiry_date IS NULL THEN NULL
            ELSE d.expiry_date-(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date END,
          'status',CASE
            WHEN d.expiry_date IS NULL THEN 'no_expiry'
            WHEN d.expiry_date<(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date THEN 'expired'
            WHEN d.expiry_date<=(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date+7 THEN 'critical'
            WHEN d.expiry_date<=(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date+30 THEN 'warning'
            WHEN d.expiry_date<=(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date+60 THEN 'notice'
            WHEN d.expiry_date<=(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date+90 THEN 'watch'
            ELSE 'valid' END)
          ORDER BY d.created_at DESC,d.id),'[]'::jsonb)
        INTO v_rows FROM academy_private.player_documents d
        WHERE d.organization_id=_owner AND d.player_id=_player
          AND d.withdrawn_at IS NULL;
        RETURN jsonb_build_object('organization_id',_owner,
          'player_id',_player,'total',v_count,'documents',v_rows);
      END $academy_document_read$;
      CREATE OR REPLACE FUNCTION academy_private.quarantine_admin_player_document(
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
      DROP INDEX academy_private.ix_document_origin_territory;
      ALTER TABLE academy_private.player_documents
        DROP CONSTRAINT fk_document_origin_city_location,
        DROP CONSTRAINT fk_document_origin_city_scope,
        DROP CONSTRAINT fk_document_origin_scope,
        DROP CONSTRAINT ck_document_origin_complete,
        DROP COLUMN origin_recorded_at,
        DROP COLUMN origin_location_id,
        DROP COLUMN origin_city_id,
        DROP COLUMN origin_scope_id;
    """);
}
