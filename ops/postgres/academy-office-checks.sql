\set ON_ERROR_STOP on
BEGIN;
DO $test$
DECLARE
  manager uuid:=gen_random_uuid();
  digest text:=repeat('c',64);
  registration uuid:=gen_random_uuid();
  order_id uuid:=gen_random_uuid();
  player text:='academy-office-fixture';
BEGIN
  INSERT INTO identity.admin_accounts(id,username,display_name,role,is_active,password_hash)
    VALUES(manager,'academy_office_fixture','Fixture Manager','manager',true,'fixture_only');
  INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id)
    VALUES(manager,'lefteria_fc_academy');
  INSERT INTO identity.admin_sessions(id,token_digest,admin_account_id,credential_digest,created_at,expires_at)
    VALUES(gen_random_uuid(),digest,manager,repeat('a',64),clock_timestamp(),clock_timestamp()+interval '1 hour');
  INSERT INTO club.players(id,organization_id,name,number,position,team_type,is_active,is_archived,is_transferred,has_explicit_picker_fields)
    VALUES(player,'lefteria_fc_academy','Fixture Player',1,'GK','Academy',true,false,false,true);

  PERFORM identity.admin_academy_office(digest,'register',jsonb_build_object('id',registration,'first_name','Fixture',
    'last_name','Applicant','birth_date','2016-01-01','guardian_name','Fixture Guardian',
    'guardian_relationship','Parent','guardian_phone','+35799123456','guardian_email','fixture@example.invalid'));
  IF NOT EXISTS(SELECT 1 FROM academy_intake.registrations WHERE id=registration AND status='pending' AND recorded_by=manager) THEN
    RAISE EXCEPTION 'Registration was not recorded';
  END IF;
  PERFORM identity.admin_academy_office(digest,'review',jsonb_build_object('id',registration,'status','under_review','version',1));
  PERFORM identity.admin_academy_office(digest,'review',jsonb_build_object('id',registration,'status','approved','version',2));
  SET CONSTRAINTS ALL IMMEDIATE;
  IF NOT EXISTS(SELECT 1 FROM academy_intake.registrations WHERE id=registration AND status='approved'
      AND decided_by=manager AND onboarded_player_id IS NULL) THEN
    RAISE EXCEPTION 'Registration review state was incorrect';
  END IF;
  IF jsonb_array_length(identity.admin_academy_office(digest,'players','{"search":"Fixture Player"}'))<>1 THEN
    RAISE EXCEPTION 'Academy player search failed';
  END IF;
  PERFORM identity.admin_academy_office(digest,'order',jsonb_build_object('id',order_id,'player_id',player,
    'item','Training kit','size','152','quantity',2));
  PERFORM identity.admin_academy_office(digest,'fulfilled',jsonb_build_object('id',order_id));
  IF NOT EXISTS(SELECT 1 FROM academy_intake.internal_orders WHERE id=order_id
      AND status='fulfilled' AND closed_by=manager AND quantity=2) THEN
    RAISE EXCEPTION 'Order was not fulfilled';
  END IF;
  BEGIN
    PERFORM identity.admin_academy_office(digest,'cancelled',jsonb_build_object('id',order_id));
    RAISE EXCEPTION 'Closed order changed';
  EXCEPTION WHEN unique_violation THEN NULL; END;
  DELETE FROM identity.admin_organization_grants WHERE admin_account_id=manager;
  BEGIN
    PERFORM identity.admin_academy_office(digest,'registrations','{}');
    RAISE EXCEPTION 'Revoked Academy authority retained access';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $test$;
ROLLBACK;
