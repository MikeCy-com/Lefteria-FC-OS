using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Enable phone-based second-guardian delegation from an independently
/// human-reviewed, OTP-confirmed Academy original guardian. The existing
/// immutable permission lineage, child scope, max-depth, role conflict,
/// account creation-after-OTP and cascading revoke rules stay authoritative.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115990000_AcademyGuardianDelegationLineage")]
public sealed class AcademyGuardianDelegationLineage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE OR REPLACE FUNCTION identity.valid_guardian_phone_grants()
      RETURNS TABLE(
        permission_id uuid,
        player_id varchar(128),
        organization_id varchar(32),
        target_phone varchar(15),
        issuer_identity_id uuid
      ) LANGUAGE sql SECURITY DEFINER STABLE
      SET search_path=pg_catalog,pg_temp AS $scope$
        WITH RECURSIVE roots(
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
          UNION
          -- A proof-confirmed original Academy guardian is also a first-class
          -- lineage root, but ONLY for the exact current, approved child.
          -- This never trusts a registration phone without the later OTP.
          SELECT grant_row.id,grant_row.player_id,
            grant_row.organization_id,grant_row.target_phone,
            grant_row.issuer_identity_id,ARRAY[grant_row.id],1
          FROM identity.family_guardian_permissions grant_row
          JOIN identity.mobile_identities issuer
            ON issuer.id=grant_row.issuer_identity_id
            AND issuer.is_active AND issuer.role='parent'
          JOIN identity.current_academy_guardian_claims(issuer.normalized_phone) root
            ON root.player_id=grant_row.player_id
            AND root.organization_id=grant_row.organization_id
            AND root.organization_id='lefteria_fc_academy'
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
        ), valid(
          permission_id,player_id,organization_id,target_phone,
          issuer_identity_id,path,depth) AS (
          SELECT permission_id,player_id,organization_id,target_phone,
            issuer_identity_id,path,depth FROM roots
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

      CREATE OR REPLACE FUNCTION identity.require_canonical_guardian_delegation_root()
      RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $root$
      BEGIN
        IF NEW.source_permission_id IS NULL
          AND NOT EXISTS (
          SELECT 1 FROM identity.mobile_identities i
          JOIN identity.mobile_record_links l
            ON l.identity_id=i.id AND l.role='parent'
            AND l.normalized_phone=i.normalized_phone
            AND l.collection='players' AND l.kind='guardian'
            AND l.record_id=NEW.player_id
          JOIN identity.canonical_contact_bindings b
            ON b.collection=l.collection AND b.kind=l.kind
            AND b.source_id=l.record_id AND b.normalized_phone=i.normalized_phone
          WHERE i.id=NEW.issuer_identity_id AND i.is_active
            AND i.role='parent')
          AND NOT EXISTS (
            -- Critical: only the ORIGINAL verified guardian can create
            -- an unanchored root. A phone-delegated guardian must preserve
            -- source_permission_id so its revocation lineage is traceable.
            SELECT 1 FROM identity.mobile_identities original
            JOIN identity.current_academy_guardian_claims(original.normalized_phone) claim
              ON claim.player_id=NEW.player_id
              AND claim.organization_id=NEW.organization_id
            WHERE original.id=NEW.issuer_identity_id
              AND original.role='parent' AND original.is_active
              AND claim.organization_id='lefteria_fc_academy')
        THEN RAISE EXCEPTION 'guardian_claim_delegation_not_enabled'
          USING ERRCODE='42501'; END IF;
        RETURN NEW;
      END $root$;
      REVOKE ALL ON FUNCTION identity.valid_guardian_phone_grants()
        FROM PUBLIC;
      REVOKE ALL ON FUNCTION identity.require_canonical_guardian_delegation_root()
        FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- A rollback must not silently abandon previously issued original
      -- Academy guardian lineages. Use a forward recovery for real history.
      DO $lineage_rollback$ BEGIN
        IF EXISTS (
          SELECT 1 FROM identity.family_guardian_permissions permission
          JOIN identity.mobile_identities issuer
            ON issuer.id=permission.issuer_identity_id
          JOIN identity.academy_guardian_otp_claims claim
            ON claim.normalized_phone=issuer.normalized_phone
          JOIN academy_intake.guardian_proof_reviews proof
            ON proof.registration_id=claim.registration_id
            AND proof.player_id=permission.player_id
          WHERE permission.source_permission_id IS NULL
            AND permission.organization_id='lefteria_fc_academy'
        ) THEN RAISE EXCEPTION 'academy_guardian_delegation_requires_forward_recovery'; END IF;
      END $lineage_rollback$;

      CREATE OR REPLACE FUNCTION identity.valid_guardian_phone_grants()
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

      CREATE OR REPLACE FUNCTION identity.require_canonical_guardian_delegation_root()
      RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $root$
      BEGIN
        IF NEW.source_permission_id IS NULL AND NOT EXISTS (
          SELECT 1 FROM identity.mobile_identities i
          JOIN identity.mobile_record_links l
            ON l.identity_id=i.id AND l.role='parent'
            AND l.normalized_phone=i.normalized_phone
            AND l.collection='players' AND l.kind='guardian'
            AND l.record_id=NEW.player_id
          JOIN identity.canonical_contact_bindings b
            ON b.collection=l.collection AND b.kind=l.kind
            AND b.source_id=l.record_id AND b.normalized_phone=i.normalized_phone
          WHERE i.id=NEW.issuer_identity_id AND i.is_active
            AND i.role='parent')
        THEN RAISE EXCEPTION 'guardian_claim_delegation_not_enabled'
          USING ERRCODE='42501'; END IF;
        RETURN NEW;
      END $root$;
      REVOKE ALL ON FUNCTION identity.valid_guardian_phone_grants()
        FROM PUBLIC;
      REVOKE ALL ON FUNCTION identity.require_canonical_guardian_delegation_root()
        FROM PUBLIC;
    """);
}
