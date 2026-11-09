using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Owner-locked Academy enquiry inbox with narrowly audited contact/lost
/// updates. Does not register a player, book a session, send SMS or expose
/// customer data to the public site or unrelated admin accounts.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261109120000_AcademyLeadAdminInbox")]
public sealed class AcademyLeadAdminInbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE marketing.lead_review_events (
        id uuid PRIMARY KEY,
        lead_id uuid NOT NULL REFERENCES marketing.public_leads(id) ON DELETE RESTRICT,
        organization_id varchar(32) NOT NULL
          REFERENCES club.organizations(id) ON DELETE RESTRICT
          CHECK (organization_id='lefteria_fc_academy'),
        actor_id uuid NOT NULL REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        previous_status varchar(16) NOT NULL CHECK (previous_status IN ('new','contacted')),
        next_status varchar(16) NOT NULL CHECK (next_status IN ('contacted','lost')),
        occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        CHECK ((previous_status='new' AND next_status IN ('contacted','lost'))
          OR (previous_status='contacted' AND next_status='lost'))
      );
      CREATE INDEX ix_marketing_lead_review_events
        ON marketing.lead_review_events(lead_id,occurred_at,id);
      REVOKE ALL ON marketing.lead_review_events FROM PUBLIC;
      ALTER TABLE marketing.lead_review_events ENABLE ROW LEVEL SECURITY;

      CREATE FUNCTION marketing.read_admin_academy_leads(
        _digest text,_owner text,_offset integer,_limit integer,_status text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $academy_leads_read$
      DECLARE v_role text; v_total integer; v_rows jsonb;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
           OR _owner<>'lefteria_fc_academy'
           OR _offset IS NULL OR _offset NOT BETWEEN 0 AND 20000
           OR _limit IS NULL OR _limit NOT BETWEEN 1 AND 50
           OR (_status IS NOT NULL AND _status NOT IN
               ('new','contacted','trial_booked','registered','lost'))
        THEN RAISE EXCEPTION 'crm_scope_invalid' USING ERRCODE='22023'; END IF;

        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'crm_authority_revoked' USING ERRCODE='42501';
        END;
        SELECT a.role INTO v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp()
            AND a.is_active AND NOT a.invite_pending
            AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
          RAISE EXCEPTION 'crm_access_denied' USING ERRCODE='42501';
        END IF;
        SELECT count(*) INTO v_total FROM marketing.public_leads l
          WHERE l.organization_id=_owner AND (_status IS NULL OR l.status=_status);
        IF v_total>200000 THEN
          RAISE EXCEPTION 'crm_result_too_large' USING ERRCODE='54000';
        END IF;
        SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',l.id,'organization_id',l.organization_id,
            'contact_name',l.contact_name,'phone',l.phone,'email',l.email,
            'player_age',l.player_age,'interested_in',l.interested_in,
            'message',l.message,'source',l.source,'partner_slug',l.partner_slug,
            'status',l.status,'created_at',l.created_at,
            'consent_marketing',l.consent_marketing)
            ORDER BY l.created_at DESC,l.id),'[]'::jsonb)
          INTO v_rows FROM (
            SELECT * FROM marketing.public_leads l
            WHERE l.organization_id=_owner AND (_status IS NULL OR l.status=_status)
            ORDER BY l.created_at DESC,l.id DESC
            LIMIT _limit OFFSET _offset
          ) l;
        RETURN jsonb_build_object('organization_id',_owner,
          'offset',_offset,'limit',_limit,'total',v_total,'leads',v_rows);
      END $academy_leads_read$;

      CREATE FUNCTION marketing.update_admin_academy_lead_status(
        _digest text,_owner text,_id uuid,_expected text,_next text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $academy_lead_status$
      DECLARE v_actor uuid; v_session uuid; v_role text;
              v_current text; v_event uuid:=gen_random_uuid();
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner<>'lefteria_fc_academy' OR _id IS NULL
          OR _expected IS NULL OR _next IS NULL
          OR _expected NOT IN ('new','contacted')
          OR _next NOT IN ('contacted','lost')
          OR NOT ((_expected='new' AND _next IN ('contacted','lost'))
            OR (_expected='contacted' AND _next='lost'))
        THEN RAISE EXCEPTION 'crm_invalid_transition' USING ERRCODE='22023';
        END IF;

        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'crm_authority_revoked' USING ERRCODE='42501';
        END;
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
          RAISE EXCEPTION 'crm_write_denied' USING ERRCODE='42501';
        END IF;
        SELECT status INTO v_current FROM marketing.public_leads
          WHERE id=_id AND organization_id=_owner FOR UPDATE;
        IF NOT FOUND THEN RAISE EXCEPTION 'crm_lead_not_found' USING ERRCODE='42501'; END IF;
        IF v_current=_next THEN
          RETURN jsonb_build_object('id',_id,'status',v_current,
            'changed',false,'replay',true);
        END IF;
        IF v_current<>_expected THEN
          RAISE EXCEPTION 'crm_status_changed' USING ERRCODE='23505';
        END IF;

        UPDATE marketing.public_leads SET status=_next
          WHERE id=_id AND organization_id=_owner AND status=_expected;
        IF NOT FOUND THEN RAISE EXCEPTION 'crm_status_changed' USING ERRCODE='23505'; END IF;
        INSERT INTO marketing.lead_review_events(
          id,lead_id,organization_id,actor_id,previous_status,next_status)
        VALUES(v_event,_id,_owner,v_actor,_expected,_next);
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
          'admin.crm.lead.status_changed','succeeded',_owner,_next);
        RETURN jsonb_build_object('id',_id,'status',_next,'changed',true,
          'replay',false,'event_id',v_event);
      END $academy_lead_status$;

      REVOKE ALL ON FUNCTION marketing.read_admin_academy_leads(
        text,text,integer,integer,text) FROM PUBLIC;
      REVOKE ALL ON FUNCTION marketing.update_admin_academy_lead_status(
        text,text,uuid,text,text) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$
      BEGIN
        IF EXISTS(SELECT 1 FROM marketing.lead_review_events LIMIT 1) THEN
          RAISE EXCEPTION 'cannot_drop_audited_lead_reviews' USING ERRCODE='23505';
        END IF;
      END $guard$;
      DROP FUNCTION marketing.update_admin_academy_lead_status(text,text,uuid,text,text);
      DROP FUNCTION marketing.read_admin_academy_leads(text,text,integer,integer,text);
      DROP TABLE marketing.lead_review_events;
    """);
}
