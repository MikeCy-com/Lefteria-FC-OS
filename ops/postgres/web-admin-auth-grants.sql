\set ON_ERROR_STOP on
\set ECHO none

-- ADMINISTRATOR-ONLY. This role belongs only to the opt-in web-admin auth
-- connection. Never reuse the mobile runtime connection or credentials.
-- Apply after migrations and identity-app-grants.sql in the dedicated database.
\if :{?admin_auth_role}
\else
  \echo 'admin_auth_role is required.'
  DO $required$ BEGIN RAISE EXCEPTION 'admin_auth_operator_input_required'; END; $required$;
\endif
\if :{?app_role}
\else
  \echo 'app_role must identify the existing separate mobile runtime role.'
  DO $required$ BEGIN RAISE EXCEPTION 'admin_auth_operator_input_required'; END; $required$;
\endif

SELECT current_database() AS admin_auth_database
\gset
BEGIN;
SELECT set_config('lefteria.admin_auth_role', :'admin_auth_role', true) AS admin_auth_role_setting,
       set_config('lefteria.mobile_runtime_role', :'app_role', true) AS mobile_runtime_role_setting
\gset

DO $guard$
DECLARE
  auth_name text := current_setting('lefteria.admin_auth_role');
  mobile_name text := current_setting('lefteria.mobile_runtime_role');
  auth_role pg_roles%ROWTYPE;
BEGIN
  IF current_database() IN ('postgres','template0','template1')
      OR octet_length(auth_name) NOT BETWEEN 1 AND 63
      OR lower(auth_name)='public' OR lower(auth_name) ~ '^pg_'
      OR auth_name=mobile_name OR auth_name=current_user THEN
    RAISE EXCEPTION 'Admin authentication requires a distinct role and dedicated rewrite database';
  END IF;
  SELECT * INTO auth_role FROM pg_roles WHERE rolname=auth_name;
  IF NOT FOUND THEN RAISE EXCEPTION 'Create the admin-auth role before applying its grants'; END IF;
  IF NOT auth_role.rolcanlogin OR auth_role.rolinherit OR auth_role.rolsuper
      OR auth_role.rolcreatedb OR auth_role.rolcreaterole OR auth_role.rolreplication OR auth_role.rolbypassrls THEN
    RAISE EXCEPTION 'Admin-auth role attributes are unsafe; no attributes were changed';
  END IF;
  IF EXISTS (SELECT 1 FROM pg_auth_members WHERE member=auth_role.oid OR roleid=auth_role.oid) THEN
    RAISE EXCEPTION 'Admin-auth role must have no inbound or outbound role memberships';
  END IF;
  IF EXISTS (SELECT 1 FROM pg_database WHERE datdba=auth_role.oid)
      OR EXISTS (SELECT 1 FROM pg_namespace WHERE nspowner=auth_role.oid AND nspname !~ '^pg_')
      OR EXISTS (SELECT 1 FROM pg_class WHERE relowner=auth_role.oid AND relpersistence<>'t')
      OR EXISTS (SELECT 1 FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
                 WHERE p.proowner=auth_role.oid AND n.nspname !~ '^pg_' AND n.nspname<>'information_schema') THEN
    RAISE EXCEPTION 'Admin-auth role must not own databases, schemas, persistent relations or routines';
  END IF;
  IF to_regclass('identity.admin_sessions') IS NULL OR to_regclass('identity.admin_auth_rate_buckets') IS NULL
      OR to_regclass('public_content.edits') IS NULL OR to_regprocedure('identity.lock_admin_club_authority(text,text)') IS NULL
      OR NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid='audit.security_events'::regclass
                      AND attname='actor_realm' AND attnum>0 AND NOT attisdropped) THEN
    RAISE EXCEPTION 'Apply the web-admin EF migration first; auth grants never create schema';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname=mobile_name)
      OR NOT has_column_privilege(mobile_name,'identity.mobile_identities','id','SELECT')
      OR has_column_privilege(mobile_name,'identity.admin_accounts','password_hash','SELECT') THEN
    RAISE EXCEPTION 'A verified credential-denied mobile runtime role is required';
  END IF;
  -- Do not silently repurpose an existing mobile/data-reading role as auth.
  IF has_column_privilege(auth_name,'identity.mobile_identities','id','SELECT') THEN
    RAISE EXCEPTION 'A role with mobile identity access cannot become the admin-auth role';
  END IF;
