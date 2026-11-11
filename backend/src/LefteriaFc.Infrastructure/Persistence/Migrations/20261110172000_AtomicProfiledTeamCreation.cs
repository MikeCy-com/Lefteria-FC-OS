using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// One atomic, request-idempotent creation of a canonical team/group plus
/// original-source profile. No membership/coach link is implicitly created.
/// Both original guarded functions run inside the same transaction.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110172000_AtomicProfiledTeamCreation")]
public sealed class AtomicProfiledTeamCreation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE club.admin_group_create_requests (
        organization_id varchar(32) NOT NULL
          CHECK(organization_id IN ('lefteria_fc','lefteria_fc_academy')),
        actor_id uuid NOT NULL REFERENCES identity.admin_accounts(id),
        request_id uuid NOT NULL CHECK(request_id<>'00000000-0000-0000-0000-000000000000'::uuid),
        group_id varchar(128) COLLATE "C" NOT NULL,
        input_sha256 char(64) COLLATE "C" NOT NULL
          CHECK(input_sha256 ~ '^[0-9a-f]{64}$'),
        created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        PRIMARY KEY(organization_id,actor_id,request_id),
        UNIQUE(organization_id,group_id)
      );
      ALTER TABLE club.admin_group_create_requests ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON club.admin_group_create_requests FROM PUBLIC;

      CREATE FUNCTION club.create_admin_group_with_profile(
        _digest text,_owner text,_id text,_name text,
        _request uuid,_profile jsonb
      ) RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER VOLATILE
      SET search_path=pg_catalog,pg_temp AS $create$
      DECLARE
        v_actor uuid; v_hash text; v_existing record;
        v_new integer; v_saved jsonb; v_profile jsonb;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _id IS NULL OR _id !~ '^grp_[0-9a-f]{32}$'
          OR _name IS NULL OR length(_name) NOT BETWEEN 1 AND 120
          OR _name<>btrim(_name) OR _name ~ '[[:cntrl:]]'
          OR _request IS NULL OR
            _request='00000000-0000-0000-0000-000000000000'::uuid
          OR _profile IS NULL OR jsonb_typeof(_profile)<>'object'
        THEN RAISE EXCEPTION 'profiled_group_create_invalid'
          USING ERRCODE='22023'; END IF;

        -- Use the existing manager+organization grant lock before reading or
        -- locking a client-request row. Revocation is checked on every replay.
        IF _owner='lefteria_fc' THEN
          v_actor:=club.authorize_admin_first_team_profile(_digest);
        ELSE
          v_actor:=club.authorize_admin_academy_profile(_digest);
        END IF;

        v_hash:=encode(sha256(convert_to(
          jsonb_build_object('name',_name,'profile',_profile)::text,'UTF8')),'hex');
        INSERT INTO club.admin_group_create_requests(
          organization_id,actor_id,request_id,group_id,input_sha256
        ) VALUES(_owner,v_actor,_request,_id,v_hash)
        ON CONFLICT(organization_id,actor_id,request_id) DO NOTHING;
        GET DIAGNOSTICS v_new=ROW_COUNT;
        IF v_new=0 THEN
          SELECT * INTO v_existing FROM club.admin_group_create_requests
            WHERE organization_id=_owner AND actor_id=v_actor
              AND request_id=_request FOR UPDATE;
          IF NOT FOUND OR v_existing.input_sha256<>v_hash THEN
            RAISE EXCEPTION 'profiled_group_replay_mismatch'
              USING ERRCODE='23505'; END IF;
          -- Cross-check the authoritative original group and stored profile,
          -- never trust a replayed group ID supplied by the client.
          IF _owner='lefteria_fc' THEN
            SELECT jsonb_build_object('id',t.id,'name',t.name,
              'kind','team','active_players',0)
              INTO v_saved FROM club.teams t
              WHERE t.organization_id=_owner AND t.id=v_existing.group_id;
            v_profile:=club.read_admin_first_team_profile(
              _digest,v_existing.group_id);
          ELSE
            SELECT jsonb_build_object('id',g.id,'name',g.name,
              'kind','academy_group','active_players',0)
              INTO v_saved FROM club.academy_groups g
              WHERE g.organization_id=_owner AND g.id=v_existing.group_id;
            v_profile:=club.read_admin_academy_group_profile(
              _digest,v_existing.group_id);
          END IF;
          IF v_saved IS NULL OR v_saved->>'name'<>_name OR
            (v_profile->>'revision')::bigint<1 THEN
            RAISE EXCEPTION 'profiled_group_replay_inconsistent'
              USING ERRCODE='23514'; END IF;
          RETURN v_saved || jsonb_build_object(
            'profile',v_profile,'replay',true);
        END IF;

        -- All operations are one PostgreSQL transaction. If any profile
        -- validation, capacity, audit or canonical group write fails, no
        -- group, profile, or idempotency request row is committed.
        v_saved:=club.create_admin_group(_digest,_owner,_id,_name);
        IF _owner='lefteria_fc' THEN
          v_profile:=club.write_admin_first_team_profile(
            _digest,_id,0,_profile);
        ELSE
          v_profile:=club.write_admin_academy_group_profile(
            _digest,_id,0,_profile);
        END IF;
        RETURN v_saved || jsonb_build_object(
          'profile',v_profile,'replay',false);
      END $create$;
      REVOKE ALL ON FUNCTION club.create_admin_group_with_profile(
        text,text,text,text,uuid,jsonb) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$ BEGIN
        IF EXISTS(SELECT 1 FROM club.admin_group_create_requests)
        THEN RAISE EXCEPTION 'profiled_group_history_requires_forward_recovery'; END IF;
      END $guard$;
      DROP FUNCTION club.create_admin_group_with_profile(text,text,text,text,uuid,jsonb);
      DROP TABLE club.admin_group_create_requests;
    """);
}
