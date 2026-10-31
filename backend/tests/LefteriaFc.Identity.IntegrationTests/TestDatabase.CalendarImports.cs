using LefteriaFc.Application.CalendarImport;
using LefteriaFc.Infrastructure.ClubCalendar;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class TestDatabase
{
    public async Task<CalendarImportOutcome> ApplyCalendarAsync(CalendarDatabaseFixture input,bool reviewOnly=false,bool runtime=false,
        string? expectedDigest=null,ValidatedCalendarImport? manifest=null,string? expectedDatabase=null,string environment="development")
    {
        var snapshot=manifest??input.Manifest;
        var result=await PostgresCalendarImport.ApplyAsync(snapshot,runtime?fixture.AppConnectionString:fixture.AdminConnectionString,
            expectedDigest??snapshot.Digest,expectedDatabase??FixtureConfiguration.ExpectedDatabase,environment,reviewOnly);
        if(result.Applied||result.Noop)calendarFixture=input;
        return result;
    }

    public async Task AssertCalendarEmptyMigrationRecoveryAsync()
    {
        await using var context=CreateContext();
        Check.True(!await context.CalendarImportStates.AnyAsync()&&!await context.CalendarItems.AnyAsync()&&!await context.CalendarItemScopes.AnyAsync()&&
            !await context.CalendarItemGuests.AnyAsync()&&!await context.CalendarResponses.AnyAsync()&&!await context.CalendarSourceDocuments.AnyAsync()&&!await context.CalendarImportIssues.AnyAsync(),
            "Empty calendar migration recovery requires all seven calendar tables to be empty.");
        var history=(await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Check.True(history.Any(row=>row.EndsWith("_ScopedCalendar",StringComparison.Ordinal)),"Calendar recovery requires the recorded ScopedCalendar migration.");
        var privileges=await CalendarSchemaMetadataAsync();
        await using(var transaction=await context.Database.BeginTransactionAsync())
        {
            try
            {
                await ExecuteMigrationOperationsAsync(context,"ScopedCalendar",true);
                Check.Equal(0,await CalendarTableCountAsync(),"Empty calendar Down did not remove all seven calendar tables.");
                await ExecuteMigrationOperationsAsync(context,"ScopedCalendar",false);
                Check.Equal(7,await CalendarTableCountAsync(),"Empty calendar Up did not recreate all seven calendar tables.");
                Check.True(!await context.CalendarImportStates.AnyAsync()&&!await context.CalendarItems.AnyAsync()&&!await context.CalendarItemScopes.AnyAsync()&&
                    !await context.CalendarItemGuests.AnyAsync()&&!await context.CalendarResponses.AnyAsync()&&!await context.CalendarSourceDocuments.AnyAsync()&&!await context.CalendarImportIssues.AnyAsync(),
                    "Empty calendar Down/Up fabricated imported records.");
            }
            finally{await transaction.RollbackAsync();}
        }
        Check.True(history.SequenceEqual(await context.Database.GetAppliedMigrationsAsync()),"Calendar recovery probe changed global migration history.");
        Check.Equal(privileges,await CalendarSchemaMetadataAsync(),"Rolled-back calendar recovery changed original table policies or privileges.");

        Task<int> CalendarTableCountAsync()=>context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::integer AS "Value" FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE (n.nspname,c.relname) IN (
              ('club','calendar_import_state'),('club','calendar_items'),('club','calendar_item_scopes'),
              ('club','calendar_item_guests'),('club','calendar_responses'),
              ('migration','calendar_source_documents'),('migration','calendar_import_issues'))
            """).SingleAsync();
        Task<string> CalendarSchemaMetadataAsync()=>context.Database.SqlQueryRaw<string>("""
            SELECT string_agg(n.nspname||'.'||c.relname||'|'||c.relrowsecurity::text||'|'||c.relforcerowsecurity::text||'|'||coalesce(c.relacl::text,''),
              '~' ORDER BY n.nspname,c.relname) AS "Value"
            FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE (n.nspname,c.relname) IN (
              ('club','calendar_import_state'),('club','calendar_items'),('club','calendar_item_scopes'),
              ('club','calendar_item_guests'),('club','calendar_responses'),
              ('migration','calendar_source_documents'),('migration','calendar_import_issues'))
            """).SingleAsync();
    }

    public async Task AssertCalendarRollbackRefusedAsync(CalendarDatabaseFixture input)
    {
        await using var context=CreateContext();
        var state=await context.CalendarImportStates.AsNoTracking().SingleAsync();
        Check.Equal(input.Manifest.Digest,state.Digest,"Calendar rollback probe requires its exact owned populated snapshot.");
        var before=await CountsAsync();var history=(await context.Database.GetAppliedMigrationsAsync()).ToArray();var refused=false;
        await using(var transaction=await context.Database.BeginTransactionAsync())
        {
            try{await ExecuteMigrationOperationsAsync(context,"ScopedCalendar",true);}
            catch(PostgresException error)when(error.SqlState==PostgresErrorCodes.RaiseException&&error.MessageText=="calendar_data_requires_forward_recovery")
            {refused=true;}
            finally{await transaction.RollbackAsync();}
        }
        Check.True(refused,"Populated calendar migration allowed destructive rollback or returned an unexpected recovery error.");
        Check.Equal(before,await CountsAsync(),"Refused calendar rollback removed imported typed, source or issue data.");
        Check.True(history.SequenceEqual(await context.Database.GetAppliedMigrationsAsync()),"Refused calendar rollback changed global migration history.");
        Check.True((await ApplyCalendarAsync(input)).Noop,"Refused calendar rollback did not preserve exact typed/raw reconciliation.");

        async Task<(int,int,int,int,int,int,int)> CountsAsync()=>(await context.CalendarImportStates.CountAsync(),await context.CalendarItems.CountAsync(),
            await context.CalendarItemScopes.CountAsync(),await context.CalendarItemGuests.CountAsync(),await context.CalendarResponses.CountAsync(),
            await context.CalendarSourceDocuments.CountAsync(),await context.CalendarImportIssues.CountAsync());
    }

    public async Task AssertCalendarRuntimePermissionsAsync(CalendarDatabaseFixture input)
    {
        await using var connection=new NpgsqlConnection(fixture.AppConnectionString);await connection.OpenAsync();
        foreach(var sql in new[]{
            "SELECT source_json FROM migration.calendar_source_documents LIMIT 1",
            "SELECT manifest_bytes FROM club.calendar_import_state LIMIT 1",
            "SELECT * FROM migration.calendar_import_issues LIMIT 1",
            "UPDATE club.calendar_import_state SET is_complete=true WHERE false",
            "UPDATE club.calendar_items SET is_archived=false WHERE false",
            "DELETE FROM club.calendar_item_scopes WHERE false",
            "INSERT INTO club.calendar_item_guests(kind,event_id,organization_id,player_id) SELECT kind,event_id,organization_id,player_id FROM club.calendar_item_guests WHERE false",
            "DELETE FROM club.calendar_responses WHERE false",
            "UPDATE club.players SET is_active=true WHERE false",
            "UPDATE club.staff SET is_active=true WHERE false",
            "UPDATE club.player_academy_groups SET player_id=player_id WHERE false"})
        {
            try{await using var command=new NpgsqlCommand(sql,connection);await command.ExecuteNonQueryAsync();}
            catch(PostgresException issue)when(issue.SqlState==PostgresErrorCodes.InsufficientPrivilege){continue;}
            throw new InvalidOperationException("Runtime exceeded the calendar archive, canonical write or physical attendance boundary.");
        }
        // An empty WHERE clause cannot exercise a row-level physical-attendance
        // guard, because the runtime needs status UPDATE for both RSVP rows.
        // Probe the exact owned physical row, always rolling back any mutation.
        await using var transaction=await connection.BeginTransactionAsync();
        var denied=false;
        try
        {
            await using var command=new NpgsqlCommand("UPDATE club.calendar_responses SET status='absent' WHERE collection='attendance' AND kind='event' AND event_id=@event AND player_id=@player",connection,transaction);
            command.Parameters.AddWithValue("event",input.Id("host"));command.Parameters.AddWithValue("player",input.Accounts.ChildId);
            denied=await command.ExecuteNonQueryAsync()==0;
        }
        catch(PostgresException issue)when(issue.SqlState==PostgresErrorCodes.InsufficientPrivilege){denied=true;}
        finally{await transaction.RollbackAsync();}
        if(!denied)throw new InvalidOperationException("Runtime changed a protected physical attendance row.");
    }
}
