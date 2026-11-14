<script lang="ts">
  import PublicShell from '../../lib/public/PublicShell.svelte';
  import PublicImage from '../../lib/public/PublicImage.svelte';
  import Fixtures from '../../lib/public/Fixtures.svelte';
  import Unavailable from '../../lib/public/Unavailable.svelte';
  import { safePublicHref } from '../../lib/public/content';
  import type { TeamPageData, TeamTab } from '../../lib/public/sections-types';
  let { data }: { data: TeamPageData } = $props();
  let search = $state(''); let position = $state('');
  const navigation: [TeamTab, string][] = [['overview','Η ΟΜΑΔΑ'],['roster','ΡΟΣΤΕΡ'],['staff','ΕΠΙΤΕΛΕΙΟ'],['schedule','ΠΡΟΓΡΑΜΜΑ'],['results','ΑΠΟΤΕΛΕΣΜΑΤΑ'],['statistics','ΣΤΑΤΙΣΤΙΚΑ'],['gallery','ΓΚΑΛΕΡΙ'],['venues','ΓΗΠΕΔΑ']];
  const title = $derived(navigation.find(([id]) => id === data.tab)?.[1] ?? 'Η ΟΜΑΔΑ');
  const fixtures = $derived((data.site?.fixtures ?? []).filter(item => data.tab === 'results' ? item.status === 'Completed' : data.tab === 'schedule' ? item.status !== 'Completed' : true));
  const players = $derived((data.sections?.players ?? []).filter(row => row.organization_id === 'lefteria_fc' && row.team_type === 'First Team'));
  const staff = $derived((data.sections?.staff ?? []).filter(row => row.organization_id === 'lefteria_fc'));
  const positions = $derived([...new Set(players.map(row => row.position).filter((value): value is string => !!value))]);
  const normalize = (value: string) => value.normalize('NFD').replace(/[\u0300-\u036f]/g,'').toLocaleLowerCase('el-GR');
  const mapHref = (value: string | null | undefined) => value ? safePublicHref(value) : null;
  const filtered = $derived(players.filter(row => (!position || row.position === position) && normalize(row.name + ' ' + row.number).includes(normalize(search.trim()))));
  const competitions = $derived([...new Set((data.sections?.standings ?? []).map(row => row.competition))]);
