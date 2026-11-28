\set ON_ERROR_STOP on
-- Disposable-only synthetic player, portrait bytes and staff; changes rolled back.
BEGIN;
DO $academy_profile_checks$
DECLARE
  v_super uuid:=gen_random_uuid();
  v_manager uuid:=gen_random_uuid();
  v_player text:='ci-profile-'||md5(gen_random_uuid()::text);
  v_super_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_manager_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_data jsonb;
  v_lifecycle_request uuid:=gen_random_uuid();
  v_image bytea:=decode('ffd8ffd9','hex');
BEGIN
  INSERT INTO identity.admin_accounts(id,username,display_name,role,is_active,
    password_hash,invite_pending,otp2fa_enabled) VALUES
    (v_super,'ci-profile-super-'||substr(md5(v_super::text),1,8),
      'CI Private Profile Super','super_admin',true,'ci-hash',false,false),
    (v_manager,'ci-profile-manager-'||substr(md5(v_manager::text),1,8),
      'CI Private Profile Manager','manager',true,'ci-hash',false,false);
  INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id)
    VALUES(v_super,'lefteria_fc_academy'),(v_manager,'lefteria_fc_academy');
  INSERT INTO identity.admin_sessions(id,token_digest,admin_account_id,
    credential_digest,created_at,expires_at) VALUES
    (gen_random_uuid(),v_super_token,v_super,repeat('a',64),
      clock_timestamp(),clock_timestamp()+interval '1 hour'),
    (gen_random_uuid(),v_manager_token,v_manager,repeat('b',64),
      clock_timestamp(),clock_timestamp()+interval '1 hour');
  INSERT INTO club.players(id,organization_id,name,number,position,team_type,
    is_active,is_archived,is_transferred,has_explicit_picker_fields)
    VALUES(v_player,'lefteria_fc_academy','CI Child Official Name',
      9,'Forward','Academy',true,false,false,true);
  v_data:=club.read_admin_academy_player_profile(v_super_token,v_player);
  IF v_data->>'official_name'<>'CI Child Official Name'
    OR (v_data->>'revision')::int<>0 OR (v_data->>'has_portrait')::boolean
  THEN RAISE EXCEPTION 'private_profile_baseline_not_verified'; END IF;
  BEGIN
    PERFORM club.read_admin_academy_player_profile(v_manager_token,v_player);
    RAISE EXCEPTION 'unassigned_player_visible_to_location_manager';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.write_admin_academy_player_profile(v_manager_token,
      v_player,0,'Unapproved','Striker',7);
    RAISE EXCEPTION 'unassigned_player_edited_by_manager';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  v_data:=club.write_admin_academy_player_profile(
    v_super_token,v_player,0,'Passing and receiving','Striker',19);
  IF (v_data->>'revision')::int<>1
    THEN RAISE EXCEPTION 'profile_initial_revision_invalid'; END IF;
  BEGIN
    PERFORM club.write_admin_academy_player_profile(
      v_super_token,v_player,0,'Stale version','Striker',19);
    RAISE EXCEPTION 'concurrent_stale_editor_overwrote_child';
  EXCEPTION WHEN unique_violation THEN NULL; END;
  v_data:=club.read_admin_academy_player_profile(v_super_token,v_player);
  IF v_data->>'biography'<>'Passing and receiving'
    OR v_data->>'official_name'<>'CI Child Official Name'
    OR (v_data->>'official_number')::int<>9
    OR (v_data->>'preferred_number')::int<>19
  THEN RAISE EXCEPTION 'official_record_or_private_profile_corrupted'; END IF;

  IF NOT club.write_admin_academy_player_portrait(v_super_token,v_player,v_image)
    OR club.read_admin_academy_player_portrait(v_super_token,v_player)<>v_image
  THEN RAISE EXCEPTION 'private_photo_not_written_or_read'; END IF;
  v_data:=club.read_admin_academy_player_profile(v_super_token,v_player);
  IF (v_data->>'revision')::int<>2 OR
    NOT (v_data->>'has_portrait')::boolean
  THEN RAISE EXCEPTION 'photo_revision_not_reflected'; END IF;
  BEGIN
    PERFORM club.write_admin_academy_player_portrait(v_manager_token,v_player,v_image);
    RAISE EXCEPTION 'manager_uploaded_unassigned_child_photo';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.write_admin_academy_player_portrait(
      v_super_token,v_player,decode('00010203','hex'));
    RAISE EXCEPTION 'unsanitized_player_photo_accepted';
  EXCEPTION WHEN invalid_parameter_value THEN NULL; END;
  IF NOT club.write_admin_academy_player_portrait(v_super_token,v_player,NULL)
    OR club.read_admin_academy_player_portrait(v_super_token,v_player) IS NOT NULL
  THEN RAISE EXCEPTION 'private_photo_removal_failed'; END IF;
  IF (SELECT count(*) FROM audit.security_events
       WHERE actor_identity_id=v_super AND
       action IN ('admin.academy.player.profile.update',
         'admin.academy.player.portrait.update',
         'admin.academy.player.portrait.remove'))<>3
  THEN RAISE EXCEPTION 'profile_audit_not_complete'; END IF;
  BEGIN
    PERFORM club.admin_academy_player_lifecycle(v_manager_token,v_player,'archive',0,gen_random_uuid(),'CI denied lifecycle');
    RAISE EXCEPTION 'manager_archived_child';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  v_data:=club.admin_academy_player_lifecycle(v_super_token,v_player,'archive',0,v_lifecycle_request,'CI reviewed departure');
  IF v_data->>'state'<>'archived' OR (v_data->>'revision')::int<>1
    OR EXISTS(SELECT 1 FROM club.players WHERE id=v_player AND (is_active OR NOT is_archived))
    THEN RAISE EXCEPTION 'archive_failed'; END IF;
  v_data:=club.admin_academy_player_lifecycle(v_super_token,v_player,'archive',0,v_lifecycle_request,'CI reviewed departure');
  IF v_data->>'replay'<>'true' THEN RAISE EXCEPTION 'archive_replay_failed'; END IF;
  BEGIN
    PERFORM club.read_admin_academy_player_profile(v_super_token,v_player);
    RAISE EXCEPTION 'archived_child_active_profile_read';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.admin_academy_player_lifecycle(v_super_token,v_player,'reactivate',0,gen_random_uuid(),'CI stale reactivation');
    RAISE EXCEPTION 'stale_lifecycle_revision_accepted';
  EXCEPTION WHEN unique_violation THEN NULL; END;
  v_data:=club.admin_academy_player_lifecycle(v_super_token,v_player,'reactivate',1,gen_random_uuid(),'CI reviewed return');
  IF v_data->>'state'<>'active' OR (v_data->>'revision')::int<>2
    OR NOT EXISTS(SELECT 1 FROM club.players WHERE id=v_player AND is_active AND NOT is_archived)
    THEN RAISE EXCEPTION 'reactivation_failed'; END IF;
  v_data:=club.read_admin_academy_player_profile(v_super_token,v_player);
  IF v_data->>'biography'<>'Passing and receiving' THEN RAISE EXCEPTION 'lifecycle_lost_player_history'; END IF;
  UPDATE identity.admin_sessions SET revoked_at=clock_timestamp()
    WHERE token_digest=v_super_token;
  BEGIN
    PERFORM club.read_admin_academy_player_profile(v_super_token,v_player);
    RAISE EXCEPTION 'revoked_staff_read_youth_profile';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $academy_profile_checks$;
ROLLBACK;
