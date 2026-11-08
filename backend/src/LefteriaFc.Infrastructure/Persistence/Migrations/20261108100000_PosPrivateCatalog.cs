using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Source-faithful POS catalog foundation: the original POS has categories,
/// products and price/cost variants in INTEGER EUR cents. Do not expose it to
/// the website, operator/POS browser or the admin API until independent audit,
/// auth and transaction gates pass. This creates no sales or payment ledger.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261108100000_PosPrivateCatalog")]
public sealed class PosPrivateCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE SCHEMA IF NOT EXISTS pos;
            REVOKE ALL ON SCHEMA pos FROM PUBLIC;

            CREATE TABLE pos.categories (
              organization_id varchar(32) NOT NULL
                REFERENCES club.organizations(id) ON DELETE RESTRICT,
              id varchar(80) NOT NULL,
              name varchar(100) NOT NULL,
              active boolean NOT NULL DEFAULT true,
              created_at timestamptz NOT NULL DEFAULT statement_timestamp(),
              version bigint NOT NULL DEFAULT 0,
              PRIMARY KEY (organization_id,id),
              CONSTRAINT ck_pos_category_owner
                CHECK (organization_id IN ('lefteria_fc','lefteria_fc_academy')),
              CONSTRAINT ck_pos_category_id
                CHECK (id ~ '^[A-Za-z0-9_-]{1,80}$'),
              CONSTRAINT ck_pos_category_name
                CHECK (length(btrim(name)) BETWEEN 1 AND 100
                  AND name=btrim(name) AND name !~ '[[:cntrl:]]'),
              CONSTRAINT ck_pos_category_version CHECK (version>=0)
            );

            CREATE TABLE pos.products (
              organization_id varchar(32) NOT NULL
                REFERENCES club.organizations(id) ON DELETE RESTRICT,
              id varchar(80) NOT NULL,
              category_id varchar(80) NOT NULL,
              name varchar(100) NOT NULL,
              image_url varchar(2048),
              active boolean NOT NULL DEFAULT true,
              created_at timestamptz NOT NULL DEFAULT statement_timestamp(),
              version bigint NOT NULL DEFAULT 0,
              PRIMARY KEY (organization_id,id),
              FOREIGN KEY (organization_id,category_id)
                REFERENCES pos.categories(organization_id,id) ON DELETE RESTRICT,
              CONSTRAINT ck_pos_product_owner
                CHECK (organization_id IN ('lefteria_fc','lefteria_fc_academy')),
              CONSTRAINT ck_pos_product_id
                CHECK (id ~ '^[A-Za-z0-9_-]{1,80}$'),
              CONSTRAINT ck_pos_product_name
                CHECK (length(btrim(name)) BETWEEN 1 AND 100
                  AND name=btrim(name) AND name !~ '[[:cntrl:]]'),
              CONSTRAINT ck_pos_product_image
                CHECK (image_url IS NULL OR image_url !~ '[[:cntrl:]]'),
              CONSTRAINT ck_pos_product_version CHECK (version>=0)
            );
            CREATE INDEX ix_pos_products_category_active
              ON pos.products(organization_id,category_id,active,id);

            CREATE TABLE pos.product_variants (
              organization_id varchar(32) NOT NULL,
              product_id varchar(80) NOT NULL,
              id varchar(60) NOT NULL,
              name varchar(80) NOT NULL,
              cost_cents integer NOT NULL,
              price_cents integer NOT NULL,
              PRIMARY KEY (organization_id,product_id,id),
              FOREIGN KEY (organization_id,product_id)
                REFERENCES pos.products(organization_id,id) ON DELETE RESTRICT,
              CONSTRAINT ck_pos_variant_id
                CHECK (id ~ '^[A-Za-z0-9_-]{1,60}$'),
              CONSTRAINT ck_pos_variant_name
                CHECK (length(btrim(name)) BETWEEN 1 AND 80
                  AND name=btrim(name) AND name !~ '[[:cntrl:]]'),
              CONSTRAINT ck_pos_variant_cost_cents
                CHECK (cost_cents BETWEEN 0 AND 10000000),
              CONSTRAINT ck_pos_variant_price_cents
                CHECK (price_cents BETWEEN 1 AND 10000000)
            );

            -- No web-admin/POS runtime access. A future catalog adapter must
            -- use fresh role + organization grants, transaction/audit,
            -- reviewed narrow privileges and location-specific visibility.
            REVOKE ALL ON TABLE pos.categories,pos.products,pos.product_variants FROM PUBLIC;
            ALTER TABLE pos.categories ENABLE ROW LEVEL SECURITY;
            ALTER TABLE pos.products ENABLE ROW LEVEL SECURITY;
            ALTER TABLE pos.product_variants ENABLE ROW LEVEL SECURITY;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Refuse data-destructive rollback; operators must use an explicit
        // forward recovery plan for any populated POS catalogue.
        migrationBuilder.Sql("""
            DO $pos_rollback$
            BEGIN
              IF EXISTS (SELECT 1 FROM pos.categories LIMIT 1)
                 OR EXISTS (SELECT 1 FROM pos.products LIMIT 1)
                 OR EXISTS (SELECT 1 FROM pos.product_variants LIMIT 1) THEN
                RAISE EXCEPTION 'pos_catalog_data_requires_forward_recovery';
              END IF;
            END $pos_rollback$;
            DROP TABLE pos.product_variants;
            DROP TABLE pos.products;
            DROP TABLE pos.categories;
            -- Leave the schema in place for later isolated POS modules.
            """);
    }
}
