\set ON_ERROR_STOP on
\set ECHO none

-- Connect as the runtime role to a disposable test database. All probe writes
-- are rolled back; this does not delete application rows or tables.
BEGIN;
DO $checks$
DECLARE
  denied boolean;
  private_table text;
  private_table_id oid;
  probe_id uuid := gen_random_uuid();
  probe_phone text := '999' || lpad(trunc(random() * 10000000000)::bigint::text, 10, '0');
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = current_user
             AND (rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls)) THEN
    RAISE EXCEPTION 'Permission checks must use an unprivileged runtime role';
  END IF;
  IF has_database_privilege(current_user, current_database(), 'CREATE')
      OR has_database_privilege(current_user, current_database(), 'TEMP')
      OR has_schema_privilege(current_user, 'public', 'CREATE')
      OR has_schema_privilege(current_user, 'identity', 'CREATE') THEN
    RAISE EXCEPTION 'Runtime role unexpectedly has DDL privileges';
  END IF;
  IF NOT row_security_active('identity.mobile_identities') THEN
    RAISE EXCEPTION 'Mobile identity RLS is not active for the runtime role';
  END IF;
  IF has_column_privilege(current_user,'identity.admin_accounts','password_hash','SELECT') THEN
    RAISE EXCEPTION 'Mobile API runtime can read imported credentials';
  END IF;
  IF has_table_privilege(current_user,'identity.admin_sessions','SELECT,INSERT,UPDATE,DELETE,TRUNCATE')
      OR has_table_privilege(current_user,'identity.admin_auth_rate_buckets','SELECT,INSERT,UPDATE,DELETE,TRUNCATE')
      OR has_table_privilege(current_user,'public_content.documents','INSERT,UPDATE,DELETE,TRUNCATE')
      OR has_table_privilege(current_user,'public_content.import_state','INSERT,UPDATE,DELETE,TRUNCATE') THEN
    RAISE EXCEPTION 'Mobile/content runtime has credential-session or public-content write privileges';
  END IF;
  PERFORM kind,organization_id,id,data_json FROM public_content.documents LIMIT 0;
  PERFORM kind,organization_id,id,data_json FROM public_content.edits LIMIT 0;
  IF has_column_privilege(current_user,'public_content.edits','version','SELECT')
      OR has_any_column_privilege(current_user,'public_content.edits','INSERT,UPDATE,REFERENCES')
      OR has_table_privilege(current_user,'public_content.edits','DELETE,TRUNCATE,TRIGGER')
      OR has_function_privilege(current_user,'identity.lock_admin_club_authority(text,text)','EXECUTE') THEN
    RAISE EXCEPTION 'Public runtime exceeds safe club payload read boundary';
  END IF;
  -- The only authorized trial settings permission is the fixed,
  -- source-backed, closed-only function; never the private settings table.
  IF NOT has_function_privilege(current_user,
       'club.read_first_team_trial_display()','EXECUTE')
     OR has_table_privilege(current_user,'club.first_team_trial_settings',
       'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER') THEN
    RAISE EXCEPTION 'Runtime first-team trials reader exceeds public display boundary';
  END IF;
  IF EXISTS (SELECT 1 FROM club.read_first_team_trial_display() WHERE is_open) THEN
    RAISE EXCEPTION 'Unverified trial registration unexpectedly opened';
  END IF;
  PERFORM legacy_subject,identity_id FROM identity.imported_mobile_subjects LIMIT 1;
  PERFORM id,time_zone,is_complete FROM club.calendar_import_state LIMIT 1;
  PERFORM id,organization_id FROM club.teams LIMIT 1;
  PERFORM id,organization_id FROM club.academy_groups LIMIT 1;
  IF has_column_privilege(current_user,'club.calendar_import_state','manifest_bytes','SELECT')
      OR has_table_privilege(current_user,'club.calendar_items','INSERT,UPDATE,DELETE,TRUNCATE')
      OR has_table_privilege(current_user,'club.calendar_item_scopes','INSERT,UPDATE,DELETE,TRUNCATE')
      OR has_table_privilege(current_user,'club.calendar_item_guests','INSERT,UPDATE,DELETE,TRUNCATE')
      OR has_table_privilege(current_user,'club.calendar_responses','DELETE,TRUNCATE')
      OR NOT row_security_active('club.calendar_responses') THEN
    RAISE EXCEPTION 'Runtime calendar import/canonical/response privileges exceed their boundary';
  END IF;
  IF has_table_privilege(current_user,'identity.imported_mobile_subjects','INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER') THEN
    RAISE EXCEPTION 'Runtime can mutate imported mobile subject projection';
  END IF;
  IF NOT has_column_privilege(current_user,'club.players','name','SELECT')
      OR NOT has_column_privilege(current_user,'club.players','is_active','SELECT')
      OR NOT has_column_privilege(current_user,'club.players','has_explicit_picker_fields','SELECT')
      OR NOT has_column_privilege(current_user,'club.players','team_id','SELECT')
      OR has_table_privilege(current_user,'club.players','INSERT')
      OR has_table_privilege(current_user,'club.players','UPDATE')
      OR has_table_privilege(current_user,'club.players','DELETE')
      OR has_table_privilege(current_user,'club.players','TRUNCATE')
      OR has_schema_privilege(current_user,'migration','USAGE') THEN
    RAISE EXCEPTION 'Runtime roster/import privileges exceed the canonical auth boundary';
  END IF;
  denied := false;
  BEGIN
    PERFORM 1 FROM migration.roster_source_documents LIMIT 1;
  EXCEPTION WHEN insufficient_privilege THEN denied := true;
  END;
  IF NOT denied THEN RAISE EXCEPTION 'Runtime can read owner-only source archives'; END IF;
  FOREACH private_table IN ARRAY ARRAY['migration.staff_relationship_import_state','migration.staff_source_documents','migration.contact_provenance',
    'migration.account_import_state','migration.account_source_documents','migration.account_import_issues','migration.legacy_account_mappings','migration.imported_admin_grants',
    'migration.calendar_source_documents','migration.calendar_import_issues',
    'migration.public_content_import_evidence','migration.public_content_source_documents']
  LOOP
    -- Resolve through the catalog: the runtime deliberately lacks migration
    -- schema USAGE, so resolving its qualified name as regclass is denied.
    SELECT c.oid INTO STRICT private_table_id
      FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
      WHERE n.nspname = split_part(private_table, '.', 1)
        AND c.relname = split_part(private_table, '.', 2);
    IF has_table_privilege(current_user, private_table_id, 'SELECT') OR has_table_privilege(current_user, private_table_id, 'INSERT')
        OR has_table_privilege(current_user, private_table_id, 'UPDATE') OR has_table_privilege(current_user, private_table_id, 'DELETE')
        OR has_table_privilege(current_user, private_table_id, 'TRUNCATE') THEN
      RAISE EXCEPTION 'Runtime has access to an owner-only staff relationship table';
    END IF;
    denied := false;
    BEGIN
      EXECUTE format('SELECT 1 FROM %s LIMIT 1', private_table);
    EXCEPTION WHEN insufficient_privilege THEN denied := true;
    END;
    IF NOT denied THEN RAISE EXCEPTION 'Runtime can read an owner-only staff relationship table'; END IF;
  END LOOP;
  FOREACH private_table IN ARRAY ARRAY['club.teams','club.academy_groups','club.staff','club.staff_teams','club.staff_academy_groups','club.academy_group_coaches',
    'identity.canonical_contact_bindings','identity.fan_registration_evidence']
  LOOP
    denied := false;
    BEGIN
      EXECUTE format('SELECT * FROM %s LIMIT 1', private_table);
    EXCEPTION WHEN insufficient_privilege THEN denied := true;
    END;
    IF NOT denied THEN RAISE EXCEPTION 'Runtime exceeded the allowlisted canonical projection'; END IF;
  END LOOP;
  FOREACH private_table IN ARRAY ARRAY['identity.mobile_record_links','identity.admin_staff_links','identity.canonical_contact_bindings',
    'identity.fan_registration_evidence','club.staff','club.staff_teams','club.staff_academy_groups','club.academy_group_coaches','club.player_academy_groups']
  LOOP
    denied := false;
    BEGIN
      EXECUTE format('DELETE FROM %s WHERE false', private_table);
    EXCEPTION WHEN insufficient_privilege THEN denied := true;
    END;
    IF NOT denied THEN RAISE EXCEPTION 'Runtime can mutate canonical relationships or evidence'; END IF;
  END LOOP;
  INSERT INTO identity.mobile_identities
    (id, normalized_phone, role, display_name, is_active, platform, created_at)
    VALUES (probe_id, probe_phone, 'fan', 'Permission probe', true, 'Test', now());
  UPDATE identity.mobile_identities SET last_login = now(), platform = 'Web/PWA' WHERE id = probe_id;
  INSERT INTO identity.mobile_avatars (identity_id, file_name, jpeg_bytes, width, height, updated_at)
    VALUES (probe_id, replace(gen_random_uuid()::text,'-','') || '_' || repeat('a',64) || '.jpg', decode('ffd8ffd9','hex'), 1, 1, now());
  UPDATE identity.mobile_avatars SET width = 1, height = 1 WHERE identity_id = probe_id;
  UPDATE identity.admin_accounts SET active_organization_id = active_organization_id WHERE false;
  INSERT INTO audit.security_events (id, occurred_at, action, outcome)
    VALUES (gen_random_uuid(), now(), 'permissions.probe', 'success');

  denied := false;
  BEGIN
    INSERT INTO identity.mobile_identities
      (id, normalized_phone, role, display_name, is_active, platform, created_at)
      VALUES (gen_random_uuid(), probe_phone, 'coach', 'Denied probe', true, 'Test', now());
  EXCEPTION WHEN insufficient_privilege THEN denied := true;
  END;
  IF NOT denied THEN RAISE EXCEPTION 'Runtime inserted a formal club identity'; END IF;

  denied := false;
  BEGIN
    UPDATE identity.mobile_identities SET role = 'player' WHERE id = probe_id;
  EXCEPTION WHEN insufficient_privilege THEN denied := true;
  END;
  IF NOT denied THEN RAISE EXCEPTION 'Runtime mutated an identity role'; END IF;

  denied := false;
  BEGIN
    UPDATE identity.mobile_avatars SET identity_id = probe_id WHERE false;
  EXCEPTION WHEN insufficient_privilege THEN denied := true;
  END;
  IF NOT denied THEN RAISE EXCEPTION 'Runtime can move avatar ownership'; END IF;
  denied := false;
  BEGIN
    UPDATE identity.mobile_avatars SET previous_avatar_url = 'untrusted' WHERE false;
  EXCEPTION WHEN insufficient_privilege THEN denied := true;
  END;
  IF NOT denied THEN RAISE EXCEPTION 'Runtime can rewrite avatar rollback provenance'; END IF;
  IF has_table_privilege(current_user, 'identity.mobile_avatars', 'DELETE')
      OR has_table_privilege(current_user, 'identity.mobile_avatars', 'TRUNCATE') THEN
    RAISE EXCEPTION 'Runtime can delete/truncate stored avatars';
  END IF;

  denied := false;
  BEGIN
    UPDATE identity.admin_accounts SET role = 'super_admin' WHERE false;
  EXCEPTION WHEN insufficient_privilege THEN denied := true;
  END;
  IF NOT denied THEN RAISE EXCEPTION 'Runtime can mutate admin roles'; END IF;

  denied := false;
  BEGIN
    UPDATE identity.admin_organization_grants SET organization_id = 'lefteria_fc' WHERE false;
  EXCEPTION WHEN insufficient_privilege THEN denied := true;
  END;
  IF NOT denied THEN RAISE EXCEPTION 'Runtime can mutate organization grants'; END IF;

  denied := false;
  BEGIN
    PERFORM 1 FROM audit.security_events LIMIT 1;
  EXCEPTION WHEN insufficient_privilege THEN denied := true;
  END;
  IF NOT denied THEN RAISE EXCEPTION 'Runtime can read audit rows'; END IF;
  denied := false;
  BEGIN
    UPDATE audit.security_events SET outcome = 'denied' WHERE false;
  EXCEPTION WHEN insufficient_privilege THEN denied := true;
  END;
  IF NOT denied THEN RAISE EXCEPTION 'Runtime can update audit rows'; END IF;
  denied := false;
  BEGIN
    DELETE FROM audit.security_events WHERE false;
  EXCEPTION WHEN insufficient_privilege THEN denied := true;
  END;
  IF NOT denied THEN RAISE EXCEPTION 'Runtime can delete audit rows'; END IF;
  IF has_table_privilege(current_user, 'audit.security_events', 'TRUNCATE') THEN
    RAISE EXCEPTION 'Runtime can truncate audit rows';
  END IF;
  IF NOT has_function_privilege(current_user,
      'club.read_mobile_chat_groups(uuid,text,text,timestamptz)','EXECUTE') THEN
    RAISE EXCEPTION 'Scoped chat directory function unavailable';
  END IF;
  IF has_table_privilege(current_user,'club.app_chat_messages',
      'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
      OR has_any_column_privilege(current_user,'club.app_chat_messages',
        'SELECT,INSERT,UPDATE,REFERENCES')
      OR NOT has_function_privilege(current_user,
        'club.read_mobile_chat_messages(uuid,text,text,timestamptz,text,text,integer)','EXECUTE')
      OR NOT has_function_privilege(current_user,
        'club.send_mobile_chat_message(uuid,text,text,timestamptz,text,text,text)','EXECUTE') THEN
    RAISE EXCEPTION 'Messaging runtime can access raw rows or cannot call checked functions';
  END IF;
  denied := false;
  BEGIN
    PERFORM club.send_mobile_chat_message(
      gen_random_uuid(),'invalid','fan',now()+interval '1 day',
      'lefteria_fc','invented_group','Unauthorized test text');
  EXCEPTION WHEN insufficient_privilege THEN denied := true;
  END;
  IF NOT denied THEN
    RAISE EXCEPTION 'Guessed chat room accepted an invalid mobile identity';
  END IF;
  IF has_table_privilege(current_user,'club.app_chat_message_revisions',
       'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
      OR has_any_column_privilege(current_user,'club.app_chat_message_revisions',
        'SELECT,INSERT,UPDATE,REFERENCES')
      OR NOT has_function_privilege(current_user,
        'club.mutate_mobile_chat_message(uuid,text,text,timestamptz,text,text,uuid,bigint,text,text)','EXECUTE') THEN
    RAISE EXCEPTION 'Chat revision audit or mutation function privilege violated';
  END IF;
  denied := false;
  BEGIN
    PERFORM club.mutate_mobile_chat_message(
      gen_random_uuid(),'invalid','fan',now()+interval '1 day',
      'lefteria_fc','invented_group',gen_random_uuid(),1,'delete',null);
  EXCEPTION WHEN insufficient_privilege THEN denied := true;
  END;
  IF NOT denied THEN
    RAISE EXCEPTION 'Guessed edit/delete was accepted without source identity';
  END IF;
  IF has_table_privilege(current_user,'club.app_chat_reactions',
       'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
      OR has_any_column_privilege(current_user,'club.app_chat_reactions',
        'SELECT,INSERT,UPDATE,REFERENCES')
      OR NOT has_function_privilege(current_user,
        'club.set_mobile_chat_reaction(uuid,text,text,timestamptz,text,text,uuid,text,boolean)','EXECUTE')
    THEN
    RAISE EXCEPTION 'Chat reactions exceed least-privilege storage boundary';
  END IF;
  denied := false;
  BEGIN
    PERFORM club.set_mobile_chat_reaction(
      gen_random_uuid(),'invalid','fan',now()+interval '1 day',
      'lefteria_fc','invented_room',gen_random_uuid(),'⚽',true);
  EXCEPTION WHEN insufficient_privilege THEN denied := true;
  END;
  IF NOT denied THEN RAISE EXCEPTION 'Invalid fan reacted to a guessed room'; END IF;
  IF has_table_privilege(current_user,'club.app_chat_read_receipts',
       'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
      OR has_any_column_privilege(current_user,'club.app_chat_read_receipts',
        'SELECT,INSERT,UPDATE,REFERENCES')
      OR has_function_privilege(current_user,
        'club.is_current_mobile_chat_reader(uuid,text,text)','EXECUTE')
      OR NOT has_function_privilege(current_user,
        'club.ack_mobile_chat_read(uuid,text,text,timestamptz,text,text,uuid,bigint)','EXECUTE')
      OR NOT has_function_privilege(current_user,
        'club.read_mobile_chat_readers(uuid,text,text,timestamptz,text,text,uuid)','EXECUTE')
    THEN
    RAISE EXCEPTION 'Read receipt storage, private helper, or scoped endpoints lack proper permissions';
  END IF;
  denied := false;
  BEGIN
    PERFORM club.read_mobile_chat_readers(
      gen_random_uuid(),'invalid','management',now()+interval '1 day',
      'lefteria_fc','invented_room',gen_random_uuid());
  EXCEPTION WHEN insufficient_privilege THEN denied := true;
  END;
  IF NOT denied THEN RAISE EXCEPTION 'Guessed room leaked private readers'; END IF;
  FOR private_table IN SELECT unnest(ARRAY[
    'club.app_private_conversations','club.app_private_messages',
    'club.app_private_message_revisions','club.app_private_conversation_views']) LOOP
    IF has_table_privilege(current_user,private_table,
         'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
      OR has_any_column_privilege(current_user,private_table,
         'SELECT,INSERT,UPDATE,REFERENCES') THEN
      RAISE EXCEPTION 'Runtime can inspect private chat rows: %',private_table;
    END IF;
  END LOOP;
  IF has_function_privilege(current_user,
      'club.mobile_private_pair_allowed(uuid,text,text,timestamptz,uuid,text,text)','EXECUTE')
    OR has_function_privilege(current_user,
      'club.private_conversation_json(club.app_private_conversations,uuid)','EXECUTE')
    OR NOT has_function_privilege(current_user,
      'club.read_mobile_private_contacts(uuid,text,text,timestamptz,text,text)','EXECUTE')
    OR NOT has_function_privilege(current_user,
      'club.read_mobile_private_conversations(uuid,text,text,timestamptz)','EXECUTE')
    OR NOT has_function_privilege(current_user,
      'club.request_mobile_private_conversation(uuid,text,text,timestamptz,text,text,uuid)','EXECUTE')
    OR NOT has_function_privilege(current_user,
      'club.respond_mobile_private_conversation(uuid,text,text,timestamptz,uuid,boolean)','EXECUTE')
    OR NOT has_function_privilege(current_user,
      'club.read_mobile_private_messages(uuid,text,text,timestamptz,uuid,integer)','EXECUTE')
    OR NOT has_function_privilege(current_user,
      'club.write_mobile_private_message(uuid,text,text,timestamptz,uuid,text,uuid,bigint,text)','EXECUTE')
    OR NOT has_function_privilege(current_user,
      'club.hide_mobile_private_conversation(uuid,text,text,timestamptz,uuid)','EXECUTE') THEN
    RAISE EXCEPTION 'Private messaging function boundary violated';
  END IF;
  denied := false;
  BEGIN
    PERFORM club.request_mobile_private_conversation(
      gen_random_uuid(),'invalid','fan',now()+interval '1 day',
      'lefteria_fc','invented_group',gen_random_uuid());
  EXCEPTION WHEN insufficient_privilege THEN denied := true;
  END;
  IF NOT denied THEN
    RAISE EXCEPTION 'A fan could create an unauthorized private invitation';
  END IF;
  denied := false;
  BEGIN
    PERFORM club.write_mobile_private_message(
      gen_random_uuid(),'invalid','player',now()+interval '1 day',
      gen_random_uuid(),'send',NULL,NULL,'unguarded');
  EXCEPTION WHEN insufficient_privilege THEN denied := true;
  END;
  IF NOT denied THEN
    RAISE EXCEPTION 'A player could bypass private conversation approval';
  END IF;
  IF has_table_privilege(current_user,'club.app_announcements',
       'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
      OR has_any_column_privilege(current_user,'club.app_announcements',
        'SELECT,INSERT,UPDATE,REFERENCES')
      OR has_function_privilege(current_user,
        'club.publish_admin_app_announcement(text,text,text,bigint)','EXECUTE')
      OR has_function_privilege(current_user,
        'club.read_admin_app_announcements(text,text)','EXECUTE')
      OR has_function_privilege(current_user,
        'club.remove_admin_app_announcement(text,text,text,bigint)','EXECUTE')
      OR has_function_privilege(current_user,
        'club.app_announcement_admin_actor(text,text)','EXECUTE')
      OR NOT has_function_privilege(current_user,
        'club.read_mobile_app_announcements(uuid,text,text,timestamptz,text,text,integer)','EXECUTE') THEN
    RAISE EXCEPTION 'App notice mobile runtime can publish/read raw rows';
  END IF;
  denied := false;
  BEGIN
    PERFORM club.read_mobile_app_announcements(
      gen_random_uuid(),'invalid','fan',now()+interval '1 day',
      'lefteria_fc','invented_team',0);
  EXCEPTION WHEN insufficient_privilege THEN denied := true;
  END;
  IF NOT denied THEN RAISE EXCEPTION 'Fan/forged group read an app notice'; END IF;
  -- A fan cannot use a member chat directory, even with guessed group IDs.
  denied := false;
  BEGIN
    PERFORM club.read_mobile_chat_groups(gen_random_uuid(),'invalid','fan',now()+interval '1 day');
  EXCEPTION WHEN insufficient_privilege THEN denied := true;
  END;
  IF NOT denied THEN RAISE EXCEPTION 'Fan accepted by member chat group directory'; END IF;
  -- Direct reads and writes of private invitations are prohibited.
  FOR private_table IN SELECT unnest(ARRAY['identity.family_invitations','identity.family_invitation_events',
    'identity.family_delegated_player_grants']) LOOP
    IF has_table_privilege(current_user,private_table,'SELECT')
      OR has_table_privilege(current_user,private_table,'INSERT')
      OR has_table_privilege(current_user,private_table,'UPDATE')
      OR has_table_privilege(current_user,private_table,'DELETE')
      OR has_table_privilege(current_user,private_table,'TRUNCATE') THEN
      RAISE EXCEPTION 'Runtime may directly access protected family data: %',private_table;
    END IF;
  END LOOP;
  IF has_any_column_privilege(current_user,
      'identity.family_delegated_player_grants','SELECT,INSERT,UPDATE,REFERENCES')
    OR has_table_privilege(current_user,'identity.family_delegated_player_grants',
      'TRIGGER') THEN
    RAISE EXCEPTION 'Runtime can access delegated family grant columns or triggers';
  END IF;
  denied := false;
  BEGIN
    PERFORM 1 FROM identity.family_delegated_player_grants LIMIT 1;
  EXCEPTION WHEN insufficient_privilege THEN denied := true;
  END;
  IF NOT denied THEN
    RAISE EXCEPTION 'Runtime can read protected delegated family grants';
  END IF;
  IF NOT has_function_privilege(current_user,
    'identity.issue_family_invitation(uuid,text,text,timestamptz,uuid,text,text,uuid)','EXECUTE')
    OR NOT has_function_privilege(current_user,
    'identity.cancel_family_invitation(uuid,text,text,timestamptz,uuid,uuid)','EXECUTE')
    OR NOT has_function_privilege(current_user,
    'identity.read_parent_family_outgoing(uuid,text,text,timestamptz)','EXECUTE')
    OR NOT has_function_privilege(current_user,
    'identity.read_recipient_family_invitations(uuid,text,text,timestamptz)','EXECUTE')
    OR NOT has_function_privilege(current_user,
    'identity.decline_recipient_family_invitation(uuid,text,text,timestamptz,uuid,uuid)','EXECUTE')
    OR NOT has_function_privilege(current_user,
    'identity.accept_recipient_family_invitation(uuid,text,text,timestamptz,uuid,uuid,uuid)','EXECUTE')
    OR NOT has_function_privilege(current_user,
    'identity.read_current_family_delegation(uuid,text,text,timestamptz)','EXECUTE')
    OR NOT has_function_privilege(current_user,
    'identity.current_family_calendar_scope(uuid,text,text,timestamptz,text,text)','EXECUTE')
    OR NOT has_function_privilege(current_user,
    'identity.read_parent_active_family_delegations(uuid,text,text,timestamptz)','EXECUTE')
    OR NOT has_function_privilege(current_user,
    'identity.revoke_family_delegated_player_grant(uuid,text,text,timestamptz,uuid,text)','EXECUTE') THEN
    RAISE EXCEPTION 'Scoped family functions not available to runtime';
  END IF;
  RAISE NOTICE 'Runtime permission checks passed; all probe writes will roll back';
END
$checks$;
\ir public-sections-permissions-check.sql
ROLLBACK;
