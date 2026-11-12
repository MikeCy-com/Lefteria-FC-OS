\set ON_ERROR_STOP on
\set ECHO none

-- Connect as the dedicated auth role to a disposable test database, with both
-- -v admin_auth_role=<auth-role> and -v app_role=<mobile-role>. Every probe write
-- is rolled back. No credentials or existing account/profile values are output.
\if :{?admin_auth_role}
\else
  \echo 'admin_auth_role is required.'
  DO $required$ BEGIN RAISE EXCEPTION 'admin_auth_operator_input_required'; END; $required$;
\endif
\if :{?app_role}
\else
  \echo 'app_role is required to verify the mobile credential boundary.'
  DO $required$ BEGIN RAISE EXCEPTION 'admin_auth_operator_input_required'; END; $required$;
\endif
BEGIN;
SELECT set_config('lefteria.admin_auth_role', :'admin_auth_role', true) AS admin_auth_role_setting,
       set_config('lefteria.mobile_runtime_role', :'app_role', true) AS mobile_runtime_role_setting
\gset
\ir admin-club-lock-boundary.sql
\ir pos-catalog-reader-boundary.sql
\ir pos-category-writer-boundary.sql
\ir pos-product-writer-boundary.sql
\ir admin-audience-directory-boundary.sql
\ir admin-group-create-boundary.sql
\ir admin-player-directory-boundary.sql
\ir admin-roster-assignment-boundary.sql
\ir admin-coach-directory-boundary.sql
\ir admin-coach-assignment-boundary.sql
\ir admin-announcement-drafts-boundary.sql
\ir app-announcement-publishing-boundary.sql

DO $checks$
DECLARE
  auth_role pg_roles%ROWTYPE;
  mobile_name text := current_setting('lefteria.mobile_runtime_role');
  denied boolean;
  target text;
  target_id oid;
  probe_id uuid := gen_random_uuid();
  probe_admin uuid;
  probe_digest text := replace(gen_random_uuid()::text,'-','') || replace(gen_random_uuid()::text,'-','');
  probe_bucket text := replace(gen_random_uuid()::text,'-','') || replace(gen_random_uuid()::text,'-','');
  probe_time timestamptz := clock_timestamp();
