\set ON_ERROR_STOP on
\set ECHO none
-- Required confirmation values are supplied as psql variables; quote with :'name'.
-- No credential material is accepted on the command line.
\if :{?apply_recovery}
\else
  \set apply_recovery false
\endif
\if :{?approve_password_only}
\else
  \set approve_password_only false
\endif
BEGIN;
\ir preview-owner-copy-function.sql
SELECT pg_temp.prepare_preview_owner_copy(
    :'expected_database', :'expected_import_sha256', :'source_legacy_id'::uuid,
    :'expected_source_sha256', :'expected_username', :'expected_email',
    :'approve_password_only'::boolean
) AS result;
\if :apply_recovery
  COMMIT;
  \echo 'Preview-only owner copy committed.'
\else
  ROLLBACK;
  \echo 'Dry run complete; all account, grant and audit changes rolled back.'
\endif
