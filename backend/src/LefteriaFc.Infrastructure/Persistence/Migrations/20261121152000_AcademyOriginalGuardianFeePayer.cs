using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Enable Super Admin to issue one explicit Academy fee to an enrolled child's
/// ORIGINAL phone-OTP and staff-proof-verified guardian, without creating any
/// legacy-imported binding or extending payer rights to delegated parents.
/// Financial liability stays pinned to one verified mobile payer identity.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261121152000_AcademyOriginalGuardianFeePayer")]
public sealed class AcademyOriginalGuardianFeePayer : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- Restore the single approved contract in place: existing legacy payer
      -- assignments retain their original preferred, canonical selection.
      CREATE OR REPLACE FUNCTION finance.issue_admin_training_fee(
                _digest text,_owner text,_id uuid,_player text,_month date,
                _cents bigint,_email text,_first text,_last text
              ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
              SET search_path=pg_catalog,pg_temp AS $fee_issue$
              DECLARE
                v_actor uuid; v_session uuid; v_role text;
                v_payer uuid; v_payer_phone varchar(15);
                v_verified_candidates integer;
                v_row finance.training_fee_issues%ROWTYPE;
                v_created boolean:=false;
              BEGIN
                IF _digest !~ '^[0-9a-f]{64}$'
                  OR _owner<>'lefteria_fc_academy' OR _id IS NULL
                  OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
                  OR _month IS NULL OR EXTRACT(day FROM _month)<>1
                  OR _month < DATE '2020-01-01' OR _month > DATE '2100-12-01'
                  OR _cents NOT BETWEEN 1 AND 100000000
                  OR length(_email) NOT BETWEEN 3 AND 50
                  OR _email !~ '^[ -~]+$' OR position('@' IN _email)=0
                  OR length(_first) NOT BETWEEN 1 AND 50 OR _first !~ '^[ -~]+$'
                  OR length(_last) NOT BETWEEN 1 AND 50 OR _last !~ '^[ -~]+$' THEN
                  RAISE EXCEPTION 'invalid_training_fee_issue' USING ERRCODE='22023';
                END IF;
                PERFORM identity.lock_admin_club_authority(_digest,_owner);
                SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
                FROM identity.admin_sessions s
                JOIN identity.admin_accounts a ON a.id=s.admin_account_id
                JOIN identity.admin_organization_grants g
                  ON g.admin_account_id=a.id AND g.organization_id=_owner
                WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                  AND s.expires_at>clock_timestamp() AND a.is_active
                  AND NOT a.invite_pending AND NOT a.otp2fa_enabled
                  AND a.password_hash IS NOT NULL;
                IF NOT FOUND OR v_role<>'super_admin' THEN
                  RAISE EXCEPTION 'fee_access_denied' USING ERRCODE='42501';
                END IF;
      
                SELECT i.id,i.normalized_phone INTO v_payer,v_payer_phone
                FROM club.players p
                JOIN identity.mobile_record_links l
                  ON l.collection='players' AND l.record_id=p.id
                  AND l.kind='guardian' AND l.role='parent'
                JOIN identity.mobile_identities i
                  ON i.id=l.identity_id AND i.normalized_phone=l.normalized_phone
                  AND i.role='parent' AND i.is_active
                JOIN identity.canonical_contact_bindings b
                  ON b.collection=l.collection AND b.source_id=l.record_id
                  AND b.kind='guardian' AND b.normalized_phone=i.normalized_phone
                WHERE p.id=_player AND p.organization_id=_owner
                  AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                ORDER BY l.is_primary DESC,i.id LIMIT 1;
                IF NOT FOUND THEN
                  -- Newly enrolled Academy children have no legacy-imported
                  -- guardian record. The original guardian becomes an eligible
                  -- payer ONLY after human proof review, a post-review phone
                  -- OTP, current consent and an active canonical placement.
                  -- Do NOT use delegated phone grants here: those give child
                  -- visibility but never silently transfer financial liability.
                  SELECT count(*)::integer,(array_agg(candidate.id))[1],
                    (array_agg(candidate.normalized_phone))[1]
                    INTO v_verified_candidates,v_payer,v_payer_phone
                  FROM (
                    SELECT DISTINCT i.id,i.normalized_phone
                    FROM club.players p
                    JOIN academy_intake.registration_placements placement
                      ON placement.player_id=p.id
                      AND placement.organization_id=p.organization_id
                    JOIN identity.academy_guardian_otp_claims verified_otp
                      ON verified_otp.registration_id=placement.registration_id
                    JOIN identity.mobile_identities i
                      ON i.normalized_phone=verified_otp.normalized_phone
                      AND i.role='parent' AND i.is_active
                    JOIN LATERAL identity.current_academy_guardian_claims(
                      i.normalized_phone) current_claim
                      ON current_claim.registration_id=placement.registration_id
                      AND current_claim.player_id=p.id
                      AND current_claim.organization_id=p.organization_id
                    WHERE p.id=_player
                      AND p.organization_id='lefteria_fc_academy'
                      AND p.is_active AND NOT p.is_archived
                      AND NOT p.is_transferred
                  ) candidate;
                  -- A second original-review candidate must be reconciled by
                  -- a Super Admin before a fee can have any assigned payer.
                  -- No arbitrary first-match and no unverified phone fallback.
                  IF v_verified_candidates<>1 THEN
                    RAISE EXCEPTION 'verified_parent_and_child_required'
                      USING ERRCODE='42501';
                  END IF;
                END IF;
      
                INSERT INTO finance.training_fee_issues(
                  id,organization_id,player_id,payer_identity_id,fee_month,
                  amount_cents,payer_phone,payer_email,payer_first_name,payer_last_name,issued_by)
                VALUES(_id,_owner,_player,v_payer,_month,_cents,'+'||v_payer_phone,_email,_first,_last,v_actor)
                ON CONFLICT (organization_id,player_id,fee_month) DO NOTHING
                RETURNING * INTO v_row;
                IF FOUND THEN v_created:=true; END IF;
                IF NOT v_created THEN
                  SELECT * INTO v_row FROM finance.training_fee_issues
                    WHERE organization_id=_owner AND player_id=_player
                      AND fee_month=_month FOR UPDATE;
                  IF NOT FOUND OR v_row.amount_cents<>_cents
                    OR v_row.payer_identity_id<>v_payer
                    OR v_row.payer_email<>_email OR v_row.payer_first_name<>_first
                    OR v_row.payer_last_name<>_last THEN
                    RAISE EXCEPTION 'fee_month_already_issued_differently'
                      USING ERRCODE='23505';
                  END IF;
                END IF;
                IF v_created THEN
                  INSERT INTO audit.security_events(
                    id,actor_realm,occurred_at,actor_identity_id,device_session_id,
                    action,outcome,organization_id,reason_code)
                  VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
                    'admin.finance.training_fee.issue','succeeded',_owner,'explicit_month');
                END IF;
                RETURN jsonb_build_object('id',v_row.id,'created',v_created,
                  'status',v_row.status,'fee_month',v_row.fee_month,
                  'amount_cents',v_row.amount_cents,
                  'payer_email',v_row.payer_email,
                  'payer_first_name',v_row.payer_first_name,
                  'payer_last_name',v_row.payer_last_name,
                  'player_id',v_row.player_id,
                  'payment_request_id',v_row.payment_request_id);
              END $fee_issue$;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- Restore canonical-import-only payer eligibility. Existing issued
      -- invoices and historical verified payer records are never erased.
      CREATE OR REPLACE FUNCTION finance.issue_admin_training_fee(
                _digest text,_owner text,_id uuid,_player text,_month date,
                _cents bigint,_email text,_first text,_last text
              ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
              SET search_path=pg_catalog,pg_temp AS $fee_issue$
              DECLARE
                v_actor uuid; v_session uuid; v_role text;
                v_payer uuid; v_payer_phone varchar(15);
                v_row finance.training_fee_issues%ROWTYPE;
                v_created boolean:=false;
              BEGIN
                IF _digest !~ '^[0-9a-f]{64}$'
                  OR _owner<>'lefteria_fc_academy' OR _id IS NULL
                  OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
                  OR _month IS NULL OR EXTRACT(day FROM _month)<>1
                  OR _month < DATE '2020-01-01' OR _month > DATE '2100-12-01'
                  OR _cents NOT BETWEEN 1 AND 100000000
                  OR length(_email) NOT BETWEEN 3 AND 50
                  OR _email !~ '^[ -~]+$' OR position('@' IN _email)=0
                  OR length(_first) NOT BETWEEN 1 AND 50 OR _first !~ '^[ -~]+$'
                  OR length(_last) NOT BETWEEN 1 AND 50 OR _last !~ '^[ -~]+$' THEN
                  RAISE EXCEPTION 'invalid_training_fee_issue' USING ERRCODE='22023';
                END IF;
                PERFORM identity.lock_admin_club_authority(_digest,_owner);
                SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
                FROM identity.admin_sessions s
                JOIN identity.admin_accounts a ON a.id=s.admin_account_id
                JOIN identity.admin_organization_grants g
                  ON g.admin_account_id=a.id AND g.organization_id=_owner
                WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                  AND s.expires_at>clock_timestamp() AND a.is_active
                  AND NOT a.invite_pending AND NOT a.otp2fa_enabled
                  AND a.password_hash IS NOT NULL;
                IF NOT FOUND OR v_role<>'super_admin' THEN
                  RAISE EXCEPTION 'fee_access_denied' USING ERRCODE='42501';
                END IF;
      
                SELECT i.id,i.normalized_phone INTO v_payer,v_payer_phone
                FROM club.players p
                JOIN identity.mobile_record_links l
                  ON l.collection='players' AND l.record_id=p.id
                  AND l.kind='guardian' AND l.role='parent'
                JOIN identity.mobile_identities i
                  ON i.id=l.identity_id AND i.normalized_phone=l.normalized_phone
                  AND i.role='parent' AND i.is_active
                JOIN identity.canonical_contact_bindings b
                  ON b.collection=l.collection AND b.source_id=l.record_id
                  AND b.kind='guardian' AND b.normalized_phone=i.normalized_phone
                WHERE p.id=_player AND p.organization_id=_owner
                  AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                ORDER BY l.is_primary DESC,i.id LIMIT 1;
                IF NOT FOUND THEN
                  RAISE EXCEPTION 'verified_parent_and_child_required' USING ERRCODE='42501';
                END IF;
      
                INSERT INTO finance.training_fee_issues(
                  id,organization_id,player_id,payer_identity_id,fee_month,
                  amount_cents,payer_phone,payer_email,payer_first_name,payer_last_name,issued_by)
                VALUES(_id,_owner,_player,v_payer,_month,_cents,'+'||v_payer_phone,_email,_first,_last,v_actor)
                ON CONFLICT (organization_id,player_id,fee_month) DO NOTHING
                RETURNING * INTO v_row;
                IF FOUND THEN v_created:=true; END IF;
                IF NOT v_created THEN
                  SELECT * INTO v_row FROM finance.training_fee_issues
                    WHERE organization_id=_owner AND player_id=_player
                      AND fee_month=_month FOR UPDATE;
                  IF NOT FOUND OR v_row.amount_cents<>_cents
                    OR v_row.payer_identity_id<>v_payer
                    OR v_row.payer_email<>_email OR v_row.payer_first_name<>_first
                    OR v_row.payer_last_name<>_last THEN
                    RAISE EXCEPTION 'fee_month_already_issued_differently'
                      USING ERRCODE='23505';
                  END IF;
                END IF;
                IF v_created THEN
                  INSERT INTO audit.security_events(
                    id,actor_realm,occurred_at,actor_identity_id,device_session_id,
                    action,outcome,organization_id,reason_code)
                  VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
                    'admin.finance.training_fee.issue','succeeded',_owner,'explicit_month');
                END IF;
                RETURN jsonb_build_object('id',v_row.id,'created',v_created,
                  'status',v_row.status,'fee_month',v_row.fee_month,
                  'amount_cents',v_row.amount_cents,
                  'payer_email',v_row.payer_email,
                  'payer_first_name',v_row.payer_first_name,
                  'payer_last_name',v_row.payer_last_name,
                  'player_id',v_row.player_id,
                  'payment_request_id',v_row.payment_request_id);
              END $fee_issue$;
    """);
}
