using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Administrator-controlled public listing of verified Academy groups.
/// Existing published profiles remain visible; future changes use an audited,
/// optimistic, manager-only visibility switch. Private roster and guardian
/// records never enter the anonymous public projection.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115120000_AcademyGroupPublicVisibility")]
public sealed class AcademyGroupPublicVisibility : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      ALTER TABLE club.academy_group_profiles
        ADD COLUMN is_public boolean NOT NULL DEFAULT true;

      CREATE OR REPLACE FUNCTION club.read_public_academy_group_directory()
      RETURNS jsonb
      LANGUAGE sql STABLE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp
      AS $public_academy$
        SELECT coalesce(jsonb_agg(
            jsonb_build_object(
              'id',g.id,
              'organization_id','lefteria_fc_academy',
              'name',g.name,
              'age_range',CASE WHEN p.group_id IS NOT NULL THEN nullif(p.age_range,'') ELSE d.data_json->>'age_range' END,
              'city',d.data_json->>'city',
              'location_name',d.data_json->>'location_name',
              'training_schedule',CASE WHEN p.group_id IS NOT NULL THEN nullif(p.training_schedule,'') ELSE d.data_json->>'training_schedule' END,
              'description',CASE WHEN p.group_id IS NOT NULL THEN nullif(p.description,'') ELSE d.data_json->>'description' END,
              'banner_url',CASE WHEN p.group_id IS NOT NULL THEN nullif(p.banner_url,'') ELSE d.data_json->>'banner_url' END,
              'season',CASE WHEN p.group_id IS NOT NULL THEN nullif(p.season,'') ELSE d.data_json->>'season' END,
              'display_order',coalesce(p.display_order,(d.data_json->>'display_order')::integer,0)
            ) ORDER BY coalesce(p.display_order,(d.data_json->>'display_order')::integer,0),g.name,g.id
          ),'[]'::jsonb)
          FROM club.academy_groups g
          JOIN club.academy_group_profiles p
            ON p.organization_id=g.organization_id AND p.group_id=g.id
          LEFT JOIN public_content.documents d ON d.kind='academy_group'
            AND d.organization_id=g.organization_id AND d.id=g.id
          WHERE g.organization_id='lefteria_fc_academy' AND p.is_public
        ;
      $public_academy$;

      CREATE FUNCTION club.read_admin_academy_group_publicity(
        _digest text,_group text
      ) RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER VOLATILE
      SET search_path=pg_catalog,pg_temp AS $read_visibility$
      DECLARE v_actor uuid; v_public boolean;
      BEGIN
        v_actor:=club.authorize_admin_academy_profile(_digest);
        IF _group IS NULL OR _group !~ '^[A-Za-z0-9_-]{1,128}$'
          OR NOT EXISTS(SELECT 1 FROM club.academy_groups
            WHERE organization_id='lefteria_fc_academy' AND id=_group)
        THEN RAISE EXCEPTION 'academy_visibility_group_not_found'
          USING ERRCODE='42501'; END IF;
        SELECT p.is_public INTO v_public
          FROM club.academy_group_profiles p
          WHERE p.organization_id='lefteria_fc_academy' AND p.group_id=_group;
        RETURN jsonb_build_object('is_public',coalesce(v_public,false));
      END $read_visibility$;

      CREATE FUNCTION club.set_admin_academy_group_publicity(
        _digest text,_group text,_expected bigint,_public boolean
      ) RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER VOLATILE
      SET search_path=pg_catalog,pg_temp AS $write_visibility$
      DECLARE v_actor uuid; v_new_revision bigint;
      BEGIN
        v_actor:=club.authorize_admin_academy_profile(_digest);
        IF _group IS NULL OR _group !~ '^[A-Za-z0-9_-]{1,128}$'
          OR _expected IS NULL OR _expected<1 OR _expected>1000000000
          OR _public IS NULL
        THEN RAISE EXCEPTION 'academy_visibility_invalid'
          USING ERRCODE='22023'; END IF;

        UPDATE club.academy_group_profiles p
          SET is_public=_public,
            revision=p.revision+1,
            updated_by=v_actor,
            updated_at=clock_timestamp()
          WHERE p.organization_id='lefteria_fc_academy'
            AND p.group_id=_group AND p.revision=_expected
          RETURNING p.revision INTO v_new_revision;
        IF NOT FOUND THEN
          IF EXISTS(SELECT 1 FROM club.academy_group_profiles
            WHERE organization_id='lefteria_fc_academy' AND group_id=_group)
          THEN RAISE EXCEPTION 'academy_visibility_revision_conflict'
            USING ERRCODE='23505'; END IF;
          RAISE EXCEPTION 'academy_visibility_group_not_found'
            USING ERRCODE='42501';
        END IF;
        INSERT INTO audit.security_events(id,actor_realm,occurred_at,
          actor_identity_id,action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),
          v_actor,'admin.academy.group.public_visibility.update',
          'succeeded','lefteria_fc_academy',
          CASE WHEN _public THEN 'published' ELSE 'unpublished' END);
        RETURN club.read_admin_academy_group_profile(_digest,_group)
          || jsonb_build_object('is_public',_public);
      END $write_visibility$;

      REVOKE ALL ON FUNCTION club.read_admin_academy_group_publicity(text,text)
        FROM PUBLIC;
      REVOKE ALL ON FUNCTION club.set_admin_academy_group_publicity(text,text,bigint,boolean)
        FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- Never roll back an Academy visibility action with retained manager data.
      DO $guard$ BEGIN
        IF EXISTS (SELECT 1 FROM club.academy_group_profiles) THEN
          RAISE EXCEPTION 'academy_group_publication_history_requires_forward_recovery';
        END IF;
      END $guard$;
      DROP FUNCTION club.set_admin_academy_group_publicity(text,text,bigint,boolean);
      DROP FUNCTION club.read_admin_academy_group_publicity(text,text);
      CREATE OR REPLACE FUNCTION club.read_public_academy_group_directory()
      RETURNS jsonb
      LANGUAGE sql STABLE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp
      AS $public_academy$
        SELECT coalesce(jsonb_agg(
            jsonb_build_object(
              'id',g.id,
              'organization_id','lefteria_fc_academy',
              'name',g.name,
              'age_range',CASE WHEN p.group_id IS NOT NULL THEN nullif(p.age_range,'') ELSE d.data_json->>'age_range' END,
              'city',d.data_json->>'city',
              'location_name',d.data_json->>'location_name',
              'training_schedule',CASE WHEN p.group_id IS NOT NULL THEN nullif(p.training_schedule,'') ELSE d.data_json->>'training_schedule' END,
              'description',CASE WHEN p.group_id IS NOT NULL THEN nullif(p.description,'') ELSE d.data_json->>'description' END,
              'banner_url',CASE WHEN p.group_id IS NOT NULL THEN nullif(p.banner_url,'') ELSE d.data_json->>'banner_url' END,
              'season',CASE WHEN p.group_id IS NOT NULL THEN nullif(p.season,'') ELSE d.data_json->>'season' END,
              'display_order',coalesce(p.display_order,(d.data_json->>'display_order')::integer,0)
            ) ORDER BY coalesce(p.display_order,(d.data_json->>'display_order')::integer,0),g.name,g.id
          ),'[]'::jsonb)
          FROM club.academy_groups g
          JOIN club.academy_group_profiles p
            ON p.organization_id=g.organization_id AND p.group_id=g.id
          LEFT JOIN public_content.documents d ON d.kind='academy_group'
            AND d.organization_id=g.organization_id AND d.id=g.id
          WHERE g.organization_id='lefteria_fc_academy'
        ;
      $public_academy$;
      ALTER TABLE club.academy_group_profiles DROP COLUMN is_public;
    """);
}
