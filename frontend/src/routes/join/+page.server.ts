import {loadPublicSite} from '../../lib/server/public-content';

// Source JoinPage accepts ?partner=<slug>. Keep this attribution as display
// metadata only until the protected marketing lead write contract is ported.
export const load=async({url}:{url:URL})=>{
  const site=await loadPublicSite(url,'lefteria_fc_academy');
  const values=url.searchParams.getAll('partner');
  // Do not render arbitrary untrusted strings, duplicate parameters or
  // oversized inputs. Never use this URL parameter to grant a role or scope.
  const partnerReferral=values.length===1 && /^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$/.test(values[0])
    ? values[0] : null;
  return {...site,partnerReferral};
};
