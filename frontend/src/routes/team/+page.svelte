<script lang="ts">
  import PublicShell from '../../lib/public/PublicShell.svelte';
  import PublicImage from '../../lib/public/PublicImage.svelte';
  import Fixtures from '../../lib/public/Fixtures.svelte';
  import PublicTeamMatchOverview from '../../lib/public/PublicTeamMatchOverview.svelte';
  import PublicMatchScheduleTable from '../../lib/public/PublicMatchScheduleTable.svelte';
  import PublicIcon from '../../lib/public/PublicIcon.svelte';
  import { publicImages } from '../../lib/public/images';
  import OrganizationSponsorStrip from '../../lib/public/OrganizationSponsorStrip.svelte';
  import Unavailable from '../../lib/public/Unavailable.svelte';
  import { safePublicHref } from '../../lib/public/content';
  import { personPublicLink } from '../../lib/public/public-urls';
  import { selectTeamGallery, selectTeamVenues } from '../../lib/public/team-scope';
  import type { TeamPageData, TeamTab } from '../../lib/public/sections-types';
  import WebsitePageBlocks from '../../lib/public/WebsitePageBlocks.svelte';
  import type {WebsiteBlock,WebsitePageLayout} from '../../lib/public/website-pages';
  let { data }: { data: TeamPageData & {editorialBlocks:WebsiteBlock[];editorialLayout:WebsitePageLayout|null} } = $props();
  let search = $state(''); let position = $state(''); let cupOnly = $state(false);
  const navigation: [TeamTab, string][] = [['overview','Η ΟΜΑΔΑ'],['roster','ΡΟΣΤΕΡ'],['staff','ΕΠΙΤΕΛΕΙΟ'],['schedule','ΠΡΟΓΡΑΜΜΑ'],['results','ΑΠΟΤΕΛΕΣΜΑΤΑ'],['statistics','ΣΤΑΤΙΣΤΙΚΑ'],['gallery','ΓΚΑΛΕΡΙ'],['venues','ΓΗΠΕΔΑ']];
  const title = $derived(navigation.find(([id]) => id === data.tab)?.[1] ?? 'Η ΟΜΑΔΑ');
  const selectedTeam = $derived((data.site?.teams ?? []).find(team => team.id === data.selectedTeamId));
  // Only a verified club team may contribute its own public hero image.
  // A neutral pitch is the safe fallback when no club banner is published.
  const teamHeadingPhoto = $derived(
    (selectedTeam?.organization_id === 'lefteria_fc' ? selectedTeam.banner_url : null) ||
    data.site?.club?.stadium_image_url || publicImages.pitch);

  const teamsCount = $derived((data.site?.teams ?? []).filter(team => team.organization_id === 'lefteria_fc').length);
  // The legacy standings projection is First-Team-only. Do not present it
  // as Women's/Selected Team standings without an authoritative team scope.
  const isSelectedFirstTeam = $derived(Boolean(selectedTeam &&
    selectedTeam.organization_id === 'lefteria_fc' &&
    /^(first team|first_team|a.? team|α.? ομάδα|πρώτη ομάδα)$/i.test(selectedTeam.level?.trim() ?? '')));
  const firstTeamStandings = $derived(isSelectedFirstTeam
    ? (data.sections?.standings ?? []).filter(row=>row.organization_id==='lefteria_fc')
    : []);
  const withTeam = (path: string, id: string | null = data.selectedTeamId) =>
    id ? path + (path.includes('?') ? '&' : '?') + 'team_id=' + encodeURIComponent(id) : path;
  const matchesTeam = (teamId: string | null) => !data.selectedTeamId ||
    teamId === data.selectedTeamId || (teamsCount <= 1 && teamId === null);
  const fixtures = $derived((data.site?.fixtures ?? []).filter(item =>
    item.organization_id === 'lefteria_fc' && matchesTeam(item.team_id) &&
    (cupOnly
      ? /(?:κύπελλ|κυπελλ|cup)/i.test(item.competition ?? '')
      : data.tab === 'results' ? item.status === 'Completed'
      : data.tab === 'schedule' ? item.status !== 'Completed' : true)));
  const players = $derived((data.sections?.players ?? []).filter(row =>
    row.organization_id === 'lefteria_fc' && matchesTeam(row.team_id) &&
    (data.selectedTeamId !== null || row.team_type === 'First Team')));
  const staff = $derived((data.sections?.staff ?? []).filter(row =>
    row.organization_id === 'lefteria_fc' &&
    (!data.selectedTeamId || row.team_ids.includes(data.selectedTeamId) ||
      (teamsCount <= 1 && row.team_ids.length === 0))));
  const positions = $derived([...new Set(players.map(row => row.position).filter((value): value is string => !!value))]);
  const normalize = (value: string) => value.normalize('NFD').replace(/[\u0300-\u036f]/g,'').toLocaleLowerCase('el-GR');
  const mapHref = (value: string | null | undefined) => value ? safePublicHref(value) : null;
  const filtered = $derived(players.filter(row => (!position || row.position === position) && normalize(row.name + ' ' + row.number).includes(normalize(search.trim()))));
  const gallery = $derived(selectTeamGallery(data.sections?.gallery ?? [], players,
    data.selectedTeamId, isSelectedFirstTeam));
  const venues = $derived(selectTeamVenues(data.sections?.venues ?? [], fixtures,
    data.selectedTeamId, isSelectedFirstTeam));
  const competitions = $derived([...new Set(firstTeamStandings.map(row => row.competition))]);
