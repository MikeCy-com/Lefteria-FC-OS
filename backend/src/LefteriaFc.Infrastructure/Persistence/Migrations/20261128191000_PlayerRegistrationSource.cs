using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Read the original approved application through the exact player
/// authorization boundary. Submitted guardian contact data is evidence, not
/// proof of current guardian app access. No sealed evidence is decrypted.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261128191000_PlayerRegistrationSource")]
public sealed class PlayerRegistrationSource : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE OR REPLACE FUNCTION club.read_admin_academy_player_profile(
        _digest text,_player text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $read_profile$
      DECLARE v_actor uuid;v_session uuid;v_name text;v_details jsonb;
      BEGIN
        SELECT actor_id,session_id,player_name INTO STRICT v_actor,v_session,v_name
          FROM club.authorize_academy_private_profile(_digest,_player,false);
        SELECT jsonb_build_object(
          'organization_id','lefteria_fc_academy','player_id',p.id,
          'official_name',v_name,'official_number',p.number,
          'official_position',p.position,
          'biography',coalesce(a.biography,''),
          'preferred_position',coalesce(a.preferred_position,''),
          'preferred_number',a.preferred_number,
          'has_portrait',a.portrait_jpeg IS NOT NULL,
          'revision',coalesce(a.revision,0),
          'updated_at',a.updated_at,
          'registration',r.details,
          'academy_group_ids',coalesce((SELECT jsonb_agg(m.group_id ORDER BY m.group_id)
            FROM (SELECT p.primary_academy_group_id AS group_id WHERE p.primary_academy_group_id IS NOT NULL
                  UNION SELECT g.academy_group_id FROM club.player_academy_groups g
                    WHERE g.organization_id=p.organization_id AND g.player_id=p.id) m),'[]'::jsonb))
        INTO v_details
        FROM club.players p
        LEFT JOIN club.academy_player_private_profiles a ON a.player_id=p.id
        LEFT JOIN LATERAL (
          SELECT jsonb_build_object(
            'id',i.id,'applicant_first_name',i.applicant_first_name,
            'applicant_last_name',i.applicant_last_name,
            'applicant_birth_date',i.applicant_birth_date,
            'guardian_name',i.guardian_name,
            'guardian_relationship',i.guardian_relationship,
            'guardian_phone',i.guardian_phone,
            'guardian_email',i.guardian_email,
            'submitted_at',i.submitted_at,'approved_at',i.decided_at
          ) AS details
          FROM academy_intake.registration_placements placement
          JOIN academy_intake.registrations i
            ON i.id=placement.registration_id
              AND i.organization_id=placement.organization_id
              AND i.onboarded_player_id=placement.player_id
              AND i.status='approved'
          WHERE placement.organization_id=p.organization_id
            AND placement.player_id=p.id
        ) r ON true
        WHERE p.id=_player AND p.organization_id='lefteria_fc_academy';
        RETURN v_details;
      END $read_profile$;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE OR REPLACE FUNCTION club.read_admin_academy_player_profile(
        _digest text,_player text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $read_profile$
      DECLARE v_actor uuid;v_session uuid;v_name text;v_details jsonb;
      BEGIN
        SELECT actor_id,session_id,player_name INTO STRICT v_actor,v_session,v_name
          FROM club.authorize_academy_private_profile(_digest,_player,false);
        SELECT jsonb_build_object(
          'organization_id','lefteria_fc_academy','player_id',p.id,
          'official_name',v_name,'official_number',p.number,
          'official_position',p.position,
          'biography',coalesce(a.biography,''),
          'preferred_position',coalesce(a.preferred_position,''),
          'preferred_number',a.preferred_number,
          'has_portrait',a.portrait_jpeg IS NOT NULL,
          'revision',coalesce(a.revision,0),
          'updated_at',a.updated_at)
        INTO v_details
        FROM club.players p
        LEFT JOIN club.academy_player_private_profiles a ON a.player_id=p.id
        WHERE p.id=_player AND p.organization_id='lefteria_fc_academy';
        RETURN v_details;
      END $read_profile$;
      """);
}
