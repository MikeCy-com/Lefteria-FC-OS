using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

// Concurrency fix for the PRIVATE Academy lead function. This migration
// grants no runtime access and maps no HTTP route. The original migration is
// kept immutable so rollback/recovery remains reproducible.
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261108080000_MarketingLeadMonotonicThrottle")]
public sealed class MarketingLeadMonotonicThrottle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // An earlier request can acquire a bucket lock AFTER a later-started
        // request has inserted the first row. statement_timestamp() then
        // predates window_started_at, so an unconditional last_attempt_at
        // assignment breaks ck_marketing_bucket_order under load.
        migrationBuilder.Sql(Guard);
        migrationBuilder.Sql(OriginalFunction.Replace(
            "last_attempt_at=v_time",
            "last_attempt_at=GREATEST(marketing.lead_rate_buckets.last_attempt_at,v_time)",
            StringComparison.Ordinal));
        migrationBuilder.Sql(DenyPublicExecution);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Restore the original function semantics for recovery.
        migrationBuilder.Sql(OriginalFunction);
        migrationBuilder.Sql(DenyPublicExecution);
    }

    private const string DenyPublicExecution = """
        REVOKE ALL ON FUNCTION marketing.store_academy_lead(
            uuid,text,text,text,integer,text,text,text,boolean,boolean,text)
        FROM PUBLIC;
        """;

    private const string Guard = """
        DO $security$
        DECLARE
            p pg_catalog.pg_proc%ROWTYPE;
        BEGIN
            SELECT * INTO p FROM pg_catalog.pg_proc
            WHERE oid='marketing.store_academy_lead(uuid,text,text,text,integer,text,text,text,boolean,boolean,text)'::regprocedure;
            IF NOT FOUND OR p.prosecdef IS DISTINCT FROM true
                OR p.provolatile IS DISTINCT FROM 'v'
                OR NOT ('search_path=pg_catalog,pg_temp'=ANY(
                    COALESCE(p.proconfig,ARRAY[]::text[]))) THEN
                RAISE EXCEPTION 'Private Academy lead security boundary changed';
            END IF;
        END
        $security$;
        """;

    // Reviewed historical SQL from MarketingLeadAtomicWrite, except CREATE
    // OR REPLACE to retain the existing function identity and privileges.
    private const string OriginalFunction = """
            CREATE OR REPLACE FUNCTION marketing.store_academy_lead(
              _lead_id uuid,_name text,_phone text,_email text,_player_age integer,
              _interest text,_message text,_partner_slug text,_gdpr boolean,
              _marketing boolean,_fingerprint text
            ) RETURNS text
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $store_academy_lead$
            DECLARE
              v_time timestamptz := statement_timestamp();
              v_attempts smallint;
              v_partner text;
            BEGIN
              -- All inputs are rechecked in the database, including those
              -- already checked in the application layer. No caller may
              -- select an organization, state, consent timestamp or audit.
              IF _lead_id IS NULL
                 OR _lead_id='00000000-0000-0000-0000-000000000000'::uuid
                 OR _name IS NULL OR length(_name) NOT BETWEEN 1 AND 120
                 OR _name<>btrim(_name) OR _name ~ '[[:cntrl:]]'
                 OR (_phone IS NULL AND _email IS NULL)
                 OR (_phone IS NOT NULL AND
                     (length(_phone) NOT BETWEEN 7 AND 32
                      OR _phone !~ '^[+0-9() -]+$'
                      OR length(regexp_replace(_phone,'[^0-9]','','g'))<7
                      OR length(regexp_replace(_phone,'[^+]','','g'))>1
                      OR (position('+' in _phone)>0 AND left(_phone,1)<>'+')))
                 OR (_email IS NOT NULL AND
                     (length(_email) NOT BETWEEN 3 AND 254
                      OR _email !~ '^[^[:space:]@]+@[^[:space:]@]+\.[^[:space:]@]+$'))
                 OR (_player_age IS NOT NULL AND _player_age NOT BETWEEN 4 AND 99)
                 OR (_interest IS NOT NULL AND
                     (length(_interest)>120 OR _interest ~ '[[:cntrl:]]'))
                 OR (_message IS NOT NULL AND length(_message)>2000)
                 OR (_partner_slug IS NOT NULL AND
                     _partner_slug !~ '^[a-z0-9]([a-z0-9-]{0,62}[a-z0-9])?$')
                 OR _gdpr IS DISTINCT FROM true
                 OR _marketing IS NULL
                 OR _fingerprint IS NULL OR _fingerprint !~ '^[0-9a-f]{64}$'
              THEN
                 RAISE EXCEPTION 'marketing_lead_invalid'
                   USING ERRCODE='22023';
              END IF;
              v_partner := _partner_slug;

              -- UPSERT holds the one fingerprint row lock. Requests six+
              -- in the same one-hour window get no bucket row and therefore
              -- cannot create either the lead OR its audit event. PostgreSQL
              -- rolls all mutations back together on subsequent failures.
              INSERT INTO marketing.lead_rate_buckets(
                fingerprint,window_started_at,attempts,last_attempt_at)
              VALUES (_fingerprint,v_time,1,v_time)
              ON CONFLICT(fingerprint) DO UPDATE SET
                attempts=CASE WHEN
                  marketing.lead_rate_buckets.window_started_at<=v_time-interval '1 hour'
                  THEN 1 ELSE marketing.lead_rate_buckets.attempts+1 END,
                window_started_at=CASE WHEN
                  marketing.lead_rate_buckets.window_started_at<=v_time-interval '1 hour'
                  THEN v_time ELSE marketing.lead_rate_buckets.window_started_at END,
                last_attempt_at=v_time
              WHERE marketing.lead_rate_buckets.window_started_at<=v_time-interval '1 hour'
                 OR marketing.lead_rate_buckets.attempts<5
              RETURNING attempts INTO v_attempts;

              IF v_attempts IS NULL THEN
                RETURN 'rate_limited';
              END IF;

              INSERT INTO marketing.public_leads(
                id,organization_id,contact_name,phone,email,player_age,
                interested_in,message,source,partner_slug,consent_gdpr,
                consent_marketing,consent_recorded_at,status,created_at)
              VALUES (
                _lead_id,'lefteria_fc_academy',_name,_phone,_email,_player_age,
                _interest,_message,
                CASE WHEN v_partner IS NULL THEN 'website' ELSE 'partner_referral' END,
                v_partner,true,_marketing,v_time,'new',v_time);

              INSERT INTO marketing.lead_events(
                id,lead_id,organization_id,event_type,occurred_at)
              VALUES (
                gen_random_uuid(),_lead_id,'lefteria_fc_academy','created',v_time);
              RETURN 'stored';
            END
            $store_academy_lead$;
        """;
}
