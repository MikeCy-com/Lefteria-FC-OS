using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Lease-only AI education worker boundary. A separately provisioned DB role
/// may claim a structured, PII-free brief and save an unpublished AI draft.
/// The worker has no direct table rights, reviewer identity, or publish path.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261112140000_HubAiEducationWorker")]
public sealed class HubAiEducationWorker : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            ALTER TABLE hub.ai_education_briefs DROP CONSTRAINT ck_ai_brief_status;
            ALTER TABLE hub.ai_education_briefs
              ADD CONSTRAINT ck_ai_brief_status
                CHECK (status IN ('pending','processing','completed','failed'));
            ALTER TABLE hub.ai_education_briefs
              ADD COLUMN attempts smallint NOT NULL DEFAULT 0,
              ADD COLUMN lease_id uuid,
              ADD COLUMN leased_until timestamptz,
              ADD COLUMN started_at timestamptz,
              ADD COLUMN completed_at timestamptz,
              ADD COLUMN content_id uuid REFERENCES hub.content(id) ON DELETE RESTRICT,
              ADD COLUMN last_error varchar(32),
              ADD CONSTRAINT ck_ai_brief_attempts CHECK (attempts BETWEEN 0 AND 3),
              ADD CONSTRAINT ck_ai_brief_leased CHECK (
                (status='processing') =
                (lease_id IS NOT NULL AND leased_until IS NOT NULL)),
              ADD CONSTRAINT ck_ai_brief_completed CHECK (
                (status='completed')=(content_id IS NOT NULL AND completed_at IS NOT NULL));
            CREATE INDEX ix_ai_brief_status_lease ON hub.ai_education_briefs(
              status,leased_until,created_at,id);

            CREATE TABLE hub.ai_education_job_events(
              id uuid PRIMARY KEY,
              brief_id uuid NOT NULL REFERENCES hub.ai_education_briefs(id) ON DELETE RESTRICT,
              organization_id varchar(32) NOT NULL REFERENCES club.organizations(id) ON DELETE RESTRICT,
              event varchar(16) NOT NULL,
              occurred_at timestamptz NOT NULL DEFAULT statement_timestamp(),
              CONSTRAINT ck_ai_job_event CHECK (event IN ('claimed','completed','retry','failed'))
            );
            CREATE INDEX ix_ai_job_events_daily ON hub.ai_education_job_events(
              organization_id,event,occurred_at DESC);
            REVOKE ALL ON TABLE hub.ai_education_job_events FROM PUBLIC;
            ALTER TABLE hub.ai_education_job_events ENABLE ROW LEVEL SECURITY;

            CREATE FUNCTION hub.claim_ai_education_brief(_lease uuid)
            RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $claim_brief$
            DECLARE v_job hub.ai_education_briefs%ROWTYPE;
            BEGIN
              IF _lease IS NULL THEN
                RAISE EXCEPTION 'invalid_ai_lease' USING ERRCODE='22023';
              END IF;
              -- Serialize claims to enforce global and per-organization caps
              -- even when many worker instances are accidentally started.
              PERFORM pg_advisory_xact_lock(520261112);
              UPDATE hub.ai_education_briefs SET
                status='failed',lease_id=NULL,leased_until=NULL,
                last_error='lease_exhausted'
                WHERE status='processing' AND attempts>=3
                  AND leased_until<clock_timestamp();
              SELECT * INTO v_job
                FROM hub.ai_education_briefs b
                WHERE (b.status='pending'
                    OR (b.status='processing' AND b.leased_until<clock_timestamp()))
                  AND b.attempts<3
                  AND (SELECT count(*)
                       FROM hub.ai_education_job_events e
                       WHERE e.organization_id=b.organization_id
                         AND e.event='claimed'
                         AND e.occurred_at>=date_trunc('day',clock_timestamp())) < 10
                  AND (SELECT count(*)
                       FROM hub.ai_education_job_events e
                       WHERE e.event='claimed'
                         AND e.occurred_at>=date_trunc('day',clock_timestamp())) < 20
                ORDER BY b.created_at,b.id
                LIMIT 1 FOR UPDATE SKIP LOCKED;
              IF NOT FOUND THEN RETURN 'null'::jsonb; END IF;

              UPDATE hub.ai_education_briefs
                SET status='processing',attempts=attempts+1,
                  started_at=clock_timestamp(),lease_id=_lease,
                  leased_until=clock_timestamp()+interval '90 seconds',
                  last_error=NULL
                WHERE id=v_job.id;
              INSERT INTO hub.ai_education_job_events(id,brief_id,organization_id,event)
              VALUES(gen_random_uuid(),v_job.id,v_job.organization_id,'claimed');

              -- Never return requested_by, member/player names, private
              -- rosters, billing info, device tokens, or coach observations.
              RETURN jsonb_build_object('id',v_job.id,
                'organization_id',v_job.organization_id,
                'audience',v_job.audience,
                'age_min',v_job.age_min,'age_max',v_job.age_max,
                'topic',v_job.topic,'language',v_job.language,
                'lease_id',_lease,'attempts',v_job.attempts+1);
            END $claim_brief$;
            REVOKE ALL ON FUNCTION hub.claim_ai_education_brief(uuid) FROM PUBLIC;

            CREATE FUNCTION hub.complete_ai_education_brief(
              _id uuid,_lease uuid,_content_id uuid,_revision_id uuid,
              _title text,_body text,_model text,_prompt_version text
            ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $complete_brief$
            DECLARE v_job hub.ai_education_briefs%ROWTYPE;
            BEGIN
              IF _id IS NULL OR _lease IS NULL OR _content_id IS NULL OR _revision_id IS NULL
                OR _title IS NULL OR length(_title) NOT BETWEEN 8 AND 180
                OR _title<>btrim(_title) OR _title ~ '[[:cntrl:]]'
                OR _body IS NULL OR length(_body) NOT BETWEEN 80 AND 6000
                OR _body<>btrim(_body) OR _body ~ '[[:cntrl:]]'
                OR _model IS NULL OR length(_model) NOT BETWEEN 1 AND 80
                OR _model !~ '^[A-Za-z0-9_.-]+$'
                OR _prompt_version IS DISTINCT FROM 'education-v1'
              THEN RAISE EXCEPTION 'invalid_ai_draft' USING ERRCODE='22023';
              END IF;

              SELECT * INTO v_job FROM hub.ai_education_briefs
                WHERE id=_id FOR UPDATE;
              IF NOT FOUND OR v_job.status<>'processing'
                OR v_job.lease_id IS DISTINCT FROM _lease
                OR v_job.leased_until<=clock_timestamp() THEN
                RAISE EXCEPTION 'ai_lease_expired' USING ERRCODE='40001';
              END IF;

              INSERT INTO hub.content
                (id,organization_id,kind,status,title,audience_parent,
                 audience_player,age_min,age_max,provenance,version)
              VALUES (_content_id,v_job.organization_id,'article','ai_draft',
                _title,v_job.audience='parent',v_job.audience='player',
                v_job.age_min,v_job.age_max,'ai',1);
              INSERT INTO hub.revisions
                (id,content_id,revision,body_text,text_review,
                 visual_review,source_provenance)
              VALUES (_revision_id,_content_id,1,_body,'pending','pending',
                'provider=openai;model='||_model||';prompt='||_prompt_version
                  ||';brief='||_id::text);
              INSERT INTO hub.review_events
                (id,content_id,revision_id,admin_account_id,action,metadata)
              VALUES (gen_random_uuid(),_content_id,_revision_id,NULL,'generated',
                jsonb_build_object('source','ai_worker','brief_id',_id,
                  'model',_model,'prompt_version',_prompt_version));
              UPDATE hub.ai_education_briefs SET
                status='completed',lease_id=NULL,leased_until=NULL,
                completed_at=clock_timestamp(),content_id=_content_id,
                last_error=NULL WHERE id=_id;
              INSERT INTO hub.ai_education_job_events(id,brief_id,organization_id,event)
              VALUES(gen_random_uuid(),_id,v_job.organization_id,'completed');

              RETURN jsonb_build_object('id',_id,'content_id',_content_id,
                'organization_id',v_job.organization_id,'status','completed',
                'draft_status','ai_draft','text_review','pending',
                'visual_review','pending');
            END $complete_brief$;
            REVOKE ALL ON FUNCTION hub.complete_ai_education_brief(
              uuid,uuid,uuid,uuid,text,text,text,text) FROM PUBLIC;

            CREATE FUNCTION hub.fail_ai_education_brief(
              _id uuid,_lease uuid,_reason text
            ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $fail_brief$
            DECLARE v_job hub.ai_education_briefs%ROWTYPE;
            DECLARE v_new text;
            BEGIN
              IF _id IS NULL OR _lease IS NULL OR _reason NOT IN (
                'provider_unavailable','invalid_output','moderation_blocked',
                'provider_rejected','quota_exceeded')
              THEN RAISE EXCEPTION 'invalid_ai_failure' USING ERRCODE='22023';
              END IF;
              SELECT * INTO v_job FROM hub.ai_education_briefs
                WHERE id=_id FOR UPDATE;
              IF NOT FOUND OR v_job.status<>'processing'
                OR v_job.lease_id IS DISTINCT FROM _lease
                OR v_job.leased_until<=clock_timestamp() THEN
                RAISE EXCEPTION 'ai_lease_expired' USING ERRCODE='40001';
              END IF;
              v_new:=CASE WHEN _reason='provider_unavailable' AND v_job.attempts<3
                THEN 'pending' ELSE 'failed' END;
              UPDATE hub.ai_education_briefs
                SET status=v_new,lease_id=NULL,leased_until=NULL,last_error=_reason
                WHERE id=_id;
              INSERT INTO hub.ai_education_job_events(id,brief_id,organization_id,event)
              VALUES(gen_random_uuid(),_id,v_job.organization_id,
                CASE WHEN v_new='pending' THEN 'retry' ELSE 'failed' END);
              RETURN jsonb_build_object('id',_id,'status',v_new);
            END $fail_brief$;
            REVOKE ALL ON FUNCTION hub.fail_ai_education_brief(uuid,uuid,text)
              FROM PUBLIC;
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP FUNCTION hub.fail_ai_education_brief(uuid,uuid,text);
            DROP FUNCTION hub.complete_ai_education_brief(
              uuid,uuid,uuid,uuid,text,text,text,text);
            DROP FUNCTION hub.claim_ai_education_brief(uuid);
            DROP TABLE hub.ai_education_job_events;
            DROP INDEX hub.ix_ai_brief_status_lease;
            ALTER TABLE hub.ai_education_briefs
              DROP CONSTRAINT ck_ai_brief_completed,
              DROP CONSTRAINT ck_ai_brief_leased,
              DROP CONSTRAINT ck_ai_brief_attempts,
              DROP COLUMN last_error,
              DROP COLUMN content_id,
              DROP COLUMN completed_at,
              DROP COLUMN started_at,
              DROP COLUMN leased_until,
              DROP COLUMN lease_id,
              DROP COLUMN attempts;
            ALTER TABLE hub.ai_education_briefs DROP CONSTRAINT ck_ai_brief_status;
            ALTER TABLE hub.ai_education_briefs
              ADD CONSTRAINT ck_ai_brief_status CHECK (status='pending');
            """);
}
