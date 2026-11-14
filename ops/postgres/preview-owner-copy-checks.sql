\set ON_ERROR_STOP on
\set ECHO none
-- Only run in a fresh, disposable database created by CI. Every fixture rolls back.
BEGIN;
DO $guard$
BEGIN
    IF current_database()<>'lefteria_os_preview'
       OR EXISTS (SELECT 1 FROM migration.account_import_state)
       OR EXISTS (SELECT 1 FROM identity.admin_accounts)
       OR EXISTS (SELECT 1 FROM club.roster_import_state)
       OR EXISTS (SELECT 1 FROM migration.staff_relationship_import_state) THEN
        RAISE EXCEPTION 'refusing_nonempty_preview_recovery_fixture';
    END IF;
END $guard$;
\ir preview-owner-copy-function.sql
CREATE FUNCTION pg_temp.expect_copy_refusal(
    expected_code text, expected_db text, digest text, legacy uuid,
    source_digest text, approved boolean
) RETURNS void LANGUAGE plpgsql AS $test$
BEGIN
    PERFORM pg_temp.prepare_preview_owner_copy(expected_db,digest,legacy,source_digest,
        'fixture-owner','owner@example.invalid',approved);
    RAISE EXCEPTION 'expected_recovery_refusal_was_missing';
EXCEPTION WHEN OTHERS THEN
    IF SQLERRM <> expected_code THEN
        RAISE EXCEPTION 'unexpected_recovery_refusal';
    END IF;
END $test$;

INSERT INTO club.roster_import_state
(id,digest,source_repository,source_commit,environment,teams_count,academy_groups_count,players_count,memberships_count,applied_at)
VALUES ('initial',repeat('1',64),'MikeCy-com/Lefteria-FC',repeat('a',40),'staging',0,0,0,0,now());
INSERT INTO migration.staff_relationship_import_state
(id,digest,roster_digest,source_commit,environment,staff_count,team_assignments_count,group_assignments_count,group_coaches_count,contacts_count,applied_at)
VALUES ('initial',repeat('2',64),repeat('1',64),repeat('a',40),'staging',0,0,0,0,0,now());
INSERT INTO migration.account_import_state
(id,digest,roster_digest,staff_digest,source_commit,environment,exported_at,applied_at,counts_json,manifest_bytes)
VALUES ('initial',repeat('3',64),repeat('1',64),repeat('2',64),repeat('a',40),'staging',now(),now(),'{}',convert_to('{}','UTF8'));
INSERT INTO migration.account_source_documents
(collection,ordinal,legacy_id,target_id,disposition,import_digest,source_json)
VALUES ('admin_users',0,'61000000-0000-4000-8000-000000000001',NULL,'quarantined',repeat('3',64),
    jsonb_build_object('id','61000000-0000-4000-8000-000000000001',
      'username','fixture-owner','name','Fixture Owner','email','owner@example.invalid',
      'role','super_admin','is_active',true,'otp_2fa_enabled',true,
      'password_hash','$2b$12$'||repeat('A',53),'staff_id','missing-fixture-staff',
      'phone','99000001','country','CY','active_organization_id','lefteria_fc_academy')::text);
INSERT INTO migration.account_import_issues(collection,ordinal,code)
VALUES ('admin_users',0,'admin_staff_reference');
INSERT INTO identity.admin_accounts(id,username,display_name,role,password_hash,is_active,invite_pending,otp2fa_enabled)
VALUES ('61000000-0000-4000-8000-000000000002','fixture-staff','Existing staff','super_admin',
        '$2b$12$'||repeat('B',53),true,false,false);
INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id)
VALUES ('61000000-0000-4000-8000-000000000002','lefteria_fc');

DO $checks$
DECLARE
    legacy constant uuid := '61000000-0000-4000-8000-000000000001';
    import_digest constant text := repeat('3',64);
    original_source text;
    source_digest text;
    staff_before jsonb;
    result text;
    copy_id uuid;
