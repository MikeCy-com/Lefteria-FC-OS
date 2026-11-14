-- Operator-only, session-local helper. Loading this file changes no persistent schema.
-- Never install as a database migration or grant it to an application role.
CREATE OR REPLACE FUNCTION pg_temp.prepare_preview_owner_copy(
    expected_database text,
    expected_import_sha256 text,
    source_legacy_id uuid,
    expected_source_sha256 text,
    expected_username text,
    expected_email text,
    approve_password_only boolean
) RETURNS text
LANGUAGE plpgsql SECURITY INVOKER
SET search_path = pg_catalog, pg_temp
AS $recovery$
DECLARE
    source_row migration.account_source_documents%ROWTYPE;
    source jsonb;
    source_id uuid;
    copy_id uuid := gen_random_uuid();
    source_phone text;
    audit_digest text;
    issue_codes text[];
BEGIN
    IF current_database() <> 'lefteria_os_preview'
       OR expected_database IS DISTINCT FROM current_database() THEN
        RAISE EXCEPTION USING MESSAGE='preview_recovery_wrong_database';
    END IF;
    IF approve_password_only IS DISTINCT FROM true THEN
        RAISE EXCEPTION USING MESSAGE='preview_password_only_approval_required';
    END IF;
    IF current_user <> pg_get_userbyid((SELECT relowner FROM pg_class WHERE oid='identity.admin_accounts'::regclass))
       OR current_user <> pg_get_userbyid((SELECT relowner FROM pg_class WHERE oid='migration.account_source_documents'::regclass)) THEN
        RAISE EXCEPTION USING MESSAGE='preview_recovery_owner_connection_required';
    END IF;
    IF expected_import_sha256 IS NULL OR expected_import_sha256 !~ '^[0-9a-f]{64}$'
       OR expected_source_sha256 IS NULL OR expected_source_sha256 !~ '^[0-9a-f]{64}$'
       OR source_legacy_id IS NULL OR source_legacy_id = '00000000-0000-0000-0000-000000000000'::uuid
       OR expected_username IS NULL OR length(expected_username) NOT BETWEEN 1 AND 80
       OR expected_email IS NULL OR length(expected_email) NOT BETWEEN 3 AND 320 THEN
        RAISE EXCEPTION USING MESSAGE='preview_recovery_invalid_confirmation';
    END IF;

    PERFORM set_config('lock_timeout','10s',true);
    PERFORM pg_advisory_xact_lock(5495884701258);
    LOCK TABLE identity.admin_accounts, identity.admin_organization_grants,
        identity.admin_staff_links, migration.account_import_state,
        migration.account_source_documents, migration.account_import_issues,
        migration.legacy_account_mappings IN SHARE ROW EXCLUSIVE MODE;
    LOCK TABLE club.staff, migration.staff_source_documents IN SHARE MODE;

    IF NOT EXISTS (
        SELECT 1 FROM migration.account_import_state
        WHERE id='initial' AND digest=expected_import_sha256 AND environment='staging'
    ) THEN
        RAISE EXCEPTION USING MESSAGE='preview_recovery_import_mismatch';
    END IF;
    IF (SELECT count(*) FROM migration.account_source_documents
        WHERE collection='admin_users' AND legacy_id=source_legacy_id::text) <> 1 THEN
        RAISE EXCEPTION USING MESSAGE='preview_recovery_source_not_unique';
    END IF;
    SELECT * INTO STRICT source_row FROM migration.account_source_documents
    WHERE collection='admin_users' AND legacy_id=source_legacy_id::text;
    IF source_row.import_digest IS DISTINCT FROM expected_import_sha256
       OR source_row.disposition <> 'quarantined' OR source_row.target_id IS NOT NULL
       OR encode(sha256(convert_to(source_row.source_json,'UTF8')),'hex') IS DISTINCT FROM expected_source_sha256 THEN
        RAISE EXCEPTION USING MESSAGE='preview_recovery_source_mismatch';
    END IF;
    SELECT array_agg(code ORDER BY code) INTO issue_codes
    FROM migration.account_import_issues
    WHERE collection=source_row.collection AND ordinal=source_row.ordinal;
    IF issue_codes IS DISTINCT FROM ARRAY['admin_staff_reference']::text[] THEN
        RAISE EXCEPTION USING MESSAGE='preview_recovery_issue_mismatch';
    END IF;

    source := source_row.source_json::jsonb;
    IF source->>'id' IS DISTINCT FROM source_legacy_id::text
       OR source->>'username' IS DISTINCT FROM expected_username
       OR source->>'email' IS DISTINCT FROM expected_email
       OR source->>'role' IS DISTINCT FROM 'super_admin'
       OR source->'is_active' IS DISTINCT FROM 'true'::jsonb
       OR source->'otp_2fa_enabled' IS DISTINCT FROM 'true'::jsonb
       OR (source ? 'invite_pending' AND source->'invite_pending' IS DISTINCT FROM 'false'::jsonb) THEN
        RAISE EXCEPTION USING MESSAGE='preview_recovery_identity_mismatch';
    END IF;
    -- This recovery covers only the reviewed legacy-super-admin default.
    -- Explicit/empty/unknown scopes and operational assignments need another review.
    IF source ? 'organization_ids' OR source ? 'organization_id'
       OR source ? 'assigned_team_id' OR source ? 'assigned_academy_group_id'
       OR source->>'active_organization_id' NOT IN ('lefteria_fc','lefteria_fc_academy')
       OR source->>'active_organization_id' IS NULL THEN
        RAISE EXCEPTION USING MESSAGE='preview_recovery_scope_requires_review';
    END IF;
    IF jsonb_typeof(source->'staff_id') IS DISTINCT FROM 'string'
       OR length(source->>'staff_id') NOT BETWEEN 1 AND 128
       OR EXISTS (SELECT 1 FROM club.staff WHERE id=source->>'staff_id')
       OR EXISTS (SELECT 1 FROM migration.staff_source_documents
                  WHERE source_id=source->>'staff_id'
                     OR source_json::jsonb->>'admin_user_id'=source_legacy_id::text) THEN
        RAISE EXCEPTION USING MESSAGE='preview_recovery_staff_reference_changed';
    END IF;
    IF jsonb_typeof(source->'password_hash') IS DISTINCT FROM 'string'
       OR (source->>'password_hash') !~ '^\$2[aby]\$(0[4-9]|[12][0-9]|3[01])\$[./A-Za-z0-9]{53}$'
       OR jsonb_typeof(source->'name') IS DISTINCT FROM 'string'
       OR length(source->>'name') > 200
       OR (source->>'name') ~ '[[:cntrl:]]'
       OR expected_username ~ '[[:cntrl:]]'
       OR expected_username <> btrim(expected_username)
       OR source->>'country' IS DISTINCT FROM 'CY'
       OR jsonb_typeof(source->'phone') IS DISTINCT FROM 'string'
       OR (source->>'phone') !~ '^[1-9][0-9]{7}$' THEN
        RAISE EXCEPTION USING MESSAGE='preview_recovery_credential_or_profile_invalid';
    END IF;
    source_phone := '357' || (source->>'phone');
    IF EXISTS (SELECT 1 FROM identity.admin_accounts
               WHERE username=expected_username OR legacy_id=source_legacy_id::text OR id=source_legacy_id)
       OR EXISTS (SELECT 1 FROM migration.legacy_account_mappings
                  WHERE collection='admin_users' AND legacy_id=source_legacy_id::text)
       OR EXISTS (SELECT 1 FROM migration.account_source_documents
                  WHERE collection='admin_users' AND ordinal<>source_row.ordinal
                    AND source_json::jsonb->>'username'=expected_username) THEN
        RAISE EXCEPTION USING MESSAGE='preview_recovery_account_collision';
    END IF;
    IF (SELECT count(*) FROM club.organizations WHERE id IN ('lefteria_fc','lefteria_fc_academy')) <> 2 THEN
        RAISE EXCEPTION USING MESSAGE='preview_recovery_organizations_missing';
    END IF;
    audit_digest := encode(sha256(convert_to(expected_import_sha256 || ':' ||
        expected_source_sha256 || ':' || source_legacy_id::text,'UTF8')),'hex');
    IF EXISTS (SELECT 1 FROM audit.security_events
               WHERE action='admin.preview_password_copy' AND subject_digest=audit_digest) THEN
        RAISE EXCEPTION USING MESSAGE='preview_recovery_already_recorded';
    END IF;

    -- A separate testing identity: no imported mapping, staff assignment,
    -- founding-owner marker or mobile-management binding is manufactured.
    INSERT INTO identity.admin_accounts
        (id,username,display_name,role,password_hash,is_active,legacy_id,
         normalized_phone,invite_pending,otp2fa_enabled,active_organization_id)
    VALUES (copy_id,expected_username,source->>'name','super_admin',source->>'password_hash',
            true,NULL,source_phone,false,false,NULL);
    INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id)
    VALUES (copy_id,'lefteria_fc'),(copy_id,'lefteria_fc_academy');
    UPDATE identity.admin_accounts SET active_organization_id=source->>'active_organization_id'
    WHERE id=copy_id;
    INSERT INTO audit.security_events
        (id,occurred_at,actor_identity_id,action,outcome,subject_digest,reason_code,actor_realm)
    VALUES (gen_random_uuid(),statement_timestamp(),copy_id,'admin.preview_password_copy',
            'succeeded',audit_digest,'approved_preview_password_only','admin');
    RETURN 'preview_password_copy_prepared';
END
$recovery$;
