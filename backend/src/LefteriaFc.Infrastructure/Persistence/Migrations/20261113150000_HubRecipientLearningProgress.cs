using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Persistent, per-identity progression through human-approved education.
/// Reading never marks a lesson as completed. Fan tracks can be persisted
/// separately, but no fan delivery permission is granted until verified
/// Payabl entitlements have their own authoritative checkout boundary.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261113150000_HubRecipientLearningProgress")]
public sealed class HubRecipientLearningProgress : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            CREATE TABLE hub.education_completions (
              identity_id uuid NOT NULL REFERENCES identity.mobile_identities(id) ON DELETE RESTRICT,
              organization_id varchar(32) NOT NULL REFERENCES club.organizations(id) ON DELETE RESTRICT,
              viewer_role varchar(8) NOT NULL CHECK(viewer_role IN ('parent','player','fan')),
              audience varchar(8) NOT NULL CHECK(audience IN ('parent','player')),
              content_id uuid NOT NULL REFERENCES hub.content(id) ON DELETE RESTRICT,
              revision_id uuid NOT NULL REFERENCES hub.revisions(id) ON DELETE RESTRICT,
              completed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
              PRIMARY KEY(identity_id,organization_id,viewer_role,audience,content_id,revision_id),
              CONSTRAINT ck_education_role_audience CHECK(
                viewer_role='fan' OR viewer_role=audience),
              CONSTRAINT ck_education_completion_org CHECK(
                organization_id IN ('lefteria_fc','lefteria_fc_academy'))
            );
            CREATE INDEX ix_education_completion_content
              ON hub.education_completions(content_id,revision_id);
            ALTER TABLE hub.education_completions ENABLE ROW LEVEL SECURITY;
            REVOKE ALL ON hub.education_completions FROM PUBLIC;

            -- Internal helper is intentionally not granted to the runtime.
            -- There is no per-account "join date" filter: the oldest *currently*
            -- published and approved lesson is offered first to each person.
            CREATE FUNCTION hub.current_mobile_education_lesson(
              _identity uuid,_phone text,_role text,_expires timestamptz,_owner text
            ) RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $education_next$
            DECLARE v_lesson jsonb;
            BEGIN
              IF _identity IS NULL OR _phone IS NULL
                OR _phone !~ '^[1-9][0-9]{7,14}$'
                OR _role NOT IN ('parent','player')
                OR _expires IS NULL OR _expires<=clock_timestamp()
                OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
              THEN
                RAISE EXCEPTION 'education_scope_denied' USING ERRCODE='42501';
              END IF;
              PERFORM 1 FROM identity.mobile_identities i
                WHERE i.id=_identity AND i.normalized_phone=_phone
                  AND i.role=_role AND i.is_active
                FOR SHARE;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'education_identity_denied' USING ERRCODE='42501';
              END IF;
              SELECT jsonb_build_object(
                  'id',c.id,'organization_id',c.organization_id,
                  'audience',_role,'title',c.title,'body',r.body_text,
                  'published_at',c.published_at,'revision_id',r.id,
                  'age_min',c.age_min,'age_max',c.age_max)
              INTO v_lesson
              FROM hub.content c
              JOIN LATERAL (
                SELECT rr.id,rr.body_text,rr.text_review,rr.visual_review
                FROM hub.revisions rr WHERE rr.content_id=c.id
                ORDER BY rr.revision DESC LIMIT 1
              ) r ON true
              WHERE c.organization_id=_owner AND c.status='published'
                AND c.kind='article' AND c.approved_by IS NOT NULL
                AND c.approved_at IS NOT NULL AND c.published_at IS NOT NULL
                AND c.published_at<=clock_timestamp()
                AND r.text_review='passed' AND r.visual_review='passed'
                AND ((_role='parent' AND c.audience_parent) OR
                     (_role='player' AND c.audience_player))
                -- Until an independently verified player/child age is linked,
                -- no age-banded article is unlocked by a client selector.
                AND c.age_min IS NULL AND c.age_max IS NULL
                AND NOT EXISTS (
                  SELECT 1 FROM hub.media_assets m
                  WHERE m.revision_id=r.id
                    AND (m.kind<>'image' OR m.moderation<>'reviewed'))
                AND NOT EXISTS (
                  SELECT 1 FROM hub.education_completions done
                  WHERE done.identity_id=_identity
                    AND done.organization_id=_owner
                    AND done.viewer_role=_role AND done.audience=_role
                    AND done.content_id=c.id AND done.revision_id=r.id)
              ORDER BY c.published_at ASC,c.id ASC
              LIMIT 1;
              RETURN jsonb_build_object(
                'organization_id',_owner,'audience',_role,'next',v_lesson);
            END $education_next$;
            REVOKE ALL ON FUNCTION hub.current_mobile_education_lesson(
              uuid,text,text,timestamptz,text) FROM PUBLIC;

            CREATE FUNCTION hub.read_mobile_next_education(
              _identity uuid,_phone text,_role text,_expires timestamptz,_owner text
            ) RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $education_read$
            BEGIN
              RETURN hub.current_mobile_education_lesson(
                _identity,_phone,_role,_expires,_owner);
            END $education_read$;
            REVOKE ALL ON FUNCTION hub.read_mobile_next_education(
              uuid,text,text,timestamptz,text) FROM PUBLIC;

            -- Explicit user completion, never an automatic GET side effect.
            -- Serialize concurrent completion calls per identity and permit
            -- ONLY the currently first eligible uncompleted lesson.
            CREATE FUNCTION hub.complete_mobile_next_education(
              _identity uuid,_phone text,_role text,_expires timestamptz,
              _owner text,_content uuid,_revision uuid
            ) RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $education_complete$
            DECLARE v_page jsonb; v_lesson jsonb;
            BEGIN
              IF _content IS NULL OR _revision IS NULL THEN
                RAISE EXCEPTION 'education_invalid_completion' USING ERRCODE='22023';
              END IF;
              -- Lock the exact identity row for the entire transaction,
              -- including the subsequent eligibility check and insert.
              PERFORM 1 FROM identity.mobile_identities i
                WHERE i.id=_identity AND i.normalized_phone=_phone
                  AND i.role=_role AND i.is_active
                FOR UPDATE;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'education_identity_denied' USING ERRCODE='42501';
              END IF;
              v_page:=hub.current_mobile_education_lesson(
                _identity,_phone,_role,_expires,_owner);
              v_lesson:=v_page->'next';
              IF v_lesson IS NULL OR v_lesson='null'::jsonb
                OR (v_lesson->>'id')::uuid<>_content
                OR (v_lesson->>'revision_id')::uuid<>_revision
              THEN
                RAISE EXCEPTION 'education_completion_conflict' USING ERRCODE='40001';
              END IF;
              INSERT INTO hub.education_completions(
                identity_id,organization_id,viewer_role,audience,content_id,revision_id)
              VALUES (_identity,_owner,_role,_role,_content,_revision);
              RETURN jsonb_build_object('organization_id',_owner,
                'audience',_role,'completed_id',_content,
                'completed_revision_id',_revision);
            END $education_complete$;
            REVOKE ALL ON FUNCTION hub.complete_mobile_next_education(
              uuid,text,text,timestamptz,text,uuid,uuid) FROM PUBLIC;
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP FUNCTION hub.complete_mobile_next_education(
              uuid,text,text,timestamptz,text,uuid,uuid);
            DROP FUNCTION hub.read_mobile_next_education(
              uuid,text,text,timestamptz,text);
            DROP FUNCTION hub.current_mobile_education_lesson(
              uuid,text,text,timestamptz,text);
            DROP TABLE hub.education_completions;
            """);
}
