using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LefteriaDbContext))]
[Migration("20261114160000_FoundingWebAdmin")]
public sealed class FoundingWebAdmin : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE identity.founding_admin (
            singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
            admin_account_id uuid NOT NULL UNIQUE REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT
        );
        REVOKE ALL ON identity.founding_admin FROM PUBLIC;

        CREATE FUNCTION identity.founding_admin_setup_available()
        RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER
        SET search_path = pg_catalog, pg_temp
        AS $setup$
            SELECT NOT EXISTS (SELECT 1 FROM identity.admin_accounts)
                AND NOT EXISTS (SELECT 1 FROM identity.founding_admin)
        $setup$;

        CREATE FUNCTION identity.create_founding_admin(account_id uuid, login_name text, owner_name text, credential text)
        RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path = pg_catalog, pg_temp AS $setup$
        BEGIN
            -- Serialize with all other account inserts, including legacy imports.
            LOCK TABLE identity.admin_accounts IN SHARE ROW EXCLUSIVE MODE;
            IF NOT identity.founding_admin_setup_available() THEN RETURN false; END IF;
            IF account_id IS NULL OR login_name IS NULL OR login_name !~ '^[a-z0-9][a-z0-9._@+-]{2,79}$'
                OR owner_name IS NULL OR length(btrim(owner_name)) NOT BETWEEN 1 AND 200
                OR credential IS NULL OR credential !~ '^[$]2[aby][$]12[$][./A-Za-z0-9]{53}$'
            THEN RAISE EXCEPTION 'invalid_owner_setup' USING ERRCODE = '22023'; END IF;

            INSERT INTO identity.admin_accounts
                (id, username, display_name, role, is_active, password_hash, invite_pending, otp2fa_enabled)
            VALUES (account_id, login_name, btrim(owner_name), 'super_admin', true, credential, false, false);
            INSERT INTO identity.admin_organization_grants (admin_account_id, organization_id)
            VALUES (account_id, 'lefteria_fc'), (account_id, 'lefteria_fc_academy');
            INSERT INTO identity.founding_admin (admin_account_id) VALUES (account_id);
            RETURN true;
        END $setup$;

        CREATE FUNCTION identity.protect_founding_admin()
        RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER
        SET search_path = pg_catalog, pg_temp AS $guard$
        BEGIN
            IF EXISTS (SELECT 1 FROM identity.founding_admin WHERE admin_account_id = OLD.id)
                AND (TG_OP = 'DELETE' OR NEW.id IS DISTINCT FROM OLD.id OR NEW.role <> 'super_admin'
                    OR NOT NEW.is_active OR NEW.invite_pending OR NEW.otp2fa_enabled OR NEW.password_hash IS NULL)
            THEN RAISE EXCEPTION 'founding_admin_protected' USING ERRCODE = '42501'; END IF;
            IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
            RETURN NEW;
        END $guard$;

        CREATE FUNCTION identity.protect_founding_admin_scope()
        RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER
        SET search_path = pg_catalog, pg_temp AS $guard$
        BEGIN
            IF OLD.organization_id IN ('lefteria_fc', 'lefteria_fc_academy')
                AND EXISTS (SELECT 1 FROM identity.founding_admin WHERE admin_account_id = OLD.admin_account_id)
                AND (TG_OP = 'DELETE' OR NEW.admin_account_id IS DISTINCT FROM OLD.admin_account_id
                    OR NEW.organization_id IS DISTINCT FROM OLD.organization_id)
            THEN RAISE EXCEPTION 'founding_admin_scope_protected' USING ERRCODE = '42501'; END IF;
            IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
            RETURN NEW;
        END $guard$;

        CREATE TRIGGER founding_admin_protection BEFORE UPDATE OR DELETE ON identity.admin_accounts
            FOR EACH ROW EXECUTE FUNCTION identity.protect_founding_admin();
        CREATE TRIGGER founding_admin_scope_protection BEFORE UPDATE OR DELETE ON identity.admin_organization_grants
            FOR EACH ROW EXECUTE FUNCTION identity.protect_founding_admin_scope();

        REVOKE ALL ON FUNCTION identity.founding_admin_setup_available() FROM PUBLIC;
        REVOKE ALL ON FUNCTION identity.create_founding_admin(uuid,text,text,text) FROM PUBLIC;
        REVOKE ALL ON FUNCTION identity.protect_founding_admin() FROM PUBLIC;
        REVOKE ALL ON FUNCTION identity.protect_founding_admin_scope() FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE identity.admin_accounts IN SHARE ROW EXCLUSIVE MODE;
        DO $guard$ BEGIN
            IF EXISTS (SELECT 1 FROM identity.founding_admin) THEN
                RAISE EXCEPTION 'Owner setup already completed. Use a forward migration to retain owner protection.';
            END IF;
        END $guard$;
        DROP TRIGGER founding_admin_scope_protection ON identity.admin_organization_grants;
        DROP TRIGGER founding_admin_protection ON identity.admin_accounts;
        DROP FUNCTION identity.protect_founding_admin_scope();
        DROP FUNCTION identity.protect_founding_admin();
        DROP FUNCTION identity.create_founding_admin(uuid,text,text,text);
        DROP FUNCTION identity.founding_admin_setup_available();
        DROP TABLE identity.founding_admin;
        """);
}
