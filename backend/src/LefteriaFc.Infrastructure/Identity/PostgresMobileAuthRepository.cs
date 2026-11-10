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
    public Task<LegacyFanRegistrationEvidence?> GetLegacyFanRegistrationAsync(PhoneNumber phone, CancellationToken cancellationToken) =>
        ReadLegacyFanAsync(database, phone, cancellationToken);

    private static Task<LegacyFanRegistrationEvidence?> ReadLegacyFanAsync(LefteriaDbContext context, PhoneNumber phone, CancellationToken token) =>
        context.FanRegistrationEvidence.AsNoTracking().Where(row => row.NormalizedPhone == phone.Digits)
            .Select(row => new LegacyFanRegistrationEvidence(row.IsActive,
                new PendingFanRegistration(row.FirstName, row.LastName, row.Birthday, row.Id)))
            .SingleOrDefaultAsync(token);
    public Task<MobileAvatarContent?> GetPublicAvatarAsync(string fileName, CancellationToken cancellationToken) =>
        !MobileAvatarLimits.IsFileName(fileName) ? Task.FromResult<MobileAvatarContent?>(null) :
        database.MobileAvatars.AsNoTracking().Where(row => row.FileName == fileName &&
            database.MobileIdentities.Any(identity => identity.Id == row.IdentityId &&
                identity.AvatarUrl == "/api/uploads/avatars/" + row.FileName))
            .Select(row => new MobileAvatarContent(row.JpegBytes)).SingleOrDefaultAsync(cancellationToken);
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
        return row is null ? null : (await MapProfilesAsync(database, [row], cancellationToken)).SingleOrDefault();
    }

    public async Task IssueOrReplaceChallengeAsync(OtpChallengeSnapshot challenge, AuthAuditEntry audit,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var pendingFirstName = challenge.PendingFan?.FirstName;
        var pendingLastName = challenge.PendingFan?.LastName;
        var pendingBirthday = challenge.PendingFan?.Birthday;
        var pendingEvidence = challenge.PendingFan?.EvidenceId;
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity.otp_challenges
              (id, normalized_phone, code_digest, created_at, expires_at, verification_calls, is_consumed,
               pending_first_name, pending_last_name, pending_birthday, pending_evidence_id)
            VALUES ({challenge.Id}, {challenge.Phone.Digits}, {challenge.CodeDigest}, {challenge.CreatedAt},
              {challenge.ExpiresAt}, 0, false, {pendingFirstName},
              {pendingLastName}, {pendingBirthday}, {pendingEvidence})
            ON CONFLICT (normalized_phone) DO UPDATE SET
              id = EXCLUDED.id, code_digest = EXCLUDED.code_digest, created_at = EXCLUDED.created_at,
              expires_at = EXCLUDED.expires_at, verification_calls = 0, is_consumed = false,
              pending_first_name = EXCLUDED.pending_first_name,
              pending_last_name = EXCLUDED.pending_last_name, pending_birthday = EXCLUDED.pending_birthday,
              pending_evidence_id = EXCLUDED.pending_evidence_id
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
            await database.AdminAccounts.Where(x => x.Id == identity.Management.AdminAccountId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ActiveOrganizationId, scope.SelectedId), cancellationToken);
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
        var canonical = await CanonicalMobileProfiles.ReadAsync(context, rows, cancellationToken);
        return rows.Where(row => canonical.TryGetValue(row.Id, out var current) && current.SignatureMatches).Select(row =>
        {
            if (!MobileRoles.TryParse(row.Role, out var role))
            {
                throw new InvalidOperationException("Stored mobile identity has an unknown role.");
            }
            var current = canonical[row.Id];
            IReadOnlyDictionary<string, bool>? preferences = null;
            if (row.NotificationPreferencesJson is not null)
            {
                try { preferences = JsonSerializer.Deserialize<Dictionary<string, bool>>(row.NotificationPreferencesJson); }
                catch (JsonException) { /* Malformed legacy preferences never grant access. */ }
            }
            return new MobileIdentityProfile(row.Id, PhoneNumber.Parse(row.NormalizedPhone), role, row.DisplayName,
                current.IsAvailable, current.LinkedPlayerId, current.LinkedPlayerIds, current.TeamId, current.GroupIds, row.AvatarUrl, row.Platform, row.CreatedAt,
                row.LastLogin ?? row.CreatedAt, current.Management, row.FavoriteTeam, row.FirstName, row.LastName,
                row.Birthday, row.FavouritePlayerId, preferences, Email: row.Email, UpdatedAt: row.UpdatedAt);
        }).ToArray();
    }

    private sealed class Transaction(LefteriaDbContext context, IDbContextTransaction transaction) : IMobileAuthTransaction
    {
        public async Task<Guid?> ResolveLegacyMobileSubjectAsync(string legacySubject,CancellationToken cancellationToken)
        {
            try
            {
                var ids=await context.Database.SqlQuery<Guid>($"""
                    SELECT identity_id AS "Value" FROM identity.imported_mobile_subjects
                    WHERE legacy_subject = {legacySubject} COLLATE "C"
                    """).Take(2).ToArrayAsync(cancellationToken);
                return ids.Length==1?ids[0]:null;
            }
            catch(Npgsql.PostgresException error)when(error.SqlState==Npgsql.PostgresErrorCodes.UndefinedTable)
            {
                throw new MobileAuthException(MobileAuthFaultKind.ServiceUnavailable,
                    MobileAuthErrorCode.LegacyBridgeUnavailable,"Η επαναφορά σύνδεσης δεν είναι διαθέσιμη προσωρινά.");
            }
        }

        public Task<LegacyFanRegistrationEvidence?> GetLegacyFanRegistrationAsync(PhoneNumber phone, CancellationToken cancellationToken) =>
            ReadLegacyFanAsync(context, phone, cancellationToken);
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
                ? new PendingFanRegistration(row.PendingFirstName, row.PendingLastName, row.PendingBirthday.Value, row.PendingEvidenceId) : null;
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
                await context.Database.SqlQuery<Guid>($"""
                    SELECT id AS "Value" FROM identity.admin_accounts WHERE id = ANY({adminIds}) ORDER BY id FOR UPDATE
                    """).ToArrayAsync(cancellationToken);
                // Assignments are read afresh below. The API cannot edit grants;
                // assignment mutation must lock the owning admin row first.
            }
            return await MapProfilesAsync(context, rows, cancellationToken);
        }

        public async Task<bool> HasCurrentGuardianPhoneGrantAsync(
            PhoneNumber phone, CancellationToken cancellationToken)
        {
            var eligibility=await context.Database.SqlQuery<bool>($"""
                SELECT identity.has_current_guardian_phone_grant({phone.Digits}) AS "Value"
                """).SingleAsync(cancellationToken);
            return eligibility;
        }

        public async Task<MobileIdentityProfile> EnsureGrantedParentIdentityAsync(
            PhoneNumber phone, string platform, DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            // This method is called only *after* OTP proof succeeds within
            // the locked challenge transaction. A parent's entered number
            // is not an authentication credential.
            if (!await HasCurrentGuardianPhoneGrantAsync(phone,cancellationToken))
                throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                    MobileAuthErrorCode.RoleUnavailable,
                    "Guardian phone grant is no longer available.");
            var existing=await context.MobileIdentities.SingleOrDefaultAsync(
                row=>row.NormalizedPhone==phone.Digits && row.Role=="parent",
                cancellationToken);
            if(existing is not null)
            {
                if(!existing.IsActive)
                    throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                        MobileAuthErrorCode.AccountUnavailable,
                        "Disabled parent identity cannot be restored by a phone grant.");
                lockedIdentities.Add(existing.Id);
                return (await MapProfilesAsync(context,[existing],cancellationToken)).Single();
            }
            var row=new MobileIdentityRecord
            {
                Id=Guid.NewGuid(),NormalizedPhone=phone.Digits,Role="parent",
                DisplayName="Γονέας / Κηδεμόνας",IsActive=true,
                Platform=platform,CreatedAt=now
            };
            context.MobileIdentities.Add(row);
            context.SecurityAuditEvents.Add(MapAudit(new AuthAuditEntry(
                "mobile.family.phone_parent.provision",
                "success",now,row.Id)));
            await context.SaveChangesAsync(cancellationToken);
            lockedIdentities.Add(row.Id);
            return (await MapProfilesAsync(context,[row],cancellationToken)).Single();
        }

        public async Task<MobileIdentityProfile> UpsertVerifiedFanAsync(PhoneNumber phone,
            PendingFanRegistration registration, string platform, DateTimeOffset now, CancellationToken cancellationToken)
        {
            VerifiedFanSeed? seed = null;
            if (registration.EvidenceId is { } evidenceId)
            {
                var alreadyVerified = await context.MobileIdentities.SingleOrDefaultAsync(
                    row => row.NormalizedPhone == phone.Digits && row.Role == "fan", cancellationToken);
                if (alreadyVerified is not null)
                {
                    // A challenge staged before another verified profile write
                    // must never restore the older registration mirror.
                    lockedIdentities.Add(alreadyVerified.Id);
                    return (await MapProfilesAsync(context, [alreadyVerified], cancellationToken)).Single();
                }
                seed = await context.FanRegistrationEvidence.AsNoTracking()
                    .Where(row => row.Id == evidenceId && row.NormalizedPhone == phone.Digits)
                    .Select(row => new VerifiedFanSeed(row.FirstName, row.LastName, row.Birthday, row.IsActive,
                        row.AvatarUrl, row.FavouritePlayerId, row.NotificationPreferencesJson))
                    .SingleOrDefaultAsync(cancellationToken);
                if (seed is not { IsActive: true })
                    throw new MobileAuthException(MobileAuthFaultKind.Forbidden, MobileAuthErrorCode.AccountUnavailable,
                        "Ο λογαριασμός δεν είναι διαθέσιμος.");
                registration = registration with { FirstName = seed.FirstName, LastName = seed.LastName, Birthday = seed.Birthday };
            }
            var (row, created) = await GetOrCreateFanAsync(phone, registration.FullName, platform, now, cancellationToken);
            if (row.IsActive && (seed is null || created))
            {
                row.FirstName = registration.FirstName;
                row.LastName = registration.LastName;
                row.Birthday = registration.Birthday;
                row.DisplayName = registration.FullName;
                if (seed is not null)
                {
                    row.AvatarUrl = seed.AvatarUrl;
                    row.FavouritePlayerId = seed.FavouritePlayerId;
                    row.NotificationPreferencesJson = seed.PreferencesJson;
                }
                await context.SaveChangesAsync(cancellationToken);
            }
            return (await MapProfilesAsync(context, [row], cancellationToken)).Single();
        }

        public async Task<MobileIdentityProfile> EnsureNeutralFanIdentityAsync(PhoneNumber phone,
            string neutralDisplayName, string platform, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var (row, _) = await GetOrCreateFanAsync(phone, neutralDisplayName, platform, now, cancellationToken);
            return (await MapProfilesAsync(context, [row], cancellationToken)).Single();
        }

        private async Task<(MobileIdentityRecord Identity, bool Created)> GetOrCreateFanAsync(PhoneNumber phone, string name,
            string platform, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var row = await context.MobileIdentities.SingleOrDefaultAsync(
                x => x.NormalizedPhone == phone.Digits && x.Role == "fan", cancellationToken);
            var created = row is null;
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
            return (row, created);
        }

        private sealed record VerifiedFanSeed(string FirstName, string LastName, DateOnly Birthday, bool IsActive,
            string? AvatarUrl, string? FavouritePlayerId, string? PreferencesJson)
        {
            public override string ToString() => "Protected verified fan seed";
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

        public async Task<MobileIdentityProfile> UpdateFavoriteTeamAsync(Guid identityId, string? favoriteTeam,
            CancellationToken cancellationToken)
        {
            if (!lockedIdentities.Contains(identityId)) throw new InvalidOperationException("Identity row must be locked before profile mutation.");
            var row = await context.MobileIdentities.SingleAsync(x => x.Id == identityId, cancellationToken);
            row.FavoriteTeam = favoriteTeam;
            return (await MapProfilesAsync(context, [row], cancellationToken)).Single();
        }

        public async Task<MobileIdentityProfile> UpdateProfileAsync(Guid identityId, MobileProfileUpdate update,
            CancellationToken cancellationToken)
        {
            if (!lockedIdentities.Contains(identityId)) throw new InvalidOperationException("Identity row must be locked before profile mutation.");
            var row = await context.MobileIdentities.SingleAsync(x => x.Id == identityId, cancellationToken);
            if (update.NameProvided) row.DisplayName = update.Name!;
            if (update.EmailProvided) row.Email = update.Email;
            if (update.FirstNameProvided) row.FirstName = update.FirstName;
            if (update.LastNameProvided) row.LastName = update.LastName;
            if (update.BirthdayProvided) row.Birthday = update.Birthday;
            if (update.FavouritePlayerIdProvided) row.FavouritePlayerId = update.FavouritePlayerId;
            if (update.NotificationPreferencesProvided)
                row.NotificationPreferencesJson = JsonSerializer.Serialize(update.NotificationPreferences);
            row.UpdatedAt = update.UpdatedAt;
            return (await MapProfilesAsync(context, [row], cancellationToken)).Single();
        }

        public async Task UpdateAvatarAsync(Guid identityId, string fileName, SanitizedMobileAvatar avatar,
            DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (!lockedIdentities.Contains(identityId)) throw new InvalidOperationException("Identity must be locked before avatar mutation.");
            if (!MobileAvatarLimits.IsFileName(fileName)) throw new InvalidOperationException("Invalid server-generated avatar name.");
            var identity = await context.MobileIdentities.SingleAsync(row => row.Id == identityId, cancellationToken);
            var stored = await context.MobileAvatars.SingleOrDefaultAsync(row => row.IdentityId == identityId, cancellationToken);
            if (stored is null)
            {
                stored = new MobileAvatarRecord { IdentityId = identityId, PreviousAvatarUrl = identity.AvatarUrl };
                context.MobileAvatars.Add(stored);
            }
            stored.FileName = fileName;
            stored.JpegBytes = avatar.JpegBytes;
            stored.Width = avatar.Width;
            stored.Height = avatar.Height;
            stored.UpdatedAt = now;
            identity.AvatarUrl = MobileAvatarLimits.Url(fileName);
            identity.UpdatedAt = now;
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
