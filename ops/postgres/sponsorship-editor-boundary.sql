\set ON_ERROR_STOP on
\set ECHO none
-- Run AFTER the migration, public-sections-app-grants.sql and
-- web-admin-auth-grants.sql in the disposable staging database.
\if :{?app_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'app_role_required'; END $required$;
\endif
\if :{?admin_auth_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'admin_auth_role_required'; END $required$;
\endif
BEGIN;
SELECT set_config('lefteria.sponsor_app_role',:'app_role',true),
       set_config('lefteria.sponsor_admin_role',:'admin_auth_role',true);
DO $audit$
DECLARE mobile text:=current_setting('lefteria.sponsor_app_role');
        admin text:=current_setting('lefteria.sponsor_admin_role');
BEGIN
  IF mobile=admin OR current_database() IN ('postgres','template0','template1')
    OR to_regclass('public_content.sponsorship_editorial') IS NULL
    OR NOT has_function_privilege(mobile,'public_content.read_published_sponsorship(text)','EXECUTE')
    OR has_function_privilege(mobile,'public_content.current_sponsorship_exists(text,text,text)','EXECUTE')
    OR has_table_privilege(mobile,'public_content.sponsorship_editorial','SELECT')
    OR has_column_privilege(mobile,'public_content.sponsorship_editorial','draft_json','SELECT')
    OR has_table_privilege(mobile,'public_content.sponsorship_editorial','INSERT')
    OR NOT has_function_privilege(admin,'public_content.current_sponsorship_exists(text,text,text)','EXECUTE')
    OR has_function_privilege(admin,'public_content.read_published_sponsorship(text)','EXECUTE')
    OR NOT has_table_privilege(admin,'public_content.sponsorship_editorial','SELECT')
    OR NOT has_column_privilege(admin,'public_content.sponsorship_editorial','draft_json','UPDATE')
  THEN RAISE EXCEPTION 'sponsorship_editorial_permission_boundary_failed'; END IF;
  IF jsonb_typeof(public_content.read_published_sponsorship('unknown_organization')) <> 'array'
     OR public_content.read_published_sponsorship('unknown_organization') <> '[]'::jsonb
     OR public_content.current_sponsorship_exists('unknown_organization','sponsor','one')
  THEN RAISE EXCEPTION 'sponsorship_unknown_owner_must_fail_closed'; END IF;
END $audit$;
-- Real disposable-owner lifecycle: private draft, approved publication,
-- later unsent edit, other-owner isolation and withdrawal. All writes roll back.
DO $native_lifecycle$
DECLARE
  id constant text := 'cms-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';
  draft jsonb := jsonb_build_object('name','Disposable sponsor','description',null,
    'image_url',null,'website_url',null,'display_order',0);
  edited jsonb := jsonb_build_object('name','Private revised sponsor','description',null,
    'image_url',null,'website_url',null,'display_order',0);
  visible jsonb;
BEGIN
  INSERT INTO public_content.sponsorship_editorial
    (organization_id,kind,item_id,draft_json,is_native)
  VALUES('lefteria_fc','sponsor',id,draft,true);
  SELECT jsonb_agg(e) INTO visible
    FROM jsonb_array_elements(public_content.read_published_sponsorship('lefteria_fc')) e
   WHERE e->>'item_id'=id;
  IF visible IS NOT NULL THEN RAISE EXCEPTION 'private_native_draft_was_public'; END IF;
  UPDATE public_content.sponsorship_editorial
    SET published_json=draft_json,published_at=clock_timestamp(),version=version+1
    WHERE organization_id='lefteria_fc' AND kind='sponsor' AND item_id=id;
  SELECT e INTO visible
    FROM jsonb_array_elements(public_content.read_published_sponsorship('lefteria_fc')) e
    WHERE e->>'item_id'=id;
  IF visible IS NULL OR visible->>'is_native'<>'true' OR
    visible->'content'->>'name'<>'Disposable sponsor'
  THEN RAISE EXCEPTION 'approved_native_sponsor_missing'; END IF;
  IF EXISTS(SELECT 1
    FROM jsonb_array_elements(public_content.read_published_sponsorship('lefteria_fc_academy')) e
    WHERE e->>'item_id'=id)
  THEN RAISE EXCEPTION 'first_team_sponsor_exposed_in_academy'; END IF;
  UPDATE public_content.sponsorship_editorial
    SET draft_json=edited,version=version+1
    WHERE organization_id='lefteria_fc' AND kind='sponsor' AND item_id=id;
  SELECT e INTO visible
    FROM jsonb_array_elements(public_content.read_published_sponsorship('lefteria_fc')) e
    WHERE e->>'item_id'=id;
  IF visible->'content'->>'name'<>'Disposable sponsor'
  THEN RAISE EXCEPTION 'unapproved_revision_leaked'; END IF;
  UPDATE public_content.sponsorship_editorial
    SET published_json=NULL,published_at=NULL,version=version+1
    WHERE organization_id='lefteria_fc' AND kind='sponsor' AND item_id=id;
  IF EXISTS(SELECT 1
    FROM jsonb_array_elements(public_content.read_published_sponsorship('lefteria_fc')) e
    WHERE e->>'item_id'=id)
  THEN RAISE EXCEPTION 'unpublished_native_record_still_public'; END IF;
END $native_lifecycle$;
ROLLBACK;
