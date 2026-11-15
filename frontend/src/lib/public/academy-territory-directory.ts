import type { PublicGroup } from './types';

/**
 * Public directory hierarchy is keyed ONLY by backend-verified IDs/slugs.
 * Legacy imported free-text city/location labels must never create a
 * geographic node, even when their text happens to match a real city.
 */
export type AcademyDirectoryLocation = {
  id: string; name: string; slug: string; groups: PublicGroup[];
};
export type AcademyDirectoryCity = {
  id: string; name: string; slug: string; locations: AcademyDirectoryLocation[];
};
export type AcademyTerritoryDirectory = {
  cities: AcademyDirectoryCity[]; unassigned: PublicGroup[];
};

const validSlug = (value: unknown): value is string =>
  typeof value === 'string' && /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(value);

export function buildAcademyTerritoryDirectory(
  groups: readonly PublicGroup[]
): AcademyTerritoryDirectory {
  const cities: AcademyDirectoryCity[] = [];
  const unassigned: PublicGroup[] = [];
  const seenCities = new Map<string, AcademyDirectoryCity>();
  const seenLocations = new Map<string, AcademyDirectoryLocation>();

  for (const group of groups) {
    const cityId = group.city_id, locationId = group.location_id;
    if (group.organization_id !== 'lefteria_fc_academy' ||
        typeof cityId !== 'string' || !cityId ||
        typeof locationId !== 'string' || !locationId ||
        !validSlug(group.city_slug) || !validSlug(group.location_slug) ||
        typeof group.city !== 'string' || !group.city.trim() ||
        typeof group.location_name !== 'string' || !group.location_name.trim()) {
      unassigned.push(group);
      continue;
    }
    let city = seenCities.get(cityId);
    if (!city) {
      city = { id:cityId, name:group.city, slug:group.city_slug, locations:[] };
      seenCities.set(cityId,city); cities.push(city);
    }
    const key = cityId + '/' + locationId;
    let location = seenLocations.get(key);
    if (!location) {
      location = { id:locationId, name:group.location_name,
        slug:group.location_slug, groups:[] };
      seenLocations.set(key,location); city.locations.push(location);
    }
    location.groups.push(group);
  }
  return { cities, unassigned };
}
