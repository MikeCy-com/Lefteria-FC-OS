using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

// Storage only: this migration does NOT accept invitations, provision player
// identities, or allow a fan to access another person's player profile.
// Activation must be introduced atomically with verified authorization paths.
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261107120000_FamilyDelegatedPlayerGrants")]
public sealed class FamilyDelegatedPlayerGrants : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE identity.family_delegated_player_grants (
              grant_id uuid PRIMARY KEY,
              invitation_id uuid NOT NULL UNIQUE
                REFERENCES identity.family_invitations(invitation_id) ON DELETE RESTRICT,
              player_id varchar(128) COLLATE "C" NOT NULL,
              organization_id varchar(32) NOT NULL,
              recipient_identity_id uuid NOT NULL
                REFERENCES identity.mobile_identities(id) ON DELETE RESTRICT,
              granted_by_identity_id uuid NOT NULL
                REFERENCES identity.mobile_identities(id) ON DELETE RESTRICT,
              status varchar(16) NOT NULL,
              granted_at timestamptz NOT NULL,
              revoked_at timestamptz,
              CONSTRAINT family_delegate_player_fk
                FOREIGN KEY (organization_id,player_id)
                REFERENCES club.players(organization_id,id) ON DELETE RESTRICT,
              CONSTRAINT family_delegate_status
                CHECK (status IN ('active','revoked','superseded')),
              CONSTRAINT family_delegate_revocation_clock
                CHECK ((status='active' AND revoked_at IS NULL)
                  OR (status IN ('revoked','superseded')
                    AND revoked_at IS NOT NULL AND revoked_at>=granted_at)),
              CONSTRAINT family_delegate_distinct_participants
                CHECK (recipient_identity_id<>granted_by_identity_id)
            );
            -- Never allow overlapping active delegations for a child or a
            -- recipient; canonical player ownership remains independent.
            CREATE UNIQUE INDEX family_delegate_one_active_player
              ON identity.family_delegated_player_grants(player_id)
              WHERE status='active';
            CREATE UNIQUE INDEX family_delegate_one_active_recipient
              ON identity.family_delegated_player_grants(recipient_identity_id)
              WHERE status='active';
            CREATE INDEX family_delegate_recipient_status
              ON identity.family_delegated_player_grants(recipient_identity_id,status);
            REVOKE ALL ON identity.family_delegated_player_grants FROM PUBLIC;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Preserve auditability: a previously issued delegation cannot vanish
        // through an ordinary rollback, even when already revoked.
        migrationBuilder.Sql("""
            DO $family_grant_guard$
            BEGIN
              IF EXISTS (SELECT 1 FROM identity.family_delegated_player_grants) THEN
                RAISE EXCEPTION 'family_grants_require_forward_recovery';
              END IF;
            END
            $family_grant_guard$;
            DROP TABLE identity.family_delegated_player_grants;
            """);
    }
}
