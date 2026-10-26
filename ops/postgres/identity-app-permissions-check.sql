\set ON_ERROR_STOP on
\set ECHO none

-- Connect as the runtime role to a disposable test database. All probe writes
-- are rolled back; this does not delete application rows or tables.
BEGIN;
DO $checks$
DECLARE
  denied boolean;
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
  RAISE NOTICE 'Runtime permission checks passed; all probe writes will roll back';
END
$checks$;
ROLLBACK;
