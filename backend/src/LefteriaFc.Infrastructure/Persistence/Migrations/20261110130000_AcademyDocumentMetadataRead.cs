using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Manager-only private document metadata with audited per-document access.
/// Never returns file bytes, object keys, hashes or raw medical content.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110130000_AcademyDocumentMetadataRead")]
public sealed class AcademyDocumentMetadataRead : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION academy_private.read_admin_player_document_metadata(
        _digest text,_owner text,_player text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $academy_document_read$
      DECLARE
        v_role text; v_actor uuid; v_session uuid;
        v_count integer; v_rows jsonb;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
           OR _owner<>'lefteria_fc_academy'
           OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
           OR _player ~ '[[:cntrl:]]' THEN
          RAISE EXCEPTION 'document_scope_invalid' USING ERRCODE='22023';
        END IF;
        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'document_authority_revoked' USING ERRCODE='42501';
        END;
        SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
          RAISE EXCEPTION 'document_access_denied' USING ERRCODE='42501';
        END IF;
        IF NOT EXISTS (
          SELECT 1 FROM club.players p WHERE p.organization_id=_owner
            AND p.id=_player AND p.team_type='Academy'
            AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
        ) THEN
          RAISE EXCEPTION 'document_player_not_available' USING ERRCODE='42501';
        END IF;
        SELECT count(*) INTO v_count FROM academy_private.player_documents d
          WHERE d.organization_id=_owner AND d.player_id=_player
            AND d.withdrawn_at IS NULL;
        IF v_count>100 THEN
          RAISE EXCEPTION 'document_list_too_large' USING ERRCODE='54000';
        END IF;
        -- Audit every disclosed document before returning any metadata.
        -- An error rolls back both audit and disclosure.
        INSERT INTO academy_private.document_audit_events (
          id,document_id,actor_id,action,request_id)
        SELECT gen_random_uuid(),d.id,v_actor,'view',gen_random_uuid()
        FROM academy_private.player_documents d
        WHERE d.organization_id=_owner AND d.player_id=_player
          AND d.withdrawn_at IS NULL;
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
          'admin.academy.documents.list','succeeded',_owner,'metadata_only');

        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'id',d.id,'document_type',d.document_type,'title',d.title,
          'issue_date',d.issue_date,'expiry_date',d.expiry_date,
          'created_at',d.created_at,
          'expires_in_days',CASE WHEN d.expiry_date IS NULL THEN NULL
            ELSE d.expiry_date-(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date END,
          'status',CASE
            WHEN d.expiry_date IS NULL THEN 'no_expiry'
            WHEN d.expiry_date<(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date THEN 'expired'
            WHEN d.expiry_date<=(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date+7 THEN 'critical'
            WHEN d.expiry_date<=(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date+30 THEN 'warning'
            WHEN d.expiry_date<=(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date+60 THEN 'notice'
            WHEN d.expiry_date<=(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date+90 THEN 'watch'
            ELSE 'valid' END)
          ORDER BY d.created_at DESC,d.id),'[]'::jsonb)
        INTO v_rows FROM academy_private.player_documents d
        WHERE d.organization_id=_owner AND d.player_id=_player
          AND d.withdrawn_at IS NULL;
        RETURN jsonb_build_object('organization_id',_owner,
          'player_id',_player,'total',v_count,'documents',v_rows);
      END $academy_document_read$;
      REVOKE ALL ON FUNCTION academy_private.read_admin_player_document_metadata(
        text,text,text) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION academy_private.read_admin_player_document_metadata(text,text,text);
    """);
}
