using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Organization-scoped, structured AI education briefs. The queue stores
/// no child identities, coach statements, free-form PII, provider credentials
/// or generated material. No worker or publishing authorization is granted.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261112120000_HubAiAudienceBriefQueue")]
public sealed class HubAiAudienceBriefQueue : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            CREATE TABLE hub.ai_education_briefs (
              id uuid PRIMARY KEY,
              organization_id varchar(32) NOT NULL REFERENCES club.organizations(id) ON DELETE RESTRICT,
              requested_by uuid NOT NULL REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
              audience varchar(12) NOT NULL,
              age_min smallint NOT NULL,
              age_max smallint NOT NULL,
              topic varchar(24) NOT NULL,
              language varchar(2) NOT NULL,
              status varchar(16) NOT NULL DEFAULT 'pending',
              created_at timestamptz NOT NULL DEFAULT statement_timestamp(),
              CONSTRAINT ck_ai_brief_owner CHECK (organization_id IN ('lefteria_fc','lefteria_fc_academy')),
              CONSTRAINT ck_ai_brief_audience CHECK (audience IN ('parent','player')),
              CONSTRAINT ck_ai_brief_age CHECK (age_min BETWEEN 6 AND 18 AND age_max BETWEEN age_min AND 18),
              CONSTRAINT ck_ai_brief_topic CHECK (
                (audience='parent' AND topic IN ('support','communication','development','wellbeing'))
                OR (audience='player' AND topic IN ('confidence','teamwork','resilience','learning','wellbeing'))),
              CONSTRAINT ck_ai_brief_language CHECK (language IN ('el','en')),
              CONSTRAINT ck_ai_brief_status CHECK (status='pending')
            );
            CREATE INDEX ix_ai_brief_owner_queue ON hub.ai_education_briefs(organization_id,created_at,id);
            REVOKE ALL ON TABLE hub.ai_education_briefs FROM PUBLIC;
            ALTER TABLE hub.ai_education_briefs ENABLE ROW LEVEL SECURITY;

            CREATE FUNCTION hub.request_admin_ai_education(
              _digest text,_owner text,_id uuid,_audience text,
              _age_min smallint,_age_max smallint,_topic text,_language text
            ) RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $ai_brief$
            DECLARE v_actor uuid; v_session uuid; v_role text;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                OR _id IS NULL OR _audience NOT IN ('parent','player')
                OR _age_min IS NULL OR _age_max IS NULL
                OR _age_min < 6 OR _age_max > 18 OR _age_min > _age_max
                OR _language NOT IN ('el','en')
                OR NOT (
                  (_audience='parent' AND _topic IN ('support','communication','development','wellbeing'))
                  OR (_audience='player' AND _topic IN ('confidence','teamwork','resilience','learning','wellbeing'))
                )
              THEN RAISE EXCEPTION 'invalid_ai_education_brief' USING ERRCODE='22023';
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
                RAISE EXCEPTION 'ai_education_permission_denied' USING ERRCODE='42501';
              END IF;

              INSERT INTO hub.ai_education_briefs
                (id,organization_id,requested_by,audience,age_min,age_max,topic,language)
              VALUES (_id,_owner,v_actor,_audience,_age_min,_age_max,_topic,_language);
              INSERT INTO audit.security_events
                (id,actor_realm,occurred_at,actor_identity_id,device_session_id,
                 action,outcome,organization_id,reason_code)
              VALUES (gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
                'admin.hub.ai.education.request','succeeded',_owner,_audience);

              RETURN jsonb_build_object('id',_id,'organization_id',_owner,
                'audience',_audience,'age_min',_age_min,'age_max',_age_max,
                'topic',_topic,'language',_language,'status','pending');
            END $ai_brief$;
            REVOKE ALL ON FUNCTION hub.request_admin_ai_education(
              text,text,uuid,text,smallint,smallint,text,text) FROM PUBLIC;
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP FUNCTION hub.request_admin_ai_education(text,text,uuid,text,smallint,smallint,text,text);
            DROP TABLE hub.ai_education_briefs;
            """);
}
