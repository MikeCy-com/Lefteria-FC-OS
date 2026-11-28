using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Separate native tally from legacy imported aggregates. No new
/// ballot, period opening, raw voter access or automatic close is enabled.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261128020000_NativePotmClosedTally")]
public sealed class NativePotmClosedTally : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION public_sections.read_native_potm(_month_key text)
      RETURNS jsonb LANGUAGE plpgsql STABLE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $native_tally$
      DECLARE
        v_now timestamptz:=statement_timestamp();
        v_month date:=date_trunc('month',v_now AT TIME ZONE 'Asia/Nicosia')::date;
        v_version integer; v_closed timestamptz; v_counts jsonb;
        v_unavailable constant jsonb:='{"state":"unavailable","counts":[]}'::jsonb;
      BEGIN
        -- This is a current-month public projection, not a private history API.
        IF _month_key IS DISTINCT FROM to_char(v_month,'YYYY-MM') THEN
          RETURN v_unavailable;
        END IF;
        SELECT snapshot_version,closed_at INTO v_version,v_closed
          FROM club.potm_periods WHERE month_start=v_month;
        IF NOT FOUND THEN
          RETURN '{"state":"absent","counts":[]}'::jsonb;
        END IF;

        -- The existence of a native period makes native the sole source.
        -- Never fall back to imported counts if later evidence conflicts.
        IF NOT EXISTS(SELECT 1 FROM public_sections.current c
          JOIN public_sections.snapshots s ON s.version=c.snapshot_version
          JOIN public_content.import_state b ON b.digest=s.base_public_digest
          WHERE c.id='current' AND c.snapshot_version=v_version
            AND s.exported_at<=v_now
            AND date_trunc('month',s.exported_at AT TIME ZONE 'Asia/Nicosia')::date=v_month
            AND s.documents_count=(SELECT count(*) FROM public_sections.documents WHERE snapshot_version=v_version)
            AND s.sources_count=(SELECT count(*) FROM migration.public_sections_source WHERE snapshot_version=v_version))
          OR NOT EXISTS(SELECT 1 FROM public_sections.documents d
            WHERE d.snapshot_version=v_version AND d.kind='module' AND d.organization_id='lefteria_fc'
              AND d.id='potm' AND d.data_json->>'status' IN ('ready','empty')
              AND d.data_json->>'quarantined_count'='0')
          OR EXISTS(SELECT 1 FROM public_sections.documents d
            WHERE d.snapshot_version=v_version AND d.kind='potm' AND d.organization_id='lefteria_fc'
              AND d.data_json->>'month_key'=_month_key)
          OR EXISTS(SELECT 1 FROM migration.public_sections_source s
            WHERE s.collection='potm_votes' AND
              (s.source_json::jsonb->>'month_key'=_month_key
               OR coalesce(s.source_json::jsonb->>'month_key','') !~ '^[0-9]{4}-(0[1-9]|1[0-2])$'))
        THEN RETURN v_unavailable; END IF;

        -- No live leaderboard or partial count while voting remains open.
        IF v_closed IS NULL OR v_closed>v_now THEN
          RETURN '{"state":"pending","counts":[]}'::jsonb;
        END IF;

        -- Preserve the choice as valid when cast, even if a player has
        -- subsequently become inactive. Publication still requires that
        -- every counted choice is in the reviewed public first-team roster.
        IF EXISTS(SELECT 1 FROM club.potm_ballots b
          WHERE b.month_start=v_month AND
            (b.updated_at>v_closed
             OR (b.player_id IS NOT NULL AND NOT EXISTS(
                SELECT 1 FROM public_sections.documents d
                WHERE d.snapshot_version=v_version AND d.kind='player'
                  AND d.organization_id='lefteria_fc' AND d.id=b.player_id
                  AND d.data_json->>'team_type'='First Team'))
             OR NOT EXISTS(SELECT 1 FROM club.potm_ballot_events e
                WHERE e.month_start=b.month_start AND e.identity_id=b.identity_id
                  AND e.revision=b.revision AND e.player_id IS NOT DISTINCT FROM b.player_id
                  AND e.occurred_at=b.updated_at AND e.snapshot_version=v_version)
             OR b.revision<>(SELECT count(*) FROM club.potm_ballot_events e
                WHERE e.month_start=b.month_start AND e.identity_id=b.identity_id)))
        THEN RETURN v_unavailable; END IF;

        -- The one-row-per-Fan primary key counts only the current choice.
        -- Null withdrawals and historical revisions are never votes.
        SELECT coalesce(jsonb_agg(jsonb_build_object('player_id',player_id,'votes',votes)
          ORDER BY votes DESC,player_id COLLATE "C"),'[]'::jsonb)
          INTO v_counts FROM (
            SELECT player_id,count(*) AS votes FROM club.potm_ballots
              WHERE month_start=v_month AND player_id IS NOT NULL
              GROUP BY player_id
          ) tally;
        RETURN jsonb_build_object('state','closed','counts',v_counts);
      END $native_tally$;
      REVOKE ALL ON FUNCTION public_sections.read_native_potm(text) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION public_sections.read_native_potm(text);
      """);
}
