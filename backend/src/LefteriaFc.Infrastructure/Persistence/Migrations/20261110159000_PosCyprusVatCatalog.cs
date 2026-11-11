using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Cyprus VAT classification is explicit, versioned, private and disabled by
/// default. A rate does not imply that a specific product qualifies for it.
/// Existing checkout functions remain exempt-only and disabled; this schema
/// does not authorize taxable sales or bypass Cyprus fiscal review.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110223000_PosCyprusVatCatalog")]
public sealed class PosCyprusVatCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE pos_cashier.vat_treatments (
        organization_id varchar(32) NOT NULL
          REFERENCES club.organizations(id) ON DELETE RESTRICT
          CHECK (organization_id IN ('lefteria_fc','lefteria_fc_academy')),
        code varchar(32) NOT NULL
          CHECK(code IN ('standard_19','reduced_9','reduced_5',
            'reduced_3','zero_0','exempt')),
        basis_points integer
          CHECK(basis_points IN (0,300,500,900,1900)),
        classification_note varchar(500) NOT NULL
          CHECK(length(btrim(classification_note)) BETWEEN 10 AND 500),
        authority_reference varchar(500) NOT NULL
          CHECK(length(btrim(authority_reference)) BETWEEN 10 AND 500),
        policy_revision integer NOT NULL CHECK(policy_revision BETWEEN 1 AND 1000000),
        effective_from date NOT NULL,
        effective_until date,
        reviewed_by uuid NOT NULL REFERENCES identity.admin_accounts(id),
        reviewed_at timestamptz NOT NULL,
        enabled boolean NOT NULL DEFAULT false,
        PRIMARY KEY (organization_id,code,policy_revision),
        CHECK(effective_until IS NULL OR effective_until>=effective_from),
        CHECK(
          (code='standard_19' AND basis_points=1900) OR
          (code='reduced_9' AND basis_points=900) OR
          (code='reduced_5' AND basis_points=500) OR
          (code='reduced_3' AND basis_points=300) OR
          (code='zero_0' AND basis_points=0) OR
          (code='exempt' AND basis_points IS NULL)
        )
      );
      -- Rates are not interchangeable with exemptions. Product assignment
      -- is a separate, reviewable decision, scoped to its exact variant.
      CREATE TABLE pos_cashier.variant_vat_assignments (
        organization_id varchar(32) NOT NULL,
        product_id varchar(80) NOT NULL,
        variant_id varchar(60) NOT NULL,
        treatment_code varchar(32) NOT NULL,
        treatment_revision integer NOT NULL,
        approved_by uuid NOT NULL REFERENCES identity.admin_accounts(id),
        approved_at timestamptz NOT NULL,
        justification varchar(500) NOT NULL
          CHECK(length(btrim(justification)) BETWEEN 10 AND 500),
        PRIMARY KEY(organization_id,product_id,variant_id),
        FOREIGN KEY(organization_id,product_id,variant_id)
          REFERENCES pos.product_variants(organization_id,product_id,id)
          ON DELETE RESTRICT,
        FOREIGN KEY(organization_id,treatment_code,treatment_revision)
          REFERENCES pos_cashier.vat_treatments(organization_id,code,policy_revision)
          ON DELETE RESTRICT
      );
      CREATE INDEX ix_pos_vat_effective
        ON pos_cashier.vat_treatments(organization_id,code,effective_from)
        WHERE enabled;
      REVOKE ALL ON TABLE pos_cashier.vat_treatments,pos_cashier.variant_vat_assignments FROM PUBLIC;
      ALTER TABLE pos_cashier.vat_treatments ENABLE ROW LEVEL SECURITY;
      ALTER TABLE pos_cashier.variant_vat_assignments ENABLE ROW LEVEL SECURITY;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$
      BEGIN
        IF EXISTS(SELECT 1 FROM pos_cashier.vat_treatments)
           OR EXISTS(SELECT 1 FROM pos_cashier.variant_vat_assignments) THEN
          RAISE EXCEPTION 'pos_vat_catalog_requires_forward_recovery';
        END IF;
      END $guard$;
      DROP TABLE pos_cashier.variant_vat_assignments;
      DROP TABLE pos_cashier.vat_treatments;
    """);
}