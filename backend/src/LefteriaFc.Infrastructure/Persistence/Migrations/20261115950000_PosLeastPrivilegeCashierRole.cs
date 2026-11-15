using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Cashier is a narrowly scoped credential with one organization and one
/// active terminal. Existing explicit SQL grants and manager review gates
/// continue to control protected checkout and refunds.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115950000_PosLeastPrivilegeCashierRole")]
public sealed class PosLeastPrivilegeCashierRole : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      ALTER TABLE identity.admin_accounts DROP CONSTRAINT ck_admin_account_role;
      ALTER TABLE identity.admin_accounts ADD CONSTRAINT ck_admin_account_role
        CHECK(role IN ('super_admin','manager','editor','marketing','coach','matchday_editor','accountant','cashier'));
      
      CREATE OR REPLACE FUNCTION identity.create_managed_admin(
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
                  OR _role NOT IN ('manager','editor','marketing','coach','matchday_editor','accountant','cashier')
                  OR _hash IS NULL OR _hash !~ '^[$]2[aby][$]12[$][./A-Za-z0-9]{53}$'
                  OR _owners IS NULL OR cardinality(_owners) NOT BETWEEN 1 AND 2
                  OR (_role='cashier' AND cardinality(_owners)<>1)
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
      
      CREATE OR REPLACE FUNCTION identity.set_managed_admin_status(
                _digest text,_subject uuid,_expected_role text,
                _expected_active boolean,_active boolean
              ) RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
              SET search_path=pg_catalog,pg_temp AS $managed_status$
              DECLARE
                v_actor uuid;v_session uuid;v_actor_role text;
                v_subject_role text;v_subject_active boolean;
                v_invited boolean;v_has_credential boolean;
                v_owner text;v_owner_count integer:=0;
              BEGIN
                IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                  OR _subject IS NULL OR _expected_role IS NULL
                  OR _expected_role NOT IN ('manager','editor','marketing','coach','matchday_editor','accountant','cashier')
                  OR _expected_active IS NULL OR _active IS NULL
                  OR _expected_active=_active
                THEN RAISE EXCEPTION 'managed_admin_status_invalid' USING ERRCODE='22023'; END IF;
      
                SELECT s.admin_account_id INTO v_actor
                  FROM identity.admin_sessions s WHERE s.token_digest=_digest;
                IF v_actor IS NULL OR v_actor=_subject THEN
                  RAISE EXCEPTION 'managed_admin_status_denied' USING ERRCODE='42501';
                END IF;
                -- Lock actor account, then actor session, then target account.
                -- Session creation takes a FOR SHARE lock on its account to ensure
                -- no new session can race suspension and survive its revocation.
                SELECT a.role INTO v_actor_role FROM identity.admin_accounts a
                  WHERE a.id=v_actor AND a.is_active AND NOT a.invite_pending
                    AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL FOR UPDATE;
                IF v_actor_role IS DISTINCT FROM 'super_admin' THEN
                  RAISE EXCEPTION 'managed_admin_status_denied' USING ERRCODE='42501';
                END IF;
                SELECT s.id INTO v_session FROM identity.admin_sessions s
                  WHERE s.token_digest=_digest AND s.admin_account_id=v_actor
                    AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp() FOR UPDATE;
                IF v_session IS NULL THEN
                  RAISE EXCEPTION 'managed_admin_status_session_denied' USING ERRCODE='42501';
                END IF;
                SELECT a.role,a.is_active,a.invite_pending,(a.password_hash IS NOT NULL)
                  INTO v_subject_role,v_subject_active,v_invited,v_has_credential
                  FROM identity.admin_accounts a WHERE a.id=_subject FOR UPDATE;
                IF v_subject_role IS NULL OR v_subject_role='super_admin'
                  OR v_subject_role NOT IN ('manager','editor','marketing','coach','matchday_editor','accountant','cashier')
                  OR v_invited OR NOT v_has_credential
                THEN RAISE EXCEPTION 'managed_admin_status_target_denied' USING ERRCODE='42501'; END IF;
                IF v_subject_role<>_expected_role OR v_subject_active IS DISTINCT FROM _expected_active THEN
                  RAISE EXCEPTION 'managed_admin_status_conflict' USING ERRCODE='40001';
                END IF;
      
                -- An administrator may only touch a subject whose EVERY assigned
                -- organization belongs to that administrator. No partial ownership.
                FOR v_owner IN SELECT g.organization_id
                  FROM identity.admin_organization_grants g
                  WHERE g.admin_account_id=_subject ORDER BY g.organization_id FOR SHARE
                LOOP
                  v_owner_count:=v_owner_count+1;
                  IF v_owner NOT IN ('lefteria_fc','lefteria_fc_academy') THEN
                    RAISE EXCEPTION 'managed_admin_status_foreign_owner' USING ERRCODE='42501';
                  END IF;
                  PERFORM 1 FROM identity.admin_organization_grants actor_grant
                    WHERE actor_grant.admin_account_id=v_actor
                      AND actor_grant.organization_id=v_owner FOR SHARE;
                  IF NOT FOUND THEN
                    RAISE EXCEPTION 'managed_admin_status_foreign_owner' USING ERRCODE='42501';
                  END IF;
                END LOOP;
                IF v_owner_count=0 OR v_owner_count>2 THEN
                  RAISE EXCEPTION 'managed_admin_status_owner_missing' USING ERRCODE='42501';
                END IF;
      
                UPDATE identity.admin_accounts SET is_active=_active WHERE id=_subject;
                UPDATE identity.admin_sessions
                  SET revoked_at=GREATEST(created_at,statement_timestamp())
                  WHERE admin_account_id=_subject AND revoked_at IS NULL;
                INSERT INTO audit.security_events
                  (id,occurred_at,actor_identity_id,device_session_id,action,
                   outcome,subject_digest,reason_code,actor_realm)
                VALUES (gen_random_uuid(),statement_timestamp(),v_actor,v_session,
                  CASE WHEN _active THEN 'admin.account.reactivate'
                    ELSE 'admin.account.suspend' END,
                  'succeeded',md5(_subject::text),
                  CASE WHEN _active THEN 'activated' ELSE 'deactivated' END,'admin');
                RETURN _active;
              END $managed_status$;
      
      CREATE OR REPLACE FUNCTION pos_cashier.read_admin_terminal_setup(
              _digest text,_owner text
            ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp AS $terminal_list$
            DECLARE v_actor uuid;v_role text;v_rows jsonb;v_users jsonb;v_canonical jsonb;
            BEGIN
              SELECT actor_id,account_role INTO STRICT v_actor,v_role
                FROM club.authorize_admin_football(_digest,_owner);
              IF v_role<>'super_admin'
              THEN RAISE EXCEPTION 'pos_terminal_super_admin_only' USING ERRCODE='42501'; END IF;
              SELECT coalesce(jsonb_agg(jsonb_build_object(
                'id',r.id,'label',r.label,'location_id',r.location_id,
                'location_name',l.name,'active',r.is_active,
                'operators',coalesce((
                  SELECT jsonb_agg(jsonb_build_object(
                    'admin_account_id',ca.admin_account_id,
                    'name',a.display_name,'active',ca.is_active)
                    ORDER BY a.display_name,a.id)
                  FROM pos_cashier.cashier_assignments ca
                  JOIN identity.admin_accounts a ON a.id=ca.admin_account_id
                  WHERE ca.organization_id=r.organization_id AND ca.register_id=r.id
                ),'[]'::jsonb)
              ) ORDER BY l.name,r.label,r.id),'[]'::jsonb) INTO v_rows
              FROM pos_cashier.registers r
              JOIN pos.locations l
                ON l.organization_id=r.organization_id AND l.id=r.location_id
              WHERE r.organization_id=_owner;
              IF jsonb_array_length(v_rows)>200 THEN
                RAISE EXCEPTION 'pos_terminal_limit_exceeded' USING ERRCODE='54000'; END IF;
              SELECT coalesce(jsonb_agg(jsonb_build_object(
                  'id',a.id,'name',a.display_name,'role',a.role)
                  ORDER BY a.display_name,a.id),'[]'::jsonb) INTO v_users
                FROM identity.admin_accounts a
                JOIN identity.admin_organization_grants g
                  ON g.admin_account_id=a.id AND g.organization_id=_owner
                WHERE a.is_active AND a.role IN ('manager','super_admin','cashier')
                  AND NOT a.invite_pending AND NOT a.otp2fa_enabled
                  AND a.password_hash IS NOT NULL;
              IF jsonb_array_length(v_users)>500
              THEN RAISE EXCEPTION 'pos_operator_limit_exceeded' USING ERRCODE='54000'; END IF;
              IF _owner='lefteria_fc_academy' THEN
                SELECT coalesce(jsonb_agg(jsonb_build_object(
                  'academy_location_id',l.id,'academy_city_id',c.id,
                  'location_name',l.name,'city_name',c.name,
                  'pos_location_id',link.pos_location_id)
                  ORDER BY c.name,l.name,l.id),'[]'::jsonb) INTO v_canonical
                FROM club.football_locations l
                JOIN club.football_cities c ON c.id=l.city_id
                LEFT JOIN pos.academy_football_location_links link
                  ON link.academy_location_id=l.id
                WHERE c.is_active AND l.is_active;
              ELSE v_canonical:='[]'::jsonb; END IF;
              RETURN jsonb_build_object('organization_id',_owner,
                'terminals',v_rows,'operator_accounts',v_users,
                'academy_locations',v_canonical);
            END $terminal_list$;
      
      CREATE OR REPLACE FUNCTION pos_cashier.set_admin_terminal_operator(
              _digest text,_owner text,_register text,_subject uuid,_enabled boolean
            ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp AS $operator_assign$
            DECLARE
              v_actor uuid;v_role text;v_subject_role text;v_location text;
              v_academy_location uuid;v_scope uuid;
            BEGIN
              SELECT actor_id,account_role INTO STRICT v_actor,v_role
                FROM club.authorize_admin_football(_digest,_owner);
              IF v_role<>'super_admin' OR _register IS NULL
                OR _register !~ '^[A-Za-z0-9_-]{1,80}$'
                OR _subject IS NULL OR _enabled IS NULL
              THEN RAISE EXCEPTION 'pos_operator_assignment_denied' USING ERRCODE='42501'; END IF;
              SELECT r.location_id INTO v_location FROM pos_cashier.registers r
                JOIN pos.locations l
                  ON l.organization_id=r.organization_id AND l.id=r.location_id
                WHERE r.organization_id=_owner AND r.id=_register
                  AND r.is_active AND l.active FOR SHARE OF r,l;
              IF v_location IS NULL
              THEN RAISE EXCEPTION 'pos_register_not_available' USING ERRCODE='42501'; END IF;
              PERFORM pos_cashier.authorize_site_finances(_digest,_owner,v_location);
              -- Grant must be current for activation. Revocation must remain
              -- possible after a manager was deactivated or moved to another site.
              IF _enabled THEN
                SELECT a.role INTO v_subject_role
                  FROM identity.admin_accounts a
                  JOIN identity.admin_organization_grants g
                    ON g.admin_account_id=a.id AND g.organization_id=_owner
                  WHERE a.id=_subject AND a.is_active AND NOT a.invite_pending
                    AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL
                  FOR UPDATE OF a FOR SHARE OF g;
                IF v_subject_role NOT IN ('manager','super_admin','cashier')
                  OR v_subject_role IS NULL
                THEN RAISE EXCEPTION 'pos_operator_account_unavailable'
                  USING ERRCODE='42501'; END IF;
                -- Serialize cashier grants by locking the subject account above.
                -- Prevent concurrent assignments to different active terminals.
                IF v_subject_role='cashier' AND EXISTS (
                  SELECT 1 FROM pos_cashier.cashier_assignments ca
                  WHERE ca.admin_account_id=_subject AND ca.is_active
                    AND (ca.organization_id<>_owner OR ca.register_id<>_register)
                ) THEN RAISE EXCEPTION 'pos_cashier_single_terminal_required'
                  USING ERRCODE='42501'; END IF;
                IF _owner='lefteria_fc_academy' THEN
                  SELECT link.academy_location_id,s.id
                    INTO v_academy_location,v_scope
                    FROM pos.academy_football_location_links link
                    JOIN club.football_operational_scopes s
                      ON s.academy_city_id=link.academy_city_id
                      AND s.organization_id=_owner
                    WHERE link.organization_id=_owner AND link.pos_location_id=v_location;
                  IF v_scope IS NULL OR (v_subject_role<>'cashier' AND NOT club.admin_football_allowed(
                    _subject,v_subject_role,_owner,v_scope,v_academy_location,true))
                  THEN RAISE EXCEPTION 'pos_operator_location_grant_required'
                     USING ERRCODE='42501'; END IF;
                END IF;
              ELSIF NOT EXISTS (
                SELECT 1 FROM pos_cashier.cashier_assignments ca
                WHERE ca.organization_id=_owner AND ca.register_id=_register
                  AND ca.admin_account_id=_subject
              ) THEN RAISE EXCEPTION 'pos_operator_assignment_not_found'
                USING ERRCODE='42501';
              END IF;
              -- Never strand an open shift by revoking its active operator.
              -- Close and reconcile the assigned shift before withdrawing access.
              IF NOT _enabled AND EXISTS (
                SELECT 1 FROM pos_cashier.shifts sh
                WHERE sh.organization_id=_owner AND sh.register_id=_register
                  AND sh.cashier_id=_subject AND sh.closed_at IS NULL
              ) THEN RAISE EXCEPTION 'pos_operator_open_shift_must_close'
                  USING ERRCODE='23514'; END IF;
              INSERT INTO pos_cashier.cashier_assignments(
                organization_id,register_id,admin_account_id,is_active)
                VALUES(_owner,_register,_subject,_enabled)
                ON CONFLICT(organization_id,register_id,admin_account_id)
                DO UPDATE SET is_active=EXCLUDED.is_active;
              INSERT INTO audit.security_events(
                id,actor_realm,occurred_at,actor_identity_id,
                action,outcome,organization_id,reason_code)
                VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
                  'admin.pos.terminal.operator','succeeded',_owner,
                  CASE WHEN _enabled THEN 'assigned' ELSE 'revoked' END);
              RETURN jsonb_build_object('organization_id',_owner,
                'register_id',_register,'admin_account_id',_subject,
                'active',_enabled);
            END $operator_assign$;
      
      CREATE OR REPLACE FUNCTION pos_cashier.current_cashier_authority(
              _digest text,_owner text,_register text
            ) RETURNS uuid LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp AS $scoped_cashier$
            DECLARE
              v_actor uuid;v_role text;v_pos_location text;v_academy_location uuid;
              v_scope uuid;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                OR _register IS NULL OR _register !~ '^[A-Za-z0-9_-]{1,80}$'
              THEN RAISE EXCEPTION 'cashier_scope_invalid' USING ERRCODE='22023'; END IF;
              BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'cashier_not_assigned_or_not_active' USING ERRCODE='42501';
        END;
              SELECT a.id,a.role,r.location_id
                INTO v_actor,v_role,v_pos_location
                FROM identity.admin_sessions s
                JOIN identity.admin_accounts a ON a.id=s.admin_account_id
                JOIN identity.admin_organization_grants g
                  ON g.admin_account_id=a.id AND g.organization_id=_owner
                JOIN pos_cashier.cashier_assignments ca
                  ON ca.organization_id=_owner AND ca.register_id=_register
                    AND ca.admin_account_id=a.id AND ca.is_active
                JOIN pos_cashier.registers r
                  ON r.organization_id=_owner AND r.id=_register AND r.is_active
                JOIN pos.locations l
                  ON l.organization_id=_owner AND l.id=r.location_id AND l.active
                WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                  AND s.expires_at>clock_timestamp() AND a.is_active
                  AND NOT a.invite_pending AND NOT a.otp2fa_enabled
                  AND a.password_hash IS NOT NULL
                FOR SHARE OF ca,r,l;
              IF NOT FOUND OR v_role NOT IN ('manager','super_admin','cashier')
              THEN RAISE EXCEPTION 'cashier_not_assigned_or_not_active'
                USING ERRCODE='42501'; END IF;
              IF _owner='lefteria_fc_academy' THEN
                SELECT link.academy_location_id,scope.id
                  INTO v_academy_location,v_scope
                  FROM pos.academy_football_location_links link
                  JOIN club.football_locations fl
                    ON fl.id=link.academy_location_id
                      AND fl.city_id=link.academy_city_id AND fl.is_active
                  JOIN club.football_cities fc ON fc.id=fl.city_id AND fc.is_active
                  JOIN club.football_operational_scopes scope
                    ON scope.academy_city_id=fl.city_id
                      AND scope.organization_id=_owner
                  WHERE link.organization_id=_owner
                    AND link.pos_location_id=v_pos_location;
                IF v_scope IS NULL OR (v_role<>'cashier' AND NOT club.admin_football_allowed(
                  v_actor,v_role,_owner,v_scope,v_academy_location,false))
                THEN RAISE EXCEPTION 'cashier_site_grant_revoked'
                   USING ERRCODE='42501'; END IF;
              END IF;
              RETURN v_actor;
            END $scoped_cashier$;
      
      CREATE OR REPLACE FUNCTION pos_cashier.read_admin_cashier_registers(
              _digest text,_owner text
            ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp AS $scoped_registers$
            DECLARE v_actor uuid;v_role text;v_rows jsonb;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
              THEN RAISE EXCEPTION 'cashier_registers_invalid' USING ERRCODE='22023'; END IF;
              BEGIN
                PERFORM identity.lock_admin_club_authority(_digest,_owner);
              EXCEPTION WHEN raise_exception THEN
                RAISE EXCEPTION 'cashier_registers_denied' USING ERRCODE='42501';
              END;
              SELECT a.id,a.role INTO v_actor,v_role
                FROM identity.admin_sessions sess
                JOIN identity.admin_accounts a ON a.id=sess.admin_account_id
                JOIN identity.admin_organization_grants g
                  ON g.admin_account_id=a.id AND g.organization_id=_owner
                WHERE sess.token_digest=_digest AND sess.revoked_at IS NULL
                  AND sess.expires_at>clock_timestamp() AND a.is_active
                  AND NOT a.invite_pending AND NOT a.otp2fa_enabled
                  AND a.password_hash IS NOT NULL
                  AND a.role IN ('manager','super_admin','cashier');
              IF v_actor IS NULL THEN
                RAISE EXCEPTION 'cashier_registers_denied' USING ERRCODE='42501';
              END IF;
              SELECT coalesce(jsonb_agg(jsonb_build_object(
                'id',r.id,'label',r.label,'location_id',r.location_id,
                'location_name',l.name,'shift_id',sh.id,
                'opened_at',sh.opened_at,'opening_cash_cents',sh.opening_cash_cents)
                ORDER BY r.id),'[]'::jsonb) INTO v_rows
              FROM pos_cashier.registers r
              JOIN pos.locations l ON l.organization_id=r.organization_id
                AND l.id=r.location_id AND l.active
              JOIN pos_cashier.cashier_assignments ca
                ON ca.organization_id=r.organization_id
                AND ca.register_id=r.id AND ca.admin_account_id=v_actor AND ca.is_active
              LEFT JOIN pos_cashier.shifts sh
                ON sh.organization_id=r.organization_id
                AND sh.register_id=r.id AND sh.closed_at IS NULL
                AND (v_role<>'cashier' OR sh.cashier_id=v_actor)
              LEFT JOIN pos.academy_football_location_links link
                ON link.organization_id=r.organization_id
                AND link.pos_location_id=r.location_id
              LEFT JOIN club.football_operational_scopes scope
                ON scope.academy_city_id=link.academy_city_id
                AND scope.organization_id=r.organization_id
              WHERE r.organization_id=_owner AND r.is_active
                AND (_owner<>'lefteria_fc_academy' OR
                   (link.academy_location_id IS NOT NULL AND scope.id IS NOT NULL
                    AND EXISTS (SELECT 1 FROM club.football_locations fl
                      JOIN club.football_cities fc ON fc.id=fl.city_id
                      WHERE fl.id=link.academy_location_id
                        AND fl.city_id=link.academy_city_id
                        AND fl.is_active AND fc.is_active)
                    AND (v_role='cashier' OR club.admin_football_allowed(
                      v_actor,v_role,_owner,scope.id,link.academy_location_id,false))));
              IF jsonb_array_length(v_rows)>100
              THEN RAISE EXCEPTION 'cashier_registers_too_many' USING ERRCODE='54000'; END IF;
              RETURN jsonb_build_object('organization_id',_owner,
                'registers',v_rows,'total',jsonb_array_length(v_rows));
            END $scoped_registers$;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $no_cashiers$ BEGIN
        IF EXISTS(SELECT 1 FROM identity.admin_accounts WHERE role='cashier')
        THEN RAISE EXCEPTION 'cashier_accounts_require_forward_recovery' USING ERRCODE='23514'; END IF;
      END $no_cashiers$;
      
      CREATE OR REPLACE FUNCTION pos_cashier.read_admin_cashier_registers(
              _digest text,_owner text
            ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp AS $scoped_registers$
            DECLARE v_actor uuid;v_role text;v_rows jsonb;
            BEGIN
              SELECT actor_id,account_role INTO STRICT v_actor,v_role
                FROM club.authorize_admin_football(_digest,_owner);
              SELECT coalesce(jsonb_agg(jsonb_build_object(
                'id',r.id,'label',r.label,'location_id',r.location_id,
                'location_name',l.name,'shift_id',sh.id,
                'opened_at',sh.opened_at,'opening_cash_cents',sh.opening_cash_cents)
                ORDER BY r.id),'[]'::jsonb) INTO v_rows
              FROM pos_cashier.registers r
              JOIN pos.locations l ON l.organization_id=r.organization_id
                AND l.id=r.location_id AND l.active
              JOIN pos_cashier.cashier_assignments ca
                ON ca.organization_id=r.organization_id
                AND ca.register_id=r.id AND ca.admin_account_id=v_actor AND ca.is_active
              LEFT JOIN pos_cashier.shifts sh
                ON sh.organization_id=r.organization_id
                AND sh.register_id=r.id AND sh.closed_at IS NULL
              LEFT JOIN pos.academy_football_location_links link
                ON link.organization_id=r.organization_id
                AND link.pos_location_id=r.location_id
              LEFT JOIN club.football_operational_scopes scope
                ON scope.academy_city_id=link.academy_city_id
                AND scope.organization_id=r.organization_id
              WHERE r.organization_id=_owner AND r.is_active
                AND (_owner<>'lefteria_fc_academy' OR
                   (link.academy_location_id IS NOT NULL AND scope.id IS NOT NULL
                    AND club.admin_football_allowed(v_actor,v_role,_owner,
                      scope.id,link.academy_location_id,false)));
              IF jsonb_array_length(v_rows)>100
              THEN RAISE EXCEPTION 'cashier_registers_too_many' USING ERRCODE='54000'; END IF;
              RETURN jsonb_build_object('organization_id',_owner,
                'registers',v_rows,'total',jsonb_array_length(v_rows));
            END $scoped_registers$;
      
      CREATE OR REPLACE FUNCTION pos_cashier.current_cashier_authority(
              _digest text,_owner text,_register text
            ) RETURNS uuid LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp AS $scoped_cashier$
            DECLARE
              v_actor uuid;v_role text;v_pos_location text;v_academy_location uuid;
              v_scope uuid;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                OR _register IS NULL OR _register !~ '^[A-Za-z0-9_-]{1,80}$'
              THEN RAISE EXCEPTION 'cashier_scope_invalid' USING ERRCODE='22023'; END IF;
              PERFORM identity.lock_admin_club_authority(_digest,_owner);
              SELECT a.id,a.role,r.location_id
                INTO v_actor,v_role,v_pos_location
                FROM identity.admin_sessions s
                JOIN identity.admin_accounts a ON a.id=s.admin_account_id
                JOIN identity.admin_organization_grants g
                  ON g.admin_account_id=a.id AND g.organization_id=_owner
                JOIN pos_cashier.cashier_assignments ca
                  ON ca.organization_id=_owner AND ca.register_id=_register
                    AND ca.admin_account_id=a.id AND ca.is_active
                JOIN pos_cashier.registers r
                  ON r.organization_id=_owner AND r.id=_register AND r.is_active
                JOIN pos.locations l
                  ON l.organization_id=_owner AND l.id=r.location_id AND l.active
                WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                  AND s.expires_at>clock_timestamp() AND a.is_active
                  AND NOT a.invite_pending AND NOT a.otp2fa_enabled
                  AND a.password_hash IS NOT NULL
                FOR SHARE OF ca,r,l;
              IF NOT FOUND OR v_role NOT IN ('manager','super_admin')
              THEN RAISE EXCEPTION 'cashier_not_assigned_or_not_active'
                USING ERRCODE='42501'; END IF;
              IF _owner='lefteria_fc_academy' THEN
                SELECT link.academy_location_id,scope.id
                  INTO v_academy_location,v_scope
                  FROM pos.academy_football_location_links link
                  JOIN club.football_locations fl
                    ON fl.id=link.academy_location_id
                      AND fl.city_id=link.academy_city_id AND fl.is_active
                  JOIN club.football_cities fc ON fc.id=fl.city_id AND fc.is_active
                  JOIN club.football_operational_scopes scope
                    ON scope.academy_city_id=fl.city_id
                      AND scope.organization_id=_owner
                  WHERE link.organization_id=_owner
                    AND link.pos_location_id=v_pos_location;
                IF v_scope IS NULL OR NOT club.admin_football_allowed(
                  v_actor,v_role,_owner,v_scope,v_academy_location,false)
                THEN RAISE EXCEPTION 'cashier_site_grant_revoked'
                   USING ERRCODE='42501'; END IF;
              END IF;
              RETURN v_actor;
            END $scoped_cashier$;
      
      CREATE OR REPLACE FUNCTION pos_cashier.set_admin_terminal_operator(
              _digest text,_owner text,_register text,_subject uuid,_enabled boolean
            ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp AS $operator_assign$
            DECLARE
              v_actor uuid;v_role text;v_subject_role text;v_location text;
              v_academy_location uuid;v_scope uuid;
            BEGIN
              SELECT actor_id,account_role INTO STRICT v_actor,v_role
                FROM club.authorize_admin_football(_digest,_owner);
              IF v_role<>'super_admin' OR _register IS NULL
                OR _register !~ '^[A-Za-z0-9_-]{1,80}$'
                OR _subject IS NULL OR _enabled IS NULL
              THEN RAISE EXCEPTION 'pos_operator_assignment_denied' USING ERRCODE='42501'; END IF;
              SELECT r.location_id INTO v_location FROM pos_cashier.registers r
                JOIN pos.locations l
                  ON l.organization_id=r.organization_id AND l.id=r.location_id
                WHERE r.organization_id=_owner AND r.id=_register
                  AND r.is_active AND l.active FOR SHARE OF r,l;
              IF v_location IS NULL
              THEN RAISE EXCEPTION 'pos_register_not_available' USING ERRCODE='42501'; END IF;
              PERFORM pos_cashier.authorize_site_finances(_digest,_owner,v_location);
              -- Grant must be current for activation. Revocation must remain
              -- possible after a manager was deactivated or moved to another site.
              IF _enabled THEN
                SELECT a.role INTO v_subject_role
                  FROM identity.admin_accounts a
                  JOIN identity.admin_organization_grants g
                    ON g.admin_account_id=a.id AND g.organization_id=_owner
                  WHERE a.id=_subject AND a.is_active AND NOT a.invite_pending
                    AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL
                  FOR SHARE OF a,g;
                IF v_subject_role NOT IN ('manager','super_admin')
                  OR v_subject_role IS NULL
                THEN RAISE EXCEPTION 'pos_operator_account_unavailable'
                  USING ERRCODE='42501'; END IF;
                IF _owner='lefteria_fc_academy' THEN
                  SELECT link.academy_location_id,s.id
                    INTO v_academy_location,v_scope
                    FROM pos.academy_football_location_links link
                    JOIN club.football_operational_scopes s
                      ON s.academy_city_id=link.academy_city_id
                      AND s.organization_id=_owner
                    WHERE link.organization_id=_owner AND link.pos_location_id=v_location;
                  IF v_scope IS NULL OR NOT club.admin_football_allowed(
                    _subject,v_subject_role,_owner,v_scope,v_academy_location,true)
                  THEN RAISE EXCEPTION 'pos_operator_location_grant_required'
                     USING ERRCODE='42501'; END IF;
                END IF;
              ELSIF NOT EXISTS (
                SELECT 1 FROM pos_cashier.cashier_assignments ca
                WHERE ca.organization_id=_owner AND ca.register_id=_register
                  AND ca.admin_account_id=_subject
              ) THEN RAISE EXCEPTION 'pos_operator_assignment_not_found'
                USING ERRCODE='42501';
              END IF;
              -- Never strand an open shift by revoking its active operator.
              -- Close and reconcile the assigned shift before withdrawing access.
              IF NOT _enabled AND EXISTS (
                SELECT 1 FROM pos_cashier.shifts sh
                WHERE sh.organization_id=_owner AND sh.register_id=_register
                  AND sh.cashier_id=_subject AND sh.closed_at IS NULL
              ) THEN RAISE EXCEPTION 'pos_operator_open_shift_must_close'
                  USING ERRCODE='23514'; END IF;
              INSERT INTO pos_cashier.cashier_assignments(
                organization_id,register_id,admin_account_id,is_active)
                VALUES(_owner,_register,_subject,_enabled)
                ON CONFLICT(organization_id,register_id,admin_account_id)
                DO UPDATE SET is_active=EXCLUDED.is_active;
              INSERT INTO audit.security_events(
                id,actor_realm,occurred_at,actor_identity_id,
                action,outcome,organization_id,reason_code)
                VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
                  'admin.pos.terminal.operator','succeeded',_owner,
                  CASE WHEN _enabled THEN 'assigned' ELSE 'revoked' END);
              RETURN jsonb_build_object('organization_id',_owner,
                'register_id',_register,'admin_account_id',_subject,
                'active',_enabled);
            END $operator_assign$;
      
      CREATE OR REPLACE FUNCTION pos_cashier.read_admin_terminal_setup(
              _digest text,_owner text
            ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp AS $terminal_list$
            DECLARE v_actor uuid;v_role text;v_rows jsonb;v_users jsonb;v_canonical jsonb;
            BEGIN
              SELECT actor_id,account_role INTO STRICT v_actor,v_role
                FROM club.authorize_admin_football(_digest,_owner);
              IF v_role<>'super_admin'
              THEN RAISE EXCEPTION 'pos_terminal_super_admin_only' USING ERRCODE='42501'; END IF;
              SELECT coalesce(jsonb_agg(jsonb_build_object(
                'id',r.id,'label',r.label,'location_id',r.location_id,
                'location_name',l.name,'active',r.is_active,
                'operators',coalesce((
                  SELECT jsonb_agg(jsonb_build_object(
                    'admin_account_id',ca.admin_account_id,
                    'name',a.display_name,'active',ca.is_active)
                    ORDER BY a.display_name,a.id)
                  FROM pos_cashier.cashier_assignments ca
                  JOIN identity.admin_accounts a ON a.id=ca.admin_account_id
                  WHERE ca.organization_id=r.organization_id AND ca.register_id=r.id
                ),'[]'::jsonb)
              ) ORDER BY l.name,r.label,r.id),'[]'::jsonb) INTO v_rows
              FROM pos_cashier.registers r
              JOIN pos.locations l
                ON l.organization_id=r.organization_id AND l.id=r.location_id
              WHERE r.organization_id=_owner;
              IF jsonb_array_length(v_rows)>200 THEN
                RAISE EXCEPTION 'pos_terminal_limit_exceeded' USING ERRCODE='54000'; END IF;
              SELECT coalesce(jsonb_agg(jsonb_build_object(
                  'id',a.id,'name',a.display_name,'role',a.role)
                  ORDER BY a.display_name,a.id),'[]'::jsonb) INTO v_users
                FROM identity.admin_accounts a
                JOIN identity.admin_organization_grants g
                  ON g.admin_account_id=a.id AND g.organization_id=_owner
                WHERE a.is_active AND a.role IN ('manager','super_admin')
                  AND NOT a.invite_pending AND NOT a.otp2fa_enabled
                  AND a.password_hash IS NOT NULL;
              IF jsonb_array_length(v_users)>500
              THEN RAISE EXCEPTION 'pos_operator_limit_exceeded' USING ERRCODE='54000'; END IF;
              IF _owner='lefteria_fc_academy' THEN
                SELECT coalesce(jsonb_agg(jsonb_build_object(
                  'academy_location_id',l.id,'academy_city_id',c.id,
                  'location_name',l.name,'city_name',c.name,
                  'pos_location_id',link.pos_location_id)
                  ORDER BY c.name,l.name,l.id),'[]'::jsonb) INTO v_canonical
                FROM club.football_locations l
                JOIN club.football_cities c ON c.id=l.city_id
                LEFT JOIN pos.academy_football_location_links link
                  ON link.academy_location_id=l.id
                WHERE c.is_active AND l.is_active;
              ELSE v_canonical:='[]'::jsonb; END IF;
              RETURN jsonb_build_object('organization_id',_owner,
                'terminals',v_rows,'operator_accounts',v_users,
                'academy_locations',v_canonical);
            END $terminal_list$;
      
      CREATE OR REPLACE FUNCTION identity.set_managed_admin_status(
                _digest text,_subject uuid,_expected_role text,
                _expected_active boolean,_active boolean
              ) RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
              SET search_path=pg_catalog,pg_temp AS $managed_status$
              DECLARE
                v_actor uuid;v_session uuid;v_actor_role text;
                v_subject_role text;v_subject_active boolean;
                v_invited boolean;v_has_credential boolean;
                v_owner text;v_owner_count integer:=0;
              BEGIN
                IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                  OR _subject IS NULL OR _expected_role IS NULL
                  OR _expected_role NOT IN ('manager','editor','marketing','coach','matchday_editor','accountant')
                  OR _expected_active IS NULL OR _active IS NULL
                  OR _expected_active=_active
                THEN RAISE EXCEPTION 'managed_admin_status_invalid' USING ERRCODE='22023'; END IF;
      
                SELECT s.admin_account_id INTO v_actor
                  FROM identity.admin_sessions s WHERE s.token_digest=_digest;
                IF v_actor IS NULL OR v_actor=_subject THEN
                  RAISE EXCEPTION 'managed_admin_status_denied' USING ERRCODE='42501';
                END IF;
                -- Lock actor account, then actor session, then target account.
                -- Session creation takes a FOR SHARE lock on its account to ensure
                -- no new session can race suspension and survive its revocation.
                SELECT a.role INTO v_actor_role FROM identity.admin_accounts a
                  WHERE a.id=v_actor AND a.is_active AND NOT a.invite_pending
                    AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL FOR UPDATE;
                IF v_actor_role IS DISTINCT FROM 'super_admin' THEN
                  RAISE EXCEPTION 'managed_admin_status_denied' USING ERRCODE='42501';
                END IF;
                SELECT s.id INTO v_session FROM identity.admin_sessions s
                  WHERE s.token_digest=_digest AND s.admin_account_id=v_actor
                    AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp() FOR UPDATE;
                IF v_session IS NULL THEN
                  RAISE EXCEPTION 'managed_admin_status_session_denied' USING ERRCODE='42501';
                END IF;
                SELECT a.role,a.is_active,a.invite_pending,(a.password_hash IS NOT NULL)
                  INTO v_subject_role,v_subject_active,v_invited,v_has_credential
                  FROM identity.admin_accounts a WHERE a.id=_subject FOR UPDATE;
                IF v_subject_role IS NULL OR v_subject_role='super_admin'
                  OR v_subject_role NOT IN ('manager','editor','marketing','coach','matchday_editor','accountant')
                  OR v_invited OR NOT v_has_credential
                THEN RAISE EXCEPTION 'managed_admin_status_target_denied' USING ERRCODE='42501'; END IF;
                IF v_subject_role<>_expected_role OR v_subject_active IS DISTINCT FROM _expected_active THEN
                  RAISE EXCEPTION 'managed_admin_status_conflict' USING ERRCODE='40001';
                END IF;
      
                -- An administrator may only touch a subject whose EVERY assigned
                -- organization belongs to that administrator. No partial ownership.
                FOR v_owner IN SELECT g.organization_id
                  FROM identity.admin_organization_grants g
                  WHERE g.admin_account_id=_subject ORDER BY g.organization_id FOR SHARE
                LOOP
                  v_owner_count:=v_owner_count+1;
                  IF v_owner NOT IN ('lefteria_fc','lefteria_fc_academy') THEN
                    RAISE EXCEPTION 'managed_admin_status_foreign_owner' USING ERRCODE='42501';
                  END IF;
                  PERFORM 1 FROM identity.admin_organization_grants actor_grant
                    WHERE actor_grant.admin_account_id=v_actor
                      AND actor_grant.organization_id=v_owner FOR SHARE;
                  IF NOT FOUND THEN
                    RAISE EXCEPTION 'managed_admin_status_foreign_owner' USING ERRCODE='42501';
                  END IF;
                END LOOP;
                IF v_owner_count=0 OR v_owner_count>2 THEN
                  RAISE EXCEPTION 'managed_admin_status_owner_missing' USING ERRCODE='42501';
                END IF;
      
                UPDATE identity.admin_accounts SET is_active=_active WHERE id=_subject;
                UPDATE identity.admin_sessions
                  SET revoked_at=GREATEST(created_at,statement_timestamp())
                  WHERE admin_account_id=_subject AND revoked_at IS NULL;
                INSERT INTO audit.security_events
                  (id,occurred_at,actor_identity_id,device_session_id,action,
                   outcome,subject_digest,reason_code,actor_realm)
                VALUES (gen_random_uuid(),statement_timestamp(),v_actor,v_session,
                  CASE WHEN _active THEN 'admin.account.reactivate'
                    ELSE 'admin.account.suspend' END,
                  'succeeded',md5(_subject::text),
                  CASE WHEN _active THEN 'activated' ELSE 'deactivated' END,'admin');
                RETURN _active;
              END $managed_status$;
      
      CREATE OR REPLACE FUNCTION identity.create_managed_admin(
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
      
      ALTER TABLE identity.admin_accounts DROP CONSTRAINT ck_admin_account_role;
      ALTER TABLE identity.admin_accounts ADD CONSTRAINT ck_admin_account_role
        CHECK(role IN ('super_admin','manager','editor','marketing','coach','matchday_editor','accountant'));
    """);
}
