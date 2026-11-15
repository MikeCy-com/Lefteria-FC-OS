using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Self-owned, server-sanitized JPEG only. No raw image survives
/// outside the bounded request; the admin-auth role has function EXECUTE
/// without private image-table SELECT, INSERT, UPDATE or DELETE.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115300000_AdminSelfProfilePhoto")]
public sealed class AdminSelfProfilePhoto : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE identity.admin_profile_photos(
          admin_account_id uuid PRIMARY KEY REFERENCES identity.admin_accounts(id) ON DELETE CASCADE,
          jpeg_bytes bytea NOT NULL,
          updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          CONSTRAINT ck_admin_profile_jpeg_sanitized CHECK (
            octet_length(jpeg_bytes) BETWEEN 4 AND 1048576
            AND get_byte(jpeg_bytes,0)=255
            AND get_byte(jpeg_bytes,1)=216
            AND get_byte(jpeg_bytes,2)=255
            AND get_byte(jpeg_bytes,octet_length(jpeg_bytes)-2)=255
            AND get_byte(jpeg_bytes,octet_length(jpeg_bytes)-1)=217)
        );
        REVOKE ALL ON identity.admin_profile_photos FROM PUBLIC;

        CREATE FUNCTION identity.read_admin_own_profile_photo(_digest text)
        RETURNS bytea LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $read_photo$
        DECLARE v_account uuid; v_bytes bytea;
        BEGIN
          IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          THEN RAISE EXCEPTION 'admin_photo_invalid' USING ERRCODE='22023'; END IF;
          SELECT s.admin_account_id INTO v_account FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'admin_photo_denied' USING ERRCODE='42501';
          END IF;
          SELECT p.jpeg_bytes INTO v_bytes FROM identity.admin_profile_photos p
            WHERE p.admin_account_id=v_account;
          RETURN v_bytes;
        END $read_photo$;

        CREATE FUNCTION identity.write_admin_own_profile_photo(
          _digest text,_jpeg bytea
        ) RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $write_photo$
        DECLARE v_account uuid;v_session uuid;
        BEGIN
          IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
            OR (_jpeg IS NOT NULL AND (octet_length(_jpeg) NOT BETWEEN 4 AND 1048576
              OR get_byte(_jpeg,0)<>255 OR get_byte(_jpeg,1)<>216
              OR get_byte(_jpeg,2)<>255
              OR get_byte(_jpeg,octet_length(_jpeg)-2)<>255
              OR get_byte(_jpeg,octet_length(_jpeg)-1)<>217))
          THEN RAISE EXCEPTION 'admin_photo_invalid' USING ERRCODE='22023'; END IF;
          SELECT s.admin_account_id INTO v_account FROM identity.admin_sessions s
            WHERE s.token_digest=_digest;
          IF NOT FOUND THEN RAISE EXCEPTION 'admin_photo_denied' USING ERRCODE='42501'; END IF;

          -- Identical lock order to the account/profile/role mutators.
          PERFORM 1 FROM identity.admin_accounts a WHERE a.id=v_account FOR UPDATE;
          SELECT s.id INTO v_session FROM identity.admin_sessions s
          WHERE s.admin_account_id=v_account AND s.token_digest=_digest
            AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp() FOR UPDATE;
          IF NOT FOUND THEN RAISE EXCEPTION 'admin_photo_denied' USING ERRCODE='42501'; END IF;
          IF NOT EXISTS (SELECT 1 FROM identity.admin_accounts a
            WHERE a.id=v_account AND a.is_active AND NOT a.invite_pending
              AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL)
          THEN RAISE EXCEPTION 'admin_photo_denied' USING ERRCODE='42501'; END IF;

          IF _jpeg IS NULL THEN
            DELETE FROM identity.admin_profile_photos WHERE admin_account_id=v_account;
          ELSE
            INSERT INTO identity.admin_profile_photos(admin_account_id,jpeg_bytes,updated_at)
              VALUES(v_account,_jpeg,statement_timestamp())
            ON CONFLICT(admin_account_id) DO UPDATE
              SET jpeg_bytes=excluded.jpeg_bytes,updated_at=excluded.updated_at;
          END IF;
          INSERT INTO audit.security_events
            (id,occurred_at,actor_identity_id,device_session_id,action,outcome,actor_realm)
          VALUES (gen_random_uuid(),statement_timestamp(),v_account,v_session,
            CASE WHEN _jpeg IS NULL THEN 'admin.profile.photo.removed'
              ELSE 'admin.profile.photo.updated' END,'succeeded','admin');
          RETURN true;
        END $write_photo$;

        REVOKE ALL ON FUNCTION identity.read_admin_own_profile_photo(text),
          identity.write_admin_own_profile_photo(text,bytea) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION identity.write_admin_own_profile_photo(text,bytea);
        DROP FUNCTION identity.read_admin_own_profile_photo(text);
        DROP TABLE identity.admin_profile_photos;
        """);
}
