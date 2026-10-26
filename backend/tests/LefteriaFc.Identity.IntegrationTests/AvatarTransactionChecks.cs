using System.Net;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private async Task AvatarAuthorizationAsync()
    {
        using var anonymous = Client();
        var calls = factory.Avatars.Calls;
        using (var invalid = new StringContent("must not be read as multipart"))
        {
            invalid.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("multipart/form-data; boundary=unread-probe");
            using var denied = await anonymous.PostAsync(AvatarPath, invalid);
            Check.Equal(HttpStatusCode.Unauthorized, denied.StatusCode, "Anonymous upload reached body validation.");
        }
        Check.Equal(calls, factory.Avatars.Calls, "Anonymous upload reached native decode.");
        using var client = Client();
        var phone = database.NewPhone();
        var id = await database.SeedIdentityAsync(phone, MobileRole.Fan);
        var login = await LoginAsync(client, phone, "fan");
        var token = login.GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", SignedToken(id, phone, IdentityApplicationFactory.Issuer, IdentityApplicationFactory.Audience, "mobile_access", "parent", false));
        await RejectAvatarAsync(client, AvatarImage(20, 20, SKEncodedImageFormat.Png), "image/png", 401, 403);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var gate = factory.Avatars.HoldNext();
        var pending = UploadAvatarResponseAsync(client, AvatarImage(20, 20, SKEncodedImageFormat.Png), "image/png");
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try { await database.SetIdentityActiveAsync(id, false).WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { gate.Release.TrySetResult(); }
        using (var response = await pending) Check.True((int)response.StatusCode is 401 or 403, "Identity revoked during decode still saved an avatar.");
        Check.True((await database.IdentityAsync(phone, MobileRole.Fan))!.AvatarUrl is null, "Revoked identity wrote its avatar URL.");
        Check.Equal(0, await AvatarCountAsync(id), "Revoked decode persisted an avatar row.");

        using var management = Client();
        var managementPhone = database.NewPhone();
        var admin = await database.SeedAdminAsync(OrganizationIds.Club);
        var managementId = await database.SeedIdentityAsync(managementPhone, MobileRole.Management, adminId: admin);
        await LoginAsync(management, managementPhone, "management");
        var adminGate = factory.Avatars.HoldNext();
        var adminPending = UploadAvatarResponseAsync(management, AvatarImage(20, 20, SKEncodedImageFormat.Png), "image/png");
        await adminGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try { await database.SetAdminActiveAsync(admin, false).WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { adminGate.Release.TrySetResult(); }
        using (var response = await adminPending) Check.True((int)response.StatusCode is 401 or 403, "Admin revoked during decode still saved an avatar.");
        Check.True((await database.IdentityAsync(managementPhone, MobileRole.Management))!.AvatarUrl is null, "Revoked admin wrote its avatar URL.");
        await RejectAvatarAsync(management, AvatarImage(20, 20, SKEncodedImageFormat.Png), "image/png", 401, 403);
        Check.Equal(0, await AvatarCountAsync(managementId), "Revoked admin left stored avatar bytes.");
    }

    private async Task AvatarRollbackAsync()
    {
        using var client = Client();
        var phone = database.NewPhone();
        var id = await database.SeedIdentityAsync(phone, MobileRole.Fan);
        await LoginAsync(client, phone, "fan");
        var original = await UploadAvatarAsync(client, AvatarImage(50, 30, SKEncodedImageFormat.Png), "image/png");
        var before = ProfileDetailsSnapshot((await database.IdentityAsync(phone, MobileRole.Fan))!);
        var audits = await database.CountSuccessfulAuditsAsync(id);
        var probe = "avatar_failure_" + Guid.NewGuid().ToString("N");
        // Exact generated identifier and fixture UUID, never user input. The
        // executable's guard checked the disposable server before any mutation.
        var create = "CREATE FUNCTION identity.PROBE() RETURNS trigger LANGUAGE plpgsql AS $failure$ BEGIN IF NEW.identity_id = 'IDENTITY'::uuid THEN RAISE EXCEPTION 'Isolated avatar storage failure'; END IF; RETURN NEW; END; $failure$; CREATE TRIGGER PROBE BEFORE INSERT OR UPDATE ON identity.mobile_avatars FOR EACH ROW EXECUTE FUNCTION identity.PROBE();"
            .Replace("PROBE", probe, StringComparison.Ordinal).Replace("IDENTITY", id.ToString("D"), StringComparison.Ordinal);
        await using var context = database.CreateContext();
        await context.Database.ExecuteSqlRawAsync(create);
        try
        {
            await RejectAvatarAsync(client, AvatarImage(60, 40, SKEncodedImageFormat.Png), "image/png", 503);
            Check.Equal(before, ProfileDetailsSnapshot((await database.IdentityAsync(phone, MobileRole.Fan))!), "Failed storage changed URL/profile timestamp.");
            Check.Equal(audits, await database.CountSuccessfulAuditsAsync(id), "Failed storage committed a success audit.");
            Check.Equal(original.Split('/')[^1], (await context.MobileAvatars.AsNoTracking().SingleAsync(row => row.IdentityId == id)).FileName, "Failed storage changed the image row.");
        }
        finally
        {
            var cleanup = "DROP TRIGGER IF EXISTS PROBE ON identity.mobile_avatars; DROP FUNCTION IF EXISTS identity.PROBE();"
                .Replace("PROBE", probe, StringComparison.Ordinal);
            await context.Database.ExecuteSqlRawAsync(cleanup);
        }
        await UploadAvatarAsync(client, AvatarImage(20, 20, SKEncodedImageFormat.Png), "image/png");
        Check.Equal(1, await AvatarCountAsync(id), "Storage recovery grew duplicate avatar rows.");
    }
}
