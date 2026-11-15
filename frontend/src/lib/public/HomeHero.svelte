<script lang="ts">
  import { onMount } from 'svelte';
  import type { PublicGroup, PublicSite } from './types';
  import type { WebsitePageContent } from './website-pages';
  import { CLUB_TIME_ZONE, nextPublicFixture, fixtureCountdown, snapshotInstant } from './fixture-clock';
  import { publicImages } from './images';
  import { firstTeamHeroBanner } from './first-team-hero';
  import { fixturePublicLink } from './public-urls';
  import Icon from './PublicIcon.svelte';
  import TeamCrest from './TeamCrest.svelte';
  let { site, navigationGroups = [], academyEditorial = null, teamEditorial = null }: {
    site: PublicSite | null; navigationGroups?: PublicGroup[];
    academyEditorial?: WebsitePageContent | null; teamEditorial?: WebsitePageContent | null
  } = $props();
  // The original markup, artwork, carousel and next-match widget remain intact.
  // Only human-approved per-organization heading and description text can replace fallback copy.
  const headingLines = (content: WebsitePageContent | null) =>
    content?.blocks[0]?.type === 'heading'
      ? content.blocks[0].text.split(/\r?\n/).map(line=>line.trim()).filter(Boolean).slice(0,3)
      : null;
  const editorialDescription = (content: WebsitePageContent | null) =>
    content?.blocks[1]?.type === 'paragraph' ? content.blocks[1].text : null;
  const academyHeading = $derived(headingLines(academyEditorial));
  const teamHeading = $derived(headingLines(teamEditorial));
  const academyDescription = $derived(editorialDescription(academyEditorial));
  const teamDescription = $derived(editorialDescription(teamEditorial));
  let index = $state(0); let paused = $state(false); let visible = $state(true); let reduced = $state(false);
  let hovered = $state(false); let focused = $state(false); let hidden = $state(false); let region: HTMLElement;
  const club = $derived(site?.club); const firstTeamBanner = $derived(firstTeamHeroBanner(site?.teams));
  const academySlide = $derived(index === 0);
  // The academy hero uses the original club editorial artwork rather than
  // borrowing a specific age group's banner (which can wrongly change the
  // homepage to a team/group promotion when CMS groups are edited).
  const backgrounds = $derived([publicImages.hero, firstTeamBanner || club?.stadium_image_url || publicImages.pitch]);
  const running = $derived(!paused && !hovered && !focused && visible && !hidden && !reduced);
  let clock = $state<number | null>(null);
  // SSR and initial hydration share the server snapshot. The mounted clock then
  // advances every second for the real countdown and refreshes on focus.
  const snapshot = $derived(site?.generated_at && site.time_zone ? { generated_at: site.generated_at, time_zone: site.time_zone } : null);
  const now = $derived(snapshotInstant(snapshot) === null ? null : clock ?? snapshotInstant(snapshot));
  const fixtureRows = $derived(site?.fixtures.filter(row => row.organization_id === 'lefteria_fc').map(row => ({ ...row,
    kickoff_at: site.fixture_clock?.find(instant => instant.id === row.id)?.kickoff_at ?? null })) ?? []);
  const fixture = $derived(nextPublicFixture(fixtureRows, now));
  const countdown = $derived(fixture && now !== null ? fixtureCountdown(fixture,now) : null);
  const fixtureDate = new Intl.DateTimeFormat('el-GR', { weekday:'long',day:'numeric',month:'long',timeZone:CLUB_TIME_ZONE });
  function select(next: number) { index = (next + 2) % 2; paused = true; }
  onMount(() => {
    const motion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const updateMotion = () => { reduced = motion.matches; if (motion.matches) paused = true; };
    const visibility = () => { hidden = document.hidden; };
    const updateClock = () => { clock = Date.now(); };
    updateMotion(); visibility(); motion.addEventListener('change', updateMotion); document.addEventListener('visibilitychange', visibility);
    const observer = new IntersectionObserver(entries => { visible = entries.some(entry => entry.isIntersecting); }, { threshold: 0.1 });
    observer.observe(region);
    const timer = window.setInterval(() => { if (running) index = (index + 1) % 2; }, 5000);
    updateClock(); window.addEventListener('focus',updateClock);
    const clockTimer = window.setInterval(updateClock,1000);
    return () => { window.clearInterval(timer); window.clearInterval(clockTimer); window.removeEventListener('focus',updateClock); observer.disconnect(); motion.removeEventListener('change', updateMotion); document.removeEventListener('visibilitychange', visibility); };
  });
