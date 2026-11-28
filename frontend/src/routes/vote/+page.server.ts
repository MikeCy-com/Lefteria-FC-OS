import { loadSectionPage } from '../../lib/server/public-sections';
import { projectPotm, type PotmView } from '../../lib/public/potm';
import type { PublicPageData } from '../../lib/public/types';

export type PotmPageData = PublicPageData & { potm: PotmView };

export const load = async ({ url }: { url: URL }): Promise<PotmPageData> => {
  const { sections, sectionsAvailable, ...site } = await loadSectionPage(url, 'lefteria_fc');
  return { ...site, potm: projectPotm(sectionsAvailable ? sections : null) };
};
