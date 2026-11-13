-- Verify human quiz editorial routines never give the mobile runtime
-- answer-key access, raw progress access, or a publication bypass.
DO $quiz_editorial_boundary$
DECLARE
  auth_role text:=current_setting('lefteria.admin_auth_role');
  mobile_role text:=current_setting('lefteria.mobile_runtime_role');
  signature text;
  routine pg_proc%ROWTYPE;
BEGIN
  FOR signature IN SELECT unnest(ARRAY[
    'hub.create_admin_player_quiz(text,text,uuid,integer,text,text,text,jsonb)',
    'hub.list_admin_player_quizzes(text,text)',
    'hub.detail_admin_player_quiz(text,text,uuid)',
    'hub.approve_admin_player_quiz(text,text,uuid)',
    'hub.publish_admin_player_quiz(text,text,uuid)',
    'hub.withdraw_admin_player_quiz(text,text,uuid)'
  ]) LOOP
    SELECT * INTO STRICT routine FROM pg_proc WHERE oid=signature::regprocedure;
    IF NOT routine.prosecdef OR routine.provolatile<>'v'
      OR routine.prorettype<>'jsonb'::regtype OR routine.prokind<>'f'
      OR routine.proowner<>(SELECT relowner FROM pg_class
                             WHERE oid='hub.player_quizzes'::regclass)
      OR NOT EXISTS (
        SELECT 1 FROM unnest(coalesce(routine.proconfig,ARRAY[]::text[])) AS conf(value)
        WHERE regexp_replace(conf.value,'[[:space:]]','','g')
          ='search_path=pg_catalog,pg_temp')
      OR EXISTS (
        SELECT 1 FROM aclexplode(coalesce(
          routine.proacl,acldefault('f',routine.proowner)))
        WHERE grantee=0 AND privilege_type='EXECUTE')
      OR NOT has_function_privilege(auth_role,routine.oid,'EXECUTE')
      OR has_function_privilege(mobile_role,routine.oid,'EXECUTE') THEN
      RAISE EXCEPTION 'Quiz editorial routine permissions unsafe: %',signature;
    END IF;
  END LOOP;
  SELECT * INTO STRICT routine FROM pg_proc
    WHERE oid='hub.authorize_admin_player_quiz(text,text,boolean)'::regprocedure;
  IF NOT routine.prosecdef OR routine.provolatile<>'v'
    OR routine.prorettype<>'jsonb'::regtype
    OR routine.proowner<>(SELECT relowner FROM pg_class
      WHERE oid='hub.player_quizzes'::regclass)
    OR has_function_privilege(auth_role,routine.oid,'EXECUTE')
    OR has_function_privilege(mobile_role,routine.oid,'EXECUTE')
    OR EXISTS (
      SELECT 1 FROM aclexplode(coalesce(
        routine.proacl,acldefault('f',routine.proowner)))
      WHERE grantee=0 AND privilege_type='EXECUTE') THEN
    RAISE EXCEPTION 'Quiz admin authority helper is not private';
  END IF;
END
$quiz_editorial_boundary$;
