using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Two-person review of bounded, original-sale-linked returns. This cannot
/// execute a payment refund; card refunds require authenticated gateway flow.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110161000_PosReturnManagerReview")]
public sealed class PosReturnManagerReview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      ALTER TABLE pos.returns ADD COLUMN review_request_id uuid;
      CREATE UNIQUE INDEX uq_pos_return_review_request
        ON pos.returns(organization_id,review_request_id)
        WHERE review_request_id IS NOT NULL;

      CREATE FUNCTION pos.request_assigned_cashier_return(
        _digest text,_owner text,_sale uuid,_request uuid,
        _reason text,_lines jsonb
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $return$
      DECLARE v_actor uuid; v_sale pos.sales%ROWTYPE; v_id uuid;
        v_existing pos.returns%ROWTYPE; v_line record; v_count integer:=0;
      BEGIN
        IF _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _sale IS NULL OR _request IS NULL
          OR _request='00000000-0000-0000-0000-000000000000'::uuid
          OR _reason NOT IN ('defective','wrong_item','customer_return','other')
          OR _lines IS NULL OR jsonb_typeof(_lines)<>'array'
          OR jsonb_array_length(_lines) NOT BETWEEN 1 AND 30
        THEN RAISE EXCEPTION 'pos_return_request_invalid' USING ERRCODE='22023'; END IF;

        SELECT * INTO v_sale FROM pos.sales
          WHERE organization_id=_owner AND id=_sale;
        IF NOT FOUND THEN RAISE EXCEPTION 'pos_return_sale_missing' USING ERRCODE='42501'; END IF;
        v_actor:=pos.current_cashier_authority(_digest,_owner,v_sale.register_id);
        SELECT * INTO v_sale FROM pos.sales
          WHERE organization_id=_owner AND id=_sale FOR UPDATE;
        IF NOT FOUND OR v_sale.status<>'paid' OR
          NOT EXISTS(SELECT 1 FROM pos.receipts
            WHERE organization_id=_owner AND sale_id=_sale) THEN
          RAISE EXCEPTION 'pos_return_requires_receipted_sale' USING ERRCODE='42501';
        END IF;

        IF EXISTS(
          SELECT 1 FROM jsonb_array_elements(_lines) x
          WHERE jsonb_typeof(x.value)<>'object'
            OR (SELECT count(*) FROM jsonb_object_keys(x.value))<>2
            OR NOT (x.value ?& ARRAY['line_number','quantity'])
            OR jsonb_typeof(x.value->'line_number')<>'number'
            OR jsonb_typeof(x.value->'quantity')<>'number'
            OR x.value->>'line_number' !~ '^[0-9]{1,2}$'
            OR x.value->>'quantity' !~ '^[0-9]{1,2}$'
            OR (x.value->>'line_number')::int NOT BETWEEN 1 AND 30
            OR (x.value->>'quantity')::int NOT BETWEEN 1 AND 99
        ) OR (
          SELECT count(DISTINCT (x.value->>'line_number')::int)
          FROM jsonb_array_elements(_lines) x
        )<>jsonb_array_length(_lines)
        THEN RAISE EXCEPTION 'pos_return_lines_invalid' USING ERRCODE='22023';
        END IF;

        SELECT * INTO v_existing FROM pos.returns
          WHERE organization_id=_owner AND request_id=_request FOR UPDATE;
        IF FOUND THEN
          IF v_existing.sale_id<>_sale OR v_existing.created_by<>v_actor
            OR v_existing.reason_code<>_reason
            OR EXISTS(
              SELECT 1 FROM jsonb_array_elements(_lines) x
              LEFT JOIN pos.return_lines l
                ON l.return_id=v_existing.id
                  AND l.sale_line_number=(x.value->>'line_number')::int
              WHERE l.quantity IS DISTINCT FROM (x.value->>'quantity')::int
            )
            OR (SELECT count(*) FROM pos.return_lines
                  WHERE return_id=v_existing.id)<>jsonb_array_length(_lines)
          THEN RAISE EXCEPTION 'pos_return_replay_mismatch' USING ERRCODE='23505'; END IF;
          RETURN jsonb_build_object('organization_id',_owner,
            'return_id',v_existing.id,'status',v_existing.status,'replay',true);
        END IF;

        v_id:=gen_random_uuid();
        INSERT INTO pos.returns(
          id,organization_id,sale_id,request_id,reason_code,created_by)
        VALUES(v_id,_owner,_sale,_request,_reason,v_actor);

        FOR v_line IN
          SELECT (x.value->>'line_number')::integer AS line_number,
             (x.value->>'quantity')::integer AS quantity
          FROM jsonb_array_elements(_lines) x
          ORDER BY line_number
        LOOP
          v_count:=v_count+1;
          INSERT INTO pos.return_lines(
            organization_id,return_id,sale_id,sale_line_number,quantity)
          VALUES(_owner,v_id,_sale,v_line.line_number,v_line.quantity);
        END LOOP;
        INSERT INTO pos.transaction_audit(
          id,organization_id,actor_id,sale_id,action,request_id)
        VALUES(gen_random_uuid(),_owner,v_actor,_sale,'return_request',_request);
        RETURN jsonb_build_object('organization_id',_owner,
          'return_id',v_id,'status','requested','replay',false);
      END $return$;

      CREATE FUNCTION pos.review_cashier_return(
        _digest text,_owner text,_return uuid,_request uuid,_approve boolean
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $review$
      DECLARE v_actor uuid; v_role text; v_return pos.returns%ROWTYPE;
        v_action text;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _return IS NULL OR _request IS NULL OR _approve IS NULL
          OR _request='00000000-0000-0000-0000-000000000000'::uuid
        THEN RAISE EXCEPTION 'pos_return_review_invalid' USING ERRCODE='22023'; END IF;
        PERFORM identity.lock_admin_club_authority(_digest,_owner);
        SELECT a.id,a.role INTO v_actor,v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
          RAISE EXCEPTION 'pos_return_review_denied' USING ERRCODE='42501'; END IF;
        SELECT * INTO v_return FROM pos.returns
          WHERE organization_id=_owner AND id=_return FOR UPDATE;
        IF NOT FOUND OR v_return.created_by=v_actor THEN
          RAISE EXCEPTION 'pos_return_self_approval_denied' USING ERRCODE='42501';
        END IF;
        IF v_return.status IN ('approved','rejected') THEN
          IF v_return.review_request_id IS DISTINCT FROM _request
             OR (v_return.status='approved') IS DISTINCT FROM _approve
             OR v_return.approved_by<>v_actor THEN
            RAISE EXCEPTION 'pos_return_review_replay_mismatch' USING ERRCODE='23505';
          END IF;
          RETURN jsonb_build_object('organization_id',_owner,
            'return_id',_return,'status',v_return.status,'replay',true);
        END IF;
        IF v_return.status<>'requested'
          OR NOT EXISTS(SELECT 1 FROM pos.return_lines
             WHERE organization_id=_owner AND return_id=_return) THEN
          RAISE EXCEPTION 'pos_return_not_reviewable' USING ERRCODE='42501';
        END IF;
        v_action:=CASE WHEN _approve THEN 'return_approve'
          ELSE 'return_reject' END;
        UPDATE pos.returns SET status=CASE WHEN _approve THEN 'approved'
          ELSE 'rejected' END,approved_by=v_actor,
          approved_at=clock_timestamp(),review_request_id=_request
          WHERE organization_id=_owner AND id=_return;
        INSERT INTO pos.transaction_audit(
          id,organization_id,actor_id,sale_id,action,request_id)
        VALUES(gen_random_uuid(),_owner,v_actor,v_return.sale_id,v_action,_request);
        RETURN jsonb_build_object('organization_id',_owner,
          'return_id',_return,'status',CASE WHEN _approve
          THEN 'approved' ELSE 'rejected' END,'replay',false);
      END $review$;
      REVOKE ALL ON FUNCTION pos.request_assigned_cashier_return(
        text,text,uuid,uuid,text,jsonb) FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos.review_cashier_return(
        text,text,uuid,uuid,boolean) FROM PUBLIC;
    """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$ BEGIN
        IF EXISTS(SELECT 1 FROM pos.returns) THEN
          RAISE EXCEPTION 'pos_return_history_requires_forward_recovery'; END IF;
      END $guard$;
      DROP FUNCTION pos.review_cashier_return(text,text,uuid,uuid,boolean);
      DROP FUNCTION pos.request_assigned_cashier_return(text,text,uuid,uuid,text,jsonb);
      DROP INDEX pos.uq_pos_return_review_request;
      ALTER TABLE pos.returns DROP COLUMN review_request_id;
    """);
}