using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Exact-category create/update only. The admin role does NOT receive INSERT
/// or UPDATE on private POS tables; the audited definer performs both steps in
/// one PostgreSQL statement and rolls back everything on audit failure.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261108110000_PosAdminCategoryWriter")]
public sealed class PosAdminCategoryWriter : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE FUNCTION pos.write_admin_category(
              _digest text, _owner text, _id text, _name text,
              _active boolean, _expected_version bigint
            ) RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $pos_category_write$
            DECLARE
              v_actor uuid;
              v_session uuid;
              v_role text;
              v_version bigint;
              v_action text;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                 OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                 OR _id IS NULL OR _id !~ '^[A-Za-z0-9_-]{1,80}$'
                 OR _name IS NULL OR length(_name) NOT BETWEEN 1 AND 100
                 OR _name<>btrim(_name) OR _name ~ '[[:cntrl:]]'
                 OR _active IS NULL
                 OR _expected_version IS NULL
                 OR _expected_version < -1
                 OR _expected_version > 9007199254740991 THEN
                RAISE EXCEPTION 'pos_category_invalid' USING ERRCODE='22023';
              END IF;

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
                RAISE EXCEPTION 'pos_category_permission_denied' USING ERRCODE='42501';
              END IF;

              IF _expected_version=-1 THEN
                INSERT INTO pos.categories(organization_id,id,name,active)
                VALUES(_owner,_id,_name,_active)
                ON CONFLICT(organization_id,id) DO NOTHING
                RETURNING version INTO v_version;
                v_action := 'admin.pos.category.create';
              ELSE
                UPDATE pos.categories
                SET name=_name, active=_active, version=version+1
                WHERE organization_id=_owner AND id=_id
                  AND version=_expected_version
                  AND version<9007199254740991
                RETURNING version INTO v_version;
                v_action := 'admin.pos.category.update';
              END IF;
              IF v_version IS NULL THEN
                RAISE EXCEPTION 'pos_category_version_conflict' USING ERRCODE='P0001';
              END IF;

              -- This audit INSERT is in the SAME transaction as the catalog
              -- mutation; a failed audit atomically cancels the category edit.
              INSERT INTO audit.security_events(
                id,actor_realm,occurred_at,actor_identity_id,
                device_session_id,action,outcome,organization_id,reason_code
              ) VALUES (
                gen_random_uuid(),'admin',clock_timestamp(),v_actor,
                v_session,v_action,'succeeded',_owner,'pos_catalog'
              );

              RETURN jsonb_build_object(
                'id',_id,'name',_name,'active',_active,'version',v_version
              );
            END
            $pos_category_write$;

            REVOKE ALL ON FUNCTION pos.write_admin_category(
              text,text,text,text,boolean,bigint
            ) FROM PUBLIC;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP FUNCTION pos.write_admin_category(text,text,text,text,boolean,bigint);
            """);
}