END
$guard$;

-- PUBLIC rights cannot be cancelled with a per-role REVOKE. Mobile bootstrap
-- already removes inherited database CREATE/TEMP and schema CREATE rights.
-- Only the two new auth tables have PUBLIC/mobile grants reset here; existing
-- mobile projections and audit permissions remain unchanged.
REVOKE ALL PRIVILEGES ON DATABASE :"admin_auth_database" FROM :"admin_auth_role";
GRANT CONNECT ON DATABASE :"admin_auth_database" TO :"admin_auth_role";
REVOKE ALL PRIVILEGES ON SCHEMA public,identity,club,audit,migration,public_content,public_sections,pos FROM :"admin_auth_role";
GRANT USAGE ON SCHEMA identity,club,audit,public_content,pos,hub TO :"admin_auth_role";
REVOKE ALL PRIVILEGES ON ALL TABLES IN SCHEMA public,identity,club,audit,migration,public_content,public_sections,pos FROM :"admin_auth_role";
REVOKE ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public,identity,club,audit,migration,public_content,public_sections,pos FROM :"admin_auth_role";
REVOKE ALL PRIVILEGES ON identity.admin_sessions,identity.admin_auth_rate_buckets FROM PUBLIC, :"app_role";

-- Table-level REVOKE does not remove previous column grants.
DO $columns$
DECLARE
  target record;
  columns text;
  auth_name text := current_setting('lefteria.admin_auth_role');
  mobile_name text := current_setting('lefteria.mobile_runtime_role');
BEGIN
  FOR target IN
    SELECT n.nspname,c.relname,c.oid
    FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
    WHERE n.nspname IN ('public','identity','club','audit','migration','public_content','public_sections','pos') AND c.relkind IN ('r','p','v','m','f')
  LOOP
    SELECT string_agg(format('%I',a.attname),', ' ORDER BY a.attnum) INTO columns
    FROM pg_attribute a WHERE a.attrelid=target.oid AND a.attnum>0 AND NOT a.attisdropped;
    IF columns IS NOT NULL THEN
      EXECUTE format('REVOKE ALL PRIVILEGES (%s) ON TABLE %I.%I FROM %I',columns,target.nspname,target.relname,auth_name);
      IF target.nspname='identity' AND target.relname IN ('admin_sessions','admin_auth_rate_buckets') THEN
        EXECUTE format('REVOKE ALL PRIVILEGES (%s) ON TABLE %I.%I FROM PUBLIC, %I',columns,target.nspname,target.relname,mobile_name);
      END IF;
    END IF;
  END LOOP;
END
$columns$;

