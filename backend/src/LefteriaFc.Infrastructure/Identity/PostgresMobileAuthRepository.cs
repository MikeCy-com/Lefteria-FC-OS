using System.Text.Json;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Domain.Organizations;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LefteriaFc.Infrastructure.Identity;

public sealed class PostgresMobileAuthRepository(LefteriaDbContext database) : IMobileAuthRepository
{
    public async Task<IMobileAuthTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new Transaction(database, await database.Database.BeginTransactionAsync(cancellationToken));

    public async Task<IReadOnlyList<MobileIdentityProfile>> GetIdentityProfilesAsync(
        PhoneNumber phone, CancellationToken cancellationToken)
    {
        var rows = await database.MobileIdentities.AsNoTracking()
            .Where(x => x.NormalizedPhone == phone.Digits).ToListAsync(cancellationToken);
        return await MapProfilesAsync(database, rows, cancellationToken);
    }

    public async Task<MobileIdentityProfile?> GetIdentityProfileAsync(Guid identityId, CancellationToken cancellationToken)
    {
        var row = await database.MobileIdentities.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == identityId, cancellationToken);
        return row is null ? null : (await MapProfilesAsync(database, [row], cancellationToken)).Single();
    }

    public async Task IssueOrReplaceChallengeAsync(OtpChallengeSnapshot challenge, AuthAuditEntry audit,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var pendingFirstName = challenge.PendingFan?.FirstName;
        var pendingLastName = challenge.PendingFan?.LastName;
        var pendingBirthday = challenge.PendingFan?.Birthday;
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity.otp_challenges
              (id, normalized_phone, code_digest, created_at, expires_at, verification_calls, is_consumed,
               pending_first_name, pending_last_name, pending_birthday)
            VALUES ({challenge.Id}, {challenge.Phone.Digits}, {challenge.CodeDigest}, {challenge.CreatedAt},
              {challenge.ExpiresAt}, 0, false, {pendingFirstName},
              {pendingLastName}, {pendingBirthday})
            ON CONFLICT (normalized_phone) DO UPDATE SET
              id = EXCLUDED.id, code_digest = EXCLUDED.code_digest, created_at = EXCLUDED.created_at,
              expires_at = EXCLUDED.expires_at, verification_calls = 0, is_consumed = false,
              pending_first_name = EXCLUDED.pending_first_name,
              pending_last_name = EXCLUDED.pending_last_name, pending_birthday = EXCLUDED.pending_birthday
            """, cancellationToken);
        database.SecurityAuditEvents.Add(MapAudit(audit));
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteChallengeIfMatchesAsync(PhoneNumber phone, Guid challengeId, AuthAuditEntry audit,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.OtpChallenges.Where(x => x.NormalizedPhone == phone.Digits && x.Id == challengeId)
            .ExecuteDeleteAsync(cancellationToken);
        database.SecurityAuditEvents.Add(MapAudit(audit));
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<OrganizationSelectionSnapshot> SelectOrganizationAsync(Guid identityId,
        string normalizedPhone, string? requestedOrganizationId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = (Transaction)await BeginTransactionAsync(cancellationToken);
        var profiles = await transaction.LoadIdentityProfilesForUpdateAsync(PhoneNumber.Parse(normalizedPhone), cancellationToken);
        var identity = profiles.SingleOrDefault(x => x.Id == identityId);
        if (identity is not { IsActive: true, Role: MobileRole.Management, Management.IsActive: true })
        {
            return new(identity, OrganizationScope.Resolve([], null));
        }
        var scope = OrganizationScope.Select(identity.Management.OrganizationIds, requestedOrganizationId);
        if (scope.Status == OrganizationScopeStatus.Selected)
        {
            var admin = await database.AdminAccounts.SingleAsync(x => x.Id == identity.Management.AdminAccountId, cancellationToken);
            admin.ActiveOrganizationId = scope.SelectedId;
            var audit = MapAudit(new AuthAuditEntry("mobile.organization.select", "success", now, identity.Id));
            audit.OrganizationId = scope.SelectedId;
            database.SecurityAuditEvents.Add(audit);
            await transaction.CommitAsync(cancellationToken);
            identity = identity with { Management = identity.Management with { ActiveOrganizationId = scope.SelectedId } };
        }
        return new(identity, scope);
    }

    private static SecurityAuditRecord MapAudit(AuthAuditEntry audit) => new()
    {
        Id = Guid.NewGuid(), OccurredAt = audit.OccurredAt, ActorIdentityId = audit.IdentityId,
        DeviceSessionId = audit.DeviceSessionId, ChallengeId = audit.ChallengeId,
        Action = audit.Action, Outcome = audit.Outcome, ReasonCode = audit.ReasonCode
    };

    private static async Task<IReadOnlyList<MobileIdentityProfile>> MapProfilesAsync(LefteriaDbContext context,
        IReadOnlyList<MobileIdentityRecord> rows, CancellationToken cancellationToken)
    {
        var adminIds = rows.Where(x => x.ManagementAdminId.HasValue).Select(x => x.ManagementAdminId!.Value).Distinct().ToArray();
        var admins = await context.AdminAccounts.AsNoTracking().Where(x => adminIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var assignments = await context.AdminOrganizationGrants.AsNoTracking()
            .Where(x => adminIds.Contains(x.AdminAccountId)).ToListAsync(cancellationToken);
        return rows.Select(row =>
        {
            if (!MobileRoles.TryParse(row.Role, out var role))
            {
                throw new InvalidOperationException("Stored mobile identity has an unknown role.");
            }
            ManagementOrganizationGrant? management = null;
            if (row.ManagementAdminId is { } adminId && admins.TryGetValue(adminId, out var admin))
            {
                management = new(admin.Id, admin.IsActive,
                    assignments.Where(x => x.AdminAccountId == admin.Id).Select(x => x.OrganizationId).ToArray(),
                    admin.ActiveOrganizationId);
            }
            IReadOnlyDictionary<string, bool>? preferences = null;
            if (row.NotificationPreferencesJson is not null)
            {
                try { preferences = JsonSerializer.Deserialize<Dictionary<string, bool>>(row.NotificationPreferencesJson); }
                catch (JsonException) { /* Malformed legacy preferences never grant access. */ }
            }
            return new MobileIdentityProfile(row.Id, PhoneNumber.Parse(row.NormalizedPhone), role, row.DisplayName,
                row.IsActive, null, [], null, [], row.AvatarUrl, row.Platform, row.CreatedAt,
                row.LastLogin ?? row.CreatedAt, management, row.FavoriteTeam, row.FirstName, row.LastName,
                row.Birthday, null, preferences);
        }).ToArray();
    }

    private sealed class Transaction(LefteriaDbContext context, IDbContextTransaction transaction) : IMobileAuthTransaction
    {
        private readonly Dictionary<Guid, OtpChallengeRecord> lockedChallenges = [];
        private readonly HashSet<Guid> lockedIdentities = [];

        public async Task<OtpChallengeSnapshot?> LockChallengeAsync(PhoneNumber phone, CancellationToken cancellationToken)
        {
            var rows = await context.OtpChallenges.FromSqlInterpolated($"""
                SELECT * FROM identity.otp_challenges WHERE normalized_phone = {phone.Digits} FOR UPDATE
                """).ToListAsync(cancellationToken);
            var row = rows.SingleOrDefault();
            if (row is null) return null;
            lockedChallenges[row.Id] = row;
            var pending = row.PendingFirstName is not null && row.PendingLastName is not null && row.PendingBirthday.HasValue
                ? new PendingFanRegistration(row.PendingFirstName, row.PendingLastName, row.PendingBirthday.Value) : null;
            return new(row.Id, phone, (byte[])row.CodeDigest.Clone(), row.CreatedAt, row.ExpiresAt,
                row.VerificationCalls, row.IsConsumed, pending);
        }

        public async Task<IReadOnlyList<MobileIdentityProfile>> LoadIdentityProfilesForUpdateAsync(
            PhoneNumber phone, CancellationToken cancellationToken)
        {
            var rows = await context.MobileIdentities.FromSqlInterpolated($"""
                SELECT * FROM identity.mobile_identities WHERE normalized_phone = {phone.Digits} ORDER BY id FOR UPDATE
                """).ToListAsync(cancellationToken);
            foreach (var row in rows) lockedIdentities.Add(row.Id);
            var adminIds = rows.Where(x => x.ManagementAdminId.HasValue).Select(x => x.ManagementAdminId!.Value).Distinct().ToArray();
            if (adminIds.Length > 0)
            {
                await context.AdminAccounts.FromSqlInterpolated($"""
                    SELECT * FROM identity.admin_accounts WHERE id = ANY({adminIds}) ORDER BY id FOR UPDATE
                    """).LoadAsync(cancellationToken);
                // Assignments are read afresh below. The API cannot edit grants;
                // assignment mutation must lock the owning admin row first.
            }
            return await MapProfilesAsync(context, rows, cancellationToken);
        }

        public async Task<MobileIdentityProfile> UpsertVerifiedFanAsync(PhoneNumber phone,
            PendingFanRegistration registration, string platform, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var row = await GetOrCreateFanAsync(phone, registration.FullName, platform, now, cancellationToken);
            if (row.IsActive)
            {
                row.FirstName = registration.FirstName;
                row.LastName = registration.LastName;
                row.Birthday = registration.Birthday;
                row.DisplayName = registration.FullName;
                await context.SaveChangesAsync(cancellationToken);
            }
            return (await MapProfilesAsync(context, [row], cancellationToken)).Single();
        }

        public async Task<MobileIdentityProfile> EnsureNeutralFanIdentityAsync(PhoneNumber phone,
            string neutralDisplayName, string platform, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var row = await GetOrCreateFanAsync(phone, neutralDisplayName, platform, now, cancellationToken);
            return (await MapProfilesAsync(context, [row], cancellationToken)).Single();
        }

        private async Task<MobileIdentityRecord> GetOrCreateFanAsync(PhoneNumber phone, string name,
            string platform, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var row = await context.MobileIdentities.SingleOrDefaultAsync(
                x => x.NormalizedPhone == phone.Digits && x.Role == "fan", cancellationToken);
            if (row is null)
            {
                row = new MobileIdentityRecord
                {
                    Id = Guid.NewGuid(), NormalizedPhone = phone.Digits, Role = "fan", DisplayName = name,
                    Platform = platform, CreatedAt = now
                };
                context.MobileIdentities.Add(row);
                await context.SaveChangesAsync(cancellationToken);
            }
            lockedIdentities.Add(row.Id);
            return row;
        }

        public async Task<MobileIdentityProfile> UpdateLastLoginAsync(Guid identityId, string platform,
            DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (!lockedIdentities.Contains(identityId)) throw new InvalidOperationException("Identity row must be locked before login.");
            var row = await context.MobileIdentities.SingleAsync(x => x.Id == identityId, cancellationToken);
            row.LastLogin = now;
            row.Platform = platform;
            await context.SaveChangesAsync(cancellationToken);
            return (await MapProfilesAsync(context, [row], cancellationToken)).Single();
        }

        public Task SaveChallengeProgressAsync(Guid challengeId, int verificationCalls, bool isConsumed,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!lockedChallenges.TryGetValue(challengeId, out var row)) throw new InvalidOperationException("Challenge row must be locked before verification.");
            row.VerificationCalls = verificationCalls;
            row.IsConsumed = isConsumed;
            return Task.CompletedTask;
        }

        public Task AppendAuditAsync(AuthAuditEntry audit, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.SecurityAuditEvents.Add(MapAudit(audit));
            return Task.CompletedTask;
        }

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
