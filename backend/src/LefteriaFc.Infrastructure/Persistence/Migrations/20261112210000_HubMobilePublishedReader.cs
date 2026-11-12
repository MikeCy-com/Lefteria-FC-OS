using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Private published-article projection. Authenticated parents and players only.
/// Player age-targeted articles remain withheld until canonical age is available.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261112210000_HubMobilePublishedReader")]
public sealed class HubMobilePublishedReader : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            CREATE FUNCTION hub.read_mobile_published_education(
              _identity uuid,_phone text,_role text,_expires timestamptz,
              _owner text,_offset integer
            ) RETURNS jsonb
            LANGUAGE plpgsql STABLE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $hub_published_mobile$
            DECLARE
              v_items jsonb;
            BEGIN
              IF _identity IS NULL OR _phone IS NULL
                OR _phone !~ '^[1-9][0-9]{7,14}$'
                OR _role NOT IN ('parent','player')
                OR _expires IS NULL OR _expires<=clock_timestamp()
                OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                OR _offset IS NULL OR _offset<0 OR _offset>1000
              THEN
                RAISE EXCEPTION 'hub_role_scope_denied' USING ERRCODE='42501';
              END IF;
              -- The mobile JWT is only a hint. Revalidate live identity/phone
              -- and exact active role in the authoritative database on every read.
              PERFORM 1 FROM identity.mobile_identities i
                WHERE i.id=_identity AND i.normalized_phone=_phone
                  AND i.role=_role AND i.is_active
                FOR SHARE;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'hub_current_identity_denied' USING ERRCODE='42501';
              END IF;
              WITH selected AS (
                SELECT c.id,c.organization_id,c.title,c.kind,c.published_at,
                       c.audience_parent,c.audience_player,c.age_min,c.age_max,
                       r.body_text,r.revision
                FROM hub.content c
                JOIN LATERAL (
                  SELECT rr.id,rr.body_text,rr.revision,rr.text_review,rr.visual_review
                  FROM hub.revisions rr WHERE rr.content_id=c.id
                  ORDER BY rr.revision DESC LIMIT 1
                ) r ON true
                WHERE c.organization_id=_owner AND c.status='published'
                  AND c.kind='article' AND c.approved_by IS NOT NULL
                  AND c.approved_at IS NOT NULL AND c.published_at IS NOT NULL
                  AND r.text_review='passed' AND r.visual_review='passed'
                  AND ((_role='parent' AND c.audience_parent) OR
                       (_role='player' AND c.audience_player AND
                        c.age_min IS NULL AND c.age_max IS NULL))
                  AND NOT EXISTS (
                    SELECT 1 FROM hub.media_assets m
                    WHERE m.revision_id=r.id
                      AND (m.moderation<>'reviewed' OR m.kind<>'image')
                  )
                ORDER BY c.published_at DESC,c.id DESC
                LIMIT 20 OFFSET _offset
              )
              SELECT coalesce(jsonb_agg(jsonb_build_object(
                'id',s.id,'organization_id',s.organization_id,
                'title',s.title,'body',s.body_text,'published_at',s.published_at,
                'audience',_role,'age_min',s.age_min,'age_max',s.age_max)
                ORDER BY s.published_at DESC,s.id DESC),'[]'::jsonb)
              INTO v_items FROM selected s;
              RETURN jsonb_build_object(
                'organization_id',_owner,'audience',_role,'items',v_items);
            END $hub_published_mobile$;
            REVOKE ALL ON FUNCTION hub.read_mobile_published_education(
              uuid,text,text,timestamptz,text,integer) FROM PUBLIC;
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP FUNCTION hub.read_mobile_published_education(
              uuid,text,text,timestamptz,text,integer);
            """);
}
