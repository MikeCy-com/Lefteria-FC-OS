/**
 * Picker-side scoping mirrors the database's enforced relationships.
 * Never treat this as authorization: all server writes must validate the
 * authenticated actor's city/location/team grant in the same transaction.
 *
 * Academy opponent and playing-field catalogs are CITY-scoped.
 * Academy training bases, groups, staff and private metrics are LOCATION-scoped.
 * Club first-team catalogs are scoped to the selected verified TEAM.
 */
export type FootballCatalogSelection =
  | { organizationId:'lefteria_fc'; teamScopeId:string; academyCityId?:never; academyLocationId?:never }
  | { organizationId:'lefteria_fc_academy'; academyCityId:string; academyLocationId:string; teamScopeId?:never };

export type FootballCatalogScope = {
  id:string;
  organization_id:'lefteria_fc'|'lefteria_fc_academy';
  team_id:string|null;
  academy_city_id:string|null;
};

export type FootballCatalogLocation = {
  id:string; city_id:string; is_active:boolean;
};

export type FootballCatalogItem = {
  id:string; scope_id:string; organization_id:'lefteria_fc'|'lefteria_fc_academy';
  name:string; is_active:boolean;
};

export type FootballTrainingBase = FootballCatalogItem & {
  academy_city_id:string|null;
  academy_location_id:string|null;
};

function verifiedScope(
  selection:FootballCatalogSelection, scopes:readonly FootballCatalogScope[],
  locations:readonly FootballCatalogLocation[]
):string|null {
  if (selection.organizationId==='lefteria_fc') {
    return scopes.find(item =>
      item.organization_id==='lefteria_fc' && item.id===selection.teamScopeId &&
      !!item.team_id && item.academy_city_id===null)?.id ?? null;
  }
  // Show even shared CITY catalogs only for an actively verified location
  // belonging to that city. The API must separately verify the actor's grant.
  if (!locations.some(location=>location.id===selection.academyLocationId &&
    location.city_id===selection.academyCityId && location.is_active===true)) return null;
  return scopes.find(item =>
    item.organization_id==='lefteria_fc_academy' &&
    item.academy_city_id===selection.academyCityId && item.team_id===null)?.id ?? null;
}

export function scopedFootballMatchOptions(
  selection:FootballCatalogSelection,
  scopes:readonly FootballCatalogScope[],
  locations:readonly FootballCatalogLocation[],
  opponents:readonly FootballCatalogItem[],
  playingFields:readonly FootballCatalogItem[]
):{opponents:FootballCatalogItem[];playingFields:FootballCatalogItem[]} {
  const scopeId=verifiedScope(selection,scopes,locations);
  if (!scopeId) return {opponents:[],playingFields:[]};
  const valid=(item:FootballCatalogItem)=>item.scope_id===scopeId &&
    item.organization_id===selection.organizationId && item.is_active===true;
  return {opponents:opponents.filter(valid),playingFields:playingFields.filter(valid)};
}

export function scopedFootballTrainingBases(
  selection:FootballCatalogSelection,
  scopes:readonly FootballCatalogScope[],
  locations:readonly FootballCatalogLocation[],
  bases:readonly FootballTrainingBase[]
):FootballTrainingBase[] {
  const scopeId=verifiedScope(selection,scopes);
  if (!scopeId) return [];
  return bases.filter(base =>
    base.is_active===true && base.scope_id===scopeId &&
    base.organization_id===selection.organizationId &&
    (selection.organizationId==='lefteria_fc'
      ? base.academy_city_id===null && base.academy_location_id===null
      : base.academy_city_id===selection.academyCityId &&
        base.academy_location_id===selection.academyLocationId));
}