GRANT SELECT (id,username,display_name,role,is_active,invite_pending,otp2fa_enabled,password_hash)
ON identity.admin_accounts TO :"admin_auth_role";
GRANT SELECT ON identity.admin_organization_grants TO :"admin_auth_role";
GRANT SELECT (id,name) ON club.organizations TO :"admin_auth_role";
GRANT SELECT,INSERT ON identity.admin_sessions,identity.admin_auth_rate_buckets TO :"admin_auth_role";
GRANT UPDATE (revoked_at) ON identity.admin_sessions TO :"admin_auth_role";
GRANT UPDATE (tokens,updated_at) ON identity.admin_auth_rate_buckets TO :"admin_auth_role";
GRANT INSERT ON audit.security_events TO :"admin_auth_role";
GRANT SELECT ON public_content.documents,public_content.import_state,public_content.edits TO :"admin_auth_role";
GRANT INSERT ON public_content.edits TO :"admin_auth_role";
GRANT UPDATE (data_json,version) ON public_content.edits TO :"admin_auth_role";
REVOKE ALL ON FUNCTION identity.lock_admin_club_authority(text,text) FROM PUBLIC, :"app_role";
REVOKE ALL ON FUNCTION identity.founding_admin_setup_available(), identity.create_founding_admin(uuid,text,text,text) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION identity.founding_admin_setup_available() TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION identity.create_founding_admin(uuid,text,text,text) TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION identity.lock_admin_club_authority(text,text) TO :"admin_auth_role";
-- The only new POS entitlement is a session/owner-checking read function.
-- Raw products, variants, categories and sales are NEVER granted.
REVOKE ALL ON FUNCTION pos.read_admin_catalog(text,text) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION pos.read_admin_catalog(text,text) TO :"admin_auth_role";
-- Only this exact audited function can change a POS category. Direct table
-- INSERT/UPDATE and any sales/checkout routine remain prohibited.
REVOKE ALL ON FUNCTION pos.write_admin_category(text,text,text,text,boolean,bigint) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION pos.write_admin_category(text,text,text,text,boolean,bigint) TO :"admin_auth_role";
-- A second exact, audited writer can replace a WHOLE product/variant set;
-- no direct table or sales privilege is added.
REVOKE ALL ON FUNCTION pos.write_admin_product(text,text,text,text,text,text,boolean,bigint,jsonb) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION pos.write_admin_product(text,text,text,text,text,text,boolean,bigint,jsonb) TO :"admin_auth_role";
-- Explicitly gated announcement drafts; no publish, delivery or raw draft SELECT.
REVOKE ALL ON FUNCTION club.create_admin_announcement_draft(text,text,text,text,text,text) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION club.create_admin_announcement_draft(text,text,text,text,text,text) TO :"admin_auth_role";
REVOKE ALL ON FUNCTION club.read_admin_announcement_drafts(text,text) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION club.read_admin_announcement_drafts(text,text) TO :"admin_auth_role";
-- Published notices are independent of private drafts; only manager
-- procedures, never the raw tables, are available to admin-auth.
REVOKE ALL ON TABLE club.app_announcements FROM PUBLIC, :"admin_auth_role";
REVOKE ALL ON FUNCTION club.app_announcement_admin_actor(text,text)
  FROM PUBLIC, :"admin_auth_role";
GRANT EXECUTE ON FUNCTION club.publish_admin_app_announcement(
  text,text,text,bigint) TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION club.read_admin_app_announcements(
  text,text) TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION club.remove_admin_app_announcement(
  text,text,text,bigint) TO :"admin_auth_role";
-- Management sessions cannot enumerate device endpoints, private keys or
-- delivery attempts; push authorization belongs exclusively to mobile.
REVOKE ALL ON TABLE club.mobile_push_subscriptions,
  club.app_announcement_push_attempts FROM PUBLIC, :"admin_auth_role";
REVOKE ALL ON FUNCTION club.save_mobile_push_subscription(
  uuid,text,text,timestamptz,text,text,text,text) FROM PUBLIC, :"admin_auth_role";
REVOKE ALL ON FUNCTION club.remove_mobile_push_subscription(
  uuid,text,text,timestamptz,text) FROM PUBLIC, :"admin_auth_role";
REVOKE ALL ON FUNCTION club.mobile_push_subscription_status(
  uuid,text,text,timestamptz) FROM PUBLIC, :"admin_auth_role";
REVOKE ALL ON FUNCTION club.claim_app_announcement_push_attempts(integer)
  FROM PUBLIC, :"admin_auth_role";
REVOKE ALL ON FUNCTION club.check_app_announcement_push_lease(uuid,uuid)
  FROM PUBLIC, :"admin_auth_role";
REVOKE ALL ON FUNCTION club.finish_app_announcement_push_attempt(
  uuid,uuid,text,integer) FROM PUBLIC, :"admin_auth_role";

-- Web management never reads raw member notification events or
-- bypasses the member-only notification inbox.
REVOKE ALL ON TABLE club.app_announcement_notification_events
  FROM PUBLIC, :"admin_auth_role";
REVOKE ALL ON FUNCTION club.read_mobile_app_notification_inbox(
  uuid,text,text,timestamptz,integer) FROM PUBLIC, :"admin_auth_role";
REVOKE ALL ON FUNCTION club.enqueue_app_announcement_notification()
  FROM PUBLIC, :"admin_auth_role";

-- Every raw banner or document is private. Only managers with the
-- exact club grant can mutate unpublished draft media via audited routines.
REVOKE ALL ON TABLE club.app_announcement_media
  FROM PUBLIC, :"admin_auth_role", :"app_role";
REVOKE ALL ON FUNCTION club.read_mobile_app_announcement_media(
  uuid,text,text,timestamptz,text,text,text,uuid) FROM PUBLIC, :"admin_auth_role";
