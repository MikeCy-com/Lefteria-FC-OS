import type { PublicSections } from './sections-types';

const club = 'lefteria_fc';
const zone = 'Asia/Nicosia';

export type PotmCandidate = {
  id: string; name: string; number: number; position: string | null;
  imageUrl: string | null; votes: number | null;
};
export type PotmView = {
  status: 'published' | 'not-published' | 'unavailable';
  monthKey: string | null; snapshotAt: string | null;
  totalVotes: number | null; candidates: PotmCandidate[];
};

function unavailable(candidates: PotmCandidate[] = [], monthKey: string | null = null,
  snapshotAt: string | null = null): PotmView {
  return { status: 'unavailable', monthKey, snapshotAt, totalVotes: null, candidates };
}

function monthInCyprus(value: string): string | null {
  const date = new Date(value);
  if (!Number.isFinite(date.getTime())) return null;
  const parts = new Intl.DateTimeFormat('en-CA', { year: 'numeric', month: '2-digit', timeZone: zone }).formatToParts(date);
  const year = parts.find(p => p.type === 'year')?.value;
  const month = parts.find(p => p.type === 'month')?.value;
  return year && month ? year + '-' + month : null;
}

function safeImage(value: string | null | undefined): string | null {
  if (!value || value.length > 2048 || /[\u0000-\u001f\u007f\\]/.test(value)) return null;
  // The backend's public projection is already filtered; do not allow an
  // arbitrary relative path to call a private application endpoint.
  if (/^\/(?:api\/uploads|uploads|club|images|mobile)\/[a-zA-Z0-9._%/-]+$/.test(value) &&
      !value.split('/').some(part => part === '..' || part === '.') && !/%(?:2e|2f|5c)/i.test(value)) return value;
  try {
    const url = new URL(value);
    return url.protocol === 'https:' && !url.username && !url.password ? url.href : null;
  } catch { return null; }
}

/** Only use the published first-team roster and the aggregate from the same
 * snapshot. Never expose voter records, invent totals or mix two months.
 * Historical aggregate rows cannot establish eligibility to submit a vote.
 */
export function projectPotm(sections: PublicSections | null): PotmView {
  if (!sections || sections.organization_id !== club || sections.time_zone !== zone ||
      !Array.isArray(sections.players) || !Array.isArray(sections.modules)) return unavailable();
  const monthKey = monthInCyprus(sections.generated_at);
  const snapshotMonth = monthInCyprus(sections.snapshot_at);
  if (!monthKey || !snapshotMonth ||
      Date.parse(sections.snapshot_at) > Date.parse(sections.generated_at)) return unavailable();

  const ids = new Set<string>();
  const candidates: PotmCandidate[] = [];
  for (const row of sections.players) {
    if (!row || row.organization_id !== club || row.team_type !== 'First Team') continue;
    if (typeof row.id !== 'string' || !row.id || row.id.length > 128 || ids.has(row.id) ||
        typeof row.name !== 'string' || !row.name.trim() || !Number.isSafeInteger(row.number))
      return unavailable([], monthKey, sections.snapshot_at);
    ids.add(row.id);
    candidates.push({ id: row.id, name: row.name, number: row.number, position: row.position ?? null,
      imageUrl: safeImage(row.image_url), votes: null });
  }
  candidates.sort((a,b) => a.number-b.number || a.name.localeCompare(b.name, 'el') || a.id.localeCompare(b.id));
  const result = sections.potm;
  const modules = sections.modules.filter(row => row?.id === 'potm' && row.organization_id === club);
  // A partial import is not a complete tally. The backend currently returns a
  // synthetic zero/empty result if a month has no imported projection, so an
  // empty aggregate must never be shown as an authoritative zero-vote count.
  if (modules.length !== 1 || !['ready','empty'].includes(modules[0].status) ||
      modules[0].quarantined_count !== 0 || !result || result.organization_id !== club ||
      result.month_key !== monthKey || !Array.isArray(result.results) ||
      !Number.isSafeInteger(result.total_votes) || result.total_votes < 0)
    return unavailable(candidates, monthKey, sections.snapshot_at);
  if (!result.results.length && result.total_votes === 0)
    return { status: 'not-published', monthKey, snapshotAt: sections.snapshot_at, totalVotes: null, candidates };
  if (modules[0].status !== 'ready' || snapshotMonth !== monthKey)
    return unavailable(candidates, monthKey, sections.snapshot_at);

  const votes = new Map<string, number>();
  let total = 0;
  for (const row of result.results) {
    if (!row || !ids.has(row.player_id) || votes.has(row.player_id) ||
        !Number.isSafeInteger(row.votes) || row.votes <= 0)
      return unavailable(candidates, monthKey, sections.snapshot_at);
    votes.set(row.player_id, row.votes);
    total += row.votes;
    if (!Number.isSafeInteger(total)) return unavailable(candidates, monthKey, sections.snapshot_at);
  }
  if (total !== result.total_votes)
    return unavailable(candidates, monthKey, sections.snapshot_at);

  const ranked = candidates.map(row => ({ ...row, votes: votes.get(row.id) ?? 0 }))
    .sort((a,b) => b.votes-a.votes || a.number-b.number || a.id.localeCompare(b.id));
  return { status: 'published', monthKey, snapshotAt: sections.snapshot_at, totalVotes: total, candidates: ranked };
}

export function potmMonthLabel(monthKey: string | null): string {
  if (!monthKey || !/^\d{4}-(0[1-9]|1[0-2])$/.test(monthKey)) return '';
  return new Intl.DateTimeFormat('el-GR', { month: 'long', year: 'numeric', timeZone: zone })
    .format(new Date(monthKey + '-15T12:00:00Z'));
}
