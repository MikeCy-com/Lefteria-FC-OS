\set ON_ERROR_STOP on
\set ECHO none

-- ADMINISTRATOR-ONLY. The current database must be dedicated to this rewrite.
-- This revokes PUBLIC temporary/create rights within that database and binds the
-- runtime policies to app_role. Apply after migrations, never as the API role.
\if :{?app_role}
\else
  \echo 'app_role is required.'
  \quit 2
\endif

SELECT current_database() AS identity_database
\gset
BEGIN;
SELECT set_config('lefteria.runtime_role', :'app_role', true)
\gset

DO $guard$
DECLARE
  runtime_name text := current_setting('lefteria.runtime_role');
  runtime_role pg_roles%ROWTYPE;
BEGIN
  IF current_database() IN ('postgres', 'template0', 'template1') THEN
    RAISE EXCEPTION 'Identity grants require a dedicated application database';
  END IF;
  SELECT * INTO runtime_role FROM pg_roles WHERE rolname = runtime_name;
  IF NOT FOUND THEN
    RAISE EXCEPTION 'Runtime role must be created by an administrator first';
  END IF;
  IF runtime_role.rolsuper OR runtime_role.rolcreatedb OR runtime_role.rolcreaterole
      OR runtime_role.rolreplication OR runtime_role.rolbypassrls THEN
    RAISE EXCEPTION 'Refusing a privileged runtime role; no role attributes were changed';
  END IF;
  IF EXISTS (SELECT 1 FROM pg_auth_members WHERE member = runtime_role.oid) THEN
    RAISE EXCEPTION 'Runtime role must have no role memberships, including SET ROLE paths';
  END IF;
  IF EXISTS (SELECT 1 FROM pg_database WHERE datdba = runtime_role.oid)
      OR EXISTS (SELECT 1 FROM pg_namespace WHERE nspowner = runtime_role.oid
                   AND nspname NOT LIKE 'pg_%')
      OR EXISTS (SELECT 1 FROM pg_class WHERE relowner = runtime_role.oid
                   AND relpersistence <> 't') THEN
    RAISE EXCEPTION 'Runtime role must not own a database, schema or persistent relation';
  END IF;
  IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE (n.nspname,c.relname) IN (('identity','mobile_identities'),('club','calendar_responses'))
               AND c.relforcerowsecurity) THEN
    RAISE EXCEPTION 'Review owner import behavior before applying to a FORCE RLS table';
  END IF;
END
$guard$;

-- PUBLIC privileges are inherited even by NOINHERIT roles; per-role REVOKE
-- cannot cancel them. These changes are scoped to the dedicated current DB.
REVOKE CREATE, TEMPORARY ON DATABASE :"identity_database" FROM PUBLIC;
REVOKE CREATE ON SCHEMA public, identity, club, audit, public_content FROM PUBLIC;
REVOKE CREATE ON SCHEMA public FROM :"app_role";
REVOKE ALL PRIVILEGES ON SCHEMA migration FROM PUBLIC, :"app_role";
REVOKE ALL PRIVILEGES ON DATABASE :"identity_database" FROM :"app_role";
GRANT CONNECT ON DATABASE :"identity_database" TO :"app_role";
REVOKE ALL PRIVILEGES ON SCHEMA identity, club, audit FROM :"app_role";
GRANT USAGE ON SCHEMA identity, club, audit, public_content TO :"app_role";

REVOKE ALL PRIVILEGES ON TABLE
  identity.mobile_identities, identity.mobile_avatars, identity.admin_accounts,
  identity.admin_organization_grants, identity.otp_challenges,
  identity.auth_rate_buckets, club.organizations, audit.security_events,
  club.teams, club.academy_groups, club.players, club.player_academy_groups,
  club.roster_import_state, migration.roster_source_documents,
  club.staff, club.staff_teams, club.staff_academy_groups, club.academy_group_coaches,
  migration.staff_relationship_import_state, migration.staff_source_documents, migration.contact_provenance,
  identity.fan_registration_evidence, identity.canonical_contact_bindings, identity.mobile_record_links, identity.admin_staff_links,
  migration.account_import_state, migration.account_source_documents, migration.account_import_issues,
  migration.legacy_account_mappings, migration.imported_admin_grants, identity.imported_mobile_subjects,
  club.calendar_import_state,club.calendar_items,club.calendar_item_scopes,club.calendar_item_guests,
  club.calendar_responses,migration.calendar_source_documents,migration.calendar_import_issues,
  public_content.documents,public_content.import_state,migration.public_content_import_evidence,migration.public_content_source_documents,
  identity.admin_sessions,identity.admin_auth_rate_buckets,public_content.edits,
  identity.family_invitations,identity.family_invitation_events,
  identity.family_delegated_player_grants
