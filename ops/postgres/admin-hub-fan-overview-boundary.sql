-- Executed after the explicit operator grant, against disposable staging SQL.
-- Never grant the admin or mobile runtime access to private subscriber tables.
DO $hub_fan_overview_boundary$
DECLARE
  auth_role text := current_setting('lefteria.admin_auth_role');
  mobile_role text := current_setting('lefteria.mobile_runtime_role');
  routine pg_proc%ROWTYPE;
  relation_name text;
BEGIN
  SELECT * INTO STRICT routine FROM pg_proc
    WHERE oid='hub.read_admin_fan_subscription_overview(text,text)'::regprocedure;
  IF NOT routine.prosecdef OR routine.prokind<>'f'
    OR routine.provolatile<>'v' OR routine.prorettype<>'jsonb'::regtype
    OR routine.proowner<>(SELECT relowner FROM pg_class WHERE oid='hub.fan_subscriptions'::regclass)
    OR NOT EXISTS (
      SELECT 1 FROM unnest(coalesce(routine.proconfig,ARRAY[]::text[])) AS conf(value)
      WHERE regexp_replace(conf.value,'[[:space:]]','','g')='search_path=pg_catalog,pg_temp')
    OR EXISTS (
      SELECT 1 FROM aclexplode(coalesce(routine.proacl,acldefault('f',routine.proowner)))
      WHERE grantee=0 AND privilege_type='EXECUTE')
    OR NOT has_function_privilege(auth_role,routine.oid,'EXECUTE')
    OR has_function_privilege(mobile_role,routine.oid,'EXECUTE')
  THEN RAISE EXCEPTION 'HUB fan overview function boundary unsafe'; END IF;

  FOREACH relation_name IN ARRAY ARRAY[
    'hub.fan_subscriptions','hub.fan_plan_prices','hub.fan_checkout_links',
    'finance.payment_requests','finance.payment_notifications'
  ] LOOP
    IF has_any_column_privilege(auth_role,relation_name,'SELECT,INSERT,UPDATE,REFERENCES')
      OR has_table_privilege(auth_role,relation_name,'DELETE,TRUNCATE,TRIGGER')
      OR has_any_column_privilege(mobile_role,relation_name,'SELECT,INSERT,UPDATE,REFERENCES')
      OR has_table_privilege(mobile_role,relation_name,'DELETE,TRUNCATE,TRIGGER')
    THEN RAISE EXCEPTION 'Raw payment or HUB fan relation privilege detected: %',relation_name; END IF;
  END LOOP;
END
$hub_fan_overview_boundary$;
