import { loadTeamPage } from '../../lib/server/public-sections';
import { readWebsitePages } from '../../lib/server/website-pages';
import { nativeEditorialBlocks } from '../../lib/public/native-editorial-regions';
import type { NativeEditorialRegion } from '../../lib/public/website-pages';

const regions:Partial<Record<string,NativeEditorialRegion>>={
  roster:'canonical-team-roster-sections',
  schedule:'canonical-team-schedule-sections',
  results:'canonical-team-results-sections',
  statistics:'canonical-team-statistics-sections'
};
export async function load({url}: {url:URL}) {
  const team=await loadTeamPage(url);
  const region=regions[team.tab];
  const pages=region?await readWebsitePages('lefteria_fc'):[];
  return {...team,editorialBlocks:region?nativeEditorialBlocks(pages,'lefteria_fc',region):[]};
}
