import type { components } from '../api/schema';

export type PublicOrganization = 'lefteria_fc' | 'lefteria_fc_academy';
export type PublicNewsScope = PublicOrganization | null;
export type PublicClub = components['schemas']['PublicClub'];
export type PublicTeam = components['schemas']['PublicTeam'];
export type PublicGroup = components['schemas']['PublicAcademyGroup'];
export type PublicArticle = components['schemas']['PublicArticle'];
export type PublicFixture = components['schemas']['PublicFixture'];
export type PublicSite = components['schemas']['PublicSiteResult'];
export type PublicNews = components['schemas']['PublicNewsResult'];
export type PublicPageData = { site: PublicSite | null; available: boolean; organization: PublicOrganization; navigationGroups: PublicGroup[] };
export type PublicHomeData = PublicPageData & { news: PublicNews | null; newsAvailable: boolean };
