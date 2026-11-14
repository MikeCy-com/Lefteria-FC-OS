import { error } from '@sveltejs/kit';
import { loadPublicSite, publicApi } from './public-content';
import type { PublicOrganization } from '../public/types';
import type { PublicSections, PublicSectionPageData, TeamPageData, TeamTab, PlayerPageData, StaffPageData } from '../public/sections-types';

export async function requestSections(organization: PublicOrganization): Promise<{ value: PublicSections | null; status: number }> {
  try {
    const result = await publicApi().GET('/api/public/sections', { params: { query: { organization_id: organization } } });
    if (result.response.status !== 200 || result.data?.organization_id !== organization) return { value: null, status: 503 };
    return { value: result.data, status: 200 };
  } catch { return { value: null, status: 503 }; }
}

export async function loadSectionPage(url: URL, organization: PublicOrganization = 'lefteria_fc'): Promise<PublicSectionPageData> {
  const [site, sections] = await Promise.all([loadPublicSite(url, organization), requestSections(organization)]);
  return { ...site, sections: sections.value, sectionsAvailable: sections.status === 200 && sections.value !== null };
}

const tabs: readonly TeamTab[] = ['overview', 'roster', 'staff', 'schedule', 'results', 'gallery', 'venues', 'statistics'];
export async function loadTeamPage(url: URL): Promise<TeamPageData> {
  const requested = url.searchParams.get('tab');
  const tab = tabs.find(item => item === requested) ?? 'overview';
  const data = await loadSectionPage(url, 'lefteria_fc');
  if (['roster', 'staff', 'gallery', 'venues', 'statistics'].includes(tab) && !data.sectionsAvailable)
    error(503, 'Το περιεχόμενο της ομάδας δεν είναι προσωρινά διαθέσιμο.');
  return { ...data, tab };
}

export function requirePublicId<T extends { id: string }>(rows: readonly T[], value: string): T {
  if (!value || value.length > 400) error(404, 'Η σελίδα δεν βρέθηκε.');
  const exact = rows.find(row => row.id === value);
  const separator = value.indexOf('--');
  const found = exact ?? (separator > 0 ? rows.find(row => row.id === value.slice(separator + 2)) : undefined);
  if (!found) error(404, 'Η σελίδα δεν βρέθηκε.');
  return found;
}

export async function loadPlayerPage(url: URL, id: string): Promise<PlayerPageData> {
  const data = await loadSectionPage(url, 'lefteria_fc');
  if (!data.sectionsAvailable) error(503, 'Το προφίλ δεν είναι προσωρινά διαθέσιμο.');
  const sections = data.sections!;
  const player = requirePublicId(sections.players.filter(row => row.organization_id === 'lefteria_fc' && row.team_type === 'First Team'), id);
  const gallery = sections.gallery.filter(image => image.organization_id === player.organization_id && image.player_id === player.id);
  const season = sections.seasons.find(item => item.organization_id === player.organization_id && item.is_current && !item.is_archived)?.name ?? null;
  return { site: data.site, available: data.available, organization: data.organization,
    branding: data.branding, navigationGroups: data.navigationGroups, player, gallery, season };
}

export async function loadStaffPage(url: URL, id: string): Promise<StaffPageData> {
  const data = await loadSectionPage(url, 'lefteria_fc');
  if (!data.sectionsAvailable) error(503, 'Το προφίλ δεν είναι προσωρινά διαθέσιμο.');
  // A coach can be explicitly assigned to both organizations. The public
  // organization projection, rather than their primary team_type, owns scope.
  const staff = requirePublicId(data.sections!.staff.filter(row => row.organization_id === 'lefteria_fc'), id);
  return { site: data.site, available: data.available, organization: data.organization,
    branding: data.branding, navigationGroups: data.navigationGroups, staff };
}
