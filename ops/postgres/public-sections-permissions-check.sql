\set ON_ERROR_STOP on
\set ECHO none
-- Restricted runtime probe. Called inside the existing rollback-only checks.
DO $sections$
DECLARE target text; denied boolean;
BEGIN
  IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND
    (rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls))
  THEN RAISE EXCEPTION 'public_sections_probe_requires_restricted_role'; END IF;
  PERFORM version,documents_count,base_public_digest,exported_at FROM public_sections.snapshots LIMIT 0;
  PERFORM snapshot_version FROM public_sections.current LIMIT 0;
  PERFORM snapshot_version,kind,organization_id,id,data_json,display_order FROM public_sections.documents LIMIT 0;
  IF has_column_privilege(current_user,'public_sections.snapshots','sources_count','SELECT')
      OR has_column_privilege(current_user,'public_sections.snapshots','issues_count','SELECT')
      OR has_column_privilege(current_user,'public_sections.snapshots','source_commit','SELECT')
  THEN RAISE EXCEPTION 'public_sections_private_metadata_exposed'; END IF;
  FOREACH target IN ARRAY ARRAY['public_sections.snapshots','public_sections.current','public_sections.documents'] LOOP
    IF has_any_column_privilege(current_user,target,'INSERT,UPDATE,REFERENCES')
       OR has_table_privilege(current_user,target,'DELETE,TRUNCATE,TRIGGER')
    THEN RAISE EXCEPTION 'public_sections_runtime_can_write'; END IF;
  END LOOP;
  FOREACH target IN ARRAY ARRAY['migration.public_sections_source','migration.public_sections_evidence','migration.public_sections_issues'] LOOP
    denied := false;
    BEGIN EXECUTE format('SELECT 1 FROM %s LIMIT 0',target);
    EXCEPTION WHEN insufficient_privilege THEN denied := true; END;
    IF NOT denied THEN RAISE EXCEPTION 'public_sections_private_source_exposed'; END IF;
  END LOOP;
END $sections$;
