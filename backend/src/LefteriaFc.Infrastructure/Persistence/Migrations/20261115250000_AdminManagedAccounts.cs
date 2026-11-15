using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Super-admin-only creation of explicitly scoped web-admin credentials.
/// The admin-auth runtime receives EXECUTE only; never raw account write grants.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115250000_AdminManagedAccounts")]
public sealed class AdminManagedAccounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION identity.create_managed_admin(
          _digest text,_id uuid,_username text,_name text,
          _role text,_hash text,_owners text[])
        RETURNS uuid LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $managed$
        DECLARE v_actor uuid; v_session uuid; v_role text; v_owner text;
        BEGIN
          IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
            OR _id IS NULL OR _username IS NULL
            OR _username !~ '^[a-z0-9][a-z0-9._@+-]{2,79}$'
            OR _name IS NULL OR length(btrim(_name)) NOT BETWEEN 1 AND 200
            OR _name <> btrim(_name) OR _name ~ '[[:cntrl:]]'
            OR _role NOT IN ('manager','editor','marketing','coach','matchday_editor','accountant')
            OR _hash IS NULL OR _hash !~ '^[$]2[aby][$]12[$][./A-Za-z0-9]{53}$'
            OR _owners IS NULL OR cardinality(_owners) NOT BETWEEN 1 AND 2
            OR (SELECT count(DISTINCT x) FROM unnest(_owners) AS x) <> cardinality(_owners)
            OR EXISTS(SELECT 1 FROM unnest(_owners) AS x
              WHERE x IS NULL OR x NOT IN ('lefteria_fc','lefteria_fc_academy'))
          THEN RAISE EXCEPTION 'managed_admin_invalid' USING ERRCODE='22023'; END IF;

          -- Lock in the order required by club authority: actor account,
          -- session, grant. A subsequent revocation cannot race creation.
          FOREACH v_owner IN ARRAY (SELECT array_agg(x ORDER BY x) FROM unnest(_owners) AS x)
          LOOP
            PERFORM identity.lock_admin_club_authority(_digest,v_owner);
          END LOOP;
          SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
            FROM identity.admin_sessions s
            JOIN identity.admin_accounts a ON a.id=s.admin_account_id
            WHERE s.token_digest=_digest AND s.revoked_at IS NULL
              AND s.expires_at>clock_timestamp() AND a.is_active
              AND NOT a.invite_pending AND NOT a.otp2fa_enabled
              AND a.password_hash IS NOT NULL
              AND a.role='super_admin';
          IF v_actor IS NULL OR v_role <> 'super_admin' THEN
            RAISE EXCEPTION 'managed_admin_super_admin_required' USING ERRCODE='42501';
          END IF;

          INSERT INTO identity.admin_accounts
            (id,username,display_name,role,is_active,password_hash,invite_pending,otp2fa_enabled)
          VALUES (_id,_username,_name,_role,true,_hash,false,false);
          INSERT INTO identity.admin_organization_grants (admin_account_id,organization_id)
            SELECT _id,x FROM unnest(_owners) AS x;

          INSERT INTO audit.security_events
            (id,occurred_at,actor_identity_id,device_session_id,action,outcome,
             subject_digest,reason_code,actor_realm)
          VALUES (gen_random_uuid(),statement_timestamp(),v_actor,v_session,
            'admin.account.create','succeeded',NULL,NULL,'admin');
          RETURN _id;
        END $managed$;
        REVOKE ALL ON FUNCTION identity.create_managed_admin(text,uuid,text,text,text,text,text[])
          FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION identity.create_managed_admin(text,uuid,text,text,text,text,text[]);
        """);
}