FROM PUBLIC, :"app_role";

-- Also remove prior column grants, which a table-level REVOKE does not remove.
DO $columns$
DECLARE
  target record;
  column_names text;
BEGIN
  FOR target IN
    SELECT n.nspname, c.relname, c.oid
    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE (n.nspname, c.relname) IN (
      ('identity','mobile_identities'), ('identity','mobile_avatars'), ('identity','admin_accounts'),
      ('identity','admin_organization_grants'), ('identity','otp_challenges'),
      ('identity','auth_rate_buckets'), ('club','organizations'), ('audit','security_events'),
      ('club','teams'), ('club','academy_groups'), ('club','players'), ('club','player_academy_groups'),
      ('club','roster_import_state'), ('migration','roster_source_documents'),
      ('club','staff'), ('club','staff_teams'), ('club','staff_academy_groups'), ('club','academy_group_coaches'),
      ('migration','staff_relationship_import_state'), ('migration','staff_source_documents'), ('migration','contact_provenance'),
      ('identity','fan_registration_evidence'), ('identity','canonical_contact_bindings'), ('identity','mobile_record_links'), ('identity','admin_staff_links'),
      ('migration','account_import_state'), ('migration','account_source_documents'), ('migration','account_import_issues'),
      ('migration','legacy_account_mappings'), ('migration','imported_admin_grants'), ('identity','imported_mobile_subjects'),
      ('club','calendar_import_state'),('club','calendar_items'),('club','calendar_item_scopes'),('club','calendar_item_guests'),
      ('club','calendar_responses'),('migration','calendar_source_documents'),('migration','calendar_import_issues'),
      ('public_content','documents'),('public_content','import_state'),('migration','public_content_import_evidence'),
      ('migration','public_content_source_documents'),('identity','admin_sessions'),('identity','admin_auth_rate_buckets'),('public_content','edits'),
      ('identity','family_invitations'),('identity','family_invitation_events'),
      ('identity','family_delegated_player_grants'))
  LOOP
    SELECT string_agg(format('%I', a.attname), ', ' ORDER BY a.attnum)
      INTO column_names
      FROM pg_attribute a WHERE a.attrelid = target.oid AND a.attnum > 0 AND NOT a.attisdropped;
    EXECUTE format('REVOKE ALL PRIVILEGES (%s) ON TABLE %I.%I FROM PUBLIC, %I',
      column_names, target.nspname, target.relname, current_setting('lefteria.runtime_role'));
  END LOOP;
END
$columns$;

GRANT SELECT, INSERT ON identity.mobile_identities TO :"app_role";
GRANT SELECT ON public_content.documents,public_content.import_state TO :"app_role";
GRANT SELECT (kind,organization_id,id,data_json) ON public_content.edits TO :"app_role";
REVOKE ALL ON FUNCTION identity.lock_admin_club_authority(text,text) FROM PUBLIC, :"app_role";
GRANT SELECT (legacy_subject,identity_id) ON identity.imported_mobile_subjects TO :"app_role";
GRANT SELECT (id,time_zone,is_complete) ON club.calendar_import_state TO :"app_role";
GRANT SELECT (id,organization_id) ON club.teams,club.academy_groups TO :"app_role";
GRANT SELECT ON club.calendar_items,club.calendar_item_scopes,club.calendar_item_guests,club.calendar_responses TO :"app_role";
GRANT INSERT (collection,kind,event_id,player_id,organization_id,status,actor_identity_id,updated_at)
ON club.calendar_responses TO :"app_role";
GRANT UPDATE (status,actor_identity_id,updated_at) ON club.calendar_responses TO :"app_role";
ALTER TABLE club.calendar_responses ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS calendar_runtime_select ON club.calendar_responses;
DROP POLICY IF EXISTS calendar_runtime_insert ON club.calendar_responses;
DROP POLICY IF EXISTS calendar_runtime_update ON club.calendar_responses;
DROP POLICY IF EXISTS calendar_runtime_insert_rsvp_only ON club.calendar_responses;
DROP POLICY IF EXISTS calendar_runtime_update_rsvp_only ON club.calendar_responses;
CREATE POLICY calendar_runtime_select ON club.calendar_responses FOR SELECT TO :"app_role" USING (true);
CREATE POLICY calendar_runtime_insert ON club.calendar_responses FOR INSERT TO :"app_role" WITH CHECK (true);
CREATE POLICY calendar_runtime_insert_rsvp_only ON club.calendar_responses AS RESTRICTIVE FOR INSERT TO :"app_role"
  WITH CHECK (collection IN ('availability','event_attendance') AND status IN ('going','not_going','maybe')
    AND actor_identity_id IS NOT NULL AND source_ordinal IS NULL);
