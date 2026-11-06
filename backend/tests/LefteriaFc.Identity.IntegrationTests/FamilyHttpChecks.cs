using System.Net.Http.Json;
using System.Text.Json;
using LefteriaFc.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private async Task FamilyReadChecksAsync(AccountImportFixture accounts, HttpClient parent,
        HttpClient player, HttpClient coach, HttpClient management, HttpClient fan)
    {
        const string path = "/api/mobile/family";
        using var anonymous = Client();
        await CalendarRequestAsync(anonymous, HttpMethod.Get, path, null, 401);
        foreach (var client in new[] { player, coach, management, fan })
        {
            var denied = await CalendarRequestAsync(client, HttpMethod.Get, path, null, 403);
            Check.True(!denied.TryGetProperty("children", out _),
                "A non-guardian role received private family records.");
        }

        var result = await CalendarRequestAsync(parent, HttpMethod.Get, path, null, 200);
        var children = result.GetProperty("children").EnumerateArray().ToArray();
        Check.Equal(3, children.Length, "Canonical parent links did not include every assigned child.");
        Check.True(children.Select(row => row.GetProperty("id").GetString()).SequenceEqual(new[]
            { accounts.ChildId, accounts.Prefix + "-child-two", accounts.Prefix + "-child-fc" }),
            "The parent chooser lost its primary-first stable order.");
        Check.True(children.All(row => row.GetProperty("id").GetString() != accounts.PlayerId),
            "A player with an unrelated phone was exposed by the family endpoint.");

        var first = children[0];
        Check.Equal(OrganizationIds.Academy, first.GetProperty("organization_id").GetString(),
            "The Academy child organization scope was lost.");
        Check.True(first.GetProperty("academy_group_ids").EnumerateArray().Any(value =>
            value.GetString() == accounts.GroupId), "Canonical Academy group membership was lost.");
        Check.Equal(OrganizationIds.Club, children[2].GetProperty("organization_id").GetString(),
            "FC child organization scope was lost.");

        // An untrusted organization selector cannot widen or filter a guardian grant.
        var misleading = await CalendarRequestAsync(parent, HttpMethod.Get,
            path + "?organization_id=lefteria_fc&player_id=" + accounts.PlayerId, null, 200);
        Check.True(misleading.GetProperty("children").GetRawText() == result.GetProperty("children").GetRawText(),
            "Unsigned query parameters changed family authorization.");

        var sensitive = result.GetRawText();
        Check.True(!sensitive.Contains("private-guardian", StringComparison.Ordinal) &&
            !sensitive.Contains("owner-only-account-marker", StringComparison.Ordinal) &&
            !sensitive.Contains("phone", StringComparison.OrdinalIgnoreCase) &&
            !accounts.Phones.Any(phone => sensitive.Contains(phone, StringComparison.Ordinal)),
            "A family listing exposed private source documents or contact fields.");

        await using var context = database.CreateContext();
        var parentId = accounts.Plan.Mobiles.Single(row => row.Role == "parent").Id;
        var link = await context.MobileRecordLinks.AsNoTracking().SingleAsync(row =>
            row.IdentityId == parentId && row.RecordId == accounts.ChildId);
        await context.MobileRecordLinks.Where(row => row.IdentityId == parentId &&
            row.RecordId == accounts.ChildId).ExecuteDeleteAsync();
        try
        {
            var revoked = await CalendarRequestAsync(parent, HttpMethod.Get, path, null, 200);
            var remaining = revoked.GetProperty("children").EnumerateArray().Select(row =>
                row.GetProperty("id").GetString()).ToArray();
            Check.True(remaining.Length == 2 && !remaining.Contains(accounts.ChildId),
                "Revoked child link was still visible with an unchanged bearer token.");
        }
        finally
        {
            context.ChangeTracker.Clear();
            context.MobileRecordLinks.Add(link);
            await context.SaveChangesAsync();
        }

        await context.RosterPlayers.Where(row => row.Id == accounts.ChildId)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsArchived, true));
        try
        {
            var archived = await CalendarRequestAsync(parent, HttpMethod.Get, path, null, 200);
            Check.True(!archived.GetProperty("children").EnumerateArray().Any(row =>
                row.GetProperty("id").GetString() == accounts.ChildId),
                "Archived player remained visible in current family roster.");
        }
        finally
        {
            await context.RosterPlayers.Where(row => row.Id == accounts.ChildId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsArchived, false));
        }

        await context.MobileIdentities.Where(row => row.Id == parentId)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsActive, false));
        try
        {
            var disabled = await CalendarRequestAsync(parent, HttpMethod.Get, path, null, 403);
            Check.True(!disabled.TryGetProperty("children", out _), "Disabled guardian received a family list.");
        }
        finally
        {
            await context.MobileIdentities.Where(row => row.Id == parentId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsActive, true));
        }

        var restored = await CalendarRequestAsync(parent, HttpMethod.Get, path, null, 200);
        Check.Equal(3, restored.GetProperty("children").GetArrayLength(),
            "Family listing did not recover after exact fixture relationships were restored.");
    }
}
