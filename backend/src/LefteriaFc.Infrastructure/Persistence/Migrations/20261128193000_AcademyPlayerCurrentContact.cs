using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Operational contact corrections are private and separate from
/// the signed application, identity proof, guardian permissions and billing.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261128193000_AcademyPlayerCurrentContact")]
public sealed class AcademyPlayerCurrentContact : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE club.academy_player_contact_details (
        player_id varchar(128) COLLATE "C" PRIMARY KEY,
        organization_id varchar(32) NOT NULL DEFAULT 'lefteria_fc_academy'
          CHECK(organization_id='lefteria_fc_academy'),
        contact_name varchar(200) NOT NULL,
        relationship varchar(80) NOT NULL,
        phone varchar(32) NOT NULL,
        email varchar(254) NOT NULL,
        revision integer NOT NULL CHECK(revision BETWEEN 1 AND 2000000000),
        updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        updated_by uuid NOT NULL REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        FOREIGN KEY(organization_id,player_id) REFERENCES club.players(organization_id,id) ON DELETE RESTRICT
      );
      ALTER TABLE club.academy_player_contact_details ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON club.academy_player_contact_details FROM PUBLIC;
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
          'current_contact',CASE WHEN c.player_id IS NULL THEN NULL ELSE jsonb_build_object(
            'name',c.contact_name,'relationship',c.relationship,'phone',c.phone,'email',c.email,
            'revision',c.revision,'updated_at',c.updated_at) END,
          'academy_group_ids',coalesce((SELECT jsonb_agg(m.group_id ORDER BY m.group_id)
            FROM (SELECT p.primary_academy_group_id AS group_id WHERE p.primary_academy_group_id IS NOT NULL
                  UNION SELECT g.academy_group_id FROM club.player_academy_groups g
                    WHERE g.organization_id=p.organization_id AND g.player_id=p.id) m),'[]'::jsonb))
        INTO v_details
        FROM club.players p
        LEFT JOIN club.academy_player_private_profiles a ON a.player_id=p.id
        LEFT JOIN club.academy_player_contact_details c ON c.player_id=p.id AND c.organization_id=p.organization_id
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
      CREATE FUNCTION club.write_admin_academy_player_contact(
        _digest text,_player text,_expected integer,_name text,_relationship text,_phone text,_email text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $contact$
      DECLARE v_actor uuid;v_session uuid;v_player_name text;v_next integer;
      BEGIN
        IF _expected IS NULL OR _expected NOT BETWEEN 0 AND 1999999999
          OR _name IS NULL OR length(_name) NOT BETWEEN 1 AND 200
          OR _relationship IS NULL OR length(_relationship) NOT BETWEEN 1 AND 80
          OR _phone IS NULL OR length(_phone) NOT BETWEEN 7 AND 32
          OR _email IS NULL OR length(_email) NOT BETWEEN 3 AND 254
          OR _name<>btrim(_name) OR _relationship<>btrim(_relationship)
          OR _phone<>btrim(_phone) OR _email<>btrim(_email)
          OR _name ~ '[[:cntrl:]]' OR _relationship ~ '[[:cntrl:]]'
          OR _phone !~ '^[+]?[0-9 ()-]{7,32}$'
          OR _email !~ '^[^[:space:]@[:cntrl:]]+@[^[:space:]@[:cntrl:]]+[.][^[:space:]@[:cntrl:]]+$'
        THEN RAISE EXCEPTION 'player_contact_invalid' USING ERRCODE='22023'; END IF;
        SELECT actor_id,session_id,player_name INTO STRICT v_actor,v_session,v_player_name
          FROM club.authorize_academy_private_profile(_digest,_player,true);
        IF _expected=0 THEN
          INSERT INTO club.academy_player_contact_details(
            player_id,contact_name,relationship,phone,email,revision,updated_by)
          VALUES(_player,_name,_relationship,_phone,_email,1,v_actor)
          ON CONFLICT(player_id) DO NOTHING RETURNING revision INTO v_next;
        ELSE
          UPDATE club.academy_player_contact_details SET contact_name=_name,
            relationship=_relationship,phone=_phone,email=_email,revision=revision+1,
            updated_at=clock_timestamp(),updated_by=v_actor
          WHERE organization_id='lefteria_fc_academy' AND player_id=_player AND revision=_expected
          RETURNING revision INTO v_next;
        END IF;
        IF v_next IS NULL THEN
          RAISE EXCEPTION 'player_contact_revision_conflict' USING ERRCODE='23505';
        END IF;
        INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
          'admin.academy.player.contact.update','succeeded','lefteria_fc_academy','operational_contact_only');
        RETURN jsonb_build_object('organization_id','lefteria_fc_academy',
          'player_id',_player,'revision',v_next,'updated',true);
      END $contact$;
      REVOKE ALL ON FUNCTION club.write_admin_academy_player_contact(text,text,integer,text,text,text,text) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$ BEGIN
        IF EXISTS(SELECT 1 FROM club.academy_player_contact_details) THEN
          RAISE EXCEPTION 'player_contact_requires_forward_recovery';
        END IF;
      END $guard$;
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
      DROP FUNCTION club.write_admin_academy_player_contact(text,text,integer,text,text,text,text);
      DROP TABLE club.academy_player_contact_details;
      """);
}
