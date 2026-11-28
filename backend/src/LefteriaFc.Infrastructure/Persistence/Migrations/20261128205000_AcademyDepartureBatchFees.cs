using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Atomic exact-child cancellation of explicitly reviewed future fees.
/// Reuses the existing locked, audited, replay-safe single-fee operation.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261128205000_AcademyDepartureBatchFees")]
public sealed class AcademyDepartureBatchFees : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION finance.cancel_admin_player_future_fees(
        _digest text,_owner text,_player text,_fees uuid[],_requests uuid[],_reason text)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $batch$
      DECLARE v_role text; v_row record; v_result jsonb; v_results jsonb:='[]'::jsonb;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner IS DISTINCT FROM 'lefteria_fc_academy'
          OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128 OR _player<>btrim(_player)
          OR _player ~ '[[:cntrl:]]'
          OR coalesce(cardinality(_fees),0) NOT BETWEEN 1 AND 120
          OR cardinality(_requests) IS DISTINCT FROM cardinality(_fees)
          OR (SELECT count(DISTINCT x) FROM unnest(_fees) x)<>cardinality(_fees)
          OR (SELECT count(DISTINCT x) FROM unnest(_requests) x)<>cardinality(_requests)
          OR '00000000-0000-0000-0000-000000000000'::uuid=ANY(_fees)
          OR '00000000-0000-0000-0000-000000000000'::uuid=ANY(_requests)
          OR _reason IS NULL OR length(_reason) NOT BETWEEN 8 AND 240
          OR _reason<>btrim(_reason) OR _reason ~ '[[:cntrl:]]'
        THEN RAISE EXCEPTION 'fee_batch_invalid' USING ERRCODE='22023'; END IF;
        PERFORM identity.lock_admin_club_authority(_digest,_owner);
        SELECT a.role INTO v_role FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role<>'super_admin'
        THEN RAISE EXCEPTION 'fee_batch_denied' USING ERRCODE='42501'; END IF;
        IF NOT EXISTS(SELECT 1 FROM club.players p
          WHERE p.organization_id=_owner AND p.id=_player AND p.team_type='Academy')
        THEN RAISE EXCEPTION 'fee_batch_denied' USING ERRCODE='42501'; END IF;
        -- Lock all selected records in deterministic order and check exact child
        -- before any cancellation. An exception rolls back every prior write.
        FOR v_row IN SELECT f.id,f.player_id FROM finance.training_fee_issues f
          WHERE f.organization_id=_owner AND f.id=ANY(_fees) ORDER BY f.id FOR UPDATE
        LOOP
          IF v_row.player_id IS DISTINCT FROM _player
          THEN RAISE EXCEPTION 'fee_batch_denied' USING ERRCODE='42501'; END IF;
        END LOOP;
        IF (SELECT count(*) FROM finance.training_fee_issues f WHERE f.organization_id=_owner
          AND f.player_id=_player AND f.id=ANY(_fees))<>cardinality(_fees)
        THEN RAISE EXCEPTION 'fee_batch_denied' USING ERRCODE='42501'; END IF;
        FOR v_row IN SELECT fee,request FROM unnest(_fees,_requests) AS x(fee,request) ORDER BY fee
        LOOP
          v_result:=finance.cancel_admin_future_training_fee(_digest,_owner,v_row.fee,v_row.request,_reason);
          IF v_result->>'player_id' IS DISTINCT FROM _player OR v_result->>'status' IS DISTINCT FROM 'cancelled'
          THEN RAISE EXCEPTION 'fee_batch_conflict' USING ERRCODE='23505'; END IF;
          v_results:=v_results||jsonb_build_array(v_result);
        END LOOP;
        RETURN jsonb_build_object('organization_id',_owner,'player_id',_player,'fees',v_results);
      END $batch$;
      REVOKE ALL ON FUNCTION finance.cancel_admin_player_future_fees(text,text,text,uuid[],uuid[],text) FROM PUBLIC;
      """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION finance.cancel_admin_player_future_fees(text,text,text,uuid[],uuid[],text);
      """);
}
