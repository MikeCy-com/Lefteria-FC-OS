using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// A placement reserves a post-enrollment workflow record in the SAME
/// transaction as approval. It is not a Viber request or an invoice.
/// No sender, grant, auto-charge or external side effect is enabled here.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261122160000_AcademyPlacementSafeWorkflowOutbox")]
public sealed class AcademyPlacementSafeWorkflowOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE academy_intake.placement_workflow_outbox (
        registration_id uuid NOT NULL REFERENCES academy_intake.registration_placements(registration_id)
          ON DELETE RESTRICT,
        organization_id varchar(32) NOT NULL
          CHECK (organization_id='lefteria_fc_academy'),
        player_id varchar(128) COLLATE "C" NOT NULL,
        group_id varchar(128) COLLATE "C" NOT NULL,
        stage varchar(32) NOT NULL CHECK (stage='welcome'),
        delivery_state varchar(32) NOT NULL DEFAULT 'blocked_verification'
          CHECK (delivery_state='blocked_verification'),
        consent_verified boolean NOT NULL DEFAULT false CHECK (NOT consent_verified),
        provider_enabled boolean NOT NULL DEFAULT false CHECK (NOT provider_enabled),
        created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        PRIMARY KEY (registration_id,stage),
        FOREIGN KEY (organization_id,player_id)
          REFERENCES club.players(organization_id,id) ON DELETE RESTRICT
      );
      REVOKE ALL ON academy_intake.placement_workflow_outbox FROM PUBLIC;
      ALTER TABLE academy_intake.placement_workflow_outbox ENABLE ROW LEVEL SECURITY;

      CREATE FUNCTION academy_intake.reserve_placement_welcome()
      RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $welcome$
      BEGIN
        -- No contact details or sensitive KOA/medical evidence copied here.
        -- This event can never directly send: explicit verified guardian
        -- consent, policy, Viber provider and KOA state checks are absent.
        IF NEW.organization_id<>'lefteria_fc_academy' THEN
          RAISE EXCEPTION 'invalid_academy_placement_event' USING ERRCODE='23514';
        END IF;
        INSERT INTO academy_intake.placement_workflow_outbox
          (registration_id,organization_id,player_id,group_id,stage)
        VALUES (NEW.registration_id,NEW.organization_id,
                NEW.player_id,NEW.group_id,'welcome')
        ON CONFLICT (registration_id,stage) DO NOTHING;
        RETURN NEW;
      END $welcome$;
      REVOKE ALL ON FUNCTION academy_intake.reserve_placement_welcome() FROM PUBLIC;
      CREATE TRIGGER reserve_placement_welcome_on_enroll
        AFTER INSERT ON academy_intake.registration_placements
        FOR EACH ROW EXECUTE FUNCTION academy_intake.reserve_placement_welcome();
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $check$
      BEGIN
        IF EXISTS (SELECT 1 FROM academy_intake.placement_workflow_outbox)
        THEN RAISE EXCEPTION 'cannot_remove_placement_workflow_evidence'
          USING ERRCODE='23505';
        END IF;
      END $check$;
      DROP TRIGGER reserve_placement_welcome_on_enroll
        ON academy_intake.registration_placements;
      DROP FUNCTION academy_intake.reserve_placement_welcome();
      DROP TABLE academy_intake.placement_workflow_outbox;
    """);
}