</script>
<svelte:head><title>{title} · {selectedTeam?.name ?? 'ΠΡΩΤΗ ΟΜΑΔΑ'} | ΛΕΥΤΕΡΙΑ FC</title><meta name="description" content={`${selectedTeam?.name ?? 'ΛΕΥΤΕΡΙΑ FC'} · Παίκτες, τεχνικό επιτελείο, πραγματικά στατιστικά, αγώνες και αποτελέσματα.`} /></svelte:head>
<PublicShell site={data.site} branding={data.branding} organization="lefteria_fc" navigationGroups={data.navigationGroups}>
  <section class="public-page-heading team-heading" class:matches-reference-heading={data.tab === 'schedule' || data.tab === 'results'}>
    {#if data.tab === 'schedule' || data.tab === 'results'}
      <div class="matches-reference-banner" style:background-image={`linear-gradient(90deg,#060808f2,#0608089c 42%,#07080824 80%),url("${publicImages.pitch}")`}>
        <p class="public-eyebrow">ΛΕΥΤΕΡΙΑ FC</p>
        <h1>ΑΓΩΝΕΣ</h1>
        <p>Πρόγραμμα αγώνων, αποτελέσματα και επιβεβαιωμένη βαθμολογία.</p>
      </div>
      <nav class="matches-reference-tabs" aria-label="Ενότητες αγώνων">
        <a href={withTeam('/team?tab=schedule')} aria-current={data.tab === 'schedule' && !cupOnly ? 'page' : undefined}><PublicIcon name="calendar" size={19}/> ΠΡΟΓΡΑΜΜΑΤΙΣΜΕΝΟΙ</a>
        <a href={withTeam('/team?tab=results')} aria-current={data.tab === 'results' && !cupOnly ? 'page' : undefined}><PublicIcon name="trophy" size={19}/> ΑΠΟΤΕΛΕΣΜΑΤΑ</a>
        <a href={data.selectedTeamId ? '/team?team_id='+encodeURIComponent(data.selectedTeamId)+'#standings' : '/team#standings'}><PublicIcon name="trophy" size={19}/> ΒΑΘΜΟΛΟΓΙΑ</a>
        <button type="button" aria-pressed={cupOnly} onclick={() => cupOnly = !cupOnly} data-testid="match-cup-filter"><PublicIcon name="trophy" size={19}/> ΚΥΠΕΛΛΟ</button>
      </nav>
          {#if teamsCount > 1}
      <nav class="public-team-selector" aria-label="Επίλεξε ομάδα" data-testid="public-team-selector">
        {#each (data.site?.teams ?? []).filter(team => team.organization_id === 'lefteria_fc') as team (team.id)}
          <a href={withTeam('/team?tab='+data.tab, team.id)}
            aria-current={team.id === data.selectedTeamId ? 'page' : undefined}
            data-testid="public-team-select">{team.name}</a>
        {/each}
      </nav>
    {/if}
      <details class="matches-extra-navigation">
        <summary>ΟΛΕΣ ΟΙ ΕΝΟΤΗΤΕΣ ΟΜΑΔΑΣ</summary>
            <nav class="public-subnav team-tabs" aria-label="Ενότητες ομάδας">{#each navigation as [id,label]}<a href={withTeam(id === 'overview' ? '/team' : '/team?tab='+id)} aria-current={data.tab === id ? 'page' : undefined}>{label}</a>{/each}</nav>
      </details>
    {:else}
      <div class="team-heading-photo" aria-hidden="true" data-testid="team-heading-photo">
        <PublicImage src={teamHeadingPhoto} alt="" eager />
      </div>
      <p class="public-eyebrow">ΤΑ ΧΡΩΜΑΤΑ ΜΑΣ. Η ΠΕΡΗΦΑΝΙΑ ΜΑΣ.</p>
      <h1>{selectedTeam?.name ?? 'ΠΡΩΤΗ ΟΜΑΔΑ'}</h1>
          {#if teamsCount > 1}
      <nav class="public-team-selector" aria-label="Επίλεξε ομάδα" data-testid="public-team-selector">
        {#each (data.site?.teams ?? []).filter(team => team.organization_id === 'lefteria_fc') as team (team.id)}
          <a href={withTeam(data.tab === 'overview' ? '/team' : '/team?tab='+data.tab, team.id)}
            aria-current={team.id === data.selectedTeamId ? 'page' : undefined}
            data-testid="public-team-select">{team.name}</a>
        {/each}
      </nav>
    {/if}
          <nav class="public-subnav team-tabs" aria-label="Ενότητες ομάδας">{#each navigation as [id,label]}<a href={withTeam(id === 'overview' ? '/team' : '/team?tab='+id)} aria-current={data.tab === id ? 'page' : undefined}>{label}</a>{/each}</nav>
    {/if}
  </section>
  {#if data.editorialBlocks?.length}
    <section class="public-section team-cms-region" aria-label="Δημοσιευμένες ενότητες ομάδας" data-testid="team-cms-region">
      <WebsitePageBlocks pageLayout={data.editorialLayout} blocks={data.editorialBlocks ?? []}/>
    </section>
  {/if}
  {#if !data.available}<Unavailable />{:else}<section class="public-section team-section">
    {#if data.tab === 'overview'}
      {#each (data.site?.teams ?? []).filter(team => team.id === data.selectedTeamId) as team (team.id)}<article class="public-team-overview">{#if team.banner_url}<PublicImage src={team.banner_url} alt="" />{/if}<div><p class="public-eyebrow">{team.level || 'ΛΕΥΤΕΡΙΑ FC'}</p><h2>{team.name}</h2>{#if team.description}<p class="preserve-lines">{team.description}</p>{/if}<a class="public-button" href={withTeam('/team?tab=roster')}>ΓΝΩΡΙΣΕ ΤΟΥΣ ΠΑΙΚΤΕΣ ↗</a></div></article>{/each}
      <div id="standings" class="standings-anchor" data-testid="team-scoped-standings">
        {#if firstTeamStandings.length}
          {#each competitions as competition}
            <div class="section-heading"><h2>ΒΑΘΜΟΛΟΓΙΑ</h2><p>{competition}</p></div>
            <div class="table-scroll"><table>
              <caption class="sr-only">Βαθμολογία {competition} · {selectedTeam?.name ?? 'ΛΕΥΤΕΡΙΑ FC'}</caption>
              <thead><tr><th scope="col">ΟΜΑΔΑ</th><th scope="col">ΑΓ</th><th scope="col">Ν</th><th scope="col">Ι</th><th scope="col">Η</th><th scope="col">ΓΚΟΛ</th><th scope="col">Β</th></tr></thead>
              <tbody>{#each firstTeamStandings.filter(row=>row.competition===competition) as row (row.id)}
                <tr><th scope="row">{row.team_name}</th><td>{row.played}</td><td>{row.won}</td><td>{row.drawn}</td><td>{row.lost}</td><td>{row.goals_for}:{row.goals_against}</td><td class="points">{row.points}</td></tr>
              {/each}</tbody>
            </table></div>
          {/each}
        {:else}
          <p class="public-empty" data-testid="team-standings-unavailable">Δεν έχει δημοσιευτεί επιβεβαιωμένη βαθμολογία για αυτή την ομάδα.</p>
        {/if}
      </div>
    {/if}
    {#if data.tab === 'overview' || data.tab === 'schedule' || data.tab === 'results'}
      {#if data.tab === 'schedule'}
        <PublicTeamMatchOverview {fixtures} standings={firstTeamStandings}
          teamId={data.selectedTeamId} clubName={data.site?.club?.name ?? 'ΛΕΥΤΕΡΙΑ FC'}
          clubLogo={data.site?.club?.logo_url ?? null} />
      {/if}
      {#if data.tab === 'schedule' || data.tab === 'results'}
        <PublicMatchScheduleTable {fixtures} clubName={selectedTeam?.name ?? data.site?.club?.name ?? 'ΛΕΥΤΕΡΙΑ FC'}
          clubLogo={data.site?.club?.logo_url ?? null} />
      {:else}
        <div class="section-heading"><h2>ΑΓΩΝΕΣ & ΠΡΟΓΡΑΜΜΑ</h2></div>
        {#if fixtures.length}<Fixtures {fixtures} />{:else}<p class="public-empty">Δεν έχουν καταχωριστεί αγώνες σε αυτή την ενότητα.</p>{/if}
      {/if}
    {:else if data.tab === 'roster'}
      <div class="roster-intro"><div><p class="public-eyebrow">ΟΙ ΑΝΘΡΩΠΟΙ ΠΙΣΩ ΑΠΟ ΤΗ ΦΑΝΕΛΑ</p><h2>ΤΟ ΡΟΣΤΕΡ ΜΑΣ</h2></div><span>{players.length} ΠΑΙΚΤΕΣ</span></div>
      <div class="roster-filters" role="search" aria-label="Αναζήτηση παικτών"><label>ΑΝΑΖΗΤΗΣΗ ΠΑΙΚΤΗ<input type="search" bind:value={search} placeholder="Όνομα ή αριθμός φανέλας" aria-controls="roster-results" /></label><label>ΘΕΣΗ<select bind:value={position} aria-controls="roster-results"><option value="">Όλες οι θέσεις</option>{#each positions as item}<option value={item}>{item}</option>{/each}</select></label>{#if search || position}<button type="button" class="roster-reset" onclick={() => { search = ''; position = ''; }}>ΚΑΘΑΡΙΣΜΟΣ ΦΙΛΤΡΩΝ</button>{/if}</div><p class="roster-count" role="status" aria-live="polite">{filtered.length} ΑΠΟ {players.length} ΠΑΙΚΤΕΣ</p>
      <div id="roster-results" class="people-grid">{#each filtered as player (player.id)}<a class="person-card" href={withTeam(personPublicLink('player',player))}><div class="person-image"><span class="shirt-number" aria-hidden="true">{player.number}</span><PublicImage src={player.image_url} alt="" /></div><div class="person-card-copy"><p>{player.position || selectedTeam?.name || 'ΛΕΥΤΕΡΙΑ FC'}<span aria-hidden="true">↗</span></p><h3>{player.name}</h3><span class="profile-label">ΤΟ ΠΡΟΦΙΛ · #{player.number}</span></div></a>{:else}<p class="public-empty">{players.length ? 'Δεν βρέθηκαν παίκτες με αυτά τα φίλτρα.' : 'Δεν έχουν δημοσιευτεί παίκτες.'}</p>{/each}</div>
    {:else if data.tab === 'staff'}
      <div class="section-heading"><h2>ΤΕΧΝΙΚΟ ΕΠΙΤΕΛΕΙΟ</h2></div><div class="people-grid">{#each staff as person (person.id)}<a class="person-card" href={withTeam(personPublicLink('staff',person))}><div class="person-image"><PublicImage src={person.image_url} alt="" /></div><div class="person-card-copy"><p>{person.role}<span aria-hidden="true">↗</span></p><h3>{person.name}</h3><span class="profile-label">ΓΝΩΡΙΣΕ ΤΟ ΕΠΙΤΕΛΕΙΟ</span></div></a>{:else}<p class="public-empty">Δεν έχει δημοσιευτεί τεχνικό επιτελείο.</p>{/each}</div>
    {:else if data.tab === 'statistics'}
      <div class="section-heading"><h2>ΣΤΑΤΙΣΤΙΚΑ ΠΑΙΚΤΩΝ</h2></div><p class="table-description">Τα καταγεγραμμένα στοιχεία της ομάδας. Η παύλα σημαίνει ότι δεν υπάρχει καταγεγραμμένο στοιχείο.</p>
      {#if players.length}<div class="table-scroll"><table><caption class="sr-only">Στατιστικά παικτών {selectedTeam?.name ?? 'της ομάδας'}</caption><thead><tr><th scope="col">ΠΑΙΚΤΗΣ</th><th scope="col">ΣΥΜΜΕΤΟΧΕΣ</th><th scope="col">ΓΚΟΛ</th><th scope="col">ΑΣΙΣΤ</th><th scope="col">ΛΕΠΤΑ</th><th scope="col">ΚΙΤΡΙΝΕΣ</th><th scope="col">ΚΟΚΚΙΝΕΣ</th></tr></thead><tbody>{#each players as player (player.id)}<tr><th scope="row"><a href={withTeam(personPublicLink('player',player))}>{player.number} · {player.name} ↗</a></th><td>{player.statistics?.appearances ?? '—'}</td><td class="points">{player.statistics?.goals ?? '—'}</td><td>{player.statistics?.assists ?? '—'}</td><td>{player.statistics?.minutes_played ?? '—'}</td><td>{player.statistics?.yellow_cards ?? '—'}</td><td>{player.statistics?.red_cards ?? '—'}</td></tr>{/each}</tbody></table></div>{:else}<p class="public-empty">Δεν έχουν δημοσιευτεί στοιχεία παικτών.</p>{/if}
    {:else if data.tab === 'gallery'}
      <div class="section-heading"><h2>ΣΤΙΓΜΕΣ ΤΗΣ ΟΜΑΔΑΣ</h2></div><div class="gallery-grid">{#each gallery as image (image.id)}<figure><PublicImage src={image.image_url} alt={image.title} /><figcaption><p class="public-eyebrow">{image.category}</p><h3>{image.title}</h3>{#if image.description}<p>{image.description}</p>{/if}</figcaption></figure>{:else}<p class="public-empty">Δεν έχουν δημοσιευτεί φωτογραφίες.</p>{/each}</div>
    {:else if data.tab === 'venues'}
      <div class="section-heading"><h2>ΤΑ ΓΗΠΕΔΑ ΜΑΣ</h2></div><div class="venue-grid">{#each venues as venue (venue.id)}<article class="venue-card">{#if venue.image_url}<PublicImage src={venue.image_url} alt={venue.name} />{/if}<div><p class="public-eyebrow">{venue.is_home_ground ? 'Η ΕΔΡΑ ΜΑΣ' : 'ΓΗΠΕΔΟ'}</p><h3>{venue.name}</h3>{#if venue.address || venue.city}<p>{[venue.address,venue.city,venue.country].filter(Boolean).join(', ')}</p>{/if}<dl>{#if venue.surface}<div><dt>ΕΠΙΦΑΝΕΙΑ</dt><dd>{venue.surface}</dd></div>{/if}{#if venue.capacity !== null}<div><dt>ΧΩΡΗΤΙΚΟΤΗΤΑ</dt><dd>{venue.capacity}</dd></div>{/if}</dl>{#if mapHref(venue.map_url)}<a class="public-button public-button-outline" href={mapHref(venue.map_url)!} target="_blank" rel="noreferrer">ΧΑΡΤΗΣ ↗</a>{/if}</div></article>{:else}<p class="public-empty">Δεν έχουν δημοσιευτεί γήπεδα.</p>{/each}</div>
    {/if}
  </section>{/if}
  {#if isSelectedFirstTeam}
    <OrganizationSponsorStrip organization="lefteria_fc" sponsors={data.sections?.sponsors ?? []} />
  {/if}
</PublicShell>

<style>
  .public-team-selector{display:flex;gap:8px;flex-wrap:wrap;margin:20px 0 18px}
  .public-team-selector a{display:inline-flex;min-height:44px;align-items:center;
    padding:9px 15px;border:1px solid #837448;border-radius:6px;
    color:#ffca46;background:#101112;font-size:14px;font-weight:700}
  .public-team-selector a[aria-current="page"]{background:#ffbf32;color:#080808;border-color:#ffbf32}
  .public-team-selector a:focus-visible{outline:2px solid #ffd068;outline-offset:3px}

  /* First Team matches hub at the club's 1536×1024 reference hierarchy.
     Keep the eight existing team sections in an expandable utility nav. */
  :global(.public-site[data-route="/team"]) .matches-reference-heading{
    min-height:0!important;padding:0!important;background:#080a0b;
  }
  .matches-reference-banner{
    position:relative;min-height:154px;display:flex;flex-direction:column;
    align-items:flex-start;justify-content:center;gap:5px;
    padding:17px clamp(23px,3vw,50px);
    background-color:#080a0b;background-size:cover;background-position:center 60%;
  }
  .matches-reference-banner .public-eyebrow{margin:0;font:800 16px 'Fira Sans Extra Condensed',sans-serif}
  .matches-reference-banner h1{
    font:italic 900 clamp(43px,3.9vw,66px)/1.01 'Fira Sans Extra Condensed',sans-serif;
    letter-spacing:-.02em;margin:0;color:#fff;
  }
  .matches-reference-banner>p:last-child{margin:0;color:#eee;font-size:14px}
  .matches-reference-tabs{display:flex;align-items:center;flex-wrap:wrap;min-height:49px;padding:0 clamp(20px,2vw,33px);border-bottom:2px solid #a78024}
  .matches-reference-tabs :is(a,button){
    display:inline-flex;align-items:center;justify-content:center;gap:10px;
    min-height:47px;padding:10px 21px;border:1px solid #454646;border-bottom:0;
    border-radius:0;background:linear-gradient(180deg,#151719,#0b0b0c);
    color:#eee;text-decoration:none;font:800 14px 'Fira Sans Extra Condensed',sans-serif;
    cursor:pointer;
  }
  .matches-reference-tabs :is(a,button) :global(svg){color:#eec052}
  .matches-reference-tabs a[aria-current='page'],.matches-reference-tabs button[aria-pressed='true']{
    background:linear-gradient(135deg,#ffd469,#e8a519);color:#060606;border-color:#e8ad23;
  }
  .matches-reference-tabs a[aria-current='page'] :global(svg),
  .matches-reference-tabs button[aria-pressed='true'] :global(svg){color:#080808}
  .matches-extra-navigation{padding:7px clamp(20px,2vw,33px);background:#0c0c0c;border-bottom:1px solid #4b3a21}
  .matches-extra-navigation summary{font:800 12px 'Fira Sans Extra Condensed',sans-serif;color:#f6c261;cursor:pointer}
  .matches-extra-navigation .team-tabs{display:flex;margin-top:10px;flex-wrap:wrap;position:static}
  :global(.public-site[data-route="/team"]) .matches-reference-heading+.team-section{padding-top:23px}
   :global(.public-site[data-route="/team"] .team-section:has(.match-overview)){padding-top:20px}
  :global(.public-site[data-route="/team"] .team-section .match-overview){margin-bottom:0}
  :global(.public-site[data-route="/team"] .team-section .match-overview-heading){
    position:absolute!important;width:1px!important;height:1px!important;
    padding:0!important;margin:-1px!important;overflow:hidden!important;
    clip:rect(0,0,0,0)!important;white-space:nowrap!important;
  }
  :global(.public-site[data-route="/team"] .team-section .match-overview-grid){gap:14px}
  :global(.public-site[data-route="/team"] .team-section .match-overview-grid .match-panel){
    background:linear-gradient(140deg,#1b1b1b,#090a0b 70%);border-radius:0;padding:13px;
  }
  @media(max-width:780px){
    .matches-reference-banner{min-height:180px;padding:26px 19px}
    .matches-reference-banner h1{font-size:clamp(34px,9.8vw,38px)}
    .matches-reference-tabs{padding:0 10px}
    .matches-reference-tabs :is(a,button){flex:1;min-height:44px;font-size:12px;padding:8px}
  }

</style>
