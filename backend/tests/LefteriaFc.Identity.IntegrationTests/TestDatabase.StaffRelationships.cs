using LefteriaFc.Application.StaffRelationships;
using LefteriaFc.Infrastructure.StaffRelationships;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class TestDatabase
{
    private readonly Dictionary<string, ValidatedStaffRelationshipImport> staffImports = new(StringComparer.Ordinal);
    public async Task<StaffRelationshipApplyResult> ApplyStaffAsync(ValidatedStaffRelationshipImport plan, bool runtime = false)
    {
        staffImports[plan.Digest] = plan;
        return await PostgresStaffRelationshipImport.ApplyAsync(plan, runtime ? fixture.AppConnectionString : fixture.AdminConnectionString,
            plan.Digest, FixtureConfiguration.ExpectedDatabase, "development");
    }
    public async Task CleanupStaffImportsAsync()
    {
        foreach (var plan in staffImports.Values.ToArray()) await CleanupStaffImportAsync(plan);
    }
    public async Task CleanupStaffImportAsync(ValidatedStaffRelationshipImport plan)
    {
        await using var context = CreateContext(); await using var transaction = await context.Database.BeginTransactionAsync();
        await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(5495884701258)");
        var state = await context.StaffRelationshipImportStates.AsNoTracking().SingleOrDefaultAsync();
        if (state?.Digest != plan.Digest) return;
        if (state.StaffCount != plan.Staff.Count || await context.Staff.CountAsync() != plan.Staff.Count ||
            await context.StaffTeams.CountAsync() != state.TeamAssignmentsCount || await context.StaffAcademyGroups.CountAsync() != state.GroupAssignmentsCount ||
            await context.AcademyGroupCoaches.CountAsync() != state.GroupCoachesCount || await context.ContactProvenance.CountAsync() != state.ContactsCount ||
            await context.StaffSourceDocuments.CountAsync() != plan.Source.Count) throw new InvalidOperationException("Refusing cleanup of an unexpected staff snapshot.");
        var ids = plan.Staff.Select(row => row.Id).ToArray();
        await context.ContactProvenance.Where(row => row.ImportDigest == plan.Digest).ExecuteDeleteAsync();
        await context.StaffSourceDocuments.Where(row => row.ImportDigest == plan.Digest).ExecuteDeleteAsync();
        await context.AcademyGroupCoaches.Where(row => ids.Contains(row.StaffId)).ExecuteDeleteAsync();
        await context.StaffAcademyGroups.Where(row => ids.Contains(row.StaffId)).ExecuteDeleteAsync();
        await context.StaffTeams.Where(row => ids.Contains(row.StaffId)).ExecuteDeleteAsync();
        await context.Staff.Where(row => ids.Contains(row.Id)).ExecuteDeleteAsync();
        await context.StaffRelationshipImportStates.Where(row => row.Digest == plan.Digest).ExecuteDeleteAsync();
        await transaction.CommitAsync(); staffImports.Remove(plan.Digest);
    }
    public async Task AssertStaffPrivatePermissionsAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString); await connection.OpenAsync();
        foreach (var table in new[] { "club.staff", "club.staff_teams", "club.staff_academy_groups", "club.academy_group_coaches",
            "migration.staff_relationship_import_state", "migration.staff_source_documents", "migration.contact_provenance" })
        foreach (var sql in new[] { "SELECT * FROM " + table + " LIMIT 1", "DELETE FROM " + table + " WHERE false" })
        {
            try { await using var command = new NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege) { continue; }
            throw new InvalidOperationException("Runtime exceeded owner-only staff relationship boundary.");
        }
    }
}
