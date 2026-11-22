using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Independent Super Admin review, with disabled future VAT treatment assignment.
/// Reviews are immutable; no receipt, checkout or provider policy is enabled.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261122151000_PosVatProposalReviews")]
public sealed class PosVatProposalReviews : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE pos_cashier.vat_classification_reviews(
        proposal_id uuid PRIMARY KEY
          REFERENCES pos_cashier.vat_classification_proposals(id) ON DELETE RESTRICT,
        organization_id varchar(32) NOT NULL
          REFERENCES club.organizations(id) ON DELETE RESTRICT,
        approved boolean NOT NULL,
        review_note varchar(500) NOT NULL
          CHECK(length(btrim(review_note)) BETWEEN 10 AND 500),
        reviewer uuid NOT NULL REFERENCES identity.admin_accounts(id),
        reviewed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        treatment_revision integer
          CHECK(treatment_revision BETWEEN 1 AND 1000000),
        CHECK(approved = (treatment_revision IS NOT NULL))
      );
      ALTER TABLE pos_cashier.vat_classification_reviews ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON pos_cashier.vat_classification_reviews FROM PUBLIC;
      CREATE TRIGGER pos_vat_review_immutable
        BEFORE UPDATE OR DELETE ON pos_cashier.vat_classification_reviews
        FOR EACH ROW EXECUTE FUNCTION pos.reject_private_pos_ledger_change();

      CREATE FUNCTION pos_cashier.review_admin_variant_vat(
        _digest text,_owner text,_proposal uuid,_approve boolean,_note text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $vat_review$
      DECLARE v_actor uuid;v_session uuid;v_role text;
        v_proposal pos_cashier.vat_classification_proposals%ROWTYPE;
        v_existing pos_cashier.vat_classification_reviews%ROWTYPE;
        v_code text;v_bp integer;v_revision integer;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _proposal IS NULL OR _approve IS NULL
          OR _note IS NULL OR length(_note) NOT BETWEEN 10 AND 500
          OR _note<>btrim(_note) OR _note ~ '[[:cntrl:]]'
        THEN RAISE EXCEPTION 'pos_vat_review_invalid' USING ERRCODE='22023'; END IF;

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
        IF NOT FOUND OR v_role<>'super_admin'
        THEN RAISE EXCEPTION 'pos_vat_review_denied' USING ERRCODE='42501'; END IF;

        SELECT * INTO v_proposal FROM pos_cashier.vat_classification_proposals
          WHERE id=_proposal AND organization_id=_owner FOR SHARE;
        IF NOT FOUND THEN
          RAISE EXCEPTION 'pos_vat_proposal_missing' USING ERRCODE='22023';
        END IF;
        IF v_proposal.requested_by=v_actor THEN
          RAISE EXCEPTION 'pos_vat_self_review_denied' USING ERRCODE='42501';
        END IF;
        SELECT * INTO v_existing FROM pos_cashier.vat_classification_reviews
          WHERE proposal_id=_proposal FOR SHARE;
        IF FOUND THEN
          IF v_existing.organization_id<>_owner OR v_existing.reviewer<>v_actor
            OR v_existing.approved<>_approve OR v_existing.review_note<>_note
          THEN RAISE EXCEPTION 'pos_vat_review_conflict' USING ERRCODE='P0001'; END IF;
          RETURN jsonb_build_object('proposal_id',_proposal,'approved',v_existing.approved,
            'treatment_revision',v_existing.treatment_revision,
            'checkout_enabled',false,'fiscal_policies_approved',false);
        END IF;

        v_revision:=NULL;
        IF _approve THEN
          v_code:=v_proposal.treatment_code;
          v_bp:=CASE v_code
            WHEN 'standard_19' THEN 1900 WHEN 'reduced_9' THEN 900
            WHEN 'reduced_5' THEN 500 WHEN 'reduced_3' THEN 300
            WHEN 'zero_0' THEN 0 ELSE NULL END;
          -- Serialize revisions for one organization + code. No history overwritten.
          PERFORM pg_advisory_xact_lock(hashtextextended(_owner||':'||v_code,0));
          SELECT COALESCE(max(policy_revision),0)+1 INTO v_revision
            FROM pos_cashier.vat_treatments
            WHERE organization_id=_owner AND code=v_code;
          IF v_revision>1000000 THEN
            RAISE EXCEPTION 'pos_vat_revision_exhausted' USING ERRCODE='22023';
          END IF;
          INSERT INTO pos_cashier.vat_treatments(
            organization_id,code,basis_points,classification_note,
            authority_reference,policy_revision,effective_from,
            reviewed_by,reviewed_at,enabled)
          VALUES(_owner,v_code,v_bp,v_proposal.justification,
            v_proposal.authority_reference,v_revision,v_proposal.effective_from,
            v_actor,clock_timestamp(),false);
          INSERT INTO pos_cashier.variant_vat_assignments(
            organization_id,product_id,variant_id,treatment_code,
            treatment_revision,approved_by,approved_at,justification)
          VALUES(_owner,v_proposal.product_id,v_proposal.variant_id,
            v_code,v_revision,v_actor,clock_timestamp(),_note)
          ON CONFLICT(organization_id,product_id,variant_id) DO UPDATE
            SET treatment_code=EXCLUDED.treatment_code,
              treatment_revision=EXCLUDED.treatment_revision,
              approved_by=EXCLUDED.approved_by,
              approved_at=EXCLUDED.approved_at,
              justification=EXCLUDED.justification;
        END IF;

        INSERT INTO pos_cashier.vat_classification_reviews(
          proposal_id,organization_id,approved,review_note,reviewer,treatment_revision)
        VALUES(_proposal,_owner,_approve,_note,v_actor,v_revision);
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
          CASE WHEN _approve THEN 'admin.pos.vat.review.approve'
               ELSE 'admin.pos.vat.review.reject' END,
          'succeeded',_owner,'fiscal_policy_still_disabled');

        RETURN jsonb_build_object('proposal_id',_proposal,'approved',_approve,
          'treatment_revision',v_revision,
          'checkout_enabled',false,'fiscal_policies_approved',false);
      END $vat_review$;
      REVOKE ALL ON FUNCTION pos_cashier.review_admin_variant_vat(
        text,text,uuid,boolean,text) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$
      BEGIN
        IF EXISTS(SELECT 1 FROM pos_cashier.vat_classification_reviews)
        THEN RAISE EXCEPTION 'pos_vat_reviews_require_forward_recovery'; END IF;
      END $guard$;
      DROP FUNCTION pos_cashier.review_admin_variant_vat(
        text,text,uuid,boolean,text);
      DROP TABLE pos_cashier.vat_classification_reviews;
    """);
}
