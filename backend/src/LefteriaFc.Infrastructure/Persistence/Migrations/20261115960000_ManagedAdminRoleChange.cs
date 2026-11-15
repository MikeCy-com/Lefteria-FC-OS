using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Audited, Super Admin-only role changes with no stale local assignments.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115960000_ManagedAdminRoleChange")]
public sealed class ManagedAdminRoleChange : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION identity.change_managed_admin_role(
        _digest text,_subject uuid,_expected_role text,_expected_active boolean,_next_role text
      ) RETURNS text LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $managed_role$
      DECLARE
        v_actor uuid;v_session uuid;v_actor_role text;
        v_role text;v_active boolean;v_invited boolean;v_credential boolean;
        v_owner text;v_owner_count integer:=0;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[a-f0-9]{64}$'
          OR _subject IS NULL OR _expected_active IS NULL
          OR _expected_role IS NULL OR _next_role IS NULL
          OR _expected_role NOT IN ('manager','coach','editor','marketing','matchday_editor','accountant')
          OR _next_role NOT IN ('manager','coach','editor','marketing','matchday_editor','accountant')
          OR _expected_role=_next_role
        THEN RAISE EXCEPTION 'managed_role_invalid' USING ERRCODE='22023'; END IF;

        SELECT s.admin_account_id INTO v_actor FROM identity.admin_sessions s
          WHERE s.token_digest=_digest;
        IF v_actor IS NULL OR v_actor=_subject
        THEN RAISE EXCEPTION 'managed_role_actor_denied' USING ERRCODE='42501'; END IF;
        SELECT a.role INTO v_actor_role FROM identity.admin_accounts a
          WHERE a.id=v_actor AND a.is_active AND NOT a.invite_pending
            AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL FOR UPDATE;
        IF v_actor_role IS DISTINCT FROM 'super_admin'
        THEN RAISE EXCEPTION 'managed_role_super_admin_required' USING ERRCODE='42501'; END IF;
        SELECT s.id INTO v_session FROM identity.admin_sessions s
          WHERE s.token_digest=_digest AND s.admin_account_id=v_actor
            AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp() FOR UPDATE;
        IF v_session IS NULL
        THEN RAISE EXCEPTION 'managed_role_session_denied' USING ERRCODE='42501'; END IF;

        -- The same account lock is held by login admission and by football
        -- assignment writers. New sessions cannot survive the role change.
        SELECT a.role,a.is_active,a.invite_pending,(a.password_hash IS NOT NULL)
          INTO v_role,v_active,v_invited,v_credential
          FROM identity.admin_accounts a WHERE a.id=_subject FOR UPDATE;
        IF v_role IS NULL OR v_role='super_admin'
          OR v_role NOT IN ('manager','coach','editor','marketing','matchday_editor','accountant')
          OR v_invited OR NOT v_credential
        THEN RAISE EXCEPTION 'managed_role_subject_denied' USING ERRCODE='42501'; END IF;
        IF v_role<>_expected_role OR v_active IS DISTINCT FROM _expected_active
        THEN RAISE EXCEPTION 'managed_role_conflict' USING ERRCODE='40001'; END IF;

        -- All subject organizations must be presently owned by this actor.
        FOR v_owner IN SELECT g.organization_id
          FROM identity.admin_organization_grants g
          WHERE g.admin_account_id=_subject ORDER BY g.organization_id FOR SHARE
        LOOP
          v_owner_count:=v_owner_count+1;
          IF v_owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          THEN RAISE EXCEPTION 'managed_role_foreign_owner' USING ERRCODE='42501'; END IF;
          PERFORM 1 FROM identity.admin_organization_grants g
            WHERE g.admin_account_id=v_actor AND g.organization_id=v_owner FOR SHARE;
          IF NOT FOUND
          THEN RAISE EXCEPTION 'managed_role_foreign_owner' USING ERRCODE='42501'; END IF;
        END LOOP;
        IF v_owner_count=0 OR v_owner_count>2
        THEN RAISE EXCEPTION 'managed_role_missing_owner' USING ERRCODE='42501'; END IF;

        -- A role cannot reinterpret old city/location/team or staff links.
        -- Existing assignments must be explicitly revoked in their own
        -- authorized tools first; never delete them as an implicit side effect.
        IF EXISTS(SELECT 1 FROM club.football_city_admin_grants WHERE admin_account_id=_subject)
          OR EXISTS(SELECT 1 FROM club.football_location_admin_grants WHERE admin_account_id=_subject)
          OR EXISTS(SELECT 1 FROM club.football_team_admin_grants WHERE admin_account_id=_subject)
          OR EXISTS(SELECT 1 FROM identity.admin_staff_links WHERE admin_account_id=_subject)
        THEN RAISE EXCEPTION 'managed_role_assignments_exist' USING ERRCODE='23514'; END IF;

        UPDATE identity.admin_accounts SET role=_next_role WHERE id=_subject;
        UPDATE identity.admin_sessions
          SET revoked_at=GREATEST(created_at,statement_timestamp())
          WHERE admin_account_id=_subject AND revoked_at IS NULL;
        INSERT INTO audit.security_events
          (id,occurred_at,actor_identity_id,device_session_id,action,
           outcome,subject_digest,reason_code,actor_realm)
        VALUES(gen_random_uuid(),statement_timestamp(),v_actor,v_session,
          'admin.account.role_change','succeeded',md5(_subject::text),
          v_role||'_to_'||_next_role,'admin');
        RETURN _next_role;
      END $managed_role$;
      REVOKE ALL ON FUNCTION identity.change_managed_admin_role(text,uuid,text,boolean,text)
        FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION identity.change_managed_admin_role(text,uuid,text,boolean,text);
      """);
}
