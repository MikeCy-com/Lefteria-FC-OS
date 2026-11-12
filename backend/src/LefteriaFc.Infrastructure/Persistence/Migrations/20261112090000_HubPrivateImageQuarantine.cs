using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Private, sanitized JPEG quarantine only. No media reader or publish path.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261112090000_HubPrivateImageQuarantine")]
public sealed class HubPrivateImageQuarantine : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            CREATE TABLE hub.quarantined_image_bytes (
              media_id uuid PRIMARY KEY REFERENCES hub.media_assets(id) ON DELETE RESTRICT,
              jpeg bytea NOT NULL,
              received_at timestamptz NOT NULL DEFAULT statement_timestamp(),
              CONSTRAINT ck_hub_image_bytes CHECK (
                octet_length(jpeg) BETWEEN 4 AND 1048576
                AND substring(jpeg from 1 for 2)=decode('ffd8','hex')
                AND substring(jpeg from octet_length(jpeg)-1 for 2)=decode('ffd9','hex'))
            );
            REVOKE ALL ON TABLE hub.quarantined_image_bytes FROM PUBLIC;
            ALTER TABLE hub.quarantined_image_bytes ENABLE ROW LEVEL SECURITY;
            CREATE UNIQUE INDEX ux_hub_one_quarantined_image_per_revision
              ON hub.media_assets(revision_id) WHERE kind='image';

            CREATE FUNCTION hub.quarantine_admin_image(
              _digest text,_owner text,_content uuid,_version bigint,
              _media uuid,_alt text,_jpeg bytea
            ) RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $hub_image$
            DECLARE
              v_actor uuid; v_session uuid; v_role text;
              v_content hub.content%ROWTYPE;
              v_revision hub.revisions%ROWTYPE;
              v_sha text;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                OR _content IS NULL OR _media IS NULL
                OR _version IS NULL OR _version<1
                OR _alt IS NULL OR length(_alt) NOT BETWEEN 1 AND 500
                OR btrim(_alt)<>_alt OR _alt ~ '[[:cntrl:]]'
                OR _jpeg IS NULL OR octet_length(_jpeg) NOT BETWEEN 4 AND 1048576
                OR substring(_jpeg from 1 for 2)<>decode('ffd8','hex')
                OR substring(_jpeg from octet_length(_jpeg)-1 for 2)<>decode('ffd9','hex')
              THEN RAISE EXCEPTION 'invalid_hub_image' USING ERRCODE='22023';
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
              IF v_actor IS NULL OR v_role NOT IN ('manager','super_admin','editor') THEN
                RAISE EXCEPTION 'hub_image_permission_denied' USING ERRCODE='42501';
              END IF;

              SELECT * INTO v_content FROM hub.content
                WHERE id=_content AND organization_id=_owner FOR UPDATE;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'hub_draft_not_found' USING ERRCODE='P0002';
              END IF;
              IF v_content.version<>_version OR v_content.status<>'ai_draft' THEN
                RAISE EXCEPTION 'hub_image_version_conflict' USING ERRCODE='40001';
              END IF;
              SELECT * INTO v_revision FROM hub.revisions
                WHERE content_id=_content ORDER BY revision DESC LIMIT 1 FOR UPDATE;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'hub_revision_not_found' USING ERRCODE='P0002';
              END IF;
              -- No verified preview or malware-scanning service is connected.
              -- Keep the media quarantined and never mutate the draft's review.
              v_sha:=encode(sha256(_jpeg),'hex');
              INSERT INTO hub.media_assets
                (id,revision_id,kind,storage_key,sha256,mime_type,
                 byte_count,alt_text,moderation,generated_by_ai)
              VALUES
                (_media,v_revision.id,'image','db-quarantine/'||_media::text,
                 v_sha,'image/jpeg',octet_length(_jpeg),_alt,'quarantined',false);
              INSERT INTO hub.quarantined_image_bytes(media_id,jpeg) VALUES(_media,_jpeg);
              INSERT INTO hub.review_events
                (id,content_id,revision_id,admin_account_id,action,metadata)
              VALUES(gen_random_uuid(),_content,v_revision.id,v_actor,'revised',
                jsonb_build_object('source','admin_image_quarantine','media_id',_media));
              INSERT INTO audit.security_events
                (id,actor_realm,occurred_at,actor_identity_id,device_session_id,
                 action,outcome,organization_id,reason_code)
              VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
                'admin.hub.image.quarantine','succeeded',_owner,'image/jpeg');
              RETURN jsonb_build_object(
                'id',_media,'organization_id',_owner,'content_id',_content,
                'revision_id',v_revision.id,'status','quarantined',
                'mime_type','image/jpeg','byte_count',octet_length(_jpeg));
            END $hub_image$;
            REVOKE ALL ON FUNCTION hub.quarantine_admin_image(
              text,text,uuid,bigint,uuid,text,bytea) FROM PUBLIC;
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP FUNCTION hub.quarantine_admin_image(text,text,uuid,bigint,uuid,text,bytea);
            DROP INDEX hub.ux_hub_one_quarantined_image_per_revision;
            DROP TABLE hub.quarantined_image_bytes;
            """);
}
