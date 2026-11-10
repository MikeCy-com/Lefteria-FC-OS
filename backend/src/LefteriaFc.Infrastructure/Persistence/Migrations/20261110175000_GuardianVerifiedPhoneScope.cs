using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Current phone grants trace to a live canonical parent through a bounded,
/// cycle-free lineage. Revoking an ancestor immediately removes descendant
/// scope without trusting a materialized role or stale token claim.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110175000_GuardianVerifiedPhoneScope")]
public sealed class GuardianVerifiedPhoneScope : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION identity.valid_guardian_phone_grants()
      RETURNS TABLE(
        permission_id uuid,
        player_id varchar(128),
        organization_id varchar(32),
        target_phone varchar(15),
        issuer_identity_id uuid
      ) LANGUAGE sql SECURITY DEFINER STABLE
      SET search_path=pg_catalog,pg_temp AS $scope$
        WITH RECURSIVE valid(
          permission_id,player_id,organization_id,target_phone,
          issuer_identity_id,path,depth) AS (
          SELECT grant_row.id,grant_row.player_id,
            grant_row.organization_id,grant_row.target_phone,
            grant_row.issuer_identity_id,ARRAY[grant_row.id],1
          FROM identity.family_guardian_permissions grant_row
          JOIN identity.mobile_identities issuer
            ON issuer.id=grant_row.issuer_identity_id
            AND issuer.is_active AND issuer.role='parent'
          JOIN identity.mobile_record_links original_link
            ON original_link.identity_id=issuer.id
            AND original_link.normalized_phone=issuer.normalized_phone
            AND original_link.role='parent' AND original_link.kind='guardian'
            AND original_link.collection='players'
            AND original_link.record_id=grant_row.player_id
          JOIN identity.canonical_contact_bindings binding
            ON binding.collection=original_link.collection
            AND binding.source_id=original_link.record_id
            AND binding.kind='guardian'
            AND binding.normalized_phone=issuer.normalized_phone
          JOIN club.players player
            ON player.id=grant_row.player_id
            AND player.organization_id=grant_row.organization_id
          WHERE grant_row.source_permission_id IS NULL
            AND grant_row.status='granted'
            AND NOT EXISTS(
              SELECT 1 FROM identity.mobile_identities blocked
              WHERE blocked.normalized_phone=grant_row.target_phone
                AND blocked.role IN ('player','coach','management'))
            AND NOT EXISTS(
              SELECT 1 FROM identity.canonical_contact_bindings blocked
              WHERE blocked.normalized_phone=grant_row.target_phone
                AND blocked.kind='player_own')
            AND player.is_active AND NOT player.is_archived
            AND NOT player.is_transferred
            AND EXISTS(SELECT 1 FROM club.roster_import_state
              WHERE id='initial')
          UNION ALL
          SELECT grant_row.id,grant_row.player_id,
            grant_row.organization_id,grant_row.target_phone,
            grant_row.issuer_identity_id,
            valid.path||grant_row.id,valid.depth+1
          FROM valid
          JOIN identity.family_guardian_permissions grant_row
            ON grant_row.source_permission_id=valid.permission_id
            AND grant_row.player_id=valid.player_id
            AND grant_row.organization_id=valid.organization_id
            AND grant_row.status='granted'
            AND NOT EXISTS(
              SELECT 1 FROM identity.mobile_identities blocked
              WHERE blocked.normalized_phone=grant_row.target_phone
                AND blocked.role IN ('player','coach','management'))
            AND NOT EXISTS(
              SELECT 1 FROM identity.canonical_contact_bindings blocked
              WHERE blocked.normalized_phone=grant_row.target_phone
                AND blocked.kind='player_own')
          JOIN identity.mobile_identities issuer
            ON issuer.id=grant_row.issuer_identity_id
            AND issuer.is_active AND issuer.role='parent'
            AND issuer.normalized_phone=valid.target_phone
          WHERE valid.depth<20 AND NOT grant_row.id=ANY(valid.path)
        )
        SELECT DISTINCT permission_id,player_id,organization_id,
          target_phone,issuer_identity_id
        FROM valid;
      $scope$;

      CREATE FUNCTION identity.has_current_guardian_phone_grant(_phone text)
      RETURNS boolean LANGUAGE sql SECURITY DEFINER STABLE
      SET search_path=pg_catalog,pg_temp AS $scope$
        SELECT _phone ~ '^[1-9][0-9]{7,14}$' AND EXISTS(
          SELECT 1 FROM identity.valid_guardian_phone_grants() valid
          WHERE valid.target_phone=_phone
        );
      $scope$;

      CREATE FUNCTION identity.current_phone_granted_children(
        _identity uuid,_phone text,_role text,_expires timestamptz
      )
      RETURNS TABLE(
        player_id varchar(128),
        organization_id varchar(32),
        team_id varchar(128),
        is_primary boolean,
        permission_id uuid)
      LANGUAGE sql SECURITY DEFINER STABLE
      SET search_path=pg_catalog,pg_temp AS $scope$
        SELECT player.id,player.organization_id,player.team_id,false,
          grant_row.permission_id
        FROM identity.mobile_identities recipient
        JOIN identity.valid_guardian_phone_grants() grant_row
          ON grant_row.target_phone=recipient.normalized_phone
        JOIN club.players player
          ON player.id=grant_row.player_id
          AND player.organization_id=grant_row.organization_id
        WHERE recipient.id=_identity AND recipient.normalized_phone=_phone
          AND recipient.role='parent' AND _role='parent'
          AND recipient.is_active AND _expires>statement_timestamp()
          AND player.is_active AND NOT player.is_archived
          AND NOT player.is_transferred;
      $scope$;

      REVOKE ALL ON FUNCTION identity.valid_guardian_phone_grants()
        FROM PUBLIC;
      REVOKE ALL ON FUNCTION identity.has_current_guardian_phone_grant(text)
        FROM PUBLIC;
      REVOKE ALL ON FUNCTION identity.current_phone_granted_children(
        uuid,text,text,timestamptz) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION identity.current_phone_granted_children(
        uuid,text,text,timestamptz);
      DROP FUNCTION identity.has_current_guardian_phone_grant(text);
      DROP FUNCTION identity.valid_guardian_phone_grants();
    """);
}
