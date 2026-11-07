using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

// Private source-grounded display projection. No app-role/public EXECUTE
// grants, no settings writes and no open application representation until the
// separate approval/consent/release workflow is implemented and verified.
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261108001500_FirstTeamTrialsDisplayProjection")]
public sealed class FirstTeamTrialsDisplayProjection : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE FUNCTION club.read_first_team_trial_display()
            RETURNS TABLE (
                is_open boolean,
                headline text,
                subtitle text,
                button_text text,
                closed_message text
            )
            LANGUAGE sql STABLE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $trial_display$
                SELECT
                    false,
                    COALESCE(settings.headline::text,
                        'Ετοιμος να Ξεκινησεις το Ταξιδι σου;'::text),
                    COALESCE(settings.subtitle::text,
                        'Γίνε μέλος της πρώτης ομάδας LEFTERIA FC και κάνε το πρώτο βήμα για να γίνεις επαγγελματίας ποδοσφαιριστής.'::text),
                    COALESCE(settings.button_text::text,
                        'Δηλωσε Συμμετοχη για Δοκιμαστικα'::text),
                    COALESCE(settings.closed_message::text,
                        'Οι εγγραφές για δοκιμαστικά είναι προσωρινά κλειστές. Παρακαλούμε δοκιμάστε ξανά σύντομα.'::text)
                FROM (SELECT true) AS one_row
                LEFT JOIN club.first_team_trial_settings AS settings
                    ON settings.organization_id='lefteria_fc';
            $trial_display$;

            -- PostgreSQL gives new functions PUBLIC EXECUTE by default.
            -- Keep this display projection private until a narrowly scoped
            -- runtime grant and a public read endpoint are separately tested.
            REVOKE ALL ON FUNCTION club.read_first_team_trial_display() FROM PUBLIC;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP FUNCTION club.read_first_team_trial_display();");
    }
}
