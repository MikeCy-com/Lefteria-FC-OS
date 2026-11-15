using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Bounded, self-only admin profile edits. No direct table writes are
/// delegated to the web runtime; current password is checked in the API and
/// the exact current hash/session is rechecked under account-first DB locks.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115260000_AdminSelfServiceProfile")]
public sealed class AdminSelfServiceProfile : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION identity.write_admin_own_name(
          _digest text,_expected_hash text,_name text
        ) RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $self_name$
        DECLARE v_account uuid;v_session uuid;v_hash text;
        BEGIN
          IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
             OR _expected_hash IS NULL OR _expected_hash !~ '^[$]2[aby][$][0-9]{2}[$][./A-Za-z0-9]{53}$'
             OR _name IS NULL OR length(_name) NOT BETWEEN 1 AND 200
             OR _name<>btrim(_name) OR _name ~ '[[:cntrl:]]'
          THEN RAISE EXCEPTION 'admin_self_invalid' USING ERRCODE='22023'; END IF;
          SELECT s.admin_account_id INTO v_account FROM identity.admin_sessions s
            WHERE s.token_digest=_digest;
          IF NOT FOUND THEN RAISE EXCEPTION 'admin_self_session_invalid' USING ERRCODE='42501'; END IF;
          -- Same lock order as the club-authority and grant mutators.
          PERFORM 1 FROM identity.admin_accounts a WHERE a.id=v_account FOR UPDATE;
          SELECT s.id INTO v_session FROM identity.admin_sessions s
            WHERE s.admin_account_id=v_account AND s.token_digest=_digest
              AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp() FOR UPDATE;
          IF NOT FOUND THEN RAISE EXCEPTION 'admin_self_session_invalid' USING ERRCODE='42501'; END IF;
          SELECT a.password_hash INTO v_hash FROM identity.admin_accounts a
            WHERE a.id=v_account AND a.is_active
              AND NOT a.invite_pending AND NOT a.otp2fa_enabled;
          IF v_hash IS NULL OR v_hash<>_expected_hash THEN
            RAISE EXCEPTION 'admin_self_credential_changed' USING ERRCODE='40001';
          END IF;
          UPDATE identity.admin_accounts SET display_name=_name WHERE id=v_account;
          INSERT INTO audit.security_events
            (id,occurred_at,actor_identity_id,device_session_id,action,outcome,actor_realm)
          VALUES (gen_random_uuid(),statement_timestamp(),v_account,v_session,
            'admin.profile.name','succeeded','admin');
          RETURN true;
        END $self_name$;

        CREATE FUNCTION identity.write_admin_own_password(
          _digest text,_expected_hash text,_next_hash text
        ) RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $self_password$
        DECLARE v_account uuid;v_session uuid;v_hash text;
        BEGIN
          IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
             OR _expected_hash IS NULL OR _expected_hash !~ '^[$]2[aby][$][0-9]{2}[$][./A-Za-z0-9]{53}$'
             OR _next_hash IS NULL OR _next_hash !~ '^[$]2[aby][$]12[$][./A-Za-z0-9]{53}$'
             OR _next_hash=_expected_hash
          THEN RAISE EXCEPTION 'admin_self_invalid' USING ERRCODE='22023'; END IF;
          SELECT s.admin_account_id INTO v_account FROM identity.admin_sessions s
            WHERE s.token_digest=_digest;
          IF NOT FOUND THEN RAISE EXCEPTION 'admin_self_session_invalid' USING ERRCODE='42501'; END IF;
          PERFORM 1 FROM identity.admin_accounts a WHERE a.id=v_account FOR UPDATE;
          SELECT s.id INTO v_session FROM identity.admin_sessions s
            WHERE s.admin_account_id=v_account AND s.token_digest=_digest
              AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp() FOR UPDATE;
          IF NOT FOUND THEN RAISE EXCEPTION 'admin_self_session_invalid' USING ERRCODE='42501'; END IF;
          SELECT a.password_hash INTO v_hash FROM identity.admin_accounts a
            WHERE a.id=v_account AND a.is_active
              AND NOT a.invite_pending AND NOT a.otp2fa_enabled;
          IF v_hash IS NULL OR v_hash<>_expected_hash THEN
            RAISE EXCEPTION 'admin_self_credential_changed' USING ERRCODE='40001';
          END IF;
          UPDATE identity.admin_accounts SET password_hash=_next_hash WHERE id=v_account;
          -- Invalidate every active web-admin session for this account,
          -- including the submitting device. Old credential digests also
          -- become invalid as a separate fail-closed guarantee.
          UPDATE identity.admin_sessions
            SET revoked_at=GREATEST(created_at,statement_timestamp())
            WHERE admin_account_id=v_account AND revoked_at IS NULL;
          INSERT INTO audit.security_events
            (id,occurred_at,actor_identity_id,device_session_id,action,outcome,actor_realm)
          VALUES (gen_random_uuid(),statement_timestamp(),v_account,v_session,
            'admin.profile.password','succeeded','admin');
          RETURN true;
        END $self_password$;
        REVOKE ALL ON FUNCTION identity.write_admin_own_name(text,text,text),
          identity.write_admin_own_password(text,text,text) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION identity.write_admin_own_password(text,text,text);
        DROP FUNCTION identity.write_admin_own_name(text,text,text);
        """);
}