BEGIN
  IF current_user<>current_setting('lefteria.admin_auth_role') OR current_user=mobile_name
      OR current_database() IN ('postgres','template0','template1') THEN
    RAISE EXCEPTION 'Connect as the distinct unprivileged admin-auth role in a disposable rewrite database';
  END IF;
  SELECT * INTO STRICT auth_role FROM pg_roles WHERE rolname=current_user;
  IF NOT auth_role.rolcanlogin OR auth_role.rolinherit OR auth_role.rolsuper OR auth_role.rolcreatedb
      OR auth_role.rolcreaterole OR auth_role.rolreplication OR auth_role.rolbypassrls
      OR EXISTS (SELECT 1 FROM pg_auth_members WHERE member=auth_role.oid OR roleid=auth_role.oid)
      OR EXISTS (SELECT 1 FROM pg_database WHERE datdba=auth_role.oid)
      OR EXISTS (SELECT 1 FROM pg_namespace WHERE nspowner=auth_role.oid AND nspname !~ '^pg_')
      OR EXISTS (SELECT 1 FROM pg_class WHERE relowner=auth_role.oid AND relpersistence<>'t')
      OR EXISTS (SELECT 1 FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
                  WHERE p.proowner=auth_role.oid AND n.nspname !~ '^pg_' AND n.nspname<>'information_schema') THEN
    RAISE EXCEPTION 'Admin-auth role attributes, ownership or memberships are unsafe';
  END IF;
  IF has_database_privilege(current_user,current_database(),'CREATE') OR has_database_privilege(current_user,current_database(),'TEMP')
      OR has_schema_privilege(current_user,'migration','USAGE')
      OR EXISTS (SELECT 1 FROM pg_namespace n WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema'
                  AND has_schema_privilege(current_user,n.oid,'CREATE')) THEN
    RAISE EXCEPTION 'Admin-auth role has DDL or migration privileges';
  END IF;
  IF has_column_privilege(mobile_name,'identity.admin_accounts','password_hash','SELECT')
      OR has_any_column_privilege(mobile_name,'identity.admin_sessions','SELECT,INSERT,UPDATE,REFERENCES')
      OR has_any_column_privilege(mobile_name,'identity.admin_auth_rate_buckets','SELECT,INSERT,UPDATE,REFERENCES')
      OR has_table_privilege(mobile_name,'identity.admin_sessions','DELETE,TRUNCATE,TRIGGER')
      OR has_table_privilege(mobile_name,'identity.admin_auth_rate_buckets','DELETE,TRUNCATE,TRIGGER') THEN
    RAISE EXCEPTION 'Mobile runtime can access web-admin credentials or authentication state';
  END IF;
  denied := false;
  BEGIN EXECUTE format('SET LOCAL ROLE %I',mobile_name);
  EXCEPTION WHEN insufficient_privilege THEN denied := true; END;
  IF NOT denied THEN RAISE EXCEPTION 'Admin-auth role can assume the mobile runtime role'; END IF;
  IF EXISTS (SELECT 1 FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
              WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema' AND p.prosecdef
                AND has_schema_privilege(current_user,n.oid,'USAGE') AND has_function_privilege(current_user,p.oid,'EXECUTE')
                AND p.oid NOT IN ('identity.lock_admin_club_authority(text,text)'::regprocedure,
                  'pos.read_admin_catalog(text,text)'::regprocedure,
                  'pos.write_admin_category(text,text,text,text,boolean,bigint)'::regprocedure,
                  'pos.write_admin_product(text,text,text,text,text,text,boolean,bigint,jsonb)'::regprocedure,
                  'club.read_admin_audiences(text,text)'::regprocedure,
                  'club.create_admin_group(text,text,text,text)'::regprocedure,
                  'club.read_admin_roster_players(text,text,integer,integer)'::regprocedure,
                  'club.write_admin_roster_assignment(text,text,text,jsonb,jsonb)'::regprocedure,
                  'club.read_admin_coaches(text,text,integer,integer)'::regprocedure,
                  'club.write_admin_coach_assignment(text,text,text,jsonb,jsonb)'::regprocedure,
                  'hub.read_admin_editorial(text,text)'::regprocedure,
                  'hub.create_admin_draft(text,text,uuid,uuid,text,text,text,boolean,boolean,smallint,smallint)'::regprocedure,
                  'hub.submit_admin_draft_review(text,text,uuid,bigint)'::regprocedure,
                  'hub.read_admin_editorial_detail(text,text,uuid)'::regprocedure,
                  'hub.approve_admin_hub_visual(text,text,uuid,bigint)'::regprocedure,
                  'hub.review_admin_text(text,text,uuid,bigint,text)'::regprocedure,
    'hub.revise_admin_draft(text,text,uuid,bigint,text,text)'::regprocedure,
    'hub.return_rejected_admin_draft(text,text,uuid,bigint)'::regprocedure,
    'hub.quarantine_admin_image(text,text,uuid,bigint,uuid,text,bytea)'::regprocedure,
    'hub.read_admin_quarantined_image(text,text,uuid,uuid)'::regprocedure,
    'hub.reject_admin_hub_image(text,text,uuid,bigint,uuid)'::regprocedure,
    'hub.request_admin_ai_education(text,text,uuid,text,smallint,smallint,text,text)'::regprocedure,
                  'club.create_admin_announcement_draft(text,text,text,text,text,text)'::regprocedure,
                  'club.read_admin_announcement_drafts(text,text)'::regprocedure,
                  'club.publish_admin_app_announcement(text,text,text,bigint)'::regprocedure,
                  'club.read_admin_app_announcements(text,text)'::regprocedure,
                  'club.read_admin_app_announcement_readers(text,text,text)'::regprocedure,
                  'club.write_admin_app_announcement_media(text,text,text,bigint,uuid,text,text,text,bytea)'::regprocedure,
                  'club.remove_admin_app_announcement_media(text,text,text,bigint,uuid)'::regprocedure,
                  'club.remove_admin_app_announcement(text,text,text,bigint)'::regprocedure,
                  'club.read_admin_academy_training_sessions(text,text,text,date,date)'::regprocedure,
                  'club.read_admin_academy_training_attendance(text,text,text)'::regprocedure,
                  'club.record_admin_academy_training_attendance(text,text,text,text,text,integer)'::regprocedure,
                  'marketing.read_admin_academy_leads(text,text,integer,integer,text)'::regprocedure,
                  'marketing.update_admin_academy_lead_status(text,text,uuid,text,text)'::regprocedure)) THEN
    RAISE EXCEPTION 'Admin-auth role can execute an unexpected security-definer routine';
  END IF;

  PERFORM id,username,display_name,role,is_active,invite_pending,otp2fa_enabled,password_hash FROM identity.admin_accounts LIMIT 1;
  PERFORM admin_account_id,organization_id FROM identity.admin_organization_grants LIMIT 1;
  PERFORM id,name FROM club.organizations LIMIT 1;
  PERFORM id,token_digest,admin_account_id,credential_digest,created_at,expires_at,revoked_at FROM identity.admin_sessions LIMIT 1;
  PERFORM key,tokens,updated_at FROM identity.admin_auth_rate_buckets LIMIT 1;
  IF has_any_column_privilege(current_user,'identity.admin_accounts','INSERT,UPDATE,REFERENCES')
      OR has_any_column_privilege(current_user,'identity.admin_organization_grants','INSERT,UPDATE,REFERENCES')
      OR has_any_column_privilege(current_user,'club.organizations','INSERT,UPDATE,REFERENCES')
      OR has_any_column_privilege(current_user,'audit.security_events','SELECT,UPDATE,REFERENCES')
      OR EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid='identity.admin_sessions'::regclass
                  AND a.attnum>0 AND NOT a.attisdropped AND a.attname<>'revoked_at'
                  AND has_column_privilege(current_user,a.attrelid,a.attnum,'UPDATE'))
      OR EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid='identity.admin_auth_rate_buckets'::regclass
                  AND a.attnum>0 AND NOT a.attisdropped AND a.attname NOT IN ('tokens','updated_at')
                  AND has_column_privilege(current_user,a.attrelid,a.attnum,'UPDATE')) THEN
    RAISE EXCEPTION 'Admin-auth column mutation privileges exceed their exact allowlist';
  END IF;
  denied := false;
  BEGIN PERFORM normalized_phone,legacy_id,active_organization_id FROM identity.admin_accounts LIMIT 1;
  EXCEPTION WHEN insufficient_privilege THEN denied := true; END;
  IF NOT denied THEN RAISE EXCEPTION 'Admin-auth role exceeded the credential/status projection'; END IF;

  FOREACH target IN ARRAY ARRAY['identity.mobile_identities','identity.mobile_avatars','identity.otp_challenges',
    'identity.auth_rate_buckets','identity.fan_registration_evidence','identity.canonical_contact_bindings',
    'identity.mobile_record_links','identity.admin_staff_links','identity.imported_mobile_subjects',
    'club.players','club.staff','club.calendar_items','club.calendar_responses',
    'migration.roster_source_documents','migration.staff_source_documents','migration.contact_provenance',
    'migration.account_source_documents','migration.account_import_state','migration.account_import_issues',
    'migration.legacy_account_mappings','migration.imported_admin_grants',
    'migration.calendar_source_documents','migration.calendar_import_issues',
    'pos.categories','pos.products','pos.product_variants']
  LOOP
    SELECT c.oid INTO STRICT target_id FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
    WHERE n.nspname=split_part(target,'.',1) AND c.relname=split_part(target,'.',2);
    IF has_any_column_privilege(current_user,target_id,'SELECT,INSERT,UPDATE,REFERENCES')
        OR has_table_privilege(current_user,target_id,'DELETE,TRUNCATE,TRIGGER') THEN
      RAISE EXCEPTION 'Admin-auth role can access a mobile, club-private or source-archive relation';
    END IF;
    denied := false;
    BEGIN EXECUTE format('SELECT 1 FROM %s LIMIT 1',target);
    EXCEPTION WHEN insufficient_privilege THEN denied := true; END;
    IF NOT denied THEN RAISE EXCEPTION 'Admin-auth role can query a forbidden relation'; END IF;
  END LOOP;

  -- A real fictional session is inserted when an owner-seeded account exists.
  -- On an empty fixture, the deliberate foreign-key refusal proves INSERT is
  -- authorized without giving this role permission to manufacture an admin.
  SELECT id INTO probe_admin FROM identity.admin_accounts ORDER BY id LIMIT 1;
  IF probe_admin IS NULL THEN
    denied := false;
    BEGIN
      INSERT INTO identity.admin_sessions(id,token_digest,admin_account_id,credential_digest,created_at,expires_at,revoked_at)
      VALUES (probe_id,probe_digest,gen_random_uuid(),repeat('a',64),probe_time,probe_time+interval '1 hour',NULL);
    EXCEPTION WHEN foreign_key_violation THEN denied := true; END;
    IF NOT denied THEN RAISE EXCEPTION 'An unknown admin unexpectedly accepted an authentication session'; END IF;
    RAISE NOTICE 'Empty-account fixture verified authorized session INSERT reaches the foreign-key guard';
  ELSE
    INSERT INTO identity.admin_sessions(id,token_digest,admin_account_id,credential_digest,created_at,expires_at,revoked_at)
    VALUES (probe_id,probe_digest,probe_admin,repeat('a',64),probe_time,probe_time+interval '1 hour',NULL);
    UPDATE identity.admin_sessions SET revoked_at=probe_time WHERE id=probe_id;
    IF NOT EXISTS (SELECT 1 FROM identity.admin_sessions WHERE id=probe_id AND revoked_at=probe_time) THEN
      RAISE EXCEPTION 'Admin-auth role could not create/read/revoke its fictional session';
    END IF;
  END IF;
  INSERT INTO identity.admin_auth_rate_buckets(key,tokens,updated_at) VALUES (probe_bucket,9,probe_time);
  UPDATE identity.admin_auth_rate_buckets SET tokens=8,updated_at=probe_time+interval '1 second' WHERE key=probe_bucket;
  IF NOT EXISTS (SELECT 1 FROM identity.admin_auth_rate_buckets WHERE key=probe_bucket AND tokens=8) THEN
    RAISE EXCEPTION 'Admin-auth role could not maintain its fictional throttle bucket';
  END IF;
  INSERT INTO audit.security_events(id,occurred_at,actor_realm,action,outcome)
  VALUES (gen_random_uuid(),probe_time,'admin','permissions.web_admin_probe','success');

  FOREACH target IN ARRAY ARRAY[
    'INSERT INTO identity.admin_accounts(id,username,display_name,role,is_active) SELECT gen_random_uuid(),''permission_probe'',''Permission probe'',''super_admin'',true WHERE false',
    'UPDATE identity.admin_accounts SET role=''super_admin'' WHERE false',
    'UPDATE identity.admin_accounts SET password_hash=password_hash WHERE false',
    'UPDATE identity.admin_accounts SET is_active=true WHERE false',
    'UPDATE identity.admin_accounts SET active_organization_id=NULL WHERE false',
    'DELETE FROM identity.admin_accounts WHERE false',
    'INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id) SELECT gen_random_uuid(),''lefteria_fc'' WHERE false',
    'UPDATE identity.admin_organization_grants SET organization_id=organization_id WHERE false',
    'DELETE FROM identity.admin_organization_grants WHERE false',
    'INSERT INTO club.organizations(id,name) SELECT ''permission_probe'',''Permission probe'' WHERE false',
    'UPDATE club.organizations SET name=name WHERE false',
    'UPDATE identity.admin_sessions SET id=id WHERE false',
    'UPDATE identity.admin_sessions SET token_digest=token_digest WHERE false',
    'UPDATE identity.admin_sessions SET admin_account_id=admin_account_id WHERE false',
    'UPDATE identity.admin_sessions SET credential_digest=credential_digest WHERE false',
    'UPDATE identity.admin_sessions SET expires_at=expires_at WHERE false',
    'UPDATE identity.admin_sessions SET created_at=created_at WHERE false',
    'DELETE FROM identity.admin_sessions WHERE false',
    'UPDATE identity.admin_auth_rate_buckets SET key=key WHERE false',
    'DELETE FROM identity.admin_auth_rate_buckets WHERE false',
    'SELECT 1 FROM audit.security_events LIMIT 1',
    'UPDATE audit.security_events SET outcome=''denied'' WHERE false',
    'DELETE FROM audit.security_events WHERE false']
  LOOP
    denied := false;
    BEGIN EXECUTE target;
    EXCEPTION WHEN insufficient_privilege THEN denied := true; END;
    IF NOT denied THEN RAISE EXCEPTION 'Admin-auth role exceeded its session/throttle/audit mutation boundary'; END IF;
  END LOOP;
  FOREACH target IN ARRAY ARRAY['identity.admin_accounts','identity.admin_organization_grants','club.organizations',
    'identity.admin_sessions','identity.admin_auth_rate_buckets','audit.security_events']
  LOOP
    IF has_table_privilege(current_user,target,'DELETE,TRUNCATE,REFERENCES,TRIGGER') THEN
      RAISE EXCEPTION 'Admin-auth role retains delete, truncate, reference or trigger privileges';
    END IF;
  END LOOP;
  RAISE NOTICE 'Web-admin auth permission checks passed; all fictional probe writes will roll back';
END
$checks$;
ROLLBACK;