BEGIN
    SELECT source_json INTO STRICT original_source FROM migration.account_source_documents
    WHERE collection='admin_users' AND ordinal=0;
    source_digest := encode(sha256(convert_to(original_source,'UTF8')),'hex');
    SELECT to_jsonb(a) INTO STRICT staff_before FROM identity.admin_accounts a WHERE username='fixture-staff';
    PERFORM pg_temp.expect_copy_refusal('preview_recovery_wrong_database','production',import_digest,legacy,source_digest,true);
    PERFORM pg_temp.expect_copy_refusal('preview_password_only_approval_required',current_database(),import_digest,legacy,source_digest,false);
    PERFORM pg_temp.expect_copy_refusal('preview_recovery_import_mismatch',current_database(),repeat('4',64),legacy,source_digest,true);
    PERFORM pg_temp.expect_copy_refusal('preview_recovery_source_mismatch',current_database(),import_digest,legacy,repeat('4',64),true);

    UPDATE migration.account_source_documents SET source_json=jsonb_set(source_json::jsonb,'{role}','"manager"')::text
    WHERE collection='admin_users' AND ordinal=0;
    PERFORM pg_temp.expect_copy_refusal('preview_recovery_identity_mismatch',current_database(),import_digest,legacy,
        (SELECT encode(sha256(convert_to(source_json,'UTF8')),'hex') FROM migration.account_source_documents WHERE collection='admin_users' AND ordinal=0),true);
    UPDATE migration.account_source_documents SET source_json=original_source WHERE collection='admin_users' AND ordinal=0;

    INSERT INTO migration.account_import_issues(collection,ordinal,code) VALUES ('admin_users',0,'phone_origin_ambiguous');
    PERFORM pg_temp.expect_copy_refusal('preview_recovery_issue_mismatch',current_database(),import_digest,legacy,source_digest,true);
    DELETE FROM migration.account_import_issues WHERE collection='admin_users' AND ordinal=0 AND code='phone_origin_ambiguous';

    UPDATE migration.account_source_documents SET source_json=(source_json::jsonb||'{"organization_ids":[]}')::text
    WHERE collection='admin_users' AND ordinal=0;
    PERFORM pg_temp.expect_copy_refusal('preview_recovery_scope_requires_review',current_database(),import_digest,legacy,
        (SELECT encode(sha256(convert_to(source_json,'UTF8')),'hex') FROM migration.account_source_documents WHERE collection='admin_users' AND ordinal=0),true);
    UPDATE migration.account_source_documents SET source_json=original_source WHERE collection='admin_users' AND ordinal=0;

    INSERT INTO club.staff(id,name,job_role,team_type,is_active,is_archived,has_explicit_active,has_explicit_archived)
    VALUES ('missing-fixture-staff','Restored staff','Head Coach','Academy',true,false,true,true);
    PERFORM pg_temp.expect_copy_refusal('preview_recovery_staff_reference_changed',current_database(),import_digest,legacy,source_digest,true);
    DELETE FROM club.staff WHERE id='missing-fixture-staff';

    -- A successful preparation inside a rolled-back subtransaction must leave no identity/audit.
    BEGIN
        PERFORM pg_temp.prepare_preview_owner_copy(current_database(),import_digest,legacy,source_digest,
            'fixture-owner','owner@example.invalid',true);
        IF (SELECT count(*) FROM identity.admin_accounts)<>2 THEN RAISE EXCEPTION 'dry_run_did_not_prepare_copy'; END IF;
        RAISE EXCEPTION 'fixture_rollback';
    EXCEPTION WHEN OTHERS THEN
        IF SQLERRM<>'fixture_rollback' THEN RAISE; END IF;
    END;
    IF (SELECT count(*) FROM identity.admin_accounts)<>1
       OR EXISTS (SELECT 1 FROM audit.security_events WHERE action='admin.preview_password_copy') THEN
        RAISE EXCEPTION 'rollback_retained_account_or_audit';
    END IF;

    result := pg_temp.prepare_preview_owner_copy(current_database(),import_digest,legacy,source_digest,
        'fixture-owner','owner@example.invalid',true);
    IF result<>'preview_password_copy_prepared' THEN RAISE EXCEPTION 'copy_result_invalid'; END IF;
    SELECT id INTO STRICT copy_id FROM identity.admin_accounts WHERE username='fixture-owner';
    IF copy_id=legacy OR NOT EXISTS (
        SELECT 1 FROM identity.admin_accounts
        WHERE id=copy_id AND role='super_admin' AND is_active AND NOT otp2fa_enabled AND NOT invite_pending
          AND legacy_id IS NULL AND password_hash=original_source::jsonb->>'password_hash'
          AND normalized_phone='35799000001' AND active_organization_id='lefteria_fc_academy'
    ) THEN RAISE EXCEPTION 'copied_identity_incorrect'; END IF;
    IF (SELECT array_agg(organization_id ORDER BY organization_id) FROM identity.admin_organization_grants WHERE admin_account_id=copy_id)
       IS DISTINCT FROM ARRAY['lefteria_fc','lefteria_fc_academy']::varchar[] THEN
        RAISE EXCEPTION 'copy_grants_incorrect';
    END IF;
    IF EXISTS (SELECT 1 FROM identity.admin_staff_links WHERE admin_account_id=copy_id)
       OR EXISTS (SELECT 1 FROM migration.legacy_account_mappings WHERE target_id=copy_id)
       OR EXISTS (SELECT 1 FROM identity.mobile_identities WHERE management_admin_id=copy_id) THEN
        RAISE EXCEPTION 'copy_manufactured_relationship';
    END IF;
    IF (SELECT to_jsonb(a) FROM identity.admin_accounts a WHERE username='fixture-staff') IS DISTINCT FROM staff_before
       OR (SELECT count(*) FROM identity.admin_organization_grants WHERE admin_account_id=(staff_before->>'id')::uuid)<>1
       OR (SELECT source_json FROM migration.account_source_documents WHERE collection='admin_users' AND ordinal=0) IS DISTINCT FROM original_source
       OR NOT EXISTS (SELECT 1 FROM migration.account_source_documents WHERE collection='admin_users' AND ordinal=0 AND disposition='quarantined' AND target_id IS NULL)
       OR (SELECT array_agg(code) FROM migration.account_import_issues WHERE collection='admin_users' AND ordinal=0) IS DISTINCT FROM ARRAY['admin_staff_reference']::varchar[] THEN
        RAISE EXCEPTION 'recovery_changed_existing_identity_or_source';
    END IF;
    IF (SELECT count(*) FROM audit.security_events WHERE actor_identity_id=copy_id
        AND action='admin.preview_password_copy' AND outcome='succeeded'
        AND reason_code='approved_preview_password_only' AND subject_digest ~ '^[0-9a-f]{64}$')<>1 THEN
        RAISE EXCEPTION 'recovery_audit_missing';
    END IF;
    PERFORM pg_temp.expect_copy_refusal('preview_recovery_account_collision',current_database(),import_digest,legacy,source_digest,true);
END $checks$;
ROLLBACK;
