/**
 * Verified, read-only club overview summaries from existing OS admin APIs.
 * No invented fixtures, attendance or notification counts.
 */
export type AdminOverviewMetric = { groupCount: number; activeAssignments: number; unassignedPlayers: number };
export type AdminPublishedNewsMetric = { publishedArticles: number };
const validCount = (value: unknown) => Number.isSafeInteger(value) && (value as number) >= 0 && (value as number) <= 1_000_000;
export function verifiedAdminAudiences(value: unknown, organizationId: string): AdminOverviewMetric | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
  const row = value as Record<string, unknown>;
  if (row.organization_id !== organizationId || row.ready !== true ||
      !validCount(row.unassigned_players) || !Array.isArray(row.groups) || row.groups.length > 300) return null;
  let activeAssignments = 0;
  for (const candidate of row.groups) {
    if (!candidate || typeof candidate !== 'object' || Array.isArray(candidate)) return null;
    const group = candidate as Record<string, unknown>;
    if (typeof group.id !== 'string' || !group.id || typeof group.name !== 'string' ||
        (group.kind !== 'team' && group.kind !== 'academy_group') || !validCount(group.active_players)) return null;
    activeAssignments += group.active_players as number;
    if (!Number.isSafeInteger(activeAssignments) || activeAssignments > 1_000_000) return null;
  }
  return { groupCount: row.groups.length, activeAssignments, unassignedPlayers: row.unassigned_players as number };
}
export function verifiedAdminPublishedNews(value: unknown, requestedLimit: number): AdminPublishedNewsMetric | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
  const row = value as Record<string, unknown>;
  if (!validCount(row.total) || row.offset !== 0 || row.limit !== requestedLimit ||
      !Array.isArray(row.items) || row.items.length > requestedLimit) return null;
  return { publishedArticles: row.total as number };
}

/** Summary of genuinely new Academy CRM enquiries, scoped to the live API. */
export function verifiedAdminNewLeads(value: unknown, organizationId: string, limit: number): number | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
  const row = value as Record<string, unknown>;
  if (organizationId !== 'lefteria_fc_academy' || row.organization_id !== organizationId ||
      row.offset !== 0 || row.limit !== limit || !validCount(row.total) ||
      !Array.isArray(row.leads) || row.leads.length > limit ||
      !row.leads.every((lead: unknown) => lead && typeof lead === 'object' && !Array.isArray(lead) &&
        (lead as Record<string,unknown>).organization_id === organizationId &&
        (lead as Record<string,unknown>).status === 'new')) return null;
  return row.total as number;
}
