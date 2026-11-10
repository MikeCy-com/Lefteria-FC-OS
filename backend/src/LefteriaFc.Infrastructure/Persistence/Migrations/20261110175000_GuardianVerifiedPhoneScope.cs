using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Direct guardian phone permissions only become usable for the
/// recipient's own verified Parent-role mobile identity. The original
/// canonical parent must remain active for each child on every read.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110175000_GuardianVerifiedPhoneScope")]
public sealed class GuardianVerifiedPhoneScope : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- A phone grant does not authenticate anyone. These two predicates
      -- are for use by the existing OTP verification transaction and by
      -- session-scoped Parent reads, never as a public customer API.
      CREATE FUNCTION identity.has_current_guardian_phone_grant(_phone text)
      RETURNS boolean LANGUAGE sql SECURITY DEFINER STABLE
      SET search_path=pg_catalog,pg_temp AS $body$
        SELECT _phone ~ '^[1-9][0-9]{7,14}$' AND EXISTS(
          SELECT 1 FROM identity.family_guardian_permissions g
          JOIN identity.mobile_identities issuer
            ON issuer.id=g.issuer_identity_id
            AND issuer.is_active AND issuer.role='parent'
          JOIN identity.mobile_record_links l
            ON l.identity_id=issuer.id
            AND l.normalized_phone=issuer.normalized_phone
            AND l.role='parent' AND l.kind='guardian'
            AND l.collection='players' AND l.record_id=g.player_id
          JOIN identity.canonical_contact_bindings b
            ON b.collection=l.collection AND b.source_id=l.record_id
            AND b.kind='guardian'
            AND b.normalized_phone=issuer.normalized_phone
          JOIN club.players player
            ON player.organization_id=g.organization_id
            AND player.id=g.player_id
          WHERE g.target_phone=_phone AND g.status='granted'
            AND player.is_active AND NOT player.is_archived
            AND NOT player.is_transferred
            AND EXISTS(SELECT 1 FROM club.roster_import_state
              WHERE id='initial')
        );
      $body$;

      CREATE FUNCTION identity.current_phone_granted_children(
        _identity uuid,_phone text,_role text,_expires timestamptz
      )
      RETURNS TABLE(
        player_id varchar(128),
        organization_id varchar(32),
        team_id varchar(128),
        is_primary boolean)
      LANGUAGE sql SECURITY DEFINER STABLE
      SET search_path=pg_catalog,pg_temp AS $body$
        SELECT p.id,p.organization_id,p.team_id,false
        FROM identity.mobile_identities recipient
        JOIN identity.family_guardian_permissions grant_row
          ON grant_row.target_phone=recipient.normalized_phone
          AND grant_row.status='granted'
        JOIN identity.mobile_identities issuer
          ON issuer.id=grant_row.issuer_identity_id
          AND issuer.is_active AND issuer.role='parent'
        JOIN identity.mobile_record_links original_link
          ON original_link.identity_id=issuer.id
          AND original_link.normalized_phone=issuer.normalized_phone
          AND original_link.role='parent' AND original_link.kind='guardian'
          AND original_link.collection='players'
          AND original_link.record_id=grant_row.player_id
        JOIN identity.canonical_contact_bindings b
          ON b.collection=original_link.collection
          AND b.source_id=original_link.record_id
          AND b.kind='guardian'
          AND b.normalized_phone=issuer.normalized_phone
        JOIN club.players p
          ON p.id=grant_row.player_id
          AND p.organization_id=grant_row.organization_id
        WHERE recipient.id=_identity AND recipient.normalized_phone=_phone
          AND recipient.role='parent' AND _role='parent'
          AND recipient.is_active AND _expires>statement_timestamp()
          AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
          AND EXISTS(SELECT 1 FROM club.roster_import_state
            WHERE id='initial')
      $body$;

      REVOKE ALL ON FUNCTION identity.has_current_guardian_phone_grant(text)
        FROM PUBLIC;
      REVOKE ALL ON FUNCTION identity.current_phone_granted_children(
        uuid,text,text,timestamptz) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION identity.current_phone_granted_children(
        uuid,text,text,timestamptz);
      DROP FUNCTION identity.has_current_guardian_phone_grant(text);
    """);
}
