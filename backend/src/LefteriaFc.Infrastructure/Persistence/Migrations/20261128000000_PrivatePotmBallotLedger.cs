using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Owner-gated native ballots. Legacy aggregates remain untouched.
/// One current ballot per verified Fan identity/month; immutable change history.
/// No HTTP endpoint or public native tally is enabled by this migration.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261128000000_PrivatePotmBallotLedger")]
public sealed class PrivatePotmBallotLedger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE club.potm_periods (
        month_start date PRIMARY KEY CHECK(extract(day from month_start)=1),
        snapshot_version integer NOT NULL REFERENCES public_sections.snapshots(version),
        approved_by uuid NOT NULL REFERENCES identity.admin_accounts(id),
        approval_reference text NOT NULL CHECK(length(btrim(approval_reference)) BETWEEN 1 AND 200),
        legacy_stopped_at timestamptz NOT NULL,
        opened_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        closed_at timestamptz,
        CHECK(legacy_stopped_at <= (month_start::timestamp AT TIME ZONE 'Asia/Nicosia')),
        CHECK(opened_at >= (month_start::timestamp AT TIME ZONE 'Asia/Nicosia')
          AND opened_at < ((month_start+interval '1 month') AT TIME ZONE 'Asia/Nicosia')),
        CHECK(closed_at IS NULL OR closed_at >= opened_at)
      );
      CREATE TABLE club.potm_ballots (
        month_start date NOT NULL REFERENCES club.potm_periods(month_start),
        identity_id uuid NOT NULL REFERENCES identity.mobile_identities(id),
        player_id varchar(128) COLLATE "C",
        revision integer NOT NULL CHECK(revision>0),
        updated_at timestamptz NOT NULL,
        PRIMARY KEY(month_start,identity_id),
        CHECK(player_id IS NULL OR length(btrim(player_id)) BETWEEN 1 AND 128)
      );
      CREATE TABLE club.potm_ballot_events (
        month_start date NOT NULL,
        identity_id uuid NOT NULL,
        revision integer NOT NULL CHECK(revision>0),
        previous_player_id varchar(128) COLLATE "C",
        player_id varchar(128) COLLATE "C",
        snapshot_version integer NOT NULL REFERENCES public_sections.snapshots(version),
        occurred_at timestamptz NOT NULL,
        PRIMARY KEY(month_start,identity_id,revision),
        FOREIGN KEY(month_start,identity_id) REFERENCES club.potm_ballots(month_start,identity_id),
        CHECK(previous_player_id IS DISTINCT FROM player_id)
      );
      ALTER TABLE club.potm_periods ENABLE ROW LEVEL SECURITY;
      ALTER TABLE club.potm_ballots ENABLE ROW LEVEL SECURITY;
      ALTER TABLE club.potm_ballot_events ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON club.potm_periods,club.potm_ballots,club.potm_ballot_events FROM PUBLIC;

      -- Identity and expiry are checked again after acquiring the row lock.
      -- No role can borrow a Fan UUID and no phone is stored in the ledger.
      CREATE FUNCTION club.lock_potm_fan(_identity uuid,_phone text,_role text,_expires timestamptz)
      RETURNS void LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $potm_actor$
      BEGIN
        IF _identity IS NULL OR _role IS DISTINCT FROM 'fan' OR _phone IS NULL
          OR _phone !~ '^[1-9][0-9]{7,14}$' OR _expires IS NULL
          OR _expires<=clock_timestamp()
        THEN RAISE EXCEPTION 'potm_actor_denied' USING ERRCODE='42501'; END IF;
        PERFORM 1 FROM identity.mobile_identities
          WHERE id=_identity AND normalized_phone=_phone AND role='fan' AND is_active
          FOR UPDATE;
        IF NOT FOUND OR _expires<=clock_timestamp()
        THEN RAISE EXCEPTION 'potm_actor_denied' USING ERRCODE='42501'; END IF;
      END $potm_actor$;
      REVOKE ALL ON FUNCTION club.lock_potm_fan(uuid,text,text,timestamptz) FROM PUBLIC;

      -- Owner review is required for every monthly cutover. A newer import
      -- automatically closes the gate until explicitly reviewed again.
      CREATE FUNCTION club.potm_open_snapshot(_month date)
      RETURNS integer LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $potm_gate$
      DECLARE v_version integer; v_count integer; v_sources integer; v_approved uuid;
      BEGIN
        IF _month IS DISTINCT FROM date_trunc('month',clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date
        THEN RETURN NULL; END IF;
        SELECT p.snapshot_version,p.approved_by INTO v_version,v_approved
          FROM club.potm_periods p
          JOIN public_sections.current c ON c.snapshot_version=p.snapshot_version AND c.id='current'
          WHERE p.month_start=_month AND p.closed_at IS NULL
            AND p.opened_at<=clock_timestamp()
          FOR SHARE OF p,c;
        IF NOT FOUND THEN RETURN NULL; END IF;
        PERFORM 1 FROM identity.admin_accounts a
          JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id
          WHERE a.id=v_approved AND a.is_active AND NOT a.invite_pending
            AND a.role='super_admin' AND g.organization_id='lefteria_fc'
          FOR SHARE OF a,g;
        IF NOT FOUND THEN RETURN NULL; END IF;
        SELECT s.documents_count,s.sources_count INTO v_count,v_sources FROM public_sections.snapshots s
          JOIN public_content.import_state b ON b.digest=s.base_public_digest
          WHERE s.version=v_version AND s.exported_at<=clock_timestamp()
            AND date_trunc('month',s.exported_at AT TIME ZONE 'Asia/Nicosia')::date=_month
          FOR SHARE OF s,b;
        IF NOT FOUND OR v_count<>(SELECT count(*) FROM public_sections.documents WHERE snapshot_version=v_version)
          OR v_sources<>(SELECT count(*) FROM migration.public_sections_source WHERE snapshot_version=v_version)
        THEN RETURN NULL; END IF;
        IF NOT EXISTS(SELECT 1 FROM public_sections.documents d
          WHERE d.snapshot_version=v_version AND d.kind='module' AND d.organization_id='lefteria_fc'
            AND d.id='potm' AND d.data_json->>'status' IN ('ready','empty')
            AND d.data_json->>'quarantined_count'='0')
          OR EXISTS(SELECT 1 FROM public_sections.documents d
            WHERE d.snapshot_version=v_version AND d.kind='potm' AND d.organization_id='lefteria_fc'
              AND d.data_json->>'month_key'=to_char(_month,'YYYY-MM'))
          OR EXISTS(SELECT 1 FROM migration.public_sections_source s
            WHERE s.collection='potm_votes' AND
              (s.source_json::jsonb->>'month_key'=to_char(_month,'YYYY-MM')
               OR coalesce(s.source_json::jsonb->>'month_key','') !~ '^[0-9]{4}-(0[1-9]|1[0-2])$'))
        THEN RETURN NULL; END IF;
        RETURN v_version;
      END $potm_gate$;
      REVOKE ALL ON FUNCTION club.potm_open_snapshot(date) FROM PUBLIC;

      CREATE FUNCTION club.read_mobile_potm_ballot(_identity uuid,_phone text,_role text,_expires timestamptz)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $potm_read$
      DECLARE v_month date; v_snapshot integer; v_player text; v_revision integer;
      BEGIN
        PERFORM club.lock_potm_fan(_identity,_phone,_role,_expires);
        v_month:=date_trunc('month',clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date;
        v_snapshot:=club.potm_open_snapshot(v_month);
        IF _expires<=clock_timestamp() THEN RAISE EXCEPTION 'potm_actor_denied' USING ERRCODE='42501'; END IF;
        SELECT player_id,revision INTO v_player,v_revision FROM club.potm_ballots
          WHERE month_start=v_month AND identity_id=_identity;
        RETURN jsonb_build_object('month_key',to_char(v_month,'YYYY-MM'),
          'voting_open',v_snapshot IS NOT NULL,'my_vote_player_id',v_player,
          'revision',coalesce(v_revision,0));
      END $potm_read$;
      REVOKE ALL ON FUNCTION club.read_mobile_potm_ballot(uuid,text,text,timestamptz) FROM PUBLIC;

      -- Null player means withdrawal; a tombstone keeps the revision/history.
      -- Compare-and-swap prevents a delayed request overwriting a newer choice.
      CREATE FUNCTION club.write_mobile_potm_ballot(_identity uuid,_phone text,_role text,_expires timestamptz,
        _month_key text,_expected_revision integer,_player text)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $potm_write$
      DECLARE v_month date; v_snapshot integer; v_old text; v_revision integer; v_now timestamptz;
      BEGIN
        PERFORM club.lock_potm_fan(_identity,_phone,_role,_expires);
        v_month:=date_trunc('month',clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date;
        IF _month_key IS DISTINCT FROM to_char(v_month,'YYYY-MM')
          OR _expected_revision IS NULL OR _expected_revision<0
          OR (_player IS NOT NULL AND (length(_player) NOT BETWEEN 1 AND 128 OR _player<>btrim(_player)))
        THEN RAISE EXCEPTION 'potm_invalid_request' USING ERRCODE='22023'; END IF;
        v_snapshot:=club.potm_open_snapshot(v_month);
        IF v_snapshot IS NULL THEN RAISE EXCEPTION 'potm_voting_closed' USING ERRCODE='55000'; END IF;
        SELECT player_id,revision INTO v_old,v_revision FROM club.potm_ballots
          WHERE month_start=v_month AND identity_id=_identity FOR UPDATE;
        v_revision:=coalesce(v_revision,0);
        IF v_revision<>_expected_revision THEN RAISE EXCEPTION 'potm_revision_conflict' USING ERRCODE='40001'; END IF;
        IF _player IS NOT NULL THEN
          PERFORM 1 FROM public_sections.documents d JOIN club.players p
            ON p.id=d.id AND p.organization_id=d.organization_id
            WHERE d.snapshot_version=v_snapshot AND d.kind='player' AND d.organization_id='lefteria_fc'
              AND d.id=_player AND d.data_json->>'team_type'='First Team'
              AND p.team_type='First Team' AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
            FOR SHARE OF d,p;
          IF NOT FOUND THEN RAISE EXCEPTION 'potm_candidate_unavailable' USING ERRCODE='22023'; END IF;
        END IF;
        v_now:=clock_timestamp();
        IF _expires<=v_now THEN RAISE EXCEPTION 'potm_actor_denied' USING ERRCODE='42501'; END IF;
        IF date_trunc('month',v_now AT TIME ZONE 'Asia/Nicosia')::date<>v_month
        THEN RAISE EXCEPTION 'potm_voting_closed' USING ERRCODE='55000'; END IF;
        IF v_old IS DISTINCT FROM _player THEN
          v_revision:=v_revision+1;
          INSERT INTO club.potm_ballots(month_start,identity_id,player_id,revision,updated_at)
            VALUES(v_month,_identity,_player,v_revision,v_now)
            ON CONFLICT(month_start,identity_id) DO UPDATE SET
              player_id=excluded.player_id,revision=excluded.revision,updated_at=excluded.updated_at;
          INSERT INTO club.potm_ballot_events(month_start,identity_id,revision,previous_player_id,player_id,snapshot_version,occurred_at)
            VALUES(v_month,_identity,v_revision,v_old,_player,v_snapshot,v_now);
        END IF;
        RETURN jsonb_build_object('month_key',to_char(v_month,'YYYY-MM'),'voting_open',true,
          'my_vote_player_id',_player,'revision',v_revision);
      END $potm_write$;
      REVOKE ALL ON FUNCTION club.write_mobile_potm_ballot(uuid,text,text,timestamptz,text,integer,text) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $potm_recovery$
      BEGIN
        IF EXISTS(SELECT 1 FROM club.potm_periods) OR EXISTS(SELECT 1 FROM club.potm_ballots)
          OR EXISTS(SELECT 1 FROM club.potm_ballot_events)
        THEN RAISE EXCEPTION 'potm_history_requires_forward_recovery'; END IF;
      END $potm_recovery$;
      DROP FUNCTION club.write_mobile_potm_ballot(uuid,text,text,timestamptz,text,integer,text);
      DROP FUNCTION club.read_mobile_potm_ballot(uuid,text,text,timestamptz);
      DROP FUNCTION club.potm_open_snapshot(date);
      DROP FUNCTION club.lock_potm_fan(uuid,text,text,timestamptz);
      DROP TABLE club.potm_ballot_events,club.potm_ballots,club.potm_periods;
      """);
}
