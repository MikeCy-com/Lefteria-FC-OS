\set ON_ERROR_STOP on
-- Manual, isolated-staging-only operator step. NEVER execute against live.
-- Run after G0 environment isolation, approved privacy policy, key backup
-- and migrations; provide -v app_role=ACTUAL_TEST_API_ROLE.
-- No application/user credentials are written into SQL or logs.
\if :{?app_role}
\else
  \echo 'ERROR: pass -v app_role=exact_isolated_staging_api_role'
  \quit
\endif
\if :{?staging_approval}
\else
  \echo 'ERROR: pass -v staging_approval=ISOLATED_STAGING_ONLY'
  \quit
\endif
BEGIN;
SELECT set_config('lefteria.academy_upload_app_role', :'app_role', true);
SELECT set_config('lefteria.academy_upload_approval', :'staging_approval', true);
DO $gate$
DECLARE
  v_role text:=current_setting('lefteria.academy_upload_app_role',true);
  v_approval text:=current_setting('lefteria.academy_upload_approval',true);
BEGIN
  IF v_approval<>'ISOLATED_STAGING_ONLY'
    OR current_database() !~* '(preview|staging|test)'
    OR v_role IS NULL OR length(v_role)>63 OR
       v_role !~ '^[a-z][a-z0-9_]*$'
    OR NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=v_role)
  THEN RAISE EXCEPTION
    'ACADEMY UPLOAD GRANTS DENIED: staging isolation/role not independently approved'
    USING ERRCODE='42501';
  END IF;
  -- Never allow creating credentials or a new application role here.
  EXECUTE format('GRANT USAGE ON SCHEMA academy_intake TO %I',v_role);
  EXECUTE format('REVOKE EXECUTE ON FUNCTION academy_intake.submit_public_registration(uuid,jsonb,bytea,text,text,text,text) FROM %I',v_role);
  EXECUTE format('GRANT EXECUTE ON FUNCTION academy_intake.submit_public_registration_with_upload(uuid,jsonb,bytea,text,text,text,text,text) TO %I',v_role);
  EXECUTE format('GRANT EXECUTE ON FUNCTION academy_intake.store_public_registration_document(uuid,text,uuid,text,date,text,bigint,text,text,bytea,bytea,bytea) TO %I',v_role);
  -- Privileges to tables are never required or granted.
  IF has_table_privilege(v_role,
      'academy_intake.registration_document_escrow','SELECT')
    OR has_table_privilege(v_role,
      'academy_intake.registration_document_escrow','INSERT')
    OR has_table_privilege(v_role,
      'academy_intake.registration_upload_capabilities','SELECT')
    OR has_table_privilege(v_role,
      'academy_private.player_documents','SELECT')
  THEN RAISE EXCEPTION 'Public intake runtime has unexpected private table privileges'
    USING ERRCODE='42501';
  END IF;
END $gate$;
COMMIT;
