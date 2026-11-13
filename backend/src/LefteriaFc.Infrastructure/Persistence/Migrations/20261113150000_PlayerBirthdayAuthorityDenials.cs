using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Normalize the trusted admin owner-lock's deliberate P0001 revocation
/// denial into SQLSTATE 42501. The API can then reject cross-org and revoked
/// credentials as 403 rather than leaking an internal 500.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261113150000_PlayerBirthdayAuthorityDenials")]
public sealed class PlayerBirthdayAuthorityDenials : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE OR REPLACE FUNCTION club.require_player_birthday_manager(
        _digest text,_owner text,_player text
      ) RETURNS uuid LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $birthday_authority$
      DECLARE v_actor uuid;v_role text;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
          OR _player ~ '[[:cntrl:]]' THEN
          RAISE EXCEPTION 'birthday_invalid_scope' USING ERRCODE='22023';
        END IF;
        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'birthday_manager_denied' USING ERRCODE='42501';
        END;
        SELECT a.id,a.role INTO v_actor,v_role
        FROM identity.admin_sessions s
        JOIN identity.admin_accounts a ON a.id=s.admin_account_id
        JOIN identity.admin_organization_grants g
          ON g.admin_account_id=a.id AND g.organization_id=_owner
        WHERE s.token_digest=_digest AND s.revoked_at IS NULL
          AND s.expires_at>clock_timestamp() AND a.is_active
          AND NOT a.invite_pending AND NOT a.otp2fa_enabled
          AND a.password_hash IS NOT NULL;
        IF v_actor IS NULL OR v_role NOT IN ('manager','super_admin')
          OR NOT EXISTS(SELECT 1 FROM club.players p
            WHERE p.id=_player AND p.organization_id=_owner
              AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred)
        THEN RAISE EXCEPTION 'birthday_manager_denied' USING ERRCODE='42501';
        END IF;
        RETURN v_actor;
      END $birthday_authority$;
      REVOKE ALL ON FUNCTION club.require_player_birthday_manager(
        text,text,text) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
      migrationBuilder.Sql("""
        CREATE OR REPLACE FUNCTION club.require_player_birthday_manager(
          _digest text,_owner text,_player text
        ) RETURNS uuid LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $birthday_authority$
        DECLARE v_actor uuid;v_role text;
        BEGIN
          IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
            OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
            OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
            OR _player ~ '[[:cntrl:]]' THEN
            RAISE EXCEPTION 'birthday_invalid_scope' USING ERRCODE='22023';
          END IF;
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
          SELECT a.id,a.role INTO v_actor,v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
          IF v_actor IS NULL OR v_role NOT IN ('manager','super_admin')
            OR NOT EXISTS(SELECT 1 FROM club.players p
              WHERE p.id=_player AND p.organization_id=_owner
                AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred)
          THEN RAISE EXCEPTION 'birthday_manager_denied' USING ERRCODE='42501';
          END IF;
          RETURN v_actor;
        END $birthday_authority$;
        REVOKE ALL ON FUNCTION club.require_player_birthday_manager(
          text,text,text) FROM PUBLIC;
      """);
}