REVOKE ALL ON FUNCTION club.write_admin_app_announcement_media(
  text,text,text,bigint,uuid,text,text,text,bytea) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION club.write_admin_app_announcement_media(
  text,text,text,bigint,uuid,text,text,text,bytea) TO :"admin_auth_role";
REVOKE ALL ON FUNCTION club.remove_admin_app_announcement_media(
  text,text,text,bigint,uuid) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION club.remove_admin_app_announcement_media(
  text,text,text,bigint,uuid) TO :"admin_auth_role";
-- Readers of official app notices are visible ONLY via this exact,
-- manager/organization/active-group verified helper, never direct mobile data.
REVOKE ALL ON TABLE club.app_announcement_read_receipts
  FROM PUBLIC, :"admin_auth_role", :"app_role";
REVOKE ALL ON FUNCTION club.read_admin_app_announcement_readers(text,text,text)
  FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION club.read_admin_app_announcement_readers(text,text,text)
  TO :"admin_auth_role";
-- Live canonical team/Academy counts, without direct SELECT on player
-- identities or memberships. No messages and no delivery authority.
REVOKE ALL ON FUNCTION club.read_admin_audiences(text,text) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION club.read_admin_audiences(text,text) TO :"admin_auth_role";
-- Manager-only roster creation; role/grant and atomic audit checked INSIDE
-- the reviewed function. No SELECT/INSERT rights on club player/team tables.
REVOKE ALL ON FUNCTION club.create_admin_group(text,text,text,text) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION club.create_admin_group(text,text,text,text) TO :"admin_auth_role";
-- Only an audited, per-player version-checked membership transaction is callable.
-- No raw roster or group table write privileges are granted.
REVOKE ALL ON FUNCTION club.write_admin_roster_assignment(text,text,text,jsonb,jsonb) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION club.write_admin_roster_assignment(text,text,text,jsonb,jsonb) TO :"admin_auth_role";
-- Audited, manager-only coach assignment replace; no direct staff DML.
REVOKE ALL ON FUNCTION club.write_admin_coach_assignment(text,text,text,jsonb,jsonb) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION club.write_admin_coach_assignment(text,text,text,jsonb,jsonb) TO :"admin_auth_role";
-- Manager-only active coach/staff assignment projection; no direct staff SELECT.
REVOKE ALL ON FUNCTION club.read_admin_coaches(text,text,integer,integer) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION club.read_admin_coaches(text,text,integer,integer) TO :"admin_auth_role";
-- Manager-only, paginated and minimized active player/group projection.
-- The web-admin role still cannot SELECT club.players directly.
REVOKE ALL ON FUNCTION club.read_admin_roster_players(text,text,integer,integer) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION club.read_admin_roster_players(text,text,integer,integer) TO :"admin_auth_role";
\ir admin-club-lock-boundary.sql
\ir pos-catalog-reader-boundary.sql
\ir pos-category-writer-boundary.sql
\ir pos-product-writer-boundary.sql
\ir admin-audience-directory-boundary.sql
\ir admin-group-create-boundary.sql
\ir admin-player-directory-boundary.sql
\ir admin-roster-assignment-boundary.sql
\ir admin-coach-directory-boundary.sql
\ir admin-coach-assignment-boundary.sql
-- Manager-approved, position-aware weekly player quiz editorial.
-- Only these narrow functions may be called by the web admin account.
-- Never grant raw question, correct answer or attempt table access.
REVOKE ALL ON FUNCTION hub.authorize_admin_player_quiz(text,text,boolean)
  FROM PUBLIC, :"app_role", :"admin_auth_role";
REVOKE ALL ON FUNCTION hub.create_admin_player_quiz(text,text,uuid,integer,text,text,text,jsonb)
  FROM PUBLIC, :"app_role", :"admin_auth_role";
REVOKE ALL ON FUNCTION hub.list_admin_player_quizzes(text,text)
  FROM PUBLIC, :"app_role", :"admin_auth_role";
REVOKE ALL ON FUNCTION hub.detail_admin_player_quiz(text,text,uuid)
  FROM PUBLIC, :"app_role", :"admin_auth_role";
REVOKE ALL ON FUNCTION hub.approve_admin_player_quiz(text,text,uuid)
  FROM PUBLIC, :"app_role", :"admin_auth_role";
