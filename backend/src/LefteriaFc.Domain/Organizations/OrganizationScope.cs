namespace LefteriaFc.Domain.Organizations;

public static class OrganizationIds
{
    public const string Club = "lefteria_fc";
    public const string Academy = "lefteria_fc_academy";
    private static readonly string[] KnownIds = [Club, Academy];

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(KnownIds);

    public static bool IsKnown(string? value) => value is Club or Academy;
}

public enum OrganizationScopeStatus
{
    Denied,
    SelectionRequired,
    Selected,
    InvalidSelection
}

public sealed class OrganizationScope
{
    private OrganizationScope(
        OrganizationScopeStatus status,
        string? selectedId,
        string[] availableIds)
    {
        Status = status;
        SelectedId = selectedId;
        AvailableIds = Array.AsReadOnly(availableIds);
    }

    public OrganizationScopeStatus Status { get; }
    public string? SelectedId { get; }
    public IReadOnlyList<string> AvailableIds { get; }

    public bool CanAccess(string? resourceOrganizationId) =>
        Status == OrganizationScopeStatus.Selected &&
        string.Equals(SelectedId, resourceOrganizationId, StringComparison.Ordinal);

    public static OrganizationScope Resolve(IEnumerable<string> assignedIds, string? activeId)
    {
        var available = KnownAssignments(assignedIds);
        if (available.Length == 0)
        {
            return new(OrganizationScopeStatus.Denied, null, available);
        }

        if (activeId is not null && available.Contains(activeId, StringComparer.Ordinal))
        {
            return new(OrganizationScopeStatus.Selected, activeId, available);
        }

        return available.Length == 1
            ? new(OrganizationScopeStatus.Selected, available[0], available)
            : new(OrganizationScopeStatus.SelectionRequired, null, available);
    }

    public static OrganizationScope Select(IEnumerable<string> assignedIds, string? requestedId)
    {
        var available = KnownAssignments(assignedIds);
        var normalizedId = requestedId?.Trim();
        if (!OrganizationIds.IsKnown(normalizedId))
        {
            return new(OrganizationScopeStatus.InvalidSelection, null, available);
        }

        return available.Contains(normalizedId!, StringComparer.Ordinal)
            ? new(OrganizationScopeStatus.Selected, normalizedId, available)
            : new(OrganizationScopeStatus.Denied, null, available);
    }

    private static string[] KnownAssignments(IEnumerable<string> assignedIds)
    {
        ArgumentNullException.ThrowIfNull(assignedIds);
        var assigned = assignedIds.ToHashSet(StringComparer.Ordinal);
        return OrganizationIds.All.Where(assigned.Contains).ToArray();
    }
}
