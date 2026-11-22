using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Private, immutable VAT classification proposals. No policy activation,
/// fiscal receipt writes, checkout grants or refund/payment changes.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261122150000_PosVatClassificationProposals")]
public sealed class PosVatClassificationProposals : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE pos_cashier.vat_classification_proposals (
        id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL,
        product_id varchar(80) NOT NULL,
        variant_id varchar(60) NOT NULL,
        treatment_code varchar(32) NOT NULL
          CHECK(treatment_code IN ('standard_19','reduced_9','reduced_5',
            'reduced_3','zero_0','exempt')),
        effective_from date NOT NULL CHECK(effective_from >= DATE '2020-01-01'),
        authority_reference varchar(500) NOT NULL
          CHECK(length(btrim(authority_reference)) BETWEEN 10 AND 500),
        justification varchar(500) NOT NULL
          CHECK(length(btrim(justification)) BETWEEN 10 AND 500),
        requested_by uuid NOT NULL REFERENCES identity.admin_accounts(id),
        requested_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        status varchar(16) NOT NULL DEFAULT 'pending' CHECK(status='pending'),
        FOREIGN KEY(organization_id,product_id,variant_id)
          REFERENCES pos.product_variants(organization_id,product_id,id)
          ON DELETE RESTRICT
      );
      CREATE INDEX ix_pos_vat_proposals_scope
        ON pos_cashier.vat_classification_proposals(
          organization_id, requested_at DESC, id);
      ALTER TABLE pos_cashier.vat_classification_proposals ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON pos_cashier.vat_classification_proposals FROM PUBLIC;
      CREATE TRIGGER pos_vat_proposal_immutable
        BEFORE UPDATE OR DELETE ON pos_cashier.vat_classification_proposals
        FOR EACH ROW EXECUTE FUNCTION pos.reject_private_pos_ledger_change();

      CREATE FUNCTION pos_cashier.propose_admin_variant_vat(
        _digest text,_owner text,_id uuid,_product text,_variant text,
        _code text,_effective date,_reference text,_reason text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $vat_propose$
      DECLARE v_actor uuid;v_session uuid;v_role text;v_existing record;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _id IS NULL OR _product !~ '^[A-Za-z0-9_-]{1,80}$'
          OR _variant !~ '^[A-Za-z0-9_-]{1,60}$'
          OR _code NOT IN ('standard_19','reduced_9','reduced_5','reduced_3','zero_0','exempt')
          OR _effective IS NULL OR _effective < DATE '2020-01-01'
          OR _reference IS NULL OR length(_reference) NOT BETWEEN 10 AND 500
          OR _reference<>btrim(_reference) OR _reference ~ '[[:cntrl:]]'
          OR _reason IS NULL OR length(_reason) NOT BETWEEN 10 AND 500
          OR _reason<>btrim(_reason) OR _reason ~ '[[:cntrl:]]'
        THEN RAISE EXCEPTION 'pos_vat_proposal_invalid' USING ERRCODE='22023'; END IF;

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
        IF NOT FOUND OR v_role NOT IN ('super_admin','manager','accountant')
        THEN RAISE EXCEPTION 'pos_vat_proposal_denied' USING ERRCODE='42501'; END IF;

        IF NOT EXISTS(SELECT 1 FROM pos.product_variants
          WHERE organization_id=_owner AND product_id=_product AND id=_variant)
        THEN RAISE EXCEPTION 'pos_vat_variant_missing' USING ERRCODE='22023'; END IF;

        SELECT * INTO v_existing FROM pos_cashier.vat_classification_proposals
        WHERE id=_id FOR UPDATE;
        IF FOUND THEN
          IF v_existing.organization_id<>_owner OR v_existing.product_id<>_product
            OR v_existing.variant_id<>_variant OR v_existing.treatment_code<>_code
            OR v_existing.effective_from<>_effective
            OR v_existing.authority_reference<>_reference
            OR v_existing.justification<>_reason OR v_existing.requested_by<>v_actor
          THEN RAISE EXCEPTION 'pos_vat_proposal_replay_conflict' USING ERRCODE='P0001'; END IF;
        ELSE
          INSERT INTO pos_cashier.vat_classification_proposals(
            id,organization_id,product_id,variant_id,treatment_code,
            effective_from,authority_reference,justification,requested_by)
          VALUES(_id,_owner,_product,_variant,_code,_effective,
            _reference,_reason,v_actor);
          INSERT INTO audit.security_events(
            id,actor_realm,occurred_at,actor_identity_id,device_session_id,
            action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
            'admin.pos.vat.proposal.create','succeeded',_owner,'fiscal_policy_pending');
        END IF;
        RETURN jsonb_build_object('id',_id,'organization_id',_owner,
          'product_id',_product,'variant_id',_variant,
          'treatment_code',_code,'effective_from',_effective,
          'authority_reference',_reference,'justification',_reason,
          'status','pending','checkout_enabled',false);
      END $vat_propose$;

      CREATE FUNCTION pos_cashier.read_admin_vat_proposals(_digest text,_owner text)
      RETURNS jsonb LANGUAGE plpgsql STABLE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $vat_read$
      DECLARE v_role text;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
        THEN RAISE EXCEPTION 'pos_vat_proposal_denied' USING ERRCODE='42501'; END IF;
        SELECT a.role INTO v_role
        FROM identity.admin_sessions s
        JOIN identity.admin_accounts a ON a.id=s.admin_account_id
        JOIN identity.admin_organization_grants g
          ON g.admin_account_id=a.id AND g.organization_id=_owner
        WHERE s.token_digest=_digest AND s.revoked_at IS NULL
          AND s.expires_at>clock_timestamp()
          AND a.is_active AND NOT a.invite_pending
          AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role NOT IN ('super_admin','manager','accountant')
        THEN RAISE EXCEPTION 'pos_vat_proposal_denied' USING ERRCODE='42501'; END IF;
        RETURN jsonb_build_object('organization_id',_owner,
          'checkout_enabled',false,'approval_available',false,
          'proposals',COALESCE((SELECT jsonb_agg(jsonb_build_object(
            'id',id,'product_id',product_id,'variant_id',variant_id,
            'treatment_code',treatment_code,'effective_from',effective_from,
            'authority_reference',authority_reference,'justification',justification,
            'requested_at',requested_at,'status',status) ORDER BY requested_at DESC,id)
          FROM (SELECT * FROM pos_cashier.vat_classification_proposals
                WHERE organization_id=_owner ORDER BY requested_at DESC,id LIMIT 100) recent),
            '[]'::jsonb));
      END $vat_read$;

      REVOKE ALL ON FUNCTION pos_cashier.propose_admin_variant_vat(
        text,text,uuid,text,text,text,date,text,text) FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos_cashier.read_admin_vat_proposals(text,text) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$
      BEGIN
        IF EXISTS(SELECT 1 FROM pos_cashier.vat_classification_proposals)
        THEN RAISE EXCEPTION 'pos_vat_proposals_require_forward_recovery'; END IF;
      END $guard$;
      DROP FUNCTION pos_cashier.read_admin_vat_proposals(text,text);
      DROP FUNCTION pos_cashier.propose_admin_variant_vat(
        text,text,uuid,text,text,text,date,text,text);
      DROP TABLE pos_cashier.vat_classification_proposals;
    """);
}
