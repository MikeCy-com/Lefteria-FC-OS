\set ON_ERROR_STOP on
-- Exercise private Academy intake migration structures and default-deny policy
-- after EF apply/rollback/reapply in the disposable PostgreSQL container.
BEGIN;
DO $checks$
DECLARE
  tbl text;
  role_name text;
  tg text;
BEGIN
  IF to_regnamespace('academy_intake') IS NULL THEN
    RAISE EXCEPTION 'private registration schema missing';
  END IF;
  FOREACH tbl IN ARRAY ARRAY['registrations','registration_consents',
                              'registration_sealed_evidence','registration_review_events']
  LOOP
    IF NOT EXISTS (
      SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
      WHERE n.nspname='academy_intake' AND c.relname=tbl AND c.relrowsecurity
    ) THEN RAISE EXCEPTION 'RLS disabled on %', tbl; END IF;
    FOR role_name IN SELECT rolname FROM pg_roles
      WHERE rolname IN ('lefteria_identity_tests_app', 'lefteria_admin_auth_tests')
    LOOP
      IF has_table_privilege(role_name,'academy_intake.'||tbl,'SELECT')
        OR has_table_privilege(role_name,'academy_intake.'||tbl,'INSERT')
        OR has_table_privilege(role_name,'academy_intake.'||tbl,'UPDATE')
        OR has_table_privilege(role_name,'academy_intake.'||tbl,'DELETE')
      THEN RAISE EXCEPTION 'Unexpected runtime data privilege on % for %',tbl,role_name;
      END IF;
    END LOOP;
  END LOOP;
  FOR role_name IN SELECT rolname FROM pg_roles WHERE rolname IN ('lefteria_identity_tests_app', 'lefteria_admin_auth_tests')
  LOOP
    IF has_schema_privilege(role_name,'academy_intake','USAGE') THEN
      RAISE EXCEPTION 'Unexpected runtime schema access for %',role_name;
    END IF;
  END LOOP;
  FOREACH tg IN ARRAY ARRAY['consent_history_append_only',
                             'review_events_append_only',
                             'registration_status_audit']
  LOOP
    IF NOT EXISTS (
      SELECT 1 FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid
      JOIN pg_namespace n ON n.oid=c.relnamespace
      WHERE n.nspname='academy_intake' AND t.tgname=tg AND t.tgenabled='O'
    ) THEN RAISE EXCEPTION 'Missing or disabled evidence trigger %',tg; END IF;
  END LOOP;
  IF NOT EXISTS (
    SELECT 1 FROM pg_trigger
    WHERE tgname='registration_status_audit' AND tgdeferrable AND tginitdeferred
  ) THEN RAISE EXCEPTION 'Registration status audit must defer to commit'; END IF;
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conrelid='academy_intake.registration_consents'::regclass
      AND contype='p' AND array_length(conkey,1)=3
  ) THEN RAISE EXCEPTION 'Versioned consent primary key missing'; END IF;
END
$checks$;
ROLLBACK;
