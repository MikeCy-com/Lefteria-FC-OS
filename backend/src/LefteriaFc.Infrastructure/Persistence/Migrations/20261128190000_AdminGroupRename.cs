using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LefteriaDbContext))]
[Migration("20261128190000_AdminGroupRename")]
public sealed class AdminGroupRename : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION club.rename_admin_group(
        _digest text,_owner text,_group text,_expected text,_name text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $rename$
      DECLARE
        v_actor uuid; v_previous text; v_location uuid;
      BEGIN
        IF _owner IS NULL OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _group IS NULL OR _group !~ '^[A-Za-z0-9_-]{1,128}$'
          OR _expected IS NULL OR length(_expected) NOT BETWEEN 1 AND 120
          OR _name IS NULL OR length(_name) NOT BETWEEN 1 AND 120
          OR _name<>btrim(_name) OR _name ~ '[[:cntrl:]]'
        THEN RAISE EXCEPTION 'group_rename_invalid' USING ERRCODE='22023'; END IF;
        -- Share the creation lock BEFORE the group row lock, so concurrent
        -- creates/renames cannot bypass scoped duplicate-name validation.
        PERFORM pg_advisory_xact_lock(hashtextextended('lefteria:roster:groups:'||_owner,0));
        v_actor:=club.require_admin_group_profile_scope(_digest,_owner,_group,true);
        IF _owner='lefteria_fc' THEN
          SELECT name INTO STRICT v_previous FROM club.teams
            WHERE organization_id=_owner AND id=_group FOR UPDATE;
        ELSE
          SELECT name INTO STRICT v_previous FROM club.academy_groups
            WHERE organization_id=_owner AND id=_group FOR UPDATE;
        END IF;
        IF v_previous<>_expected THEN
          RAISE EXCEPTION 'group_rename_conflict' USING ERRCODE='23505';
        END IF;
        IF v_previous<>_name THEN
          IF _owner='lefteria_fc' THEN
            IF EXISTS(SELECT 1 FROM club.teams t WHERE t.organization_id=_owner
              AND t.id<>_group AND lower(t.name)=lower(_name)) THEN
              RAISE EXCEPTION 'group_rename_duplicate' USING ERRCODE='23505';
            END IF;
            UPDATE club.teams SET name=_name WHERE organization_id=_owner AND id=_group;
          ELSE
            SELECT location_id INTO v_location FROM club.academy_group_location_assignments
              WHERE organization_id=_owner AND group_id=_group;
            IF EXISTS(SELECT 1 FROM club.academy_groups g
              LEFT JOIN club.academy_group_location_assignments a
                ON a.organization_id=g.organization_id AND a.group_id=g.id
              WHERE g.organization_id=_owner AND g.id<>_group
                AND a.location_id IS NOT DISTINCT FROM v_location
                AND lower(g.name)=lower(_name)) THEN
              RAISE EXCEPTION 'group_rename_duplicate' USING ERRCODE='23505';
            END IF;
            UPDATE club.academy_groups SET name=_name WHERE organization_id=_owner AND id=_group;
          END IF;
          INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,
            action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
            'admin.roster.group.rename','succeeded',_owner,'group_name');
        END IF;
        RETURN jsonb_build_object('organization_id',_owner,'id',_group,'name',_name);
      END $rename$;
      REVOKE ALL ON FUNCTION club.rename_admin_group(text,text,text,text,text) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION club.rename_admin_group(text,text,text,text,text);
      """);
}