</script>
<section class="public-home-hero" data-testid="hero-section">
  <div class="public-hero-stage public-hero-carousel" bind:this={region} role="region" aria-roledescription="καρουζέλ" aria-label="Ακαδημία και πρώτη ομάδα" data-testid="hero-carousel" data-slide={academySlide ? 'academy' : 'first-team'} data-rotating={running}
    onpointerenter={event => { if (event.pointerType === 'mouse') hovered = true; }} onpointerleave={() => { hovered = false; }}
    onfocusin={() => { focused = true; }} onfocusout={event => { if (!event.currentTarget.contains(event.relatedTarget as Node)) focused = false; }}
    ontouchstart={event => { if (!(event.target as Element).closest('[data-testid="hero-slide-toggle"]')) paused = true; }}>
    {#each backgrounds as image, position}<div class="public-hero-visual" class:is-active={index === position} style:background-image={`url("${image.replaceAll('"','%22')}")`} aria-hidden="true" data-testid={`hero-slide-background-${position}`}></div>{/each}
    <div class="public-hero-copy" aria-live={running ? 'off' : 'polite'} aria-atomic="true"><p class="public-eyebrow" data-testid="public-hero-brand">{academySlide ? club?.name || 'ΛΕΥΤΕΡΙΑ FC' : 'ΠΡΩΤΗ ΟΜΑΔΑ'}</p>
      <h1 class="hero-copy-stack" data-testid="public-hero-title">
        <span aria-hidden={!academySlide}>{#if academyHeading?.length}
          {#each academyHeading as line, lineIndex (lineIndex)}
            {#if lineIndex === 1}<em>{line}</em>{:else}{line}{/if}
            {#if lineIndex < academyHeading.length-1}<br />{/if}
          {/each}
        {:else}Η ΠΟΛΗ ΜΑΣ.<br /><em>ΤΑ ΠΑΙΔΙΑ ΜΑΣ.</em><br />ΤΟ ΜΕΛΛΟΝ ΤΟΥΣ.{/if}</span>
        <span aria-hidden={academySlide}>{#if teamHeading?.length}
          {#each teamHeading as line, lineIndex (lineIndex)}
            {#if lineIndex === 1}<em>{line}</em>{:else}{line}{/if}
            {#if lineIndex < teamHeading.length-1}<br />{/if}
          {/each}
        {:else}ΤΑ ΧΡΩΜΑΤΑ ΜΑΣ.<br /><em>Η ΠΕΡΗΦΑΝΙΑ ΜΑΣ.</em>{/if}</span>
      </h1>
      <p class="public-hero-description hero-copy-stack" data-testid="public-hero-description"><span aria-hidden={!academySlide}>{academyDescription ?? (club?.description || 'Χτίζουμε πρωταθλητές μέσα από το πάθος, την πειθαρχία και την ομαδικότητα. Ελάτε μαζί μας στο ταξίδι προς την κορυφή.')}</span><span aria-hidden={academySlide}>{teamDescription ?? 'Στο πλευρό της ομάδας μας, σε κάθε αγώνα.'}</span></p>
      <div class="public-hero-actions">{#if academySlide}<a href="/academy" class="public-button" data-testid="hero-join-academy">ΔΕΣ ΤΗΝ ΑΚΑΔΗΜΙΑ <Icon name="arrow" size={18} /></a><a href="/team" class="public-button public-button-outline" data-testid="hero-view-team">Η ΠΡΩΤΗ ΟΜΑΔΑ <Icon name="arrow" size={18} /></a>{:else}<a href="/team" class="public-button" data-testid="hero-first-team-link">ΓΝΩΡΙΣΕ ΤΗΝ ΟΜΑΔΑ <Icon name="arrow" size={18} /></a><a href="/team?tab=schedule" class="public-button public-button-outline" data-testid="hero-first-team-schedule">ΠΡΟΓΡΑΜΜΑ ΑΓΩΝΩΝ <Icon name="arrow" size={18} /></a>{/if}</div>
    </div>
    {#if fixture}<aside class="public-hero-fixture" data-testid="public-hero-fixture">
      <div class="public-fixture-heading"><span><Icon name="calendar" size={22} />ΕΠΟΜΕΝΟΣ ΑΓΩΝΑΣ</span><a href="/team?tab=schedule" aria-label="Όλοι οι αγώνες" title="Όλοι οι αγώνες"><Icon name="external" size={18} /></a></div>
      <p class="public-fixture-competition">{fixture.competition}</p>
      <a href={fixturePublicLink(fixture)} class="public-fixture-teams"><div><TeamCrest name={fixture.home_team} logo={fixture.home_team_logo} clubName={club?.name} clubLogo={club?.logo_url} /><strong>{fixture.home_team}</strong></div><span class="fixture-versus" aria-label="εναντίον">VS</span><div><TeamCrest name={fixture.away_team} logo={fixture.away_team_logo} clubName={club?.name} clubLogo={club?.logo_url} /><strong>{fixture.away_team}</strong></div></a>
      <div class="public-fixture-meta">
        <p class="public-fixture-date"><Icon name="calendar" size={16} /><span>{fixtureDate.format(new Date(fixture.match_date+'T12:00:00Z'))}{fixture.match_time ? ` · ${fixture.match_time}` : ''}</span></p>
        {#if fixture.venue}<p class="public-fixture-venue"><Icon name="pin" size={16} /><span>{fixture.venue}</span></p>{/if}
      </div>
      {#if countdown}<div class="public-countdown" data-testid="public-hero-countdown">{#each countdown as value,index}<div><strong>{String(value).padStart(2,'0')}</strong><span>{['ΗΜΕΡΕΣ','ΩΡΕΣ','ΛΕΠΤΑ','ΔΕΥΤ.'][index]}</span></div>{/each}</div>{/if}
      <a href={fixturePublicLink(fixture)} class="public-button public-fixture-details" data-testid="hero-fixture-details">ΛΕΠΤΟΜΕΡΕΙΕΣ ΑΓΩΝΑ <Icon name="arrow" size={18} /></a>
    </aside>{:else}<aside class="public-hero-fixture public-hero-fixture-empty"
      data-testid="public-hero-fixture" data-state="unavailable" aria-label="Πρόγραμμα πρώτης ομάδας">
      <div class="public-fixture-heading"><span><Icon name="calendar" size={22} />ΕΠΟΜΕΝΟΣ ΑΓΩΝΑΣ</span>
        <a href="/team?tab=schedule" aria-label="Πρόγραμμα πρώτης ομάδας"><Icon name="external" size={18} /></a>
      </div>
      <div class="public-fixture-empty-copy" role="status">
        <Icon name="calendar" size={30} />
        <strong>{site ? 'ΔΕΝ ΕΧΕΙ ΔΗΜΟΣΙΕΥΤΕΙ ΕΠΟΜΕΝΟΣ ΑΓΩΝΑΣ' : 'ΤΟ ΠΡΟΓΡΑΜΜΑ ΔΕΝ ΕΙΝΑΙ ΔΙΑΘΕΣΙΜΟ'}</strong>
        <p>{site ? 'Παρακολούθησε το επίσημο πρόγραμμα για τον επόμενο αγώνα της πρώτης ομάδας.' : 'Δεν ήταν δυνατή η φόρτωση του επίσημου προγράμματος αυτή τη στιγμή.'}</p>
      </div>
      <a href="/team?tab=schedule" class="public-button public-fixture-details" data-testid="hero-fixture-schedule">ΔΕΣ ΤΟ ΠΡΟΓΡΑΜΜΑ <Icon name="arrow" size={18} /></a>
    </aside>{/if}
    <div class="public-hero-bottom"><p class="public-hero-motto">{club?.motto || 'ΜΙΑ ΟΜΑΔΑ. ΕΝΑ ΟΝΕΙΡΟ.'}</p>
      <div class="public-hero-controls" role="group" aria-label="Εναλλαγή κεντρικών εικόνων" data-testid="hero-slide-controls">
        <button type="button" onclick={() => select(index - 1)} aria-label="Προηγούμενη εικόνα" title="Προηγούμενη εικόνα" data-testid="hero-slide-previous"><Icon name="back" size={19} /></button>
        {#each ['Ακαδημία','Πρώτη ομάδα'] as label, position}<button type="button" onclick={() => select(position)} class="public-hero-dot" aria-label={label} aria-pressed={index === position} title={label} data-testid={`hero-slide-select-${position}`}><span></span></button>{/each}
        <button type="button" onclick={() => select(index + 1)} aria-label="Επόμενη εικόνα" title="Επόμενη εικόνα" data-testid="hero-slide-next"><Icon name="chevron" size={19} /></button>
        <button type="button" onclick={() => { paused = !paused; focused = false; }} aria-label={paused ? 'Έναρξη αυτόματης εναλλαγής' : 'Παύση αυτόματης εναλλαγής'} title={paused ? 'Έναρξη αυτόματης εναλλαγής' : 'Παύση αυτόματης εναλλαγής'} aria-pressed={paused} data-testid="hero-slide-toggle"><Icon name={paused ? 'play' : 'pause'} size={16} /></button>
      </div>
    </div>
  </div>
</section>
