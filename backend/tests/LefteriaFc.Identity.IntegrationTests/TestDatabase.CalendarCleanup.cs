using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class TestDatabase
{
    private CalendarDatabaseFixture? calendarFixture;

    public async Task CleanupCalendarImportsAsync()
    {
        var input=calendarFixture;if(input is null)return;
        await using var context=CreateContext();await using var transaction=await context.Database.BeginTransactionAsync();
        await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(5495884701258)");
        var state=await context.CalendarImportStates.AsNoTracking().SingleOrDefaultAsync();
        if(state is null)throw new InvalidOperationException("Refusing calendar cleanup without its exact owned snapshot.");
        var prefix=input.Accounts.Prefix;
        var items=await context.CalendarItems.AsNoTracking().Select(row=>new{row.Kind,row.Id}).ToArrayAsync();
        var expected=input.Plan.Items.Select(row=>(row.Kind,row.Id)).ToHashSet();
        var sources=await context.CalendarSourceDocuments.AsNoTracking().ToArrayAsync();
        var expectedSources=input.Manifest.Rows.Select(row=>(row.Collection,row.Ordinal)).ToHashSet();
        var scopes=await context.CalendarItemScopes.AsNoTracking().ToArrayAsync();
        var expectedScopes=input.Plan.Items.SelectMany(item=>item.Scopes.Select(scope=>(item.Kind,item.Id,scope.Kind,scope.RecordId))).ToHashSet();
        var guests=await context.CalendarItemGuests.AsNoTracking().ToArrayAsync();
        var expectedGuests=input.Plan.Items.SelectMany(item=>item.GuestPlayerIds.Select(player=>(item.Kind,item.Id,player))).ToHashSet();
        var players=input.Accounts.Roster.Players.Select(row=>row.Id).ToArray();
        var ownedPhones=input.Accounts.Phones;
        var responses=await context.CalendarResponses.AsNoTracking().ToArrayAsync();
        var actorIds=responses.Where(row=>row.ActorIdentityId.HasValue).Select(row=>row.ActorIdentityId!.Value).Distinct().ToArray();
        var actors=await context.MobileIdentities.AsNoTracking().Where(row=>actorIds.Contains(row.Id)).ToArrayAsync();
        if(state.Digest!=input.Manifest.Digest||!state.ManifestBytes.AsSpan().SequenceEqual(input.Manifest.CopySourceBytes())||
            items.Length!=expected.Count||items.Any(row=>!expected.Contains((row.Kind,row.Id))||!row.Id.StartsWith(prefix+"-calendar-",StringComparison.Ordinal))||
            sources.Length!=expectedSources.Count||sources.Any(row=>row.ImportDigest!=input.Manifest.Digest||!expectedSources.Contains((row.Collection,row.Ordinal)))||
            responses.Any(row=>!expected.Contains((row.Kind,row.EventId))||!players.Contains(row.PlayerId))||
            actors.Length!=actorIds.Length||actors.Any(row=>!ownedPhones.Contains(row.NormalizedPhone))||
            scopes.Length!=expectedScopes.Count||scopes.Any(row=>!expectedScopes.Contains((row.Kind,row.EventId,row.ScopeKind,row.RecordId)))||
            guests.Length!=expectedGuests.Count||guests.Any(row=>!expectedGuests.Contains((row.Kind,row.EventId,row.PlayerId))))
            throw new InvalidOperationException("Refusing calendar cleanup outside exact owned manifest, source IDs and fictional phones.");
        var eventIds=items.Select(row=>row.Id).ToArray();
        await context.CalendarResponses.Where(row=>eventIds.Contains(row.EventId)&&players.Contains(row.PlayerId)).ExecuteDeleteAsync();
        await context.CalendarItemGuests.Where(row=>eventIds.Contains(row.EventId)).ExecuteDeleteAsync();
        await context.CalendarItemScopes.Where(row=>eventIds.Contains(row.EventId)).ExecuteDeleteAsync();
        await context.CalendarItems.Where(row=>eventIds.Contains(row.Id)).ExecuteDeleteAsync();
        await context.CalendarImportIssues.Where(row=>context.CalendarSourceDocuments.Any(source=>source.ImportDigest==input.Manifest.Digest&&source.Collection==row.Collection&&source.Ordinal==row.Ordinal)).ExecuteDeleteAsync();
        await context.CalendarSourceDocuments.Where(row=>row.ImportDigest==input.Manifest.Digest).ExecuteDeleteAsync();
        await context.CalendarImportStates.Where(row=>row.Digest==input.Manifest.Digest).ExecuteDeleteAsync();
        await transaction.CommitAsync();calendarFixture=null;
        // All security audits remain append-only, including HTTP RSVP audits.
    }

}
