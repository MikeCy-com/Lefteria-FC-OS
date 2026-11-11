using System.Globalization;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Domain.Organizations;

namespace LefteriaFc.Application.MobileAuth;

public sealed partial class MobileAuthService(
    IMobileAuthRepository repository,
    ISmsSender smsSender,
    IAuthThrottle throttle,
    IOtpProtector otpProtector,
    IMobileTokenIssuer tokenIssuer,
    TimeProvider timeProvider)
{
    public const string NeutralFanDisplayName = "Φίλος της Λευτεριάς";

    public async Task<OtpRequestResult> RequestOtpAsync(string? rawPhone,
        MobileAuthRequestContext context, CancellationToken cancellationToken = default)
    {
        var phone = ParsePhone(rawPhone);
        await EnforceOtpRequestThrottleAsync(phone, context, cancellationToken);
        var profiles = ValidateProfiles(await repository.GetIdentityProfilesAsync(phone, cancellationToken), phone);
        var evidence = profiles.Any(profile => profile.Role == MobileRole.Fan) ? null :
            await repository.GetLegacyFanRegistrationAsync(phone, cancellationToken);
        var pending = evidence is { IsActive: true } ? evidence.Registration : null;
        var detectedRole = Available(profiles).FirstOrDefault()?.Role ?? (pending is not null ? MobileRole.Fan : (MobileRole?)null);
        await IssueChallengeAsync(phone, pending, detectedRole, cancellationToken);
        return new OtpRequestResult("OTP sent", phone.WireValue, detectedRole?.ToWireValue(), 300);
    }

    public async Task<FanRegistrationResult> RegisterFanAsync(FanRegistrationCommand command,
        MobileAuthRequestContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var phone = ParsePhone(command.Phone);
        var registration = ValidateRegistration(command);
        await EnforceOtpRequestThrottleAsync(phone, context, cancellationToken);
        var profiles = ValidateProfiles(await repository.GetIdentityProfilesAsync(phone, cancellationToken), phone);
        if (profiles.Any(profile => profile.Role != MobileRole.Fan))
        {
            throw Fault(MobileAuthFaultKind.BadRequest, MobileAuthErrorCode.FormalAccountAlreadyExists,
                "Αυτός ο αριθμός είναι ήδη καταχωρημένος στη Λευτεριά FC. Παρακαλώ συνδεθείτε κανονικά.");
        }
        if (profiles.Any(profile => profile.Role == MobileRole.Fan && !IsAvailable(profile)))
        {
            throw AccountUnavailable();
        }
        if (!profiles.Any(profile => profile.Role == MobileRole.Fan) &&
            await repository.GetLegacyFanRegistrationAsync(phone, cancellationToken) is { IsActive: false })
            throw AccountUnavailable();

        // Personal fields remain on the challenge until this phone proves possession.
        await IssueChallengeAsync(phone, registration, MobileRole.Fan, cancellationToken);
        return new FanRegistrationResult(phone.WireValue, MobileRole.Fan.ToWireValue());
    }

    public async Task<MobileVerificationResult> VerifyOtpAsync(MobileOtpVerificationCommand command,
        MobileAuthRequestContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var phone = ParsePhone(command.Phone);
        var code = command.Code?.Trim();
        if (string.IsNullOrEmpty(code))
        {
            throw Fault(MobileAuthFaultKind.BadRequest, MobileAuthErrorCode.InvalidInput, "Phone and code required");
        }
        if (code.Length > 64)
        {
            throw InvalidCode();
        }
        var duration = ResolveRememberDevice(command.RememberDevice);
        await EnforceThrottleAsync(MobileAuthThrottleScope.OtpVerify, context, cancellationToken);

        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        var stored = await transaction.LockChallengeAsync(phone, cancellationToken);
        if (stored is null)
        {
            throw ChallengeUnavailable();
        }
        if (stored.Phone != phone)
        {
            throw InvalidIdentityState();
        }

        OtpChallenge challenge;
        try
        {
            challenge = new OtpChallenge(stored.Id, stored.CodeDigest, stored.CreatedAt,
                stored.ExpiresAt, stored.VerificationCalls, stored.IsConsumed);
        }
        catch (ArgumentException)
        {
            await RejectChallengeAsync(transaction, stored.Id,
                Math.Clamp(stored.VerificationCalls, 0, OtpChallenge.MaximumVerificationCalls), true,
                "invalid_challenge_state", ChallengeUnavailable(), cancellationToken);
            throw InvalidIdentityState();
        }

        var profiles = ValidateProfiles(
            await transaction.LoadIdentityProfilesForUpdateAsync(phone, cancellationToken), phone);
        var requestedWire = FirstRoleValue(command.Role, command.RequestedRole);
        var requestedIsKnown = MobileRoles.TryParse(requestedWire, out var requestedRole);
        var predictedRoles = Available(profiles).Select(profile => profile.Role).ToHashSet();
        var hasFan = profiles.Any(profile => profile.Role == MobileRole.Fan);
        var evidence = hasFan ? null : await transaction.GetLegacyFanRegistrationAsync(phone, cancellationToken);
        var canCreateFan = !hasFan && evidence is not { IsActive: false } &&
            (stored.PendingFan is not null || profiles.Count > 0 || evidence is { IsActive: true });
        if (canCreateFan)
        {
            predictedRoles.Add(MobileRole.Fan);
        }
        // The issuing parent grants access by phone without inviting the
        // recipient. The recipient must still prove that phone via OTP.
        // An inactive/existing Parent identity is never silently replaced.
        var hasParent=profiles.Any(p=>p.Role==MobileRole.Parent);
        var canCreateParent=!hasParent &&
            await transaction.HasCurrentGuardianPhoneGrantAsync(phone,cancellationToken);
        if(canCreateParent)
            predictedRoles.Add(MobileRole.Parent);
        var needsPicker = requestedWire is null && predictedRoles.Count > 1;
        var requestedIsAvailable = requestedWire is null ||
            requestedIsKnown && predictedRoles.Contains(requestedRole);
        // Invalid requested roles leave a valid challenge alive, but still spend a call.
        // Unknown phones consume the proof before returning the source-compatible 404.
        var consume = predictedRoles.Count == 0 || (!needsPicker && requestedIsAvailable);
        var now = timeProvider.GetUtcNow();
        var outcome = challenge.Verify(otpProtector.ComputeDigest(stored.Id, phone, code), now, consume);
        if (outcome != OtpVerificationOutcome.Accepted)
        {
            var terminal = outcome is OtpVerificationOutcome.Expired or OtpVerificationOutcome.NotYetValid
                or OtpVerificationOutcome.TooManyAttempts or OtpVerificationOutcome.AlreadyConsumed;
            await RejectChallengeAsync(transaction, stored.Id, challenge.VerificationCalls,
                terminal || challenge.IsConsumed, outcome.ToString(), FaultFor(outcome), cancellationToken);
        }

        if (predictedRoles.Count == 0)
        {
            var fault = profiles.Count == 0
                ? Fault(MobileAuthFaultKind.NotFound, MobileAuthErrorCode.AccountNotFound,
                    "No account found for this phone number. Contact the academy.")
                : AccountUnavailable();
            await RejectChallengeAsync(transaction, stored.Id, challenge.VerificationCalls, true,
                "no_available_identity", fault, cancellationToken);
        }
        if (!requestedIsAvailable)
        {
            await RejectChallengeAsync(transaction, stored.Id, challenge.VerificationCalls, false,
                "requested_role_unavailable",
                Fault(MobileAuthFaultKind.BadRequest, MobileAuthErrorCode.RoleUnavailable,
                    "Ο ρόλος που ζητήθηκε δεν είναι διαθέσιμος για αυτόν τον αριθμό."), cancellationToken);
        }

        var platform = NormalizePlatform(context.Platform);
        var registration = stored.PendingFan ?? (canCreateFan && evidence is { IsActive: true } ? evidence.Registration : null);
        if (registration is not null && (hasFan || canCreateFan) &&
            !profiles.Any(profile => profile.Role == MobileRole.Fan && !IsAvailable(profile)))
        {
            await transaction.UpsertVerifiedFanAsync(phone, registration, platform, now, cancellationToken);
        }
        else if (canCreateFan)
        {
            await transaction.EnsureNeutralFanIdentityAsync(phone, NeutralFanDisplayName,
                platform, now, cancellationToken);
        }

        if (canCreateParent)
        {
            // Never create a Parent role before the successful OTP check.
            // The transaction revalidates the current phone grant before
            // provisioning; revocation makes the login fail closed.
            await transaction.EnsureGrantedParentIdentityAsync(phone,platform,now,
                cancellationToken);
        }

        profiles = ValidateProfiles(
            await transaction.LoadIdentityProfilesForUpdateAsync(phone, cancellationToken), phone);
        var available = Available(profiles);
        if (!predictedRoles.SetEquals(available.Select(profile => profile.Role)))
        {
            // Roll back instead of issuing a token with an unconsumed/reinterpreted proof.
            throw InvalidIdentityState();
        }

        if (needsPicker)
        {
            await transaction.SaveChallengeProgressAsync(stored.Id, challenge.VerificationCalls, false, cancellationToken);
            await transaction.AppendAuditAsync(new AuthAuditEntry("mobile.login.role_picker", "success", now,
                ChallengeId: stored.Id), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new MobileRolePickerResult(available.Select(profile =>
                new MobileIdentityOption(profile.Role.ToWireValue(), profile.Name)).ToArray(), phone.WireValue);
        }

        if (!challenge.IsConsumed)
        {
            throw InvalidIdentityState();
        }
        var selected = requestedWire is null
            ? available[0]
            : available.Single(profile => profile.Role == requestedRole);
        var selectedId = selected.Id;
        var selectedRole = selected.Role;
        selected = await transaction.UpdateLastLoginAsync(selectedId, platform, now, cancellationToken);
        RequireProfileIdentity(selected, phone, selectedId, selectedRole);
        if (!IsAvailable(selected))
        {
            throw InvalidIdentityState();
        }
        var session = new MobileSession(Guid.NewGuid(), selected.Id, phone.Digits, selected.Role, now + duration);
        await transaction.SaveChallengeProgressAsync(stored.Id, challenge.VerificationCalls, true, cancellationToken);
        await transaction.AppendAuditAsync(new AuthAuditEntry("mobile.login", "success", now,
            selected.Id, session.DeviceSessionId, stored.Id), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var roles = WireRoles(available);
        return new MobileLoginResult(tokenIssuer.Issue(session), ToUserDto(selected, roles, true),
            selected.Role.ToWireValue(), roles);
    }

    public async Task<MobileRoleSwitchResult> SwitchRoleAsync(MobileSession session, string? rawRole,
        MobileAuthRequestContext context, CancellationToken cancellationToken = default)
    {
        RequireSession(session);
        var requested = rawRole?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(requested))
        {
            throw Fault(MobileAuthFaultKind.BadRequest, MobileAuthErrorCode.RoleRequired, "role required");
        }
        if (!MobileRoles.TryParse(requested, out var targetRole))
        {
            throw RoleUnavailable();
        }
        var phone = PhoneNumber.Parse(session.NormalizedPhone);
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        var profiles = ValidateProfiles(
            await transaction.LoadIdentityProfilesForUpdateAsync(phone, cancellationToken), phone);
        var current = profiles.SingleOrDefault(profile => profile.Id == session.IdentityId);
        if (current is not null)
        {
            RequireProfileIdentity(current, phone, session.IdentityId, session.Role);
        }
        var available = Available(profiles);
        var target = available.SingleOrDefault(profile => profile.Role == targetRole) ?? throw RoleUnavailable();
        MobileRoleSwitch switched;
        try
        {
            switched = current is not null && IsAvailable(current)
                ? session.SwitchRole(AsGrant(current), AsGrant(target), timeProvider.GetUtcNow())
                : targetRole == MobileRole.Fan
                    ? session.RecoverFanAccess(AsGrant(target), timeProvider.GetUtcNow())
                    : throw RoleUnavailable();
        }
        catch (MobileSessionAccessException exception)
        {
            throw exception.Reason == MobileSessionDenial.Expired ? ExpiredSession() : RoleUnavailable();
        }
        if (switched.IsNoop)
        {
            await transaction.CommitAsync(cancellationToken);
            return new MobileRoleSwitchResult { Token = null, Noop = true, Role = requested };
        }

        var now = timeProvider.GetUtcNow();
        var targetId = target.Id;
        target = await transaction.UpdateLastLoginAsync(targetId, NormalizePlatform(context.Platform), now, cancellationToken);
        RequireProfileIdentity(target, phone, targetId, targetRole);
        if (!IsAvailable(target))
        {
            throw RoleUnavailable();
        }
        await transaction.AppendAuditAsync(new AuthAuditEntry("mobile.role.switch", "success", now,
            target.Id, session.DeviceSessionId, ReasonCode: target.Role.ToWireValue()), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var roles = WireRoles(available);
        return new MobileRoleSwitchResult
        {
            Token = tokenIssuer.Issue(switched.Session),
            User = ToUserDto(target, roles, true), Role = target.Role.ToWireValue(), AvailableRoles = roles
        };
    }

    public async Task<MobileUserDto> GetMeAsync(MobileSession session,
        CancellationToken cancellationToken = default)
    {
        RequireSession(session);
        var phone = PhoneNumber.Parse(session.NormalizedPhone);
        var profile = await repository.GetIdentityProfileAsync(session.IdentityId, cancellationToken)
            ?? throw Fault(MobileAuthFaultKind.Unauthorized, MobileAuthErrorCode.AccountNotFound, "User not found");
        RequireProfileIdentity(profile, phone, session.IdentityId, session.Role);
        var profiles = ValidateProfiles(await repository.GetIdentityProfilesAsync(phone, cancellationToken), phone);
        var latest = profiles.SingleOrDefault(item => item.Id == session.IdentityId);
        if (latest is not null)
        {
            RequireProfileIdentity(latest, phone, session.IdentityId, session.Role);
        }
        return ToUserDto(latest ?? profile, WireRoles(Available(profiles)), latest is not null && IsAvailable(latest));
    }

    public async Task<MobileProfilePreferenceResult> UpdateFavoriteTeamAsync(MobileSession session,
        bool wasProvided, string? favoriteTeam, CancellationToken cancellationToken = default)
    {
        RequireSession(session);
        if (!wasProvided)
            throw Fault(MobileAuthFaultKind.BadRequest, MobileAuthErrorCode.NoProfileChanges, "Καμία αλλαγή");
        if (favoriteTeam is not (null or "first_team" or "academy" or "both"))
            throw Fault(MobileAuthFaultKind.Validation, MobileAuthErrorCode.InvalidTeamPreference,
                "Επιλέξτε πρώτη ομάδα, ακαδημία ή και τις δύο.");

        var phone = PhoneNumber.Parse(session.NormalizedPhone);
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        var profiles = ValidateProfiles(await transaction.LoadIdentityProfilesForUpdateAsync(phone, cancellationToken), phone);
        RequireSession(session);
        var current = profiles.SingleOrDefault(profile => profile.Id == session.IdentityId) ?? throw AccountUnavailable();
        RequireProfileIdentity(current, phone, session.IdentityId, session.Role);
        if (!IsAvailable(current)) throw AccountUnavailable();
        var updated = await transaction.UpdateFavoriteTeamAsync(current.Id, favoriteTeam, cancellationToken);
        RequireProfileIdentity(updated, phone, session.IdentityId, session.Role);
        await transaction.AppendAuditAsync(new AuthAuditEntry("mobile.profile.preference.update", "success",
            timeProvider.GetUtcNow(), current.Id, session.DeviceSessionId, ReasonCode: "favorite_team"), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(true, ToUserDto(updated, WireRoles(Available(profiles)), true));
    }

    public async Task<MobileOrganizationContextDto> GetOrganizationAsync(MobileSession session,
        CancellationToken cancellationToken = default)
    {
        RequireManagementSession(session);
        var phone = PhoneNumber.Parse(session.NormalizedPhone);
        var profile = await repository.GetIdentityProfileAsync(session.IdentityId, cancellationToken)
            ?? throw AccountUnavailable();
        RequireProfileIdentity(profile, phone, session.IdentityId, MobileRole.Management);
        if (!IsAvailable(profile) || profile.Management is null)
        {
            throw AccountUnavailable();
        }
        var scope = OrganizationScope.Resolve(profile.Management.OrganizationIds,
            profile.Management.ActiveOrganizationId);
        if (scope.Status == OrganizationScopeStatus.Selected &&
            !string.Equals(profile.Management.ActiveOrganizationId, scope.SelectedId, StringComparison.Ordinal))
        {
            // Source parity: a single available organization becomes persisted shared
            // admin context. The write re-reads grants so this read cannot race revocation.
            return await SelectOrganizationAsync(session, scope.SelectedId, cancellationToken);
        }
        return ToOrganizationDto(scope);
    }

    public async Task<MobileOrganizationContextDto> SelectOrganizationAsync(MobileSession session,
        string? requestedOrganizationId, CancellationToken cancellationToken = default)
    {
        RequireManagementSession(session);
        var result = await repository.SelectOrganizationAsync(session.IdentityId,
            session.NormalizedPhone, requestedOrganizationId, timeProvider.GetUtcNow(), cancellationToken);
        var phone = PhoneNumber.Parse(session.NormalizedPhone);
        if (result.Identity is null || !IsAvailable(result.Identity))
        {
            throw AccountUnavailable();
        }
        RequireProfileIdentity(result.Identity, phone, session.IdentityId, MobileRole.Management);
        if (result.Scope.Status == OrganizationScopeStatus.InvalidSelection)
        {
            throw Fault(MobileAuthFaultKind.BadRequest,
                string.IsNullOrWhiteSpace(requestedOrganizationId)
                    ? MobileAuthErrorCode.OrganizationSelectionRequired : MobileAuthErrorCode.OrganizationUnknown,
                string.IsNullOrWhiteSpace(requestedOrganizationId)
                    ? "An organization must be selected" : "Unknown organization");
        }
        return ToOrganizationDto(result.Scope);
    }

    private async Task IssueChallengeAsync(PhoneNumber phone, PendingFanRegistration? registration,
        MobileRole? detectedRole, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var now = timeProvider.GetUtcNow();
        var code = otpProtector.CreateSixDigitCode();
        if (code.Length != 6 || code.Any(character => character is < '0' or > '9'))
        {
            throw DeliveryUnavailable();
        }
        var digest = otpProtector.ComputeDigest(id, phone, code);
        _ = new OtpChallenge(id, digest, now, now + OtpChallenge.MaximumLifetime);
        await repository.IssueOrReplaceChallengeAsync(new OtpChallengeSnapshot(id, phone, digest,
            now, now + OtpChallenge.MaximumLifetime, 0, false, registration),
            new AuthAuditEntry("mobile.otp.issue", "success", now, ChallengeId: id), cancellationToken);
        bool sent;
        try
        {
            sent = await smsSender.SendOtpAsync(phone, code, detectedRole, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RemoveFailedChallengeAsync(phone, id);
            throw;
        }
        catch (Exception)
        {
            sent = false;
        }
        if (!sent)
        {
            await RemoveFailedChallengeAsync(phone, id);
            throw DeliveryUnavailable();
        }
    }

    private async Task RemoveFailedChallengeAsync(PhoneNumber phone, Guid id)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await repository.DeleteChallengeIfMatchesAsync(phone, id,
            new AuthAuditEntry("mobile.otp.delivery", "failure", timeProvider.GetUtcNow(),
                ChallengeId: id, ReasonCode: "delivery_unavailable"), budget.Token);
    }

    private async Task EnforceThrottleAsync(MobileAuthThrottleScope scope,
        MobileAuthRequestContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var key = string.IsNullOrWhiteSpace(context.ClientKey) ? "unknown" : context.ClientKey;
        await EnforceThrottleKeyAsync(scope, key, cancellationToken);
    }

    private async Task EnforceOtpRequestThrottleAsync(PhoneNumber phone,
        MobileAuthRequestContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var client = string.IsNullOrWhiteSpace(context.ClientKey) ? "unknown" : context.ClientKey;
        await EnforceThrottleKeyAsync(MobileAuthThrottleScope.OtpRequest, "ip:" + client, cancellationToken);
        // Deliberate tightening: distributed clients cannot flood one destination.
        // The repository adapter hashes these transient keys before persisting them.
        await EnforceThrottleKeyAsync(MobileAuthThrottleScope.OtpRequest, "phone:" + phone.Digits, cancellationToken);
    }

    private async Task EnforceThrottleKeyAsync(MobileAuthThrottleScope scope, string key,
        CancellationToken cancellationToken)
    {
        var decision = await throttle.TryAcquireAsync(scope, key, timeProvider.GetUtcNow(), cancellationToken);
        if (!decision.IsAllowed)
        {
            var retry = Math.Max(1, decision.RetryAfterSeconds ?? 60);
            throw new MobileAuthException(MobileAuthFaultKind.TooManyRequests,
                MobileAuthErrorCode.RateLimited, $"Πολλα αιτηματα · δοκιμαστε ξανα σε {retry}s", retry);
        }
    }

    private async Task RejectChallengeAsync(IMobileAuthTransaction transaction, Guid id,
        int calls, bool consumed, string reason, MobileAuthException fault, CancellationToken cancellationToken)
    {
        await transaction.SaveChallengeProgressAsync(id, calls, consumed, cancellationToken);
        await transaction.AppendAuditAsync(new AuthAuditEntry("mobile.otp.verify", "failure", timeProvider.GetUtcNow(),
            ChallengeId: id, ReasonCode: reason), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        throw fault;
    }

    private PendingFanRegistration ValidateRegistration(FanRegistrationCommand command)
    {
        var first = command.FirstName?.Trim() ?? "";
        var last = command.LastName?.Trim() ?? "";
        if (first.Length == 0 && !string.IsNullOrWhiteSpace(command.Name))
        {
            var parts = command.Name.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            first = parts[0];
            last = parts.Length > 1 ? parts[1].Trim() : "";
        }
        if (first.Length == 0 || last.Length == 0 || string.IsNullOrWhiteSpace(command.Birthday))
        {
            throw Fault(MobileAuthFaultKind.BadRequest, MobileAuthErrorCode.InvalidInput,
                "Συμπληρώστε όνομα, επώνυμο, τηλέφωνο και ημερομηνία γέννησης");
        }
        if (first.Length > 100 || last.Length > 100 || first.Any(char.IsControl) || last.Any(char.IsControl))
        {
            throw Fault(MobileAuthFaultKind.BadRequest, MobileAuthErrorCode.InvalidInput,
                "Το όνομα και το επώνυμο πρέπει να περιέχουν έως 100 χαρακτήρες.");
        }
        if (!DateOnly.TryParseExact(command.Birthday, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var birthday) ||
            birthday > DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime))
        {
            throw Fault(MobileAuthFaultKind.BadRequest, MobileAuthErrorCode.InvalidBirthday,
                "Η ημερομηνία γέννησης πρέπει να είναι έγκυρη (YYYY-MM-DD).");
        }
        return new PendingFanRegistration(first, last, birthday);
    }

    private static TimeSpan ResolveRememberDevice(RememberDeviceSelection selection)
    {
        try
        {
            return RememberDevicePolicy.Resolve(SessionRealm.Mobile, selection.WasProvided, selection.Days);
        }
        catch (ArgumentException)
        {
            throw Fault(MobileAuthFaultKind.Validation, MobileAuthErrorCode.InvalidRememberSelection,
                "Επιλέξτε 30, 60 ή 90 ημέρες.");
        }
    }

    private static PhoneNumber ParsePhone(string? value) => PhoneNumber.TryParse(value, out var phone)
        ? phone : throw Fault(MobileAuthFaultKind.BadRequest, MobileAuthErrorCode.InvalidPhone,
            "Phone number required or invalid");

    private void RequireSession(MobileSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.ExpiresAt <= timeProvider.GetUtcNow())
        {
            throw ExpiredSession();
        }
        if (session.DeviceSessionId == Guid.Empty || session.IdentityId == Guid.Empty ||
            !Enum.IsDefined(session.Role) ||
            !PhoneNumber.TryParse(session.NormalizedPhone, out var phone) || phone.Digits != session.NormalizedPhone)
        {
            throw Fault(MobileAuthFaultKind.Unauthorized, MobileAuthErrorCode.SessionInvalid, "Invalid token");
        }
    }

    private void RequireManagementSession(MobileSession session)
    {
        RequireSession(session);
        if (session.Role != MobileRole.Management)
        {
            throw Fault(MobileAuthFaultKind.Forbidden, MobileAuthErrorCode.RoleUnavailable, "Not a management account");
        }
    }

    private static void RequireProfileIdentity(MobileIdentityProfile profile, PhoneNumber phone,
        Guid expectedId, MobileRole expectedRole)
    {
        if (profile.Id != expectedId || profile.Phone != phone || profile.Role != expectedRole)
        {
            throw Fault(MobileAuthFaultKind.Unauthorized, MobileAuthErrorCode.SessionInvalid, "Invalid token");
        }
    }

    private static IReadOnlyList<MobileIdentityProfile> ValidateProfiles(
        IReadOnlyList<MobileIdentityProfile> profiles, PhoneNumber phone)
    {
        if (profiles.Any(profile => profile.Id == Guid.Empty || profile.Phone != phone || !Enum.IsDefined(profile.Role)) ||
            profiles.Select(profile => profile.Id).Distinct().Count() != profiles.Count ||
            profiles.Select(profile => profile.Role).Distinct().Count() != profiles.Count)
        {
            throw InvalidIdentityState();
        }
        return profiles;
    }

    private static bool IsAvailable(MobileIdentityProfile profile) => profile.IsActive &&
        (profile.Role != MobileRole.Management ||
         profile.Management is { IsActive: true } grant && grant.AdminAccountId != Guid.Empty);

    private static IReadOnlyList<MobileIdentityProfile> Available(IReadOnlyList<MobileIdentityProfile> profiles) =>
        profiles.Where(IsAvailable).OrderBy(profile => profile.Role).ToArray();

    private static string[] WireRoles(IReadOnlyList<MobileIdentityProfile> profiles) =>
        profiles.Select(profile => profile.Role.ToWireValue()).ToArray();

    private static MobileIdentityGrant AsGrant(MobileIdentityProfile profile) =>
        new(profile.Id, profile.Phone.Digits, profile.Role, IsAvailable(profile));

    private static MobileUserDto ToUserDto(MobileIdentityProfile profile, IReadOnlyList<string> roles,
        bool currentRoleAvailable) => new()
    {
        Id = profile.Id, Phone = profile.Phone.WireValue, Role = profile.Role.ToWireValue(), Name = profile.Name,
        LinkedPlayerId = currentRoleAvailable ? profile.LinkedPlayerId : null,
        LinkedPlayerIds = currentRoleAvailable ? profile.LinkedPlayerIds.ToArray() : [],
        TeamId = currentRoleAvailable ? profile.TeamId : null,
        AcademyGroupIds = currentRoleAvailable ? profile.AcademyGroupIds.ToArray() : [],
        AvatarUrl = profile.AvatarUrl, Platform = profile.Platform, CreatedAt = profile.CreatedAt,
        LastLogin = profile.LastLogin, AvailableRoles = roles.ToArray(), CurrentRoleAvailable = currentRoleAvailable,
        FavoriteTeam = profile.FavoriteTeam, FirstName = profile.FirstName, LastName = profile.LastName,
        Birthday = profile.Birthday?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        FavouritePlayerId = profile.FavouritePlayerId,
        NotificationPreferences = profile.NotificationPreferences is null ? null :
            new Dictionary<string, bool>(profile.NotificationPreferences, StringComparer.Ordinal),
        Email = profile.Email, UpdatedAt = profile.UpdatedAt
    };

    private static MobileOrganizationContextDto ToOrganizationDto(OrganizationScope scope)
    {
        if (scope.Status == OrganizationScopeStatus.Denied)
        {
            throw Fault(MobileAuthFaultKind.Forbidden, MobileAuthErrorCode.OrganizationUnavailable,
                "No active club organization is assigned");
        }
        return new MobileOrganizationContextDto(scope.SelectedId,
            scope.SelectedId is null ? null : OrganizationName(scope.SelectedId),
            scope.AvailableIds.Select(id => new OrganizationOptionDto(id, OrganizationName(id))).ToArray(),
            scope.Status == OrganizationScopeStatus.SelectionRequired);
    }

    private static string OrganizationName(string id) => id switch
    {
        OrganizationIds.Club => "Lefteria FC",
        OrganizationIds.Academy => "Lefteria FC Academy",
        _ => throw InvalidIdentityState()
    };

    private static string NormalizePlatform(string? platform) => platform is "iOS" or "Android" or "Web/PWA"
        ? platform : "Άλλο";
    private static string? FirstRoleValue(string? first, string? second)
    {
        var value = (!string.IsNullOrEmpty(first) ? first : second)?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(value) ? null : value;
    }
    private static MobileAuthException Fault(MobileAuthFaultKind kind, MobileAuthErrorCode code, string detail) =>
        new(kind, code, detail);
    private static MobileAuthException InvalidIdentityState() => Fault(MobileAuthFaultKind.ServiceUnavailable,
        MobileAuthErrorCode.IdentityStateInvalid, "Η σύνδεση δεν μπορεί να ολοκληρωθεί. Δοκιμάστε ξανά.");
    private static MobileAuthException DeliveryUnavailable() => Fault(MobileAuthFaultKind.ServiceUnavailable,
        MobileAuthErrorCode.DeliveryUnavailable, "Η αποστολή του κωδικού δεν είναι διαθέσιμη. Δοκιμάστε ξανά.");
    private static MobileAuthException ChallengeUnavailable() => Fault(MobileAuthFaultKind.BadRequest,
        MobileAuthErrorCode.ChallengeUnavailable, "No OTP found for this number. Request a new one.");
    private static MobileAuthException AccountUnavailable() => Fault(MobileAuthFaultKind.Forbidden,
        MobileAuthErrorCode.AccountUnavailable, "Η πρόσβαση σε αυτόν τον λογαριασμό δεν είναι διαθέσιμη.");
    private static MobileAuthException InvalidCode() => Fault(MobileAuthFaultKind.BadRequest,
        MobileAuthErrorCode.InvalidCode, "Invalid code");
    private static MobileAuthException ExpiredSession() => Fault(MobileAuthFaultKind.Unauthorized,
        MobileAuthErrorCode.SessionExpired, "Token expired");
    private static MobileAuthException RoleUnavailable() => Fault(MobileAuthFaultKind.Forbidden,
        MobileAuthErrorCode.RoleUnavailable, "Δεν έχεις αυτόν τον ρόλο διαθέσιμο.");
    private static MobileAuthException FaultFor(OtpVerificationOutcome outcome) => outcome switch
    {
        OtpVerificationOutcome.InvalidCode => InvalidCode(),
        OtpVerificationOutcome.Expired => Fault(MobileAuthFaultKind.BadRequest,
            MobileAuthErrorCode.ChallengeExpired, "OTP expired. Request a new one."),
        OtpVerificationOutcome.TooManyAttempts => Fault(MobileAuthFaultKind.TooManyRequests,
            MobileAuthErrorCode.ChallengeAttemptsExceeded, "Too many attempts. Request a new OTP."),
        _ => ChallengeUnavailable()
    };
}
