using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Independent second physical cash count after suspended-cashier recovery.
/// The original closer cannot approve their own closure. Unreviewed or
/// disputed recovery blocks reuse of the same physical POS terminal.
/// All sums and variances remain immutable; no payment is posted.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115952000_PosIndependentCashCountReview")]
public sealed class PosIndependentCashCountReview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE pos_cashier.shift_recovery_reviews (
        shift_id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL,
        register_id varchar(80) NOT NULL,
        recovery_actor_id uuid NOT NULL REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        reviewer_id uuid NOT NULL REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        review_request_id uuid NOT NULL,
        independent_counted_cash_cents bigint NOT NULL
          CHECK(independent_counted_cash_cents BETWEEN 0 AND 100000000),
        disposition varchar(28) NOT NULL CHECK (
          disposition IN ('balanced_confirmed','variance_flagged','count_disputed')),
        reviewed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        CHECK (reviewer_id<>recovery_actor_id),
        UNIQUE(organization_id,review_request_id),
        FOREIGN KEY(shift_id,organization_id,register_id)
          REFERENCES pos_cashier.shifts(id,organization_id,register_id) ON DELETE RESTRICT
      );
      CREATE INDEX ix_pos_recovery_reviews_owner
        ON pos_cashier.shift_recovery_reviews(organization_id,reviewed_at DESC);
      REVOKE ALL ON pos_cashier.shift_recovery_reviews FROM PUBLIC;
      ALTER TABLE pos_cashier.shift_recovery_reviews ENABLE ROW LEVEL SECURITY;
      CREATE TRIGGER pos_recovery_reviews_immutable
        BEFORE UPDATE OR DELETE ON pos_cashier.shift_recovery_reviews
        FOR EACH ROW EXECUTE FUNCTION pos_cashier.reject_transaction_history_edit();
      ALTER TABLE pos_cashier.transaction_audit DROP CONSTRAINT transaction_audit_action_check;
      ALTER TABLE pos_cashier.transaction_audit ADD CONSTRAINT transaction_audit_action_check
        CHECK (action IN ('shift_open','shift_close','shift_recovery_close','shift_recovery_review','sale_create','cash_verify',
        'card_capture_verify','sale_paid','receipt_issue',
        'return_request','return_approve','return_reject','refund_verify'));
      CREATE FUNCTION pos_cashier.read_admin_shift_recovery_review_queue(
        _digest text,_owner text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $review_queue$
      DECLARE v_actor uuid; v_role text; v_rows jsonb; v_total bigint;
      BEGIN
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);
        IF v_role<>'super_admin' THEN
          RAISE EXCEPTION 'pos_shift_review_queue_super_admin_required'
          USING ERRCODE='42501'; END IF;
        SELECT count(*) INTO v_total FROM pos_cashier.shifts sh
          WHERE sh.organization_id=_owner AND sh.recovery_actor_id IS NOT NULL;
        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'shift_id',rows.shift_id,'register_id',rows.register_id,
          'register_label',rows.register_label,'location_name',rows.location_name,
          'closed_at',rows.closed_at,'cashier_name',rows.cashier_name,
          'recovery_actor_id',rows.recovery_actor_id,
          'disposition',rows.disposition)
          ORDER BY rows.is_reviewed,rows.closed_at DESC,rows.shift_id),'[]'::jsonb)
          INTO v_rows
        FROM (
          SELECT sh.id AS shift_id,sh.register_id,r.label AS register_label,
            l.name AS location_name,sh.closed_at,cashier.display_name AS cashier_name,
            sh.recovery_actor_id,rev.disposition,
            (rev.shift_id IS NOT NULL) AS is_reviewed
          FROM pos_cashier.shifts sh
          JOIN pos_cashier.registers r ON r.organization_id=sh.organization_id
            AND r.id=sh.register_id
          JOIN pos.locations l ON l.organization_id=r.organization_id
            AND l.id=r.location_id
          JOIN identity.admin_accounts cashier ON cashier.id=sh.cashier_id
          LEFT JOIN pos_cashier.shift_recovery_reviews rev ON rev.shift_id=sh.id
          WHERE sh.organization_id=_owner AND sh.recovery_actor_id IS NOT NULL
          ORDER BY (rev.shift_id IS NOT NULL),sh.closed_at DESC,sh.id
          LIMIT 60
        ) rows;
        RETURN jsonb_build_object('organization_id',_owner,'shifts',v_rows,'total',v_total);
      END $review_queue$;
      CREATE FUNCTION pos_cashier.review_recovered_cashier_shift(
        _digest text,_owner text,_shift uuid,_independent_count bigint,_request uuid
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $second_count$
      DECLARE
        v_actor uuid; v_role text; v_session uuid; v_location text;
        v_shift pos_cashier.shifts%ROWTYPE;
        v_previous pos_cashier.shift_recovery_reviews%ROWTYPE;
        v_disposition text;
      BEGIN
        IF _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _shift IS NULL OR _shift='00000000-0000-0000-0000-000000000000'::uuid
          OR _request IS NULL OR _request='00000000-0000-0000-0000-000000000000'::uuid
          OR _independent_count IS NULL OR _independent_count NOT BETWEEN 0 AND 100000000
        THEN RAISE EXCEPTION 'pos_shift_second_count_invalid' USING ERRCODE='22023'; END IF;
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);
        IF v_role<>'super_admin' THEN
          RAISE EXCEPTION 'pos_shift_second_count_super_admin_required'
          USING ERRCODE='42501'; END IF;
        SELECT s.id INTO v_session FROM identity.admin_sessions s
          WHERE s.token_digest=_digest AND s.admin_account_id=v_actor
            AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp();
        IF v_session IS NULL THEN
          RAISE EXCEPTION 'pos_shift_second_count_session_revoked' USING ERRCODE='42501'; END IF;
      
        SELECT r.location_id INTO v_location FROM pos_cashier.shifts sh
          JOIN pos_cashier.registers r ON r.organization_id=sh.organization_id
            AND r.id=sh.register_id
          WHERE sh.id=_shift AND sh.organization_id=_owner;
        IF v_location IS NULL THEN
          RAISE EXCEPTION 'pos_shift_second_count_not_found' USING ERRCODE='42501'; END IF;
        PERFORM pos_cashier.authorize_site_finances(_digest,_owner,v_location);
        -- Lock shift before review row and before checking replay: all reviewers
        -- are serialized, so a second review cannot replace the first.
        SELECT * INTO v_shift FROM pos_cashier.shifts
          WHERE organization_id=_owner AND id=_shift FOR UPDATE;
        IF NOT FOUND OR v_shift.recovery_actor_id IS NULL OR v_shift.closed_at IS NULL
          OR v_shift.counted_cash_cents IS NULL OR v_shift.expected_cash_cents IS NULL
        THEN RAISE EXCEPTION 'pos_shift_second_count_not_recoverable' USING ERRCODE='42501'; END IF;
        IF v_actor=v_shift.recovery_actor_id OR v_actor=v_shift.cashier_id THEN
          RAISE EXCEPTION 'pos_shift_second_count_distinct_supervisor_required'
          USING ERRCODE='42501'; END IF;
        SELECT * INTO v_previous FROM pos_cashier.shift_recovery_reviews
          WHERE shift_id=_shift FOR UPDATE;
        IF FOUND THEN
          IF v_previous.organization_id<>_owner OR v_previous.reviewer_id<>v_actor
            OR v_previous.review_request_id<>_request
            OR v_previous.independent_counted_cash_cents<>_independent_count
          THEN RAISE EXCEPTION 'pos_shift_second_count_already_reviewed'
            USING ERRCODE='23505'; END IF;
          RETURN jsonb_build_object(
            'organization_id',_owner,'shift_id',_shift,'register_id',v_shift.register_id,
            'reviewer_id',v_actor,'disposition',v_previous.disposition,
            'first_count_cents',v_shift.counted_cash_cents,
            'second_count_cents',v_previous.independent_counted_cash_cents,
            'difference_between_counts_cents',
              v_previous.independent_counted_cash_cents-v_shift.counted_cash_cents,
            'expected_cash_cents',v_shift.expected_cash_cents,
            'variance_cents',v_shift.variance_cents,
            'requires_investigation',v_previous.disposition<>'balanced_confirmed',
            'terminal_hold',v_previous.disposition='count_disputed','replay',true);
        END IF;
      
        v_disposition:=CASE
          WHEN _independent_count<>v_shift.counted_cash_cents THEN 'count_disputed'
          WHEN v_shift.variance_cents<>0 THEN 'variance_flagged'
          ELSE 'balanced_confirmed' END;
        INSERT INTO pos_cashier.shift_recovery_reviews(
          organization_id,register_id,shift_id,recovery_actor_id,reviewer_id,
          review_request_id,independent_counted_cash_cents,disposition)
        VALUES(_owner,v_shift.register_id,_shift,v_shift.recovery_actor_id,v_actor,
          _request,_independent_count,v_disposition);
        INSERT INTO pos_cashier.transaction_audit(
          id,organization_id,actor_id,action,request_id)
        VALUES(gen_random_uuid(),_owner,v_actor,'shift_recovery_review',_request);
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,subject_digest,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
          'admin.pos.cashier.second_count','succeeded',_owner,md5(_shift::text),v_disposition);
        RETURN jsonb_build_object(
          'organization_id',_owner,'shift_id',_shift,'register_id',v_shift.register_id,
          'reviewer_id',v_actor,'disposition',v_disposition,
          'first_count_cents',v_shift.counted_cash_cents,
          'second_count_cents',_independent_count,
          'difference_between_counts_cents',_independent_count-v_shift.counted_cash_cents,
          'expected_cash_cents',v_shift.expected_cash_cents,
          'variance_cents',v_shift.variance_cents,
          'requires_investigation',v_disposition<>'balanced_confirmed',
          'terminal_hold',v_disposition='count_disputed','replay',false);
      END $second_count$;
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
                    -- Never activate an operator while the register has an
                    -- unreviewed or disputed suspended-cashier close.
                    IF _enabled AND EXISTS (
                      SELECT 1 FROM pos_cashier.shifts sh
                      LEFT JOIN pos_cashier.shift_recovery_reviews rev ON rev.shift_id=sh.id
                      WHERE sh.organization_id=_owner AND sh.register_id=_register
                        AND sh.recovery_actor_id IS NOT NULL
                        AND (rev.shift_id IS NULL OR rev.disposition='count_disputed')
                    ) THEN RAISE EXCEPTION 'pos_terminal_recovery_requires_second_count'
                      USING ERRCODE='55000'; END IF;
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
      CREATE OR REPLACE FUNCTION pos_cashier.open_admin_cashier_shift(
              _digest text,_owner text,_register text,_opening bigint,_request uuid
            ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp AS $open$
            DECLARE v_actor uuid; v_id uuid; v_existing pos_cashier.shifts%ROWTYPE;
            BEGIN
              IF _opening IS NULL OR _opening NOT BETWEEN 0 AND 100000000
                OR _request IS NULL OR _request='00000000-0000-0000-0000-000000000000'::uuid
              THEN RAISE EXCEPTION 'cashier_open_invalid' USING ERRCODE='22023';
              END IF;
              v_actor:=pos_cashier.current_cashier_authority(_digest,_owner,_register);
              -- A credential already assigned to this terminal must not bypass
              -- independent recovery review by starting another shift.
              IF EXISTS (
                SELECT 1 FROM pos_cashier.shifts sh
                LEFT JOIN pos_cashier.shift_recovery_reviews rev ON rev.shift_id=sh.id
                WHERE sh.organization_id=_owner AND sh.register_id=_register
                  AND sh.recovery_actor_id IS NOT NULL
                  AND (rev.shift_id IS NULL OR rev.disposition='count_disputed')
              ) THEN RAISE EXCEPTION 'pos_shift_independent_review_required'
                USING ERRCODE='55000'; END IF;
              SELECT * INTO v_existing FROM pos_cashier.shifts
                WHERE organization_id=_owner AND open_request_id=_request FOR UPDATE;
              IF FOUND THEN
                IF v_existing.register_id<>_register OR v_existing.cashier_id<>v_actor
                   OR v_existing.opening_cash_cents<>_opening THEN
                  RAISE EXCEPTION 'cashier_open_replay_mismatch' USING ERRCODE='23505';
                END IF;
                RETURN jsonb_build_object('organization_id',_owner,
                  'register_id',_register,'shift_id',v_existing.id,
                  'opening_cash_cents',v_existing.opening_cash_cents,'replay',true);
              END IF;
              v_id:=gen_random_uuid();
              INSERT INTO pos_cashier.shifts(id,organization_id,register_id,cashier_id,
                opening_cash_cents,open_request_id)
              VALUES(v_id,_owner,_register,v_actor,_opening,_request);
              INSERT INTO pos_cashier.transaction_audit(id,organization_id,actor_id,action,request_id)
              VALUES(gen_random_uuid(),_owner,v_actor,'shift_open',_request);
              RETURN jsonb_build_object('organization_id',_owner,
                'register_id',_register,'shift_id',v_id,
                'opening_cash_cents',_opening,'replay',false);
            END $open$;
      REVOKE ALL ON FUNCTION pos_cashier.read_admin_shift_recovery_review_queue(text,text) FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos_cashier.review_recovered_cashier_shift(text,text,uuid,bigint,uuid) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $review_rollback_guard$ BEGIN
        IF EXISTS(SELECT 1 FROM pos_cashier.shift_recovery_reviews)
          OR EXISTS(SELECT 1 FROM pos_cashier.transaction_audit WHERE action='shift_recovery_review')
        THEN RAISE EXCEPTION 'pos_cashier_independent_review_requires_forward_recovery'
          USING ERRCODE='23514'; END IF;
      END $review_rollback_guard$;
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
      CREATE OR REPLACE FUNCTION pos_cashier.open_admin_cashier_shift(
              _digest text,_owner text,_register text,_opening bigint,_request uuid
            ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp AS $open$
            DECLARE v_actor uuid; v_id uuid; v_existing pos_cashier.shifts%ROWTYPE;
            BEGIN
              IF _opening IS NULL OR _opening NOT BETWEEN 0 AND 100000000
                OR _request IS NULL OR _request='00000000-0000-0000-0000-000000000000'::uuid
              THEN RAISE EXCEPTION 'cashier_open_invalid' USING ERRCODE='22023';
              END IF;
              v_actor:=pos_cashier.current_cashier_authority(_digest,_owner,_register);
              SELECT * INTO v_existing FROM pos_cashier.shifts
                WHERE organization_id=_owner AND open_request_id=_request FOR UPDATE;
              IF FOUND THEN
                IF v_existing.register_id<>_register OR v_existing.cashier_id<>v_actor
                   OR v_existing.opening_cash_cents<>_opening THEN
                  RAISE EXCEPTION 'cashier_open_replay_mismatch' USING ERRCODE='23505';
                END IF;
                RETURN jsonb_build_object('organization_id',_owner,
                  'register_id',_register,'shift_id',v_existing.id,
                  'opening_cash_cents',v_existing.opening_cash_cents,'replay',true);
              END IF;
              v_id:=gen_random_uuid();
              INSERT INTO pos_cashier.shifts(id,organization_id,register_id,cashier_id,
                opening_cash_cents,open_request_id)
              VALUES(v_id,_owner,_register,v_actor,_opening,_request);
              INSERT INTO pos_cashier.transaction_audit(id,organization_id,actor_id,action,request_id)
              VALUES(gen_random_uuid(),_owner,v_actor,'shift_open',_request);
              RETURN jsonb_build_object('organization_id',_owner,
                'register_id',_register,'shift_id',v_id,
                'opening_cash_cents',_opening,'replay',false);
            END $open$;
      DROP FUNCTION pos_cashier.review_recovered_cashier_shift(text,text,uuid,bigint,uuid);
      DROP FUNCTION pos_cashier.read_admin_shift_recovery_review_queue(text,text);
      ALTER TABLE pos_cashier.transaction_audit DROP CONSTRAINT transaction_audit_action_check;
      ALTER TABLE pos_cashier.transaction_audit ADD CONSTRAINT transaction_audit_action_check
        CHECK (action IN ('shift_open','shift_close','shift_recovery_close','sale_create','cash_verify',
        'card_capture_verify','sale_paid','receipt_issue',
        'return_request','return_approve','return_reject','refund_verify'));
      DROP TRIGGER pos_recovery_reviews_immutable ON pos_cashier.shift_recovery_reviews;
      DROP TABLE pos_cashier.shift_recovery_reviews;
    """);
}
