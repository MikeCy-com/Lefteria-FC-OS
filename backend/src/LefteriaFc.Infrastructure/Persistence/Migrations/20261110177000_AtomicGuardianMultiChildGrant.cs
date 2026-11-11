using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Multiple-child parent sharing is all-or-nothing. Reuses the exact
/// per-child guardian authority checks and current grant lineage.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110177000_AtomicGuardianMultiChildGrant")]
public sealed class AtomicGuardianMultiChildGrant : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION identity.issue_many_guardian_permissions(
        _actor uuid,_phone text,_role text,_expires timestamptz,
        _device uuid,_target text,_items jsonb
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $bulk$
      DECLARE
        v_item jsonb;
        v_player text;
        v_request uuid;
        v_result jsonb;
        v_results jsonb:='[]'::jsonb;
        v_seen text[]:='{}'::text[];
      BEGIN
        IF _role IS DISTINCT FROM 'parent'
          OR _items IS NULL OR jsonb_typeof(_items)<>'array'
          OR jsonb_array_length(_items) NOT BETWEEN 1 AND 100
          OR _target IS NULL OR _target !~ '^[1-9][0-9]{7,14}$'
        THEN RAISE EXCEPTION 'guardian_bulk_invalid' USING ERRCODE='22023'; END IF;

        FOR v_item IN SELECT value FROM jsonb_array_elements(_items) LOOP
          IF jsonb_typeof(v_item)<>'object'
            OR (SELECT count(*) FROM jsonb_object_keys(v_item))<>2
            OR NOT (v_item ?& ARRAY['player_id','request_id'])
            OR jsonb_typeof(v_item->'player_id')<>'string'
            OR jsonb_typeof(v_item->'request_id')<>'string'
            OR (v_item->>'request_id') !~
              '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
          THEN RAISE EXCEPTION 'guardian_bulk_item_invalid'
            USING ERRCODE='22023'; END IF;
          v_player:=v_item->>'player_id';
          IF v_player IS NULL OR v_player !~ '^[A-Za-z0-9_-]{1,128}$'
            OR v_player=ANY(v_seen)
          THEN RAISE EXCEPTION 'guardian_bulk_duplicate_or_invalid_child'
            USING ERRCODE='22023'; END IF;
          v_seen:=array_append(v_seen,v_player);
          v_request:=(v_item->>'request_id')::uuid;
          IF v_request='00000000-0000-0000-0000-000000000000'::uuid
          THEN RAISE EXCEPTION 'guardian_bulk_empty_id'
            USING ERRCODE='22023'; END IF;
          v_result:=identity.issue_guardian_permission(
            _actor,_phone,_role,_expires,_device,v_player,_target,v_request);
          IF v_result->>'status'<>'granted'
            OR v_result->>'player_id'<>v_player
            OR v_result->>'id'<>v_request::text
          THEN RAISE EXCEPTION 'guardian_bulk_unverified_result'
            USING ERRCODE='23514'; END IF;
          v_results:=v_results||jsonb_build_array(v_result);
        END LOOP;
        RETURN jsonb_build_object('items',v_results);
      END $bulk$;
      REVOKE ALL ON FUNCTION identity.issue_many_guardian_permissions(
        uuid,text,text,timestamptz,uuid,text,jsonb) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION identity.issue_many_guardian_permissions(
        uuid,text,text,timestamptz,uuid,text,jsonb);
    """);
}