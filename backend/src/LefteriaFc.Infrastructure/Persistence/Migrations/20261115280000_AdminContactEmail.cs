using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// An unverified contact address for the signed-in administrator, never a
/// login, password-recovery or invitation address. Only self-authorized
/// functions are callable by the admin-auth runtime.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115280000_AdminContactEmail")]
public sealed class AdminContactEmail : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE identity.admin_accounts
          ADD COLUMN unverified_contact_email varchar(254) NULL;
        ALTER TABLE identity.admin_accounts
          ADD CONSTRAINT ck_admin_unverified_contact_email
          CHECK (unverified_contact_email IS NULL OR
            (length(unverified_contact_email) BETWEEN 5 AND 254 AND
              unverified_contact_email = lower(btrim(unverified_contact_email)) AND
              unverified_contact_email ~ '^[a-z0-9._%+-]+@[a-z0-9.-]+[.][a-z]{2,63}$'));

        CREATE FUNCTION identity.read_admin_own_contact_email(_digest text)
        RETURNS text LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $read_contact$
        DECLARE v_email text;
        BEGIN
          IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$' THEN
            RAISE EXCEPTION 'admin_contact_invalid' USING ERRCODE='22023';
          END IF;
          IF NOT EXISTS(
            SELECT 1 FROM identity.admin_sessions s
            JOIN identity.admin_accounts a ON a.id=s.admin_account_id
            WHERE s.token_digest=_digest AND s.revoked_at IS NULL
              AND s.expires_at>clock_timestamp() AND a.is_active
              AND NOT a.invite_pending AND NOT a.otp2fa_enabled
              AND a.password_hash IS NOT NULL) THEN
            RAISE EXCEPTION 'admin_contact_denied' USING ERRCODE='42501';
          END IF;
          SELECT a.unverified_contact_email INTO v_email
            FROM identity.admin_sessions s
            JOIN identity.admin_accounts a ON a.id=s.admin_account_id
            WHERE s.token_digest=_digest AND s.revoked_at IS NULL
              AND s.expires_at>clock_timestamp() AND a.is_active
              AND NOT a.invite_pending AND NOT a.otp2fa_enabled
              AND a.password_hash IS NOT NULL;
          RETURN v_email;
        END $read_contact$;

        CREATE FUNCTION identity.write_admin_own_contact_email(
          _digest text,_expected_hash text,_email text
        ) RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $write_contact$
        DECLARE v_account uuid;v_session uuid;v_hash text;
        BEGIN
          IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
            OR _expected_hash IS NULL
            OR _expected_hash !~ '^[$]2[aby][$][0-9]{2}[$][./A-Za-z0-9]{53}$'
            OR _email IS NULL OR length(_email) NOT BETWEEN 5 AND 254
            OR _email <> lower(btrim(_email))
            OR _email !~ '^[a-z0-9._%+-]+@[a-z0-9.-]+[.][a-z]{2,63}$'
          THEN RAISE EXCEPTION 'admin_contact_invalid' USING ERRCODE='22023'; END IF;
          SELECT s.admin_account_id INTO v_account FROM identity.admin_sessions s
            WHERE s.token_digest=_digest;
          IF NOT FOUND THEN RAISE EXCEPTION 'admin_contact_denied' USING ERRCODE='42501'; END IF;

          -- Account-first lock ensures grants/session/password cannot be
          -- replaced between the current-password proof and the update.
          PERFORM 1 FROM identity.admin_accounts a WHERE a.id=v_account FOR UPDATE;
          SELECT s.id INTO v_session FROM identity.admin_sessions s
            WHERE s.admin_account_id=v_account AND s.token_digest=_digest
              AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp() FOR UPDATE;
          IF NOT FOUND THEN RAISE EXCEPTION 'admin_contact_denied' USING ERRCODE='42501'; END IF;
          SELECT a.password_hash INTO v_hash FROM identity.admin_accounts a
            WHERE a.id=v_account AND a.is_active
              AND NOT a.invite_pending AND NOT a.otp2fa_enabled;
          IF v_hash IS NULL OR v_hash<>_expected_hash THEN
            RAISE EXCEPTION 'admin_contact_conflict' USING ERRCODE='40001';
          END IF;
          UPDATE identity.admin_accounts
            SET unverified_contact_email=_email WHERE id=v_account;
          INSERT INTO audit.security_events
            (id,occurred_at,actor_identity_id,device_session_id,action,outcome,actor_realm)
          VALUES (gen_random_uuid(),statement_timestamp(),v_account,v_session,
            'admin.profile.contact_email','succeeded','admin');
          RETURN true;
        END $write_contact$;
        REVOKE ALL ON FUNCTION identity.read_admin_own_contact_email(text),
          identity.write_admin_own_contact_email(text,text,text) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION identity.write_admin_own_contact_email(text,text,text);
        DROP FUNCTION identity.read_admin_own_contact_email(text);
        ALTER TABLE identity.admin_accounts
          DROP CONSTRAINT ck_admin_unverified_contact_email;
        ALTER TABLE identity.admin_accounts DROP COLUMN unverified_contact_email;
        """);
}
