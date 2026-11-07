import {loadPublicSite} from '../../lib/server/public-content';
import {partnerReferralSlug} from '../../lib/public/referral';

// Original /join partner attribution is display-only until a secure
// persisted marketing-lead submission contract is implemented and verified.
export const load=async({url}:{url:URL})=>{
  const site=await loadPublicSite(url,'lefteria_fc_academy');
  return {...site,partnerReferral:partnerReferralSlug(url)};
};