</script>
<svelte:head><title>{title} · ΠΡΩΤΗ ΟΜΑΔΑ | ΛΕΥΤΕΡΙΑ FC</title><meta name="description" content="Η πρώτη ομάδα της Λευτεριάς FC. Παίκτες, τεχνικό επιτελείο, πραγματικά στατιστικά, αγώνες και αποτελέσματα." /></svelte:head>
<PublicShell site={data.site} branding={data.branding} organization="lefteria_fc" navigationGroups={data.navigationGroups}>
  <section class="public-page-heading team-heading"><p class="public-eyebrow">ΤΑ ΧΡΩΜΑΤΑ ΜΑΣ. Η ΠΕΡΗΦΑΝΙΑ ΜΑΣ.</p><h1>ΠΡΩΤΗ ΟΜΑΔΑ</h1><nav class="public-subnav team-tabs" aria-label="Πρώτη ομάδα">{#each navigation as [id,label]}<a href={id === 'overview' ? '/team' : '/team?tab='+id} aria-current={data.tab === id ? 'page' : undefined}>{label}</a>{/each}</nav></section>
  {#if !data.available}<Unavailable />{:else}<section class="public-section team-section">
    {#if data.tab === 'overview'}
      {#each data.site?.teams ?? [] as team (team.id)}<article class="public-team-overview">{#if team.banner_url}<PublicImage src={team.banner_url} alt="" />{/if}<div><p class="public-eyebrow">{team.level || 'ΛΕΥΤΕΡΙΑ FC'}</p><h2>{team.name}</h2>{#if team.description}<p class="preserve-lines">{team.description}</p>{/if}<a class="public-button" href="/team?tab=roster">ΓΝΩΡΙΣΕ ΤΟΥΣ ΠΑΙΚΤΕΣ ↗</a></div></article>{/each}
      <div id="standings" class="standings-anchor">{#each competitions as competition}<div class="section-heading"><h2>ΒΑΘΜΟΛΟΓΙΑ</h2><p>{competition}</p></div><div class="table-scroll"><table><caption class="sr-only">Βαθμολογία {competition}</caption><thead><tr><th scope="col">ΟΜΑΔΑ</th><th scope="col">ΑΓ</th><th scope="col">Ν</th><th scope="col">Ι</th><th scope="col">Η</th><th scope="col">ΓΚΟΛ</th><th scope="col">Β</th></tr></thead><tbody>{#each (data.sections?.standings ?? []).filter(row=>row.competition===competition) as row (row.id)}<tr><th scope="row">{row.team_name}</th><td>{row.played}</td><td>{row.won}</td><td>{row.drawn}</td><td>{row.lost}</td><td>{row.goals_for}:{row.goals_against}</td><td class="points">{row.points}</td></tr>{/each}</tbody></table></div>{/each}</div>
    {/if}
    {#if data.tab === 'overview' || data.tab === 'schedule' || data.tab === 'results'}
      <div class="section-heading"><h2>{data.tab === 'results' ? 'ΑΠΟΤΕΛΕΣΜΑΤΑ' : 'ΑΓΩΝΕΣ & ΠΡΟΓΡΑΜΜΑ'}</h2></div>{#if fixtures.length}<Fixtures {fixtures} />{:else}<p class="public-empty">Δεν έχουν καταχωριστεί αγώνες σε αυτή την ενότητα.</p>{/if}
    {:else if data.tab === 'roster'}
      <div class="roster-intro"><div><p class="public-eyebrow">ΟΙ ΑΝΘΡΩΠΟΙ ΠΙΣΩ ΑΠΟ ΤΗ ΦΑΝΕΛΑ</p><h2>ΤΟ ΡΟΣΤΕΡ ΜΑΣ</h2></div><span>{players.length} ΠΑΙΚΤΕΣ</span></div>
      <div class="roster-filters" role="search" aria-label="Αναζήτηση παικτών"><label>ΑΝΑΖΗΤΗΣΗ ΠΑΙΚΤΗ<input type="search" bind:value={search} placeholder="Όνομα ή αριθμός φανέλας" aria-controls="roster-results" /></label><label>ΘΕΣΗ<select bind:value={position} aria-controls="roster-results"><option value="">Όλες οι θέσεις</option>{#each positions as item}<option value={item}>{item}</option>{/each}</select></label>{#if search || position}<button type="button" class="roster-reset" onclick={() => { search = ''; position = ''; }}>ΚΑΘΑΡΙΣΜΟΣ ΦΙΛΤΡΩΝ</button>{/if}</div><p class="roster-count" role="status" aria-live="polite">{filtered.length} ΑΠΟ {players.length} ΠΑΙΚΤΕΣ</p>
      <div id="roster-results" class="people-grid">{#each filtered as player (player.id)}<a class="person-card" href={'/player/'+encodeURIComponent(player.id)}><div class="person-image"><span class="shirt-number" aria-hidden="true">{player.number}</span><PublicImage src={player.image_url} alt="" /></div><div class="person-card-copy"><p>{player.position || 'ΠΡΩΤΗ ΟΜΑΔΑ'}<span aria-hidden="true">↗</span></p><h3>{player.name}</h3><span class="profile-label">ΤΟ ΠΡΟΦΙΛ · #{player.number}</span></div></a>{:else}<p class="public-empty">{players.length ? 'Δεν βρέθηκαν παίκτες με αυτά τα φίλτρα.' : 'Δεν έχουν δημοσιευτεί παίκτες.'}</p>{/each}</div>
    {:else if data.tab === 'staff'}
      <div class="section-heading"><h2>ΤΕΧΝΙΚΟ ΕΠΙΤΕΛΕΙΟ</h2></div><div class="people-grid">{#each staff as person (person.id)}<a class="person-card" href={'/staff/'+encodeURIComponent(person.id)}><div class="person-image"><PublicImage src={person.image_url} alt="" /></div><div class="person-card-copy"><p>{person.role}<span aria-hidden="true">↗</span></p><h3>{person.name}</h3><span class="profile-label">ΓΝΩΡΙΣΕ ΤΟ ΕΠΙΤΕΛΕΙΟ</span></div></a>{:else}<p class="public-empty">Δεν έχει δημοσιευτεί τεχνικό επιτελείο.</p>{/each}</div>
    {:else if data.tab === 'statistics'}
      <div class="section-heading"><h2>ΣΤΑΤΙΣΤΙΚΑ ΠΑΙΚΤΩΝ</h2></div><p class="table-description">Τα καταγεγραμμένα στοιχεία της ομάδας. Η παύλα σημαίνει ότι δεν υπάρχει καταγεγραμμένο στοιχείο.</p>
      {#if players.length}<div class="table-scroll"><table><caption class="sr-only">Στατιστικά παικτών πρώτης ομάδας</caption><thead><tr><th scope="col">ΠΑΙΚΤΗΣ</th><th scope="col">ΣΥΜΜΕΤΟΧΕΣ</th><th scope="col">ΓΚΟΛ</th><th scope="col">ΑΣΙΣΤ</th><th scope="col">ΛΕΠΤΑ</th><th scope="col">ΚΙΤΡΙΝΕΣ</th><th scope="col">ΚΟΚΚΙΝΕΣ</th></tr></thead><tbody>{#each players as player (player.id)}<tr><th scope="row"><a href={'/player/'+encodeURIComponent(player.id)}>{player.number} · {player.name} ↗</a></th><td>{player.statistics?.appearances ?? '—'}</td><td class="points">{player.statistics?.goals ?? '—'}</td><td>{player.statistics?.assists ?? '—'}</td><td>{player.statistics?.minutes_played ?? '—'}</td><td>{player.statistics?.yellow_cards ?? '—'}</td><td>{player.statistics?.red_cards ?? '—'}</td></tr>{/each}</tbody></table></div>{:else}<p class="public-empty">Δεν έχουν δημοσιευτεί στοιχεία παικτών.</p>{/if}
    {:else if data.tab === 'gallery'}
      <div class="section-heading"><h2>ΣΤΙΓΜΕΣ ΤΗΣ ΟΜΑΔΑΣ</h2></div><div class="gallery-grid">{#each data.sections?.gallery ?? [] as image (image.id)}<figure><PublicImage src={image.image_url} alt={image.title} /><figcaption><p class="public-eyebrow">{image.category}</p><h3>{image.title}</h3>{#if image.description}<p>{image.description}</p>{/if}</figcaption></figure>{:else}<p class="public-empty">Δεν έχουν δημοσιευτεί φωτογραφίες.</p>{/each}</div>
    {:else if data.tab === 'venues'}
      <div class="section-heading"><h2>ΤΑ ΓΗΠΕΔΑ ΜΑΣ</h2></div><div class="venue-grid">{#each data.sections?.venues ?? [] as venue (venue.id)}<article class="venue-card">{#if venue.image_url}<PublicImage src={venue.image_url} alt={venue.name} />{/if}<div><p class="public-eyebrow">{venue.is_home_ground ? 'Η ΕΔΡΑ ΜΑΣ' : 'ΓΗΠΕΔΟ'}</p><h3>{venue.name}</h3>{#if venue.address || venue.city}<p>{[venue.address,venue.city,venue.country].filter(Boolean).join(', ')}</p>{/if}<dl>{#if venue.surface}<div><dt>ΕΠΙΦΑΝΕΙΑ</dt><dd>{venue.surface}</dd></div>{/if}{#if venue.capacity !== null}<div><dt>ΧΩΡΗΤΙΚΟΤΗΤΑ</dt><dd>{venue.capacity}</dd></div>{/if}</dl>{#if mapHref(venue.map_url)}<a class="public-button public-button-outline" href={mapHref(venue.map_url)!} target="_blank" rel="noreferrer">ΧΑΡΤΗΣ ↗</a>{/if}</div></article>{:else}<p class="public-empty">Δεν έχουν δημοσιευτεί γήπεδα.</p>{/each}</div>
    {/if}
  </section>{/if}
</PublicShell>
