using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Canonical Academy city/site binding for POS plus site-only financial reads.
/// No finance, checkout, Payabl, fiscal policy or cashier runtime grants.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115910000_PosAcademySiteFinancialBoundary")]
public sealed class PosAcademySiteFinancialBoundary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- POS locations are operational records, not independent Academy geography.
      -- Each Academy POS location must explicitly reference ONE canonical site.
      CREATE TABLE pos.academy_football_location_links (
        organization_id varchar(32) NOT NULL DEFAULT 'lefteria_fc_academy'
          CHECK(organization_id='lefteria_fc_academy'),
        pos_location_id varchar(80) COLLATE "C" NOT NULL,
        academy_city_id uuid NOT NULL,
        academy_location_id uuid NOT NULL,
        PRIMARY KEY(organization_id,pos_location_id),
        UNIQUE(academy_location_id),
        FOREIGN KEY(organization_id,pos_location_id)
          REFERENCES pos.locations(organization_id,id) ON DELETE RESTRICT,
        FOREIGN KEY(academy_city_id,academy_location_id)
          REFERENCES club.football_locations(city_id,id) ON DELETE RESTRICT
      );
      ALTER TABLE pos.academy_football_location_links ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON TABLE pos.academy_football_location_links FROM PUBLIC;

      -- Unscoped legacy POS location endpoints must never expose another
      -- Academy site to a manager. Only a super admin may use these legacy
      -- owner-wide provisioning/catalog/stock functions.
      CREATE OR REPLACE FUNCTION pos.require_admin_location_owner(
        _digest text,_owner text
      ) RETURNS TABLE(actor_id uuid,session_id uuid)
      LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $pos_admin_only$
      DECLARE v_role text;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy') THEN
          RAISE EXCEPTION 'pos_location_scope_invalid' USING ERRCODE='22023';
        END IF;
        PERFORM identity.lock_admin_club_authority(_digest,_owner);
        SELECT a.id,s.id,a.role INTO actor_id,session_id,v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role<>'super_admin' THEN
          RAISE EXCEPTION 'pos_all_locations_super_admin_only' USING ERRCODE='42501';
        END IF;
        RETURN NEXT;
      END $pos_admin_only$;

      -- A site report is authorized from current Academy city/location grants,
      -- never by user-supplied city, location or organization alone.
      CREATE FUNCTION pos_cashier.authorize_site_finances(
        _digest text,_owner text,_location text
      ) RETURNS uuid LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $site_authority$
      DECLARE
        v_actor uuid; v_role text; v_academy_location uuid; v_scope uuid;
      BEGIN
        IF _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _location IS NULL OR _location !~ '^[A-Za-z0-9_-]{1,80}$'
        THEN RAISE EXCEPTION 'pos_site_invalid' USING ERRCODE='22023'; END IF;
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);
        IF NOT EXISTS(SELECT 1 FROM pos.locations
          WHERE organization_id=_owner AND id=_location AND active)
        THEN RAISE EXCEPTION 'pos_site_access_denied' USING ERRCODE='42501'; END IF;
        IF _owner='lefteria_fc_academy' THEN
          SELECT link.academy_location_id,s.id
            INTO v_academy_location,v_scope
            FROM pos.academy_football_location_links link
            JOIN club.football_locations l
              ON l.id=link.academy_location_id AND l.city_id=link.academy_city_id
              AND l.is_active
            JOIN club.football_cities c ON c.id=l.city_id AND c.is_active
            JOIN club.football_operational_scopes s
              ON s.academy_city_id=l.city_id AND s.organization_id=_owner
            WHERE link.organization_id=_owner
              AND link.pos_location_id=_location;
          IF v_scope IS NULL OR NOT club.admin_football_allowed(
             v_actor,v_role,_owner,v_scope,v_academy_location,false)
          THEN RAISE EXCEPTION 'pos_site_access_denied' USING ERRCODE='42501';
          END IF;
        ELSIF v_role<>'super_admin' THEN
          -- First-team team-to-POS-location mapping is a separate approval.
          RAISE EXCEPTION 'pos_first_team_site_unmapped' USING ERRCODE='42501';
        END IF;
        RETURN v_actor;
      END $site_authority$;

      -- Super admin creates the POS counterpart of an existing Academy site.
      -- No arbitrary second city, invented site or payment enablement.
      CREATE FUNCTION pos_cashier.ensure_admin_academy_pos_site(
        _digest text,_canonical_location uuid
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $ensure_site$
      DECLARE
        v_actor uuid; v_role text; v_city uuid;
        v_city_name text; v_location_name text;
        v_pos_city text; v_pos_location text; v_code text;
      BEGIN
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,'lefteria_fc_academy');
        IF v_role<>'super_admin' OR _canonical_location IS NULL
        THEN RAISE EXCEPTION 'pos_site_provision_denied' USING ERRCODE='42501'; END IF;
        SELECT c.id,c.name,l.name
          INTO v_city,v_city_name,v_location_name
          FROM club.football_locations l
          JOIN club.football_cities c ON c.id=l.city_id
          WHERE l.id=_canonical_location AND l.is_active AND c.is_active
          FOR SHARE OF l,c;
        IF v_city IS NULL OR length(v_city_name)>100
          OR length(v_location_name)>100
        THEN RAISE EXCEPTION 'pos_canonical_site_invalid' USING ERRCODE='22023'; END IF;
        v_pos_city:='city_'||replace(v_city::text,'-','');
        v_pos_location:='loc_'||replace(_canonical_location::text,'-','');
        v_code:='site-'||replace(_canonical_location::text,'-','');
        INSERT INTO pos.cities(organization_id,id,name)
          VALUES('lefteria_fc_academy',v_pos_city,v_city_name)
          ON CONFLICT(organization_id,id) DO NOTHING;
        INSERT INTO pos.locations(organization_id,id,city_id,code,name)
          VALUES('lefteria_fc_academy',v_pos_location,v_pos_city,v_code,v_location_name)
          ON CONFLICT(organization_id,id) DO NOTHING;
        IF NOT EXISTS (SELECT 1 FROM pos.locations
             WHERE organization_id='lefteria_fc_academy'
               AND id=v_pos_location AND city_id=v_pos_city AND active) THEN
          RAISE EXCEPTION 'pos_canonical_site_conflict' USING ERRCODE='23505';
        END IF;
        INSERT INTO pos.academy_football_location_links(
           organization_id,pos_location_id,academy_city_id,academy_location_id)
          VALUES('lefteria_fc_academy',v_pos_location,v_city,_canonical_location)
          ON CONFLICT(organization_id,pos_location_id) DO NOTHING;
        IF NOT EXISTS (SELECT 1 FROM pos.academy_football_location_links
             WHERE organization_id='lefteria_fc_academy'
               AND pos_location_id=v_pos_location
               AND academy_city_id=v_city AND academy_location_id=_canonical_location)
        THEN RAISE EXCEPTION 'pos_canonical_site_conflict' USING ERRCODE='23505';
        END IF;
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,
          action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
            'admin.pos.academy_site.ensure','succeeded',
            'lefteria_fc_academy','canonical_binding');
        RETURN jsonb_build_object('organization_id','lefteria_fc_academy',
          'pos_location_id',v_pos_location,'academy_location_id',_canonical_location,
          'academy_city_id',v_city,'city_name',v_city_name,
          'location_name',v_location_name);
      END $ensure_site$;

      CREATE FUNCTION pos_cashier.read_admin_pos_sites(
        _digest text,_owner text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $read_sites$
      DECLARE v_actor uuid;v_role text;v_rows jsonb;
      BEGIN
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);
        IF _owner='lefteria_fc_academy' THEN
          SELECT coalesce(jsonb_agg(jsonb_build_object(
            'pos_location_id',pl.id,'name',fl.name,
            'city_name',fc.name,'academy_location_id',fl.id,
            'academy_city_id',fc.id) ORDER BY fc.name,fl.name),'[]'::jsonb)
          INTO v_rows
          FROM pos.academy_football_location_links link
          JOIN pos.locations pl ON pl.organization_id=link.organization_id
             AND pl.id=link.pos_location_id AND pl.active
          JOIN club.football_locations fl
             ON fl.id=link.academy_location_id
             AND fl.city_id=link.academy_city_id AND fl.is_active
          JOIN club.football_cities fc ON fc.id=fl.city_id AND fc.is_active
          JOIN club.football_operational_scopes scope
             ON scope.academy_city_id=fl.city_id
             AND scope.organization_id=_owner
          WHERE link.organization_id=_owner
            AND club.admin_football_allowed(v_actor,v_role,_owner,
              scope.id,fl.id,false);
        ELSIF v_role='super_admin' THEN
          SELECT coalesce(jsonb_agg(jsonb_build_object(
            'pos_location_id',pl.id,'name',pl.name,
            'city_name',c.name,'academy_location_id',NULL,
            'academy_city_id',NULL) ORDER BY c.name,pl.name),'[]'::jsonb)
          INTO v_rows
          FROM pos.locations pl JOIN pos.cities c
            ON c.organization_id=pl.organization_id AND c.id=pl.city_id
          WHERE pl.organization_id=_owner AND pl.active AND c.active;
        ELSE
          RAISE EXCEPTION 'pos_first_team_site_unmapped' USING ERRCODE='42501';
        END IF;
        IF jsonb_array_length(v_rows)>80
        THEN RAISE EXCEPTION 'pos_site_list_too_large' USING ERRCODE='54000'; END IF;
        RETURN jsonb_build_object('organization_id',_owner,'sites',v_rows);
      END $read_sites$;

      CREATE FUNCTION pos_cashier.read_admin_pos_site_finances(
        _digest text,_owner text,_location text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $site_report$
      DECLARE
        v_actor uuid;v_name text;v_sales bigint;v_paid bigint;v_pending bigint;
        v_gross bigint;v_cash bigint;v_card bigint;v_refunds bigint;
        v_registers bigint;v_receipts bigint;v_unsettled bigint;
      BEGIN
        v_actor:=pos_cashier.authorize_site_finances(_digest,_owner,_location);
        SELECT name INTO STRICT v_name FROM pos.locations
          WHERE organization_id=_owner AND id=_location;
        SELECT count(*) INTO v_registers FROM pos_cashier.registers r
          WHERE r.organization_id=_owner AND r.location_id=_location;
        SELECT count(*),count(*) FILTER(WHERE s.status='paid'),
          count(*) FILTER(WHERE s.status IN ('awaiting_payment','partially_paid'))
          INTO v_sales,v_paid,v_pending
          FROM pos_cashier.sales s JOIN pos_cashier.registers r
          ON r.organization_id=s.organization_id AND r.id=s.register_id
          WHERE s.organization_id=_owner AND r.location_id=_location;
        IF v_sales>1000000
        THEN RAISE EXCEPTION 'pos_site_report_too_large' USING ERRCODE='54000'; END IF;
        SELECT count(*),coalesce(sum(s.total_cents),0)
          INTO v_receipts,v_gross
          FROM pos_cashier.receipts rc
          JOIN pos_cashier.sales s
            ON s.organization_id=rc.organization_id AND s.id=rc.sale_id
          JOIN pos_cashier.registers r
            ON r.organization_id=s.organization_id AND r.id=s.register_id
          WHERE s.organization_id=_owner AND r.location_id=_location
            AND s.status='paid';
        SELECT coalesce(sum(p.amount_cents)
             FILTER(WHERE p.payment_kind='cash'),0),
          coalesce(sum(p.amount_cents)
             FILTER(WHERE p.payment_kind='card'),0)
          INTO v_cash,v_card
          FROM pos_cashier.payment_events p
          JOIN pos_cashier.sales s
            ON s.organization_id=p.organization_id AND s.id=p.sale_id
          JOIN pos_cashier.registers r
            ON r.organization_id=s.organization_id AND r.id=s.register_id
          WHERE s.organization_id=_owner AND r.location_id=_location
            AND p.status='verified' AND s.status='paid';
        SELECT coalesce(sum(f.amount_cents),0) INTO v_refunds
          FROM pos_cashier.refund_events f
          JOIN pos_cashier.returns t
            ON t.organization_id=f.organization_id AND t.id=f.return_id
          JOIN pos_cashier.sales s
            ON s.organization_id=t.organization_id AND s.id=t.sale_id
          JOIN pos_cashier.registers r
            ON r.organization_id=s.organization_id AND r.id=s.register_id
          WHERE s.organization_id=_owner AND r.location_id=_location
            AND f.status='verified';
        SELECT count(*) INTO v_unsettled
          FROM pos_cashier.sales s
          JOIN pos_cashier.registers r
            ON r.organization_id=s.organization_id AND r.id=s.register_id
          WHERE s.organization_id=_owner AND r.location_id=_location
            AND s.status='paid'
            AND NOT EXISTS(SELECT 1 FROM pos_cashier.receipts rc
               WHERE rc.organization_id=s.organization_id AND rc.sale_id=s.id);
        RETURN jsonb_build_object('organization_id',_owner,
          'pos_location_id',_location,'location_name',v_name,
          'registers_total',v_registers,'sales_total',v_sales,
          'sales_paid',v_paid,'sales_pending',v_pending,
          'receipts_issued',v_receipts,'receipted_gross_cents',v_gross,
          'verified_cash_cents',v_cash,'verified_card_cents',v_card,
          'verified_refunds_cents',v_refunds,
          'receipted_net_after_refunds_cents',v_gross-v_refunds,
          'paid_without_receipt',v_unsettled,
          'finance_posting_active',false);
      END $site_report$;


      -- The existing organization-wide POS finance report MUST NOT be callable
      -- by a city or location manager. Their only finance entry is the
      -- exact-site reader above. This preserves a Super Admin overview.
      CREATE OR REPLACE FUNCTION pos_cashier.read_admin_finance_handoff(
        _digest text,_owner text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $global_finance$
      DECLARE
        v_actor uuid;v_role text;v_events bigint;v_sales bigint;
        v_refunds bigint;v_gross bigint;v_refunded bigint;v_earliest timestamptz;
      BEGIN
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);
        IF v_role<>'super_admin'
        THEN RAISE EXCEPTION 'pos_global_finance_super_admin_only'
          USING ERRCODE='42501'; END IF;
        SELECT count(*),count(*) FILTER(WHERE event_kind='sale_receipted'),
          count(*) FILTER(WHERE event_kind='refund_verified'),
          coalesce(sum(amount_cents) FILTER(WHERE event_kind='sale_receipted'),0),
          coalesce(sum(amount_cents) FILTER(WHERE event_kind='refund_verified'),0),
          min(created_at)
          INTO v_events,v_sales,v_refunds,v_gross,v_refunded,v_earliest
          FROM pos_cashier.finance_posting_outbox WHERE organization_id=_owner;
        RETURN jsonb_build_object(
          'organization_id',_owner,
          'posting_status','awaiting_verified_finance_consumer',
          'total_events',v_events,'sale_receipt_events',v_sales,
          'verified_refund_events',v_refunds,
          'sale_gross_cents',v_gross,'refund_gross_cents',v_refunded,
          'net_event_cents',v_gross-v_refunded,
          'oldest_event_at',v_earliest,
          'journal_entries_confirmed',0,'finance_posting_active',false);
      END $global_finance$;


      -- An assigned register must also belong to the actor's current
      -- Academy city/location grant, even if the assignment is stale.
      CREATE OR REPLACE FUNCTION pos_cashier.current_cashier_authority(
        _digest text,_owner text,_register text
      ) RETURNS uuid LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $scoped_cashier$
      DECLARE
        v_actor uuid;v_role text;v_pos_location text;v_academy_location uuid;
        v_scope uuid;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _register IS NULL OR _register !~ '^[A-Za-z0-9_-]{1,80}$'
        THEN RAISE EXCEPTION 'cashier_scope_invalid' USING ERRCODE='22023'; END IF;
        PERFORM identity.lock_admin_club_authority(_digest,_owner);
        SELECT a.id,a.role,r.location_id
          INTO v_actor,v_role,v_pos_location
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          JOIN pos_cashier.cashier_assignments ca
            ON ca.organization_id=_owner AND ca.register_id=_register
              AND ca.admin_account_id=a.id AND ca.is_active
          JOIN pos_cashier.registers r
            ON r.organization_id=_owner AND r.id=_register AND r.is_active
          JOIN pos.locations l
            ON l.organization_id=_owner AND l.id=r.location_id AND l.active
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL
          FOR SHARE OF ca,r,l;
        IF NOT FOUND OR v_role NOT IN ('manager','super_admin')
        THEN RAISE EXCEPTION 'cashier_not_assigned_or_not_active'
          USING ERRCODE='42501'; END IF;
        IF _owner='lefteria_fc_academy' THEN
          SELECT link.academy_location_id,scope.id
            INTO v_academy_location,v_scope
            FROM pos.academy_football_location_links link
            JOIN club.football_locations fl
              ON fl.id=link.academy_location_id
                AND fl.city_id=link.academy_city_id AND fl.is_active
            JOIN club.football_cities fc ON fc.id=fl.city_id AND fc.is_active
            JOIN club.football_operational_scopes scope
              ON scope.academy_city_id=fl.city_id
                AND scope.organization_id=_owner
            WHERE link.organization_id=_owner
              AND link.pos_location_id=v_pos_location;
          IF v_scope IS NULL OR NOT club.admin_football_allowed(
            v_actor,v_role,_owner,v_scope,v_academy_location,false)
          THEN RAISE EXCEPTION 'cashier_site_grant_revoked'
             USING ERRCODE='42501'; END IF;
        END IF;
        RETURN v_actor;
      END $scoped_cashier$;

      -- Discover only currently authorized, assigned and active registers.
      CREATE OR REPLACE FUNCTION pos_cashier.read_admin_cashier_registers(
        _digest text,_owner text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $scoped_registers$
      DECLARE v_actor uuid;v_role text;v_rows jsonb;
      BEGIN
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);
        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'id',r.id,'label',r.label,'location_id',r.location_id,
          'location_name',l.name,'shift_id',sh.id,
          'opened_at',sh.opened_at,'opening_cash_cents',sh.opening_cash_cents)
          ORDER BY r.id),'[]'::jsonb) INTO v_rows
        FROM pos_cashier.registers r
        JOIN pos.locations l ON l.organization_id=r.organization_id
          AND l.id=r.location_id AND l.active
        JOIN pos_cashier.cashier_assignments ca
          ON ca.organization_id=r.organization_id
          AND ca.register_id=r.id AND ca.admin_account_id=v_actor AND ca.is_active
        LEFT JOIN pos_cashier.shifts sh
          ON sh.organization_id=r.organization_id
          AND sh.register_id=r.id AND sh.closed_at IS NULL
        LEFT JOIN pos.academy_football_location_links link
          ON link.organization_id=r.organization_id
          AND link.pos_location_id=r.location_id
        LEFT JOIN club.football_operational_scopes scope
          ON scope.academy_city_id=link.academy_city_id
          AND scope.organization_id=r.organization_id
        WHERE r.organization_id=_owner AND r.is_active
          AND (_owner<>'lefteria_fc_academy' OR
             (link.academy_location_id IS NOT NULL AND scope.id IS NOT NULL
              AND club.admin_football_allowed(v_actor,v_role,_owner,
                scope.id,link.academy_location_id,false)));
        IF jsonb_array_length(v_rows)>100
        THEN RAISE EXCEPTION 'cashier_registers_too_many' USING ERRCODE='54000'; END IF;
        RETURN jsonb_build_object('organization_id',_owner,
          'registers',v_rows,'total',jsonb_array_length(v_rows));
      END $scoped_registers$;

      -- Security-definer operations are not automatically granted to
      -- any web or mobile account by migration.
      REVOKE ALL ON FUNCTION pos_cashier.authorize_site_finances(text,text,text) FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos_cashier.ensure_admin_academy_pos_site(text,uuid) FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos_cashier.read_admin_pos_sites(text,text) FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos_cashier.read_admin_pos_site_finances(text,text,text) FROM PUBLIC;
    """);

    // A disposal-only rollback is allowed when no POS site, terminal, sale,
    // or site link exists. A populated estate must use reviewed forward
    // recovery: removing canonical links would invalidate cashier authority.
    // The legacy readers remain super-admin-only (or fail closed) until
    // their own earlier migrations are removed by a full rollback.
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$ BEGIN
        IF EXISTS(SELECT 1 FROM pos.academy_football_location_links)
          OR EXISTS(SELECT 1 FROM pos.locations)
          OR EXISTS(SELECT 1 FROM pos_cashier.registers)
          OR EXISTS(SELECT 1 FROM pos_cashier.sales)
        THEN RAISE EXCEPTION 'pos_site_security_requires_reviewed_forward_recovery'
          USING ERRCODE='23514'; END IF;
      END $guard$;
      DROP FUNCTION pos_cashier.read_admin_pos_site_finances(text,text,text);
      DROP FUNCTION pos_cashier.read_admin_pos_sites(text,text);
      DROP FUNCTION pos_cashier.ensure_admin_academy_pos_site(text,uuid);
      DROP FUNCTION pos_cashier.authorize_site_finances(text,text,text);
      DROP TABLE pos.academy_football_location_links;
    """);
}
