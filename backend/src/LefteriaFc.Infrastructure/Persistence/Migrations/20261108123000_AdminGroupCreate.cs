using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Creates real canonical FC teams or Academy groups. No player
/// assignment, chat, announcement or push delivery is created implicitly.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261108123000_AdminGroupCreate")]
public sealed class AdminGroupCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE FUNCTION club.create_admin_group(
              _digest text,_owner text,_id text,_name text
            ) RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $admin_group_create$
            DECLARE
              v_actor uuid;
              v_session uuid;
              v_role text;
              v_count integer;
              v_kind text;
              v_action text;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                OR _id IS NULL OR _id !~ '^grp_[0-9a-f]{32}$'
                OR _name IS NULL OR length(_name) NOT BETWEEN 1 AND 120
                OR _name<>btrim(_name) OR _name ~ '[[:cntrl:]]' THEN
                RAISE EXCEPTION 'admin_group_invalid' USING ERRCODE='22023';
              END IF;

              -- The auth lock holds session, admin account and exact owner
              -- grant against concurrent revocation throughout this write.
              PERFORM identity.lock_admin_club_authority(_digest,_owner);
              SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
              FROM identity.admin_sessions s
              JOIN identity.admin_accounts a ON a.id=s.admin_account_id
              JOIN identity.admin_organization_grants g
                ON g.admin_account_id=a.id AND g.organization_id=_owner
              WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                AND s.expires_at>clock_timestamp()
                AND a.is_active AND NOT a.invite_pending
                AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL;
              IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
                RAISE EXCEPTION 'admin_group_permission_denied' USING ERRCODE='42501';
              END IF;
              IF NOT EXISTS (SELECT 1 FROM club.roster_import_state WHERE id='initial') THEN
                RAISE EXCEPTION 'admin_group_roster_not_ready' USING ERRCODE='55000';
              END IF;

              -- Scoped advisory lock ensures the 150-group selector budget
              -- also holds under concurrent administrator creates.
              PERFORM pg_advisory_xact_lock(
                hashtextextended('lefteria:roster:groups:'||_owner,0)
              );
              IF _owner='lefteria_fc' THEN
                SELECT count(*) INTO v_count FROM club.teams WHERE organization_id=_owner;
                IF v_count>=150 THEN
                  RAISE EXCEPTION 'admin_group_quota' USING ERRCODE='54000';
                END IF;
                INSERT INTO club.teams(id,organization_id,name)
                  VALUES(_id,_owner,_name) ON CONFLICT(id) DO NOTHING;
                GET DIAGNOSTICS v_count=ROW_COUNT;
                v_kind := 'team';
                v_action := 'admin.roster.team.create';
              ELSE
                SELECT count(*) INTO v_count FROM club.academy_groups WHERE organization_id=_owner;
                IF v_count>=150 THEN
                  RAISE EXCEPTION 'admin_group_quota' USING ERRCODE='54000';
                END IF;
                INSERT INTO club.academy_groups(id,organization_id,name)
                  VALUES(_id,_owner,_name) ON CONFLICT(id) DO NOTHING;
                GET DIAGNOSTICS v_count=ROW_COUNT;
                v_kind := 'academy_group';
                v_action := 'admin.roster.academy_group.create';
              END IF;
              IF v_count<>1 THEN
                RAISE EXCEPTION 'admin_group_id_conflict' USING ERRCODE='23505';
              END IF;

              -- If the audit insert fails, the group creation is reverted in
              -- the same PostgreSQL transaction. There is no unlogged group.
              INSERT INTO audit.security_events(
                id,actor_realm,occurred_at,actor_identity_id,
                device_session_id,action,outcome,organization_id,reason_code
              ) VALUES (
                gen_random_uuid(),'admin',clock_timestamp(),v_actor,
                v_session,v_action,'succeeded',_owner,'roster_groups'
              );
              RETURN jsonb_build_object(
                'id',_id,'name',_name,'kind',v_kind,'active_players',0
              );
            END
            $admin_group_create$;

            REVOKE ALL ON FUNCTION club.create_admin_group(text,text,text,text) FROM PUBLIC;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP FUNCTION club.create_admin_group(text,text,text,text);
            """);
}
