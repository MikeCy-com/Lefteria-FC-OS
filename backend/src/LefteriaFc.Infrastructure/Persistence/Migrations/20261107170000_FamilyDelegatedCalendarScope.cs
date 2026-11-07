using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

// Exact player+organization authorization predicate for future delegated
// calendar reads. It is NOT an independent permission to retrieve child data:
// the eventual calendar query must call this inside its own SQL statement,
// rejoining the current calendar visibility rules and event eligibility.
// Never use this as a separate application preflight before an unguarded query.
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261107170000_FamilyDelegatedCalendarScope")]
public sealed class FamilyDelegatedCalendarScope : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE FUNCTION identity.current_family_calendar_scope(
              _recipient uuid, _phone text, _role text, _expires timestamptz,
              _player text, _organization text)
            RETURNS boolean LANGUAGE plpgsql STABLE SECURITY DEFINER
            SET search_path = pg_catalog, pg_temp
            AS $family_calendar_scope$
            DECLARE
              v_count integer;
            BEGIN
              -- Only a current OTP-verified FAN identity can hold delegation.
              -- Validate requested scope syntactically before any private
              -- table lookup; no player name, roster or contact is returned.
              IF _recipient IS NULL OR
                 _recipient='00000000-0000-0000-0000-000000000000'::uuid
                 OR _role IS DISTINCT FROM 'fan'
                 OR _expires IS NULL OR _expires<=statement_timestamp()
                 OR _phone IS NULL OR _phone !~ '^[1-9][0-9]{7,14}$'
              THEN
                RAISE EXCEPTION 'family_invitation_denied' USING ERRCODE='P0001';
              END IF;
              IF _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
                OR _player <> btrim(_player)
                OR _player ~ '[[:cntrl:]]'
                OR _organization IS NULL OR length(_organization) NOT BETWEEN 1 AND 128
                OR _organization <> btrim(_organization)
                OR _organization ~ '[[:cntrl:]]'
              THEN
                RETURN false;
              END IF;

              -- Reuses the already-reviewed live canonical guardian, active
              -- child, unconflicted recipient phone, matching invitation and
              -- nonrevoked grant checks. Never trust a stored active flag or a
              -- caller-selected child alone. Exactly one result is required.
              SELECT count(*)::integer INTO v_count
              FROM identity.read_current_family_delegation(
                _recipient,_phone,_role,_expires) current_grant
              WHERE current_grant.player_id=_player
                AND current_grant.organization_id=_organization;

              RETURN v_count=1;
            END
            $family_calendar_scope$;

            REVOKE ALL ON FUNCTION
              identity.current_family_calendar_scope(uuid,text,text,timestamptz,text,text)
              FROM PUBLIC;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP FUNCTION
              identity.current_family_calendar_scope(uuid,text,text,timestamptz,text,text);
            """);
    }
}