REVOKE ALL ON FUNCTION hub.publish_admin_player_quiz(text,text,uuid)
  FROM PUBLIC, :"app_role", :"admin_auth_role";
GRANT EXECUTE ON FUNCTION hub.create_admin_player_quiz(text,text,uuid,integer,text,text,text,jsonb)
  TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION hub.list_admin_player_quizzes(text,text)
  TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION hub.detail_admin_player_quiz(text,text,uuid)
  TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION hub.approve_admin_player_quiz(text,text,uuid)
  TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION hub.publish_admin_player_quiz(text,text,uuid)
  TO :"admin_auth_role";
REVOKE ALL ON FUNCTION hub.withdraw_admin_player_quiz(text,text,uuid)
  FROM PUBLIC, :"app_role", :"admin_auth_role";
GRANT EXECUTE ON FUNCTION hub.withdraw_admin_player_quiz(text,text,uuid)
  TO :"admin_auth_role";
REVOKE ALL ON TABLE hub.player_quizzes,hub.player_quiz_questions,
  hub.player_quiz_tracks,hub.player_quiz_attempts,
  hub.player_quiz_review_events FROM PUBLIC, :"app_role", :"admin_auth_role";

-- HUB editorial is exposed only through a scoped security-definer function.
-- Never grant direct hub table access or callable access to the mobile role.
REVOKE ALL ON FUNCTION hub.read_admin_editorial(text,text) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION hub.read_admin_editorial(text,text) TO :"admin_auth_role";
REVOKE ALL ON FUNCTION hub.create_admin_draft(text,text,uuid,uuid,text,text,text,boolean,boolean,smallint,smallint)
  FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION hub.create_admin_draft(text,text,uuid,uuid,text,text,text,boolean,boolean,smallint,smallint)
  TO :"admin_auth_role";
REVOKE ALL ON FUNCTION hub.submit_admin_draft_review(text,text,uuid,bigint) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION hub.submit_admin_draft_review(text,text,uuid,bigint) TO :"admin_auth_role";
REVOKE ALL ON FUNCTION hub.read_admin_editorial_detail(text,text,uuid) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION hub.read_admin_editorial_detail(text,text,uuid) TO :"admin_auth_role";
REVOKE ALL ON FUNCTION hub.request_admin_ai_education(text,text,uuid,text,smallint,smallint,text,text) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION hub.request_admin_ai_education(text,text,uuid,text,smallint,smallint,text,text) TO :"admin_auth_role";
REVOKE ALL ON FUNCTION hub.reject_admin_hub_image(text,text,uuid,bigint,uuid) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION hub.reject_admin_hub_image(text,text,uuid,bigint,uuid) TO :"admin_auth_role";
REVOKE ALL ON FUNCTION hub.read_admin_quarantined_image(text,text,uuid,uuid) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION hub.read_admin_quarantined_image(text,text,uuid,uuid) TO :"admin_auth_role";
REVOKE ALL ON FUNCTION hub.quarantine_admin_image(text,text,uuid,bigint,uuid,text,bytea) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION hub.quarantine_admin_image(text,text,uuid,bigint,uuid,text,bytea) TO :"admin_auth_role";
REVOKE ALL ON FUNCTION hub.return_rejected_admin_draft(text,text,uuid,bigint) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION hub.return_rejected_admin_draft(text,text,uuid,bigint) TO :"admin_auth_role";
REVOKE ALL ON FUNCTION hub.revise_admin_draft(text,text,uuid,bigint,text,text) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION hub.revise_admin_draft(text,text,uuid,bigint,text,text) TO :"admin_auth_role";
REVOKE ALL ON FUNCTION hub.publish_admin_hub_content(text,text,uuid,bigint) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION hub.publish_admin_hub_content(text,text,uuid,bigint) TO :"admin_auth_role";
REVOKE ALL ON FUNCTION hub.approve_admin_hub_content(text,text,uuid,bigint) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION hub.approve_admin_hub_content(text,text,uuid,bigint) TO :"admin_auth_role";
REVOKE ALL ON FUNCTION hub.approve_admin_hub_visual(text,text,uuid,bigint) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION hub.approve_admin_hub_visual(text,text,uuid,bigint) TO :"admin_auth_role";
REVOKE ALL ON FUNCTION hub.review_admin_text(text,text,uuid,bigint,text) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION hub.review_admin_text(text,text,uuid,bigint,text) TO :"admin_auth_role";
\ir admin-hub-editorial-boundary.sql
\ir admin-hub-quiz-boundary.sql
\ir admin-announcement-drafts-boundary.sql
\ir app-announcement-publishing-boundary.sql

