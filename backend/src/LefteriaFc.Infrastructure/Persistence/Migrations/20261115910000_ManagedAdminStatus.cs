using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Audited Super Admin-only account suspension/reactivation.
/// Account row locks serialize with session creation; all historical sessions
/// are revoked on either transition. Existing organization grants are retained.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115910000_ManagedAdminStatus")]
public sealed class ManagedAdminStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION identity.set_managed_admin_status(
          _digest text,_subject uuid,_expected_role text,
          _expected_active boolean,_active boolean
        ) RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $managed_status$
        DECLARE
          v_actor uuid;v_session uuid;v_actor_role text;
          v_subject_role text;v_subject_active boolean;
          v_invited boolean;v_has_credential boolean;
          v_owner text;v_owner_count integer:=0;
        BEGIN
          IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
            OR _subject IS NULL OR _expected_role IS NULL
            OR _expected_role NOT IN ('manager','editor','marketing','coach','matchday_editor','accountant')
            OR _expected_active IS NULL OR _active IS NULL
            OR _expected_active=_active
          THEN RAISE EXCEPTION 'managed_admin_status_invalid' USING ERRCODE='22023'; END IF;

          SELECT s.admin_account_id INTO v_actor
            FROM identity.admin_sessions s WHERE s.token_digest=_digest;
          IF v_actor IS NULL OR v_actor=_subject THEN
            RAISE EXCEPTION 'managed_admin_status_denied' USING ERRCODE='42501';
          END IF;
          -- Lock actor account, then actor session, then target account.
          -- Session creation takes a FOR SHARE lock on its account to ensure
          -- no new session can race suspension and survive its revocation.
          SELECT a.role INTO v_actor_role FROM identity.admin_accounts a
            WHERE a.id=v_actor AND a.is_active AND NOT a.invite_pending
              AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL FOR UPDATE;
          IF v_actor_role IS DISTINCT FROM 'super_admin' THEN
            RAISE EXCEPTION 'managed_admin_status_denied' USING ERRCODE='42501';
          END IF;
          SELECT s.id INTO v_session FROM identity.admin_sessions s
            WHERE s.token_digest=_digest AND s.admin_account_id=v_actor
              AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp() FOR UPDATE;
          IF v_session IS NULL THEN
            RAISE EXCEPTION 'managed_admin_status_session_denied' USING ERRCODE='42501';
          END IF;
          SELECT a.role,a.is_active,a.invite_pending,(a.password_hash IS NOT NULL)
            INTO v_subject_role,v_subject_active,v_invited,v_has_credential
            FROM identity.admin_accounts a WHERE a.id=_subject FOR UPDATE;
          IF v_subject_role IS NULL OR v_subject_role='super_admin'
            OR v_subject_role NOT IN ('manager','editor','marketing','coach','matchday_editor','accountant')
            OR v_invited OR NOT v_has_credential
          THEN RAISE EXCEPTION 'managed_admin_status_target_denied' USING ERRCODE='42501'; END IF;
          IF v_subject_role<>_expected_role OR v_subject_active IS DISTINCT FROM _expected_active THEN
            RAISE EXCEPTION 'managed_admin_status_conflict' USING ERRCODE='40001';
          END IF;

          -- An administrator may only touch a subject whose EVERY assigned
          -- organization belongs to that administrator. No partial ownership.
          FOR v_owner IN SELECT g.organization_id
            FROM identity.admin_organization_grants g
            WHERE g.admin_account_id=_subject ORDER BY g.organization_id FOR SHARE
          LOOP
            v_owner_count:=v_owner_count+1;
            IF v_owner NOT IN ('lefteria_fc','lefteria_fc_academy') THEN
              RAISE EXCEPTION 'managed_admin_status_foreign_owner' USING ERRCODE='42501';
            END IF;
            PERFORM 1 FROM identity.admin_organization_grants actor_grant
              WHERE actor_grant.admin_account_id=v_actor
                AND actor_grant.organization_id=v_owner FOR SHARE;
            IF NOT FOUND THEN
              RAISE EXCEPTION 'managed_admin_status_foreign_owner' USING ERRCODE='42501';
            END IF;
          END LOOP;
          IF v_owner_count=0 OR v_owner_count>2 THEN
            RAISE EXCEPTION 'managed_admin_status_owner_missing' USING ERRCODE='42501';
          END IF;

          UPDATE identity.admin_accounts SET is_active=_active WHERE id=_subject;
          UPDATE identity.admin_sessions
            SET revoked_at=GREATEST(created_at,statement_timestamp())
            WHERE admin_account_id=_subject AND revoked_at IS NULL;
          INSERT INTO audit.security_events
            (id,occurred_at,actor_identity_id,device_session_id,action,
             outcome,subject_digest,reason_code,actor_realm)
          VALUES (gen_random_uuid(),statement_timestamp(),v_actor,v_session,
            CASE WHEN _active THEN 'admin.account.reactivate'
              ELSE 'admin.account.suspend' END,
            'succeeded',md5(_subject::text),
            CASE WHEN _active THEN 'activated' ELSE 'deactivated' END,'admin');
          RETURN _active;
        END $managed_status$;
        REVOKE ALL ON FUNCTION identity.set_managed_admin_status(text,uuid,text,boolean,boolean)
          FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION identity.set_managed_admin_status(text,uuid,text,boolean,boolean);
        """);
}