CREATE POLICY calendar_runtime_update ON club.calendar_responses FOR UPDATE TO :"app_role" USING (true) WITH CHECK (true);
CREATE POLICY calendar_runtime_update_rsvp_only ON club.calendar_responses AS RESTRICTIVE FOR UPDATE TO :"app_role"
  USING (collection IN ('availability','event_attendance'))
  WITH CHECK (collection IN ('availability','event_attendance') AND status IN ('going','not_going','maybe') AND actor_identity_id IS NOT NULL);
GRANT SELECT, INSERT ON identity.mobile_avatars TO :"app_role";
GRANT UPDATE (file_name, jpeg_bytes, width, height, updated_at)
ON identity.mobile_avatars TO :"app_role";
GRANT UPDATE (display_name, first_name, last_name, birthday, avatar_url,
  favorite_team, email, favourite_player_id, updated_at, notification_preferences, platform, last_login)
ON identity.mobile_identities TO :"app_role";
GRANT SELECT (id, is_active, invite_pending, normalized_phone, active_organization_id)
ON identity.admin_accounts TO :"app_role";
GRANT SELECT ON identity.admin_organization_grants, club.organizations TO :"app_role";
GRANT UPDATE (active_organization_id) ON identity.admin_accounts TO :"app_role";
GRANT SELECT, INSERT, UPDATE, DELETE ON identity.otp_challenges TO :"app_role";
GRANT SELECT, INSERT ON identity.auth_rate_buckets TO :"app_role";
GRANT UPDATE (tokens, updated_at) ON identity.auth_rate_buckets TO :"app_role";
GRANT INSERT ON audit.security_events TO :"app_role";
-- Pending family invitations are callable through owner-checked functions ONLY.
-- Never grant direct SELECT/DML on phone-bearing family records.
REVOKE ALL ON FUNCTION identity.issue_family_invitation(uuid,text,text,timestamptz,uuid,text,text,uuid) FROM PUBLIC, :"app_role";
REVOKE ALL ON FUNCTION identity.cancel_family_invitation(uuid,text,text,timestamptz,uuid,uuid) FROM PUBLIC, :"app_role";
REVOKE ALL ON FUNCTION identity.read_parent_family_outgoing(uuid,text,text,timestamptz) FROM PUBLIC, :"app_role";
REVOKE ALL ON FUNCTION identity.read_recipient_family_invitations(uuid,text,text,timestamptz)
  FROM PUBLIC, :"app_role";
REVOKE ALL ON FUNCTION identity.decline_recipient_family_invitation(uuid,text,text,timestamptz,uuid,uuid)
  FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION identity.issue_family_invitation(uuid,text,text,timestamptz,uuid,text,text,uuid) TO :"app_role";
GRANT EXECUTE ON FUNCTION identity.cancel_family_invitation(uuid,text,text,timestamptz,uuid,uuid) TO :"app_role";
GRANT EXECUTE ON FUNCTION identity.read_parent_family_outgoing(uuid,text,text,timestamptz) TO :"app_role";
GRANT EXECUTE ON FUNCTION identity.read_recipient_family_invitations(uuid,text,text,timestamptz)
  TO :"app_role";
GRANT EXECUTE ON FUNCTION identity.decline_recipient_family_invitation(uuid,text,text,timestamptz,uuid,uuid)
  TO :"app_role";
