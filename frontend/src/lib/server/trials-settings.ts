import { publicApi } from './public-content';
import { verifiedClosedTrialSettings } from '../public/trials-settings-policy';
import type { FirstTeamTrialSettings } from '../public/trials-settings-policy';

export async function loadFirstTeamTrialSettings(): Promise<FirstTeamTrialSettings> {
  try {
    // Fixed, credential-free OS internal API origin, never a user-supplied URL.
    const result = await publicApi().GET('/api/trials/settings');
    return result.response.status === 200
      ? verifiedClosedTrialSettings(result.data)
      : verifiedClosedTrialSettings(null);
  } catch {
    return verifiedClosedTrialSettings(null);
  }
}
