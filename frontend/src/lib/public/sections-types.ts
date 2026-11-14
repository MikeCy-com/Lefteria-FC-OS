import type { components } from '../api/schema';
import type { PublicPageData } from './types';

export type PublicSections = components['schemas']['PublicSectionsResult'];
export type SectionPlayer = components['schemas']['PublicSectionPlayer'];
export type SectionStaff = components['schemas']['PublicSectionStaff'];
export type SectionStatistics = components['schemas']['PublicSectionStatistics'];
export type TeamTab = 'overview' | 'roster' | 'staff' | 'schedule' | 'results' | 'gallery' | 'venues' | 'statistics';
export type PublicSectionPageData = PublicPageData & { sections: PublicSections | null; sectionsAvailable: boolean };
export type TeamPageData = PublicSectionPageData & { tab: TeamTab };
export type PlayerPageData = PublicPageData & { player: SectionPlayer; gallery: PublicSections['gallery']; season: string | null };
export type StaffPageData = PublicPageData & { staff: SectionStaff };
