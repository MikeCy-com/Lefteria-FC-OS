\set ON_ERROR_STOP on
-- Isolated synthetic fixture only. All manager/player/secret data is rolled
-- back; no DOB, parent contacts, social posts, or public card is generated.
BEGIN;
DO $birthday_lifecycle$
DECLARE
  v_owner constant text:='lefteria_fc_academy';
  v_actor uuid:=gen_random_uuid();
  v_session uuid:=gen_random_uuid();
  v_player text:='ci_birthday_'||left(replace(gen_random_uuid()::text,'-',''),20);
  v_digest text:=repeat('e',64);
  v_group text:='ci_birthday_grp_'||left(replace(gen_random_uuid()::text,'-',''),12);
  v_result jsonb;
BEGIN
  INSERT INTO club.roster_import_state(
    id,digest,source_repository,source_commit,environment,
    teams_count,academy_groups_count,players_count,memberships_count,applied_at)
  VALUES('initial',repeat('a',64),'MikeCy-com/Lefteria-FC',
    repeat('a',40),'development',0,1,0,0,clock_timestamp());
  INSERT INTO club.academy_groups(id,organization_id,name)
    VALUES(v_group,v_owner,'Disposable CI birthday group');
  INSERT INTO club.players(id,organization_id,name,number,position,
    team_type,team_id,primary_academy_group_id,image_url,
    is_active,is_archived,is_transferred,has_explicit_picker_fields)
  VALUES(v_player,v_owner,'Synthetic private birthday record',17,'Midfielder',
    'Academy',NULL,v_group,NULL,true,false,false,true);
  INSERT INTO identity.admin_accounts(
    id,username,display_name,role,is_active,password_hash)
  VALUES(v_actor,'ci_birthday_'||left(replace(v_actor::text,'-',''),14),
    'CI Birthday Manager','manager',true,'ci_not_a_real_password_hash');
  INSERT INTO identity.admin_organization_grants(
    admin_account_id,organization_id) VALUES(v_actor,v_owner);
  INSERT INTO identity.admin_sessions(
    id,token_digest,admin_account_id,credential_digest,
    created_at,expires_at,revoked_at)
  VALUES(v_session,v_digest,v_actor,repeat('b',64),
    clock_timestamp(),clock_timestamp()+interval '1 hour',NULL);

  v_result:=club.read_admin_player_birthday_preferences(
    v_digest,v_owner,v_player);
  IF (v_result->>'version')::integer<>0
    OR v_result->>'disabled'<>'true'
    OR v_result->>'public_enabled'<>'false'
    OR v_result->>'auto_post_facebook'<>'false'
    OR v_result->>'auto_post_instagram'<>'false' THEN
    RAISE EXCEPTION 'Default birthday preferences not private opt-out';
  END IF;
  BEGIN
    PERFORM club.write_admin_player_birthday_preferences(
      v_digest,v_owner,v_player,0,true,'Greek greeting','#00000G','🎂');
    RAISE EXCEPTION 'Invalid birthday card color was accepted';
  EXCEPTION WHEN invalid_parameter_value THEN NULL;
  END;
  BEGIN
    PERFORM club.write_admin_player_birthday_preferences(
      v_digest,v_owner,v_player,0,true,'Greeting'||chr(10),'#F5A623','🎂');
    RAISE EXCEPTION 'Control character in birthday wish accepted';
  EXCEPTION WHEN invalid_parameter_value THEN NULL;
  END;
  v_result:=club.write_admin_player_birthday_preferences(
    v_digest,v_owner,v_player,0,true,'Χρόνια πολλά!','#F5A623','🎂');
  IF (v_result->>'version')::integer<>1
    OR v_result->>'disabled'<>'true'
    OR v_result->>'wish_text'<>'Χρόνια πολλά!' THEN
    RAISE EXCEPTION 'Private birthday card create failed';
  END IF;
  BEGIN
    PERFORM club.write_admin_player_birthday_preferences(
      v_digest,v_owner,v_player,0,false,'Stale','#F5A623','🎉');
    RAISE EXCEPTION 'Duplicate private birthday card create was accepted';
  EXCEPTION WHEN unique_violation THEN NULL;
  END;
  v_result:=club.write_admin_player_birthday_preferences(
    v_digest,v_owner,v_player,1,false,'Ιδιωτική ευχή','#3B82F6','🎉');
  IF (v_result->>'version')::integer<>2
    OR v_result->>'disabled'<>'false'
    OR v_result->>'public_enabled'<>'false'
    OR v_result->>'auto_post_facebook'<>'false'
    OR v_result->>'auto_post_instagram'<>'false' THEN
    RAISE EXCEPTION 'Private birthday card edit enabled public sharing';
  END IF;
  IF (SELECT count(*) FROM club.player_birthday_card_preference_events
      WHERE player_id=v_player AND organization_id=v_owner)<>2
    OR EXISTS(SELECT 1 FROM club.player_birthday_card_preferences
      WHERE player_id=v_player AND organization_id=v_owner
        AND version<>2) THEN
    RAISE EXCEPTION 'Private birthday preference version history missing';
  END IF;
  BEGIN
    PERFORM club.read_admin_player_birthday_preferences(
      v_digest,'lefteria_fc',v_player);
    RAISE EXCEPTION 'Cross-owner birthday data access allowed';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;
  BEGIN
    PERFORM club.read_admin_player_birthday_preferences(
      v_digest,v_owner,'unassigned_player');
    RAISE EXCEPTION 'Unknown-player birthday data access allowed';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;
  UPDATE identity.admin_sessions SET revoked_at=clock_timestamp()
    WHERE id=v_session;
  BEGIN
    PERFORM club.write_admin_player_birthday_preferences(
      v_digest,v_owner,v_player,2,true,'Revoked','#F5A623','🎂');
    RAISE EXCEPTION 'Revoked manager could update birthday data';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;
  BEGIN
    PERFORM club.read_admin_player_birthday_preferences(
      v_digest,v_owner,v_player);
    RAISE EXCEPTION 'Revoked manager could read birthday data';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;
END $birthday_lifecycle$;
ROLLBACK;
