using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LefteriaDbContext))]
[Migration("20261114164000_AcademyOffice")]
public sealed class AcademyOffice : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE academy_intake.registrations ADD COLUMN recorded_by uuid
          REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT;

        CREATE TABLE academy_intake.internal_orders (
            id uuid PRIMARY KEY,
            organization_id varchar(32) NOT NULL CHECK (organization_id='lefteria_fc_academy'),
            player_id varchar(128) NOT NULL,
            item varchar(120) NOT NULL CHECK (length(btrim(item)) > 0),
            size varchar(40),
            quantity integer NOT NULL CHECK (quantity BETWEEN 1 AND 100),
            created_by uuid NOT NULL REFERENCES identity.admin_accounts(id),
            created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            closed_by uuid REFERENCES identity.admin_accounts(id),
            closed_at timestamptz,
            status text NOT NULL DEFAULT 'pending' CHECK (status IN ('pending','fulfilled','cancelled')),
            CHECK ((status='pending' AND closed_by IS NULL AND closed_at IS NULL)
                OR (status<>'pending' AND closed_by IS NOT NULL AND closed_at IS NOT NULL)),
            FOREIGN KEY (organization_id,player_id) REFERENCES club.players(organization_id,id) ON DELETE RESTRICT
        );
        CREATE INDEX ix_academy_office_orders ON academy_intake.internal_orders(created_at DESC,id);
        ALTER TABLE academy_intake.internal_orders ENABLE ROW LEVEL SECURITY;
        REVOKE ALL ON academy_intake.internal_orders FROM PUBLIC;

        CREATE FUNCTION identity.admin_academy_office(_digest text,_operation text,_body jsonb)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $office$
        DECLARE
          owner_id constant text := 'lefteria_fc_academy';
          actor uuid;
          request_id uuid;
          player text;
          birth_date date;
          page_offset integer;
          current_state text;
          next_state text;
          version integer;
        BEGIN
          IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$' THEN
            RAISE EXCEPTION 'academy_office_denied' USING ERRCODE='42501';
          END IF;
          BEGIN
            PERFORM identity.lock_admin_club_authority(_digest,owner_id);
          EXCEPTION WHEN raise_exception THEN
            RAISE EXCEPTION 'academy_office_denied' USING ERRCODE='42501';
          END;
          SELECT a.id INTO actor
            FROM identity.admin_sessions s JOIN identity.admin_accounts a ON a.id=s.admin_account_id
            JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id AND g.organization_id=owner_id
            WHERE s.token_digest=_digest AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp()
              AND a.is_active AND NOT a.invite_pending AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL
              AND a.role IN ('manager','super_admin');
          IF actor IS NULL THEN RAISE EXCEPTION 'academy_office_denied' USING ERRCODE='42501'; END IF;

          IF _operation IN ('registrations','orders','players') THEN
            page_offset := coalesce((_body->>'offset')::integer,0);
            IF page_offset NOT BETWEEN 0 AND 20000 OR length(coalesce(_body->>'search',''))>100 THEN
              RAISE EXCEPTION 'invalid_filters' USING ERRCODE='22023';
            END IF;
            IF _operation='registrations' THEN
              RETURN (SELECT coalesce(jsonb_agg(to_jsonb(r) ORDER BY r.submitted_at DESC,r.id),'[]')
                FROM (SELECT id,applicant_first_name,applicant_last_name,applicant_birth_date,
                  guardian_name,guardian_relationship,guardian_phone,guardian_email,status,status_version,submitted_at
                  FROM academy_intake.registrations WHERE organization_id=owner_id
                  ORDER BY submitted_at DESC,id LIMIT 26 OFFSET page_offset) r);
            ELSIF _operation='players' THEN
              RETURN (SELECT coalesce(jsonb_agg(to_jsonb(p) ORDER BY p.name,p.id),'[]')
                FROM (SELECT id,name FROM club.players WHERE organization_id=owner_id
                  AND is_active AND NOT is_archived AND NOT is_transferred
                  AND strpos(lower(name),lower(coalesce(_body->>'search','')))>0
                  ORDER BY name,id LIMIT 30) p);
            END IF;
            RETURN (SELECT coalesce(jsonb_agg(to_jsonb(o) ORDER BY o.created_at DESC,o.id),'[]')
              FROM (SELECT i.id,p.name AS player_name,i.item,i.size,i.quantity,i.status,i.created_at
                FROM academy_intake.internal_orders i JOIN club.players p
                  ON p.organization_id=i.organization_id AND p.id=i.player_id
                WHERE i.organization_id=owner_id ORDER BY i.created_at DESC,i.id LIMIT 26 OFFSET page_offset) o);
          END IF;

          request_id := (_body->>'id')::uuid;
          IF request_id IS NULL OR request_id='00000000-0000-0000-0000-000000000000' THEN
            RAISE EXCEPTION 'invalid_record_id' USING ERRCODE='22023';
          END IF;

          IF _operation='register' THEN
            birth_date := (_body->>'birth_date')::date;
            IF birth_date IS NULL OR birth_date NOT BETWEEN DATE '2000-01-01'
                AND (clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date
                OR coalesce(length(btrim(_body->>'guardian_relationship')),0)=0
                OR coalesce(_body->>'guardian_phone','') !~ '^([+])?[0-9 ()-]{6,32}$'
                OR coalesce(_body->>'guardian_email','') !~ '^[^[:space:]@]+@[^[:space:]@]+[.][^[:space:]@]+$'
            THEN RAISE EXCEPTION 'invalid_registration' USING ERRCODE='22023'; END IF;
            INSERT INTO academy_intake.registrations (
              id,organization_id,applicant_first_name,applicant_last_name,applicant_birth_date,
              guardian_name,guardian_relationship,guardian_phone,guardian_email,recorded_by)
            VALUES (request_id,owner_id,btrim(_body->>'first_name'),btrim(_body->>'last_name'),birth_date,
              btrim(_body->>'guardian_name'),btrim(_body->>'guardian_relationship'),
              btrim(_body->>'guardian_phone'),btrim(_body->>'guardian_email'),actor)
            ON CONFLICT (id) DO NOTHING;
            IF NOT FOUND AND NOT EXISTS (SELECT 1 FROM academy_intake.registrations r
              WHERE r.id=request_id AND r.organization_id=owner_id AND r.recorded_by=actor
                AND r.applicant_first_name=btrim(_body->>'first_name') AND r.applicant_last_name=btrim(_body->>'last_name')
                AND r.applicant_birth_date=birth_date AND r.guardian_name=btrim(_body->>'guardian_name')
                AND r.guardian_relationship=btrim(_body->>'guardian_relationship')
                AND r.guardian_phone=btrim(_body->>'guardian_phone') AND r.guardian_email=btrim(_body->>'guardian_email')) THEN
              RAISE EXCEPTION 'registration_request_conflict' USING ERRCODE='23505';
            END IF;

          ELSIF _operation='review' THEN
            next_state := _body->>'status';
            SELECT status,status_version INTO current_state,version
              FROM academy_intake.registrations WHERE id=request_id AND organization_id=owner_id FOR UPDATE;
            IF NOT FOUND OR version IS DISTINCT FROM (_body->>'version')::integer THEN
              RAISE EXCEPTION 'registration_changed' USING ERRCODE='23505';
            END IF;
            IF NOT coalesce(
               (current_state='pending' AND next_state='under_review') OR
               (current_state='under_review' AND next_state IN ('needs_information','approved','rejected')) OR
               (current_state='needs_information' AND next_state='under_review'),false)
            THEN RAISE EXCEPTION 'invalid_review' USING ERRCODE='22023'; END IF;

            UPDATE academy_intake.registrations SET status=next_state,status_version=version+1,
              decided_by=CASE WHEN next_state IN ('approved','rejected') THEN actor END,
              decided_at=CASE WHEN next_state IN ('approved','rejected') THEN clock_timestamp() END
              WHERE id=request_id AND organization_id=owner_id;
            INSERT INTO academy_intake.registration_review_events
              (id,registration_id,actor_id,previous_status,next_status,status_version,reason_code)
              VALUES (gen_random_uuid(),request_id,actor,current_state,next_state,version+1,'staff_review');

          ELSIF _operation='order' THEN
            player := _body->>'player_id';
            PERFORM 1 FROM club.players WHERE organization_id=owner_id AND id=player
              AND is_active AND NOT is_archived AND NOT is_transferred FOR SHARE;
            IF NOT FOUND THEN RAISE EXCEPTION 'invalid_player' USING ERRCODE='22023'; END IF;
            INSERT INTO academy_intake.internal_orders (id,organization_id,player_id,item,size,quantity,created_by)
            VALUES (request_id,owner_id,player,btrim(_body->>'item'),nullif(btrim(_body->>'size'),''),
              (_body->>'quantity')::integer,actor)
            ON CONFLICT (id) DO NOTHING;
            IF NOT FOUND AND NOT EXISTS (SELECT 1 FROM academy_intake.internal_orders o
              WHERE o.id=request_id AND o.organization_id=owner_id AND o.created_by=actor AND o.player_id=player
                AND o.item=btrim(_body->>'item') AND o.size IS NOT DISTINCT FROM nullif(btrim(_body->>'size'),'')
                AND o.quantity=(_body->>'quantity')::integer) THEN
              RAISE EXCEPTION 'order_request_conflict' USING ERRCODE='23505';
            END IF;

          ELSIF _operation IN ('fulfilled','cancelled') THEN
            UPDATE academy_intake.internal_orders SET status=_operation,closed_by=actor,closed_at=clock_timestamp()
              WHERE id=request_id AND organization_id=owner_id AND status='pending';
            IF NOT FOUND THEN RAISE EXCEPTION 'order_changed' USING ERRCODE='23505'; END IF;
          ELSE
            RAISE EXCEPTION 'invalid_operation' USING ERRCODE='22023';
          END IF;
          RETURN jsonb_build_object('id',request_id);
        END $office$;
        REVOKE ALL ON FUNCTION identity.admin_academy_office(text,text,jsonb) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $guard$ BEGIN
          IF EXISTS(SELECT 1 FROM academy_intake.internal_orders)
             OR EXISTS(SELECT 1 FROM academy_intake.registrations WHERE recorded_by IS NOT NULL)
             OR EXISTS(SELECT 1 FROM academy_intake.registration_review_events WHERE reason_code='staff_review') THEN
            RAISE EXCEPTION 'Academy office contains records. Use a forward migration to preserve them.';
          END IF;
        END $guard$;
        DROP FUNCTION identity.admin_academy_office(text,text,jsonb);
        DROP TABLE academy_intake.internal_orders;
        ALTER TABLE academy_intake.registrations DROP COLUMN recorded_by;
        """);
}
