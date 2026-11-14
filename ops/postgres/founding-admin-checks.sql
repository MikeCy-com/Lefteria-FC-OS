\set ON_ERROR_STOP on
BEGIN;
DO $check$
DECLARE
    owner_id constant uuid := '60000000-0000-4000-8000-000000000001';
    other_id constant uuid := '60000000-0000-4000-8000-000000000002';
    -- A syntactically valid fixture hash. The HTTP suite verifies bcrypt login.
    credential constant text := '$2b$12$' || repeat('A', 53);
BEGIN
    INSERT INTO identity.admin_accounts (id, username, display_name, role, is_active, invite_pending, otp2fa_enabled)
    VALUES (other_id, 'fixture-existing-admin', 'Fixture Admin', 'manager', true, false, false);
    IF identity.founding_admin_setup_available() OR
       identity.create_founding_admin(owner_id, 'fixture-owner', 'Fixture Owner', credential) THEN
        RAISE EXCEPTION 'An existing install accepted owner setup';
    END IF;
    DELETE FROM identity.admin_accounts WHERE id = other_id;

    IF NOT identity.founding_admin_setup_available() OR
       NOT identity.create_founding_admin(owner_id, 'fixture-owner', 'Fixture Owner', credential) THEN
        RAISE EXCEPTION 'Empty installation could not create its owner';
    END IF;
    IF identity.founding_admin_setup_available() OR
       identity.create_founding_admin(other_id, 'fixture-other', 'Fixture Other', credential) THEN
        RAISE EXCEPTION 'Owner setup was reusable';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM identity.admin_accounts WHERE id=owner_id AND role='super_admin' AND is_active)
       OR (SELECT count(*) FROM identity.admin_organization_grants WHERE admin_account_id=owner_id) <> 2 THEN
        RAISE EXCEPTION 'Owner authority was incomplete';
    END IF;

    BEGIN
        UPDATE identity.admin_accounts SET role='manager' WHERE id=owner_id;
        RAISE EXCEPTION 'Owner demotion succeeded';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;
    BEGIN
        UPDATE identity.admin_accounts SET is_active=false WHERE id=owner_id;
        RAISE EXCEPTION 'Owner deactivation succeeded';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;
    BEGIN
        DELETE FROM identity.admin_organization_grants WHERE admin_account_id=owner_id;
        RAISE EXCEPTION 'Owner grants were removed';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;
    BEGIN
        DELETE FROM identity.admin_accounts WHERE id=owner_id;
        RAISE EXCEPTION 'Owner deletion succeeded';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;

    UPDATE identity.admin_accounts SET password_hash='$2b$12$' || repeat('B',53) WHERE id=owner_id;
END $check$;
ROLLBACK;