-- Acceptance stores a revocable grant without changing a fan login role.
REVOKE ALL ON FUNCTION
  identity.accept_recipient_family_invitation(uuid,text,text,timestamptz,uuid,uuid,uuid)
  FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION
  identity.accept_recipient_family_invitation(uuid,text,text,timestamptz,uuid,uuid,uuid)
  TO :"app_role";
-- Only this scoped function may inspect a current accepted recipient's
-- delegation; no direct SELECT on the grant/phone-bearing invitation tables.
REVOKE ALL ON FUNCTION
  identity.read_current_family_delegation(uuid,text,text,timestamptz)
  FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION
  identity.read_current_family_delegation(uuid,text,text,timestamptz)
  TO :"app_role";
-- Parent can read only exact current own-child identifiers, no recipient PII.
REVOKE ALL ON FUNCTION
  identity.read_parent_active_family_delegations(uuid,text,text,timestamptz)
  FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION
  identity.read_parent_active_family_delegations(uuid,text,text,timestamptz)
  TO :"app_role";
-- The runtime can request a guardian-verified revoke but cannot directly
-- read, create, or mutate the private delegated grant row.
REVOKE ALL ON FUNCTION
  identity.revoke_family_delegated_player_grant(uuid,text,text,timestamptz,uuid,text)
  FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION
  identity.revoke_family_delegated_player_grant(uuid,text,text,timestamptz,uuid,text)
  TO :"app_role";
GRANT SELECT (id, organization_id, name, number, position, image_url, team_type, team_id,
  is_active, is_archived, is_transferred, has_explicit_picker_fields) ON club.players TO :"app_role";
GRANT SELECT (id) ON club.roster_import_state TO :"app_role";
-- Canonical auth reads expose only validated ownership/status/assignment facts.
-- Raw contacts, source archives, quarantine and owner import state remain denied.
GRANT SELECT (player_id, organization_id, academy_group_id) ON club.player_academy_groups TO :"app_role";
GRANT SELECT (id, is_active, is_archived) ON club.staff TO :"app_role";
GRANT SELECT (staff_id, team_id) ON club.staff_teams TO :"app_role";
GRANT SELECT (staff_id, academy_group_id, from_plural, from_singular, from_group_coach) ON club.staff_academy_groups TO :"app_role";
GRANT SELECT (academy_group_id, staff_id) ON club.academy_group_coaches TO :"app_role";
GRANT SELECT (collection, source_id, kind, normalized_phone) ON identity.canonical_contact_bindings TO :"app_role";
GRANT SELECT ON identity.mobile_record_links, identity.admin_staff_links TO :"app_role";
GRANT SELECT (id, normalized_phone, first_name, last_name, birthday, is_active, avatar_url, favourite_player_id, notification_preferences_json)
ON identity.fan_registration_evidence TO :"app_role";

-- The API can create verified fan records, never formal club-role grants.
-- Owner migrations/imports retain the normal table-owner RLS bypass.
ALTER TABLE identity.mobile_identities ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS mobile_runtime_select ON identity.mobile_identities;
DROP POLICY IF EXISTS mobile_runtime_insert ON identity.mobile_identities;
DROP POLICY IF EXISTS mobile_runtime_insert_fan_only ON identity.mobile_identities;
DROP POLICY IF EXISTS mobile_runtime_update ON identity.mobile_identities;
CREATE POLICY mobile_runtime_select ON identity.mobile_identities
  FOR SELECT TO :"app_role" USING (true);
CREATE POLICY mobile_runtime_insert ON identity.mobile_identities
  FOR INSERT TO :"app_role" WITH CHECK (true);
CREATE POLICY mobile_runtime_insert_fan_only ON identity.mobile_identities
  AS RESTRICTIVE FOR INSERT TO :"app_role" WITH CHECK (role = 'fan');
CREATE POLICY mobile_runtime_update ON identity.mobile_identities
  FOR UPDATE TO :"app_role" USING (true) WITH CHECK (true);

-- A dedicated database should have no unrelated schema grants. Refuse rather
-- than silently changing permissions on an unexpected application schema.
DO $ddl_guard$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_namespace n
             WHERE n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
               AND has_schema_privilege(current_setting('lefteria.runtime_role'), n.oid, 'CREATE')) THEN
    RAISE EXCEPTION 'Runtime role retains CREATE on an unexpected schema; review its grants';
  END IF;
END
$ddl_guard$;
\ir public-sections-app-grants.sql
COMMIT;