DO $boundary$
DECLARE
  auth_name text := current_setting('lefteria.admin_auth_role');
  mobile_name text := current_setting('lefteria.mobile_runtime_role');
  target record;
  can_select boolean;
  can_insert boolean;
  can_update boolean;
BEGIN
  IF has_database_privilege(auth_name,current_database(),'CREATE') OR has_database_privilege(auth_name,current_database(),'TEMP')
      OR has_schema_privilege(auth_name,'migration','USAGE') THEN
    RAISE EXCEPTION 'Admin-auth role retains inherited DDL or migration access; review bootstrap';
  END IF;
  IF EXISTS (SELECT 1 FROM pg_namespace n WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema'
              AND has_schema_privilege(auth_name,n.oid,'CREATE')) THEN
    RAISE EXCEPTION 'Admin-auth role retains schema CREATE privilege';
  END IF;
  IF EXISTS (SELECT 1 FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
              WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema' AND p.prosecdef
                AND has_schema_privilege(auth_name,n.oid,'USAGE') AND has_function_privilege(auth_name,p.oid,'EXECUTE')
                AND p.oid NOT IN ('identity.founding_admin_setup_available()'::regprocedure,
                  'identity.create_founding_admin(uuid,text,text,text)'::regprocedure,
                  'identity.lock_admin_club_authority(text,text)'::regprocedure,
                  'pos.read_admin_catalog(text,text)'::regprocedure,
                  'pos.write_admin_category(text,text,text,text,boolean,bigint)'::regprocedure,
                  'pos.write_admin_product(text,text,text,text,text,text,boolean,bigint,jsonb)'::regprocedure,
                  'club.read_admin_audiences(text,text)'::regprocedure,
                  'club.create_admin_group(text,text,text,text)'::regprocedure,
                  'club.read_admin_roster_players(text,text,integer,integer)'::regprocedure,
                  'club.write_admin_roster_assignment(text,text,text,jsonb,jsonb)'::regprocedure,
                  'club.read_admin_coaches(text,text,integer,integer)'::regprocedure,
                  'club.write_admin_coach_assignment(text,text,text,jsonb,jsonb)'::regprocedure,
                  'hub.read_admin_editorial(text,text)'::regprocedure,
                  'hub.create_admin_draft(text,text,uuid,uuid,text,text,text,boolean,boolean,smallint,smallint)'::regprocedure,
                  'hub.submit_admin_draft_review(text,text,uuid,bigint)'::regprocedure,
                  'hub.read_admin_editorial_detail(text,text,uuid)'::regprocedure,
                  'hub.publish_admin_hub_content(text,text,uuid,bigint)'::regprocedure,
                   'hub.approve_admin_hub_content(text,text,uuid,bigint)'::regprocedure,
                   'hub.approve_admin_hub_visual(text,text,uuid,bigint)'::regprocedure,
                   'hub.review_admin_text(text,text,uuid,bigint,text)'::regprocedure,
                  'hub.revise_admin_draft(text,text,uuid,bigint,text,text)'::regprocedure,
                  'hub.return_rejected_admin_draft(text,text,uuid,bigint)'::regprocedure,
                  'hub.quarantine_admin_image(text,text,uuid,bigint,uuid,text,bytea)'::regprocedure,
                  'hub.read_admin_quarantined_image(text,text,uuid,uuid)'::regprocedure,
                  'hub.reject_admin_hub_image(text,text,uuid,bigint,uuid)'::regprocedure,
                  'hub.request_admin_ai_education(text,text,uuid,text,smallint,smallint,text,text)'::regprocedure,
                  'hub.create_admin_player_quiz(text,text,uuid,integer,text,text,text,jsonb)'::regprocedure,
                  'hub.list_admin_player_quizzes(text,text)'::regprocedure,
                  'hub.detail_admin_player_quiz(text,text,uuid)'::regprocedure,
                  'hub.approve_admin_player_quiz(text,text,uuid)'::regprocedure,
                  'hub.publish_admin_player_quiz(text,text,uuid)'::regprocedure,
                  'hub.withdraw_admin_player_quiz(text,text,uuid)'::regprocedure,
                  'club.create_admin_announcement_draft(text,text,text,text,text,text)'::regprocedure,
                  'club.read_admin_announcement_drafts(text,text)'::regprocedure,
                  'club.publish_admin_app_announcement(text,text,text,bigint)'::regprocedure,
                  'club.read_admin_app_announcements(text,text)'::regprocedure,
                  'club.read_admin_app_announcement_readers(text,text,text)'::regprocedure,
                  'club.write_admin_app_announcement_media(text,text,text,bigint,uuid,text,text,text,bytea)'::regprocedure,
                  'club.remove_admin_app_announcement_media(text,text,text,bigint,uuid)'::regprocedure,
                  'club.remove_admin_app_announcement(text,text,text,bigint)'::regprocedure)) THEN
    RAISE EXCEPTION 'Review unexpected callable security-definer routines before enabling admin auth';
  END IF;
  FOR target IN
    SELECT n.nspname,c.relname,c.oid,a.attnum,a.attname
    FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
    JOIN pg_attribute a ON a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped
    WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema' AND c.relkind IN ('r','p','v','m','f')
  LOOP
    can_select := (target.nspname='identity' AND target.relname IN ('admin_sessions','admin_auth_rate_buckets','admin_organization_grants'))
      OR (target.nspname='identity' AND target.relname='admin_accounts' AND target.attname IN
          ('id','username','display_name','role','is_active','invite_pending','otp2fa_enabled','password_hash'))
      OR (target.nspname='club' AND target.relname='organizations' AND target.attname IN ('id','name'));
    can_select := can_select OR (target.nspname='public_content' AND target.relname IN ('documents','import_state','edits'));
    can_insert := (target.nspname='identity' AND target.relname IN ('admin_sessions','admin_auth_rate_buckets'))
      OR (target.nspname='audit' AND target.relname='security_events');
    can_insert := can_insert OR (target.nspname='public_content' AND target.relname='edits');
    can_update := (target.nspname='identity' AND target.relname='admin_sessions' AND target.attname='revoked_at')
      OR (target.nspname='identity' AND target.relname='admin_auth_rate_buckets' AND target.attname IN ('tokens','updated_at'));
    can_update := can_update OR (target.nspname='public_content' AND target.relname='edits' AND target.attname IN ('data_json','version'));
    IF has_column_privilege(auth_name,target.oid,target.attnum,'SELECT')<>can_select
        OR has_column_privilege(auth_name,target.oid,target.attnum,'INSERT')<>can_insert
        OR has_column_privilege(auth_name,target.oid,target.attnum,'UPDATE')<>can_update
        OR has_column_privilege(auth_name,target.oid,target.attnum,'REFERENCES')
        OR has_table_privilege(auth_name,target.oid,'DELETE,TRUNCATE,TRIGGER') THEN
      RAISE EXCEPTION 'Admin-auth effective table/column privileges exceed the explicit auth boundary';
    END IF;
  END LOOP;
  IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
              WHERE c.relkind='S' AND n.nspname !~ '^pg_' AND n.nspname<>'information_schema'
                AND CASE WHEN c.relkind='S' THEN has_sequence_privilege(auth_name,c.oid,'USAGE,SELECT,UPDATE') ELSE false END) THEN
    RAISE EXCEPTION 'Admin-auth role retains unexpected sequence access';
  END IF;
  IF has_column_privilege(mobile_name,'identity.admin_accounts','password_hash','SELECT')
      OR has_any_column_privilege(mobile_name,'identity.admin_sessions','SELECT,INSERT,UPDATE,REFERENCES')
      OR has_any_column_privilege(mobile_name,'identity.admin_auth_rate_buckets','SELECT,INSERT,UPDATE,REFERENCES')
      OR has_table_privilege(mobile_name,'identity.admin_sessions','DELETE,TRUNCATE,TRIGGER')
      OR has_table_privilege(mobile_name,'identity.admin_auth_rate_buckets','DELETE,TRUNCATE,TRIGGER') THEN
    RAISE EXCEPTION 'Mobile runtime must remain outside credentials and web-admin session state';
  END IF;
END
$boundary$;
COMMIT;
