-- Verify the only HUB editorial reader is a locked-down SECURITY DEFINER
-- routine with no raw HUB table grants for either runtime role.
DO $hub_editorial_boundary$
DECLARE
  auth_role text := current_setting('lefteria.admin_auth_role');
  mobile_role text := current_setting('lefteria.mobile_runtime_role');
  routine pg_proc%ROWTYPE;
  writer pg_proc%ROWTYPE;
  reviewer pg_proc%ROWTYPE;
  detail_reader pg_proc%ROWTYPE;
  text_reviewer pg_proc%ROWTYPE;
  draft_reviser pg_proc%ROWTYPE;
  rejection_returner pg_proc%ROWTYPE;
  image_quarantiner pg_proc%ROWTYPE;
  image_previewer pg_proc%ROWTYPE;
  visual_rejecter pg_proc%ROWTYPE;
  visual_approver pg_proc%ROWTYPE;
  content_approver pg_proc%ROWTYPE;
  ai_brief_writer pg_proc%ROWTYPE;
  target record;
BEGIN
  SELECT * INTO STRICT routine FROM pg_proc
    WHERE oid='hub.read_admin_editorial(text,text)'::regprocedure;
  IF NOT routine.prosecdef OR routine.prorettype<>'jsonb'::regtype
    OR routine.provolatile<>'v' OR routine.prokind<>'f'
    OR routine.proowner<>(SELECT relowner FROM pg_class WHERE oid='hub.content'::regclass)
    OR NOT EXISTS(
      SELECT 1 FROM unnest(coalesce(routine.proconfig,ARRAY[]::text[])) AS conf(value)
      WHERE regexp_replace(conf.value,'[[:space:]]','','g')='search_path=pg_catalog,pg_temp')
    OR EXISTS (
      SELECT 1 FROM aclexplode(coalesce(routine.proacl,acldefault('f',routine.proowner)))
      WHERE grantee=0 AND privilege_type='EXECUTE')
    OR NOT has_function_privilege(auth_role,routine.oid,'EXECUTE')
    OR has_function_privilege(mobile_role,routine.oid,'EXECUTE') THEN
    RAISE EXCEPTION 'Hub editorial function permissions unsafe';
  END IF;
  SELECT * INTO STRICT writer FROM pg_proc
    WHERE oid='hub.create_admin_draft(text,text,uuid,uuid,text,text,text,boolean,boolean,smallint,smallint)'::regprocedure;
  IF NOT writer.prosecdef OR writer.provolatile<>'v'
    OR writer.prorettype<>'jsonb'::regtype OR writer.prokind<>'f'
    OR writer.proowner<>(SELECT relowner FROM pg_class WHERE oid='hub.content'::regclass)
    OR NOT EXISTS (
      SELECT 1 FROM unnest(coalesce(writer.proconfig,ARRAY[]::text[])) AS conf(value)
      WHERE regexp_replace(conf.value,'[[:space:]]','','g')='search_path=pg_catalog,pg_temp')
    OR EXISTS (
      SELECT 1 FROM aclexplode(coalesce(writer.proacl,acldefault('f',writer.proowner)))
      WHERE grantee=0 AND privilege_type='EXECUTE')
    OR NOT has_function_privilege(auth_role,writer.oid,'EXECUTE')
    OR has_function_privilege(mobile_role,writer.oid,'EXECUTE') THEN
    RAISE EXCEPTION 'Hub draft writer function permissions unsafe';
  END IF;
  SELECT * INTO STRICT reviewer FROM pg_proc
    WHERE oid='hub.submit_admin_draft_review(text,text,uuid,bigint)'::regprocedure;
  IF NOT reviewer.prosecdef OR reviewer.provolatile<>'v'
    OR reviewer.prorettype<>'jsonb'::regtype OR reviewer.prokind<>'f'
    OR reviewer.proowner<>(SELECT relowner FROM pg_class WHERE oid='hub.content'::regclass)
    OR NOT EXISTS (
      SELECT 1 FROM unnest(coalesce(reviewer.proconfig,ARRAY[]::text[])) AS conf(value)
      WHERE regexp_replace(conf.value,'[[:space:]]','','g')='search_path=pg_catalog,pg_temp')
    OR EXISTS (
      SELECT 1 FROM aclexplode(coalesce(reviewer.proacl,acldefault('f',reviewer.proowner)))
      WHERE grantee=0 AND privilege_type='EXECUTE')
    OR NOT has_function_privilege(auth_role,reviewer.oid,'EXECUTE')
    OR has_function_privilege(mobile_role,reviewer.oid,'EXECUTE') THEN
    RAISE EXCEPTION 'Hub review submit function permissions unsafe';
  END IF;
  SELECT * INTO STRICT detail_reader FROM pg_proc
    WHERE oid='hub.read_admin_editorial_detail(text,text,uuid)'::regprocedure;
  IF NOT detail_reader.prosecdef OR detail_reader.provolatile<>'v'
    OR detail_reader.prorettype<>'jsonb'::regtype OR detail_reader.prokind<>'f'
    OR detail_reader.proowner<>(SELECT relowner FROM pg_class WHERE oid='hub.content'::regclass)
    OR NOT EXISTS(
      SELECT 1 FROM unnest(coalesce(detail_reader.proconfig,ARRAY[]::text[])) AS conf(value)
      WHERE regexp_replace(conf.value,'[[:space:]]','','g')='search_path=pg_catalog,pg_temp')
    OR EXISTS(
      SELECT 1 FROM aclexplode(coalesce(detail_reader.proacl,acldefault('f',detail_reader.proowner)))
      WHERE grantee=0 AND privilege_type='EXECUTE')
    OR NOT has_function_privilege(auth_role,detail_reader.oid,'EXECUTE')
    OR has_function_privilege(mobile_role,detail_reader.oid,'EXECUTE') THEN
    RAISE EXCEPTION 'HUB draft detail reader permissions unsafe';
  END IF;
  SELECT * INTO STRICT text_reviewer FROM pg_proc
    WHERE oid='hub.review_admin_text(text,text,uuid,bigint,text)'::regprocedure;
  IF NOT text_reviewer.prosecdef OR text_reviewer.provolatile<>'v'
    OR text_reviewer.prorettype<>'jsonb'::regtype OR text_reviewer.prokind<>'f'
    OR text_reviewer.proowner<>(SELECT relowner FROM pg_class WHERE oid='hub.content'::regclass)
    OR NOT EXISTS (
      SELECT 1 FROM unnest(coalesce(text_reviewer.proconfig,ARRAY[]::text[])) AS conf(value)
      WHERE regexp_replace(conf.value,'[[:space:]]','','g')='search_path=pg_catalog,pg_temp')
    OR EXISTS (
      SELECT 1 FROM aclexplode(coalesce(text_reviewer.proacl,acldefault('f',text_reviewer.proowner)))
      WHERE grantee=0 AND privilege_type='EXECUTE')
    OR NOT has_function_privilege(auth_role,text_reviewer.oid,'EXECUTE')
    OR has_function_privilege(mobile_role,text_reviewer.oid,'EXECUTE') THEN
    RAISE EXCEPTION 'Hub text review function permissions unsafe';
  END IF;
  SELECT * INTO STRICT draft_reviser FROM pg_proc
    WHERE oid='hub.revise_admin_draft(text,text,uuid,bigint,text,text)'::regprocedure;
  IF NOT draft_reviser.prosecdef OR draft_reviser.provolatile<>'v'
    OR draft_reviser.prorettype<>'jsonb'::regtype OR draft_reviser.prokind<>'f'
    OR draft_reviser.proowner<>(SELECT relowner FROM pg_class WHERE oid='hub.content'::regclass)
    OR NOT EXISTS (
      SELECT 1 FROM unnest(coalesce(draft_reviser.proconfig,ARRAY[]::text[])) AS conf(value)
      WHERE regexp_replace(conf.value,'[[:space:]]','','g')='search_path=pg_catalog,pg_temp')
    OR EXISTS (
      SELECT 1 FROM aclexplode(coalesce(draft_reviser.proacl,acldefault('f',draft_reviser.proowner)))
      WHERE grantee=0 AND privilege_type='EXECUTE')
    OR NOT has_function_privilege(auth_role,draft_reviser.oid,'EXECUTE')
    OR has_function_privilege(mobile_role,draft_reviser.oid,'EXECUTE') THEN
    RAISE EXCEPTION 'Hub draft revision function permissions unsafe';
  END IF;
  SELECT * INTO STRICT rejection_returner FROM pg_proc
    WHERE oid='hub.return_rejected_admin_draft(text,text,uuid,bigint)'::regprocedure;
  IF NOT rejection_returner.prosecdef OR rejection_returner.provolatile<>'v'
    OR rejection_returner.prorettype<>'jsonb'::regtype OR rejection_returner.prokind<>'f'
    OR rejection_returner.proowner<>(SELECT relowner FROM pg_class WHERE oid='hub.content'::regclass)
    OR NOT EXISTS (
      SELECT 1 FROM unnest(coalesce(rejection_returner.proconfig,ARRAY[]::text[])) AS conf(value)
      WHERE regexp_replace(conf.value,'[[:space:]]','','g')='search_path=pg_catalog,pg_temp')
    OR EXISTS (
      SELECT 1 FROM aclexplode(coalesce(rejection_returner.proacl,acldefault('f',rejection_returner.proowner)))
      WHERE grantee=0 AND privilege_type='EXECUTE')
    OR NOT has_function_privilege(auth_role,rejection_returner.oid,'EXECUTE')
    OR has_function_privilege(mobile_role,rejection_returner.oid,'EXECUTE') THEN
    RAISE EXCEPTION 'Hub rejected-return function permissions unsafe';
  END IF;
  SELECT * INTO STRICT image_quarantiner FROM pg_proc
    WHERE oid='hub.quarantine_admin_image(text,text,uuid,bigint,uuid,text,bytea)'::regprocedure;
  IF NOT image_quarantiner.prosecdef OR image_quarantiner.provolatile<>'v'
    OR image_quarantiner.prorettype<>'jsonb'::regtype OR image_quarantiner.prokind<>'f'
    OR image_quarantiner.proowner<>(SELECT relowner FROM pg_class WHERE oid='hub.content'::regclass)
    OR NOT EXISTS (
      SELECT 1 FROM unnest(coalesce(image_quarantiner.proconfig,ARRAY[]::text[])) AS conf(value)
      WHERE regexp_replace(conf.value,'[[:space:]]','','g')='search_path=pg_catalog,pg_temp')
    OR EXISTS (
      SELECT 1 FROM aclexplode(coalesce(image_quarantiner.proacl,acldefault('f',image_quarantiner.proowner)))
      WHERE grantee=0 AND privilege_type='EXECUTE')
    OR NOT has_function_privilege(auth_role,image_quarantiner.oid,'EXECUTE')
    OR has_function_privilege(mobile_role,image_quarantiner.oid,'EXECUTE') THEN
    RAISE EXCEPTION 'Hub image quarantine function permissions unsafe';
  END IF;
  SELECT * INTO STRICT image_previewer FROM pg_proc
    WHERE oid='hub.read_admin_quarantined_image(text,text,uuid,uuid)'::regprocedure;
  IF NOT image_previewer.prosecdef OR image_previewer.provolatile<>'v'
    OR image_previewer.prorettype<>'jsonb'::regtype OR image_previewer.prokind<>'f'
    OR image_previewer.proowner<>(SELECT relowner FROM pg_class WHERE oid='hub.content'::regclass)
    OR NOT EXISTS (
      SELECT 1 FROM unnest(coalesce(image_previewer.proconfig,ARRAY[]::text[])) AS conf(value)
      WHERE regexp_replace(conf.value,'[[:space:]]','','g')='search_path=pg_catalog,pg_temp')
    OR EXISTS (
      SELECT 1 FROM aclexplode(coalesce(image_previewer.proacl,acldefault('f',image_previewer.proowner)))
      WHERE grantee=0 AND privilege_type='EXECUTE')
    OR NOT has_function_privilege(auth_role,image_previewer.oid,'EXECUTE')
    OR has_function_privilege(mobile_role,image_previewer.oid,'EXECUTE') THEN
    RAISE EXCEPTION 'Hub private preview function permissions unsafe';
  END IF;
  SELECT * INTO STRICT visual_rejecter FROM pg_proc
    WHERE oid='hub.reject_admin_hub_image(text,text,uuid,bigint,uuid)'::regprocedure;
  IF NOT visual_rejecter.prosecdef OR visual_rejecter.provolatile<>'v'
    OR visual_rejecter.prorettype<>'jsonb'::regtype OR visual_rejecter.prokind<>'f'
    OR visual_rejecter.proowner<>(SELECT relowner FROM pg_class WHERE oid='hub.content'::regclass)
    OR NOT EXISTS (
      SELECT 1 FROM unnest(coalesce(visual_rejecter.proconfig,ARRAY[]::text[])) AS conf(value)
      WHERE regexp_replace(conf.value,'[[:space:]]','','g')='search_path=pg_catalog,pg_temp')
    OR EXISTS (
      SELECT 1 FROM aclexplode(coalesce(visual_rejecter.proacl,acldefault('f',visual_rejecter.proowner)))
      WHERE grantee=0 AND privilege_type='EXECUTE')
    OR NOT has_function_privilege(auth_role,visual_rejecter.oid,'EXECUTE')
    OR has_function_privilege(mobile_role,visual_rejecter.oid,'EXECUTE') THEN
    RAISE EXCEPTION 'Hub manager visual rejection permissions unsafe';
  END IF;
  SELECT * INTO STRICT visual_approver FROM pg_proc
    WHERE oid='hub.approve_admin_hub_visual(text,text,uuid,bigint)'::regprocedure;
  IF NOT visual_approver.prosecdef OR visual_approver.provolatile<>'v'
    OR visual_approver.prorettype<>'jsonb'::regtype OR visual_approver.prokind<>'f'
    OR visual_approver.proowner<>(SELECT relowner FROM pg_class WHERE oid='hub.content'::regclass)
    OR NOT EXISTS (
      SELECT 1 FROM unnest(coalesce(visual_approver.proconfig,ARRAY[]::text[])) AS conf(value)
      WHERE regexp_replace(conf.value,'[[:space:]]','','g')='search_path=pg_catalog,pg_temp')
    OR EXISTS (
      SELECT 1 FROM aclexplode(coalesce(visual_approver.proacl,acldefault('f',visual_approver.proowner)))
      WHERE grantee=0 AND privilege_type='EXECUTE')
    OR NOT has_function_privilege(auth_role,visual_approver.oid,'EXECUTE')
    OR has_function_privilege(mobile_role,visual_approver.oid,'EXECUTE') THEN
    RAISE EXCEPTION 'Hub manager visual approval permissions unsafe';
  END IF;
  SELECT * INTO STRICT ai_brief_writer FROM pg_proc
    WHERE oid='hub.request_admin_ai_education(text,text,uuid,text,smallint,smallint,text,text)'::regprocedure;
  IF NOT ai_brief_writer.prosecdef OR ai_brief_writer.provolatile<>'v'
    OR ai_brief_writer.prorettype<>'jsonb'::regtype OR ai_brief_writer.prokind<>'f'
    OR ai_brief_writer.proowner<>(SELECT relowner FROM pg_class WHERE oid='hub.content'::regclass)
    OR NOT EXISTS (
      SELECT 1 FROM unnest(coalesce(ai_brief_writer.proconfig,ARRAY[]::text[])) AS conf(value)
      WHERE regexp_replace(conf.value,'[[:space:]]','','g')='search_path=pg_catalog,pg_temp')
    OR EXISTS (
      SELECT 1 FROM aclexplode(coalesce(ai_brief_writer.proacl,acldefault('f',ai_brief_writer.proowner)))
      WHERE grantee=0 AND privilege_type='EXECUTE')
    OR NOT has_function_privilege(auth_role,ai_brief_writer.oid,'EXECUTE')
    OR has_function_privilege(mobile_role,ai_brief_writer.oid,'EXECUTE') THEN
    RAISE EXCEPTION 'Hub AI education brief permissions unsafe';
  END IF;
  FOR target IN SELECT table_name
    FROM information_schema.tables
    WHERE table_schema='hub' AND table_type='BASE TABLE'
  LOOP
    IF has_any_column_privilege(auth_role,format('hub.%I',target.table_name),'SELECT,INSERT,UPDATE,REFERENCES')
      OR has_table_privilege(auth_role,format('hub.%I',target.table_name),'DELETE,TRUNCATE,TRIGGER')
      OR has_any_column_privilege(mobile_role,format('hub.%I',target.table_name),'SELECT,INSERT,UPDATE,REFERENCES')
      OR has_table_privilege(mobile_role,format('hub.%I',target.table_name),'DELETE,TRUNCATE,TRIGGER') THEN
      RAISE EXCEPTION 'Direct HUB table access is forbidden';
    END IF;
  END LOOP;
END $hub_editorial_boundary$;
