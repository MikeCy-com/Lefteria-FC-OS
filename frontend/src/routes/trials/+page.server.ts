import { loadPublicSite } from '../../lib/server/public-content';
import { loadFirstTeamTrialSettings } from '../../lib/server/trials-settings';

// First-team trials are owned by FC, never by the Academy URL selector.
// Load safe source-default settings even when the internal API is unavailable.
export const load = async ({ url }: { url: URL }) => {
  const [site, trialSettings] = await Promise.all([
    loadPublicSite(url, 'lefteria_fc'),
    loadFirstTeamTrialSettings()
  ]);
  return { ...site, trialSettings };
};
