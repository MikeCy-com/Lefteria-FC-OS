using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// A single locked authority, entire-product optimistic write and audit.
/// POS checkout and payment systems are intentionally NOT enabled.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261108113000_PosAdminProductWriter")]
public sealed class PosAdminProductWriter : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE FUNCTION pos.write_admin_product(
              _digest text, _owner text, _id text, _category text,
              _name text, _image text, _active boolean,
              _expected_version bigint, _variants jsonb
            ) RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $pos_product_write$
            DECLARE
              v_actor uuid;
              v_session uuid;
              v_role text;
              v_version bigint;
              v_action text;
              v_variant jsonb;
              v_count integer;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                 OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                 OR _id IS NULL OR _id !~ '^[A-Za-z0-9_-]{1,80}$'
                 OR _category IS NULL OR _category !~ '^[A-Za-z0-9_-]{1,80}$'
                 OR _name IS NULL OR length(_name) NOT BETWEEN 1 AND 100
                 OR _name<>btrim(_name) OR _name ~ '[[:cntrl:]]'
                 OR _active IS NULL
                 OR _expected_version IS NULL OR _expected_version < -1
                 OR _expected_version > 9007199254740991
                 OR (_image IS NOT NULL AND (
                      length(_image)>2048 OR _image ~ '[[:cntrl:]]'
                      OR _image !~ '^https://[^[:space:]]+$'))
                 OR _variants IS NULL OR jsonb_typeof(_variants)<>'array'
                 OR jsonb_array_length(_variants) NOT BETWEEN 1 AND 16 THEN
                RAISE EXCEPTION 'pos_product_invalid' USING ERRCODE='22023';
              END IF;

              -- Validate all variants before touching any table or audit.
              -- jsonb normalized input already has duplicate JSON keys
              -- rejected at the HTTP boundary, including nested objects.
              FOR v_variant IN SELECT value FROM jsonb_array_elements(_variants)
              LOOP
                IF jsonb_typeof(v_variant)<>'object'
                   OR (SELECT count(*) FROM jsonb_object_keys(v_variant))<>4
                   OR NOT (v_variant ?& ARRAY['id','name','cost_cents','price_cents'])
                   OR jsonb_typeof(v_variant->'id')<>'string'
                   OR jsonb_typeof(v_variant->'name')<>'string'
                   OR jsonb_typeof(v_variant->'cost_cents')<>'number'
                   OR jsonb_typeof(v_variant->'price_cents')<>'number'
                   OR v_variant->>'id' !~ '^[A-Za-z0-9_-]{1,60}$'
                   OR length(v_variant->>'name') NOT BETWEEN 1 AND 80
                   OR v_variant->>'name' <> btrim(v_variant->>'name')
                   OR v_variant->>'name' ~ '[[:cntrl:]]'
                   OR v_variant->>'cost_cents' !~ '^[0-9]{1,8}$'
                   OR v_variant->>'price_cents' !~ '^[0-9]{1,8}$'
                   OR (v_variant->>'cost_cents')::integer NOT BETWEEN 0 AND 10000000
                   OR (v_variant->>'price_cents')::integer NOT BETWEEN 1 AND 10000000 THEN
                  RAISE EXCEPTION 'pos_variant_invalid' USING ERRCODE='22023';
                END IF;
              END LOOP;
              SELECT count(DISTINCT value->>'id') INTO v_count FROM jsonb_array_elements(_variants);
              IF v_count<>jsonb_array_length(_variants) THEN
                RAISE EXCEPTION 'pos_variant_duplicate' USING ERRCODE='22023';
              END IF;

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
              IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
                RAISE EXCEPTION 'pos_product_permission_denied' USING ERRCODE='42501';
              END IF;
              IF NOT EXISTS (SELECT 1 FROM pos.categories
                  WHERE organization_id=_owner AND id=_category AND active) THEN
                RAISE EXCEPTION 'pos_product_category_unavailable' USING ERRCODE='22023';
              END IF;
              IF _expected_version=-1 THEN
                -- Protect the private catalog reader's 250-item ceiling.
                IF (SELECT count(*) FROM pos.products WHERE organization_id=_owner)>=250 THEN
                  RAISE EXCEPTION 'pos_product_catalog_full' USING ERRCODE='54000';
                END IF;
                INSERT INTO pos.products(
                  organization_id,id,category_id,name,image_url,active)
                VALUES (_owner,_id,_category,_name,_image,_active)
                ON CONFLICT(organization_id,id) DO NOTHING
                RETURNING version INTO v_version;
                v_action := 'admin.pos.product.create';
              ELSE
                UPDATE pos.products
                SET category_id=_category,name=_name,image_url=_image,
                    active=_active,version=version+1
                WHERE organization_id=_owner AND id=_id
                  AND version=_expected_version
                  AND version<9007199254740991
                RETURNING version INTO v_version;
                v_action := 'admin.pos.product.update';
              END IF;
              IF v_version IS NULL THEN
                RAISE EXCEPTION 'pos_product_version_conflict' USING ERRCODE='P0001';
              END IF;

              -- The product and its entire variant set change atomically,
              -- without any ability to mutate completed sale snapshots.
              DELETE FROM pos.product_variants
                WHERE organization_id=_owner AND product_id=_id;
              INSERT INTO pos.product_variants(
                organization_id,product_id,id,name,cost_cents,price_cents)
              SELECT _owner,_id,value->>'id',value->>'name',
                (value->>'cost_cents')::integer,(value->>'price_cents')::integer
              FROM jsonb_array_elements(_variants);

              INSERT INTO audit.security_events(
                id,actor_realm,occurred_at,actor_identity_id,
                device_session_id,action,outcome,organization_id,reason_code)
              VALUES (gen_random_uuid(),'admin',clock_timestamp(),v_actor,
                v_session,v_action,'succeeded',_owner,'pos_catalog');

              RETURN jsonb_build_object(
                'id',_id,'category_id',_category,'name',_name,
                'image_url',_image,'active',_active,'version',v_version,
                'variants',(
                  SELECT COALESCE(jsonb_agg(jsonb_build_object(
                    'id',id,'name',name,'cost_cents',cost_cents,
                    'price_cents',price_cents) ORDER BY id),'[]'::jsonb)
                  FROM pos.product_variants
                  WHERE organization_id=_owner AND product_id=_id
                )
              );
            END
            $pos_product_write$;

            REVOKE ALL ON FUNCTION pos.write_admin_product(
              text,text,text,text,text,text,boolean,bigint,jsonb
            ) FROM PUBLIC;
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP FUNCTION pos.write_admin_product(
              text,text,text,text,text,text,boolean,bigint,jsonb);
            """);
}
