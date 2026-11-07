using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

// Private first-team settings only. No web/admin endpoint or write grant.
// Public trials remain CLOSED unless a separately reviewed approval pathway
// explicitly changes settings and enables a safe athlete-consent workflow.
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261107231500_FirstTeamTrialsSettings")]
public sealed class FirstTeamTrialsSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE club.first_team_trial_settings (
                organization_id varchar(32) PRIMARY KEY
                    REFERENCES club.organizations(id) ON DELETE RESTRICT,
                is_open boolean NOT NULL DEFAULT false,
                headline varchar(180) NOT NULL DEFAULT
                    'Ετοιμος να Ξεκινησεις το Ταξιδι σου;',
                subtitle varchar(500) NOT NULL DEFAULT
                    'Γίνε μέλος της πρώτης ομάδας LEFTERIA FC και κάνε το πρώτο βήμα για να γίνεις επαγγελματίας ποδοσφαιριστής.',
                button_text varchar(100) NOT NULL DEFAULT
                    'Δηλωσε Συμμετοχη για Δοκιμαστικα',
                closed_message varchar(500) NOT NULL DEFAULT
                    'Οι εγγραφές για δοκιμαστικά είναι προσωρινά κλειστές. Παρακαλούμε δοκιμάστε ξανά σύντομα.',
                updated_at timestamptz NOT NULL DEFAULT statement_timestamp(),
                CONSTRAINT ck_first_team_trials_scope
                    CHECK (organization_id = 'lefteria_fc'),
                CONSTRAINT ck_first_team_trials_headline
                    CHECK (length(btrim(headline)) BETWEEN 1 AND 180
                      AND headline=btrim(headline)),
                CONSTRAINT ck_first_team_trials_subtitle
                    CHECK (length(btrim(subtitle)) BETWEEN 1 AND 500
                      AND subtitle=btrim(subtitle)),
                CONSTRAINT ck_first_team_trials_button
                    CHECK (length(btrim(button_text)) BETWEEN 1 AND 100
                      AND button_text=btrim(button_text)),
                CONSTRAINT ck_first_team_trials_closed_message
                    CHECK (length(btrim(closed_message)) BETWEEN 1 AND 500
                      AND closed_message=btrim(closed_message))
            );

            -- Never make settings writable or readable to the app runtime by
            -- adding this migration. A later scoped admin capability and
            -- public read projection need separate SQL security verification.
            REVOKE ALL ON TABLE club.first_team_trial_settings FROM PUBLIC;
            ALTER TABLE club.first_team_trial_settings ENABLE ROW LEVEL SECURITY;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE club.first_team_trial_settings;");
    }
}
