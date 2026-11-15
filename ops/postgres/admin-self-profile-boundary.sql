-- Executed by the dedicated web-admin role inside a disposable transaction.
-- No real account, password, phone, token or personal details may be read.
DO $self_profile_boundary$
DECLARE probe text:=replace(gen_random_uuid()::text,'-','')||
                    replace(gen_random_uuid()::text,'-','');
  fake_hash text:='$2b$12$'||repeat('.',53);
  denied boolean;
BEGIN
  IF EXISTS (SELECT 1 FROM identity.admin_sessions s WHERE s.token_digest=probe)
  THEN RAISE EXCEPTION 'Unexpected probe token collision'; END IF;
  denied:=false;
  BEGIN
    PERFORM identity.write_admin_own_name(probe,fake_hash,'Test person');
  EXCEPTION WHEN insufficient_privilege THEN denied:=true; END;
  IF NOT denied THEN
    RAISE EXCEPTION 'Self-name API SQL accepted a non-existent current session';
  END IF;
  denied:=false;
  BEGIN
    PERFORM identity.write_admin_own_password(probe,fake_hash,
      '$2b$12$'||repeat('A',53));
  EXCEPTION WHEN insufficient_privilege THEN denied:=true; END;
  IF NOT denied THEN
    RAISE EXCEPTION 'Self-password API SQL accepted a non-existent current session';
  END IF;
END $self_profile_boundary$;
