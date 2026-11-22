<script lang="ts">
  import PublicShell from '../../../lib/public/PublicShell.svelte';
  import PublicImage from '../../../lib/public/PublicImage.svelte';
  import Fixtures from '../../../lib/public/Fixtures.svelte';
  import { personPublicLink, publicCanonicalHref, publicSocialImage } from '../../../lib/public/public-urls';
  import { academyGroupLink } from '../../../lib/public/navigation';
  import type { PublicPageData, PublicGroup, PublicFixture } from '../../../lib/public/types';
  import type { SectionPlayer, SectionStaff, PublicSections } from '../../../lib/public/sections-types';
  let { data }: { data: PublicPageData & { group: PublicGroup; groupFixtures: PublicFixture[]; groupPlayers: SectionPlayer[]; groupCoaches: SectionStaff[]; groupGallery: PublicSections['gallery']; groupSectionsAvailable: boolean } } = $props();
  const group = $derived(data.group);
  const canonical = $derived(publicCanonicalHref(academyGroupLink(group)));
  const socialImage = $derived(publicSocialImage(group.banner_url));
  const upcoming = $derived(data.groupFixtures.filter(fixture => fixture.status !== 'Completed'));
  const results = $derived(data.groupFixtures.filter(fixture => fixture.status === 'Completed').reverse());
</script>

<svelte:head>
  <title>{group.name} | ΑΚΑΔΗΜΙΑ ΛΕΥΤΕΡΙΑ FC</title>
  <meta name="description" content={group.description || `Πληροφορίες, προπονήσεις και αγώνες του τμήματος ${group.name} της ακαδημίας Λευτεριά FC.`} />
  <meta property="og:type" content="website" />
  <meta property="og:title" content={group.name+' | ΑΚΑΔΗΜΙΑ ΛΕΥΤΕΡΙΑ FC'} />
  <meta property="og:description" content={group.description || 'Επίσημο ηλικιακό τμήμα της Ακαδημίας Λευτεριά FC.'} />
  <meta property="og:url" content={canonical} />
  <meta name="twitter:card" content={socialImage ? 'summary_large_image' : 'summary'} />
  {#if socialImage}<meta property="og:image" content={socialImage} />{/if}
</svelte:head>
<PublicShell site={data.site} branding={data.branding} organization="lefteria_fc_academy" navigationGroups={data.navigationGroups}>
  <section class="group-hero">
    {#if group.banner_url}<div class="group-hero-image"><PublicImage src={group.banner_url} alt="" eager /></div>{/if}
    <div class="group-hero-copy"><p class="public-eyebrow">ΑΚΑΔΗΜΙΑ ΛΕΥΤΕΡΙΑ FC{group.season ? ` · ${group.season}` : ''}</p><h1>{group.name}</h1>
      {#if group.age_range}<p class="group-age">{group.age_range}</p>{/if}
      {#if group.description}<p class="group-description">{group.description}</p>{/if}
      <a class="back-groups" href="/academy/groups">← ΟΛΑ ΤΑ ΤΜΗΜΑΤΑ</a>
    </div>
  </section>
  <nav class="group-tabs" aria-label="Πληροφορίες τμήματος"><a href="#roster">ΡΟΣΤΕΡ</a><a href="#training">ΠΡΟΠΟΝΗΣΕΙΣ</a><a href="#matches">ΑΓΩΝΕΣ</a><a href="#results">ΑΠΟΤΕΛΕΣΜΑΤΑ</a><a href="#statistics">ΣΤΑΤΙΣΤΙΚΑ</a><a href="#gallery">ΓΚΑΛΕΡΙ</a></nav>
  <section class="academy-group-roster public-section" id="roster" aria-labelledby="roster-heading" data-testid="group-roster-section">
    <p class="public-eyebrow">ΑΚΑΔΗΜΙΑ · {group.name}</p>
    <h2 id="roster-heading">ΤΟ ΡΟΣΤΕΡ ΜΑΣ</h2>
    {#if data.groupCoaches.length}<div class="academy-group-coaches"><span>ΠΡΟΠΟΝΗΤΕΣ</span>{#each data.groupCoaches as coach (coach.id)}<strong>{coach.name}</strong>{/each}</div>{/if}
    {#if data.groupSectionsAvailable}
      {#if data.groupPlayers.length}
        <div class="academy-roster-grid">{#each data.groupPlayers as player (player.id)}
          <a class="academy-roster-player" href={personPublicLink('player',player)} data-testid="group-public-player">
            <div class="academy-player-photo"><PublicImage src={player.image_url} alt={player.name} />{#if player.number != null}<span>#{player.number}</span>{/if}</div>
            <div class="academy-player-text"><h3>{player.name}</h3>{#if player.position}<p>{player.position}</p>{/if}</div>
          </a>{/each}</div>
      {:else}<p class="empty-group">Δεν έχουν δημοσιευτεί αθλητές για αυτό το τμήμα.</p>{/if}
    {:else}<p class="empty-group" role="status">Το δημοσιευμένο ρόστερ δεν είναι προσωρινά διαθέσιμο.</p>{/if}
  </section>
  <section class="group-information" id="training" aria-labelledby="training-heading">
    <div><p class="public-eyebrow">ΤΟ ΤΜΗΜΑ ΜΑΣ</p><h2 id="training-heading">ΜΑΖΙ ΣΤΟ ΓΗΠΕΔΟ.</h2><p class="information-copy">Το καταχωρισμένο πρόγραμμα και ο χώρος προπονήσεων για το τμήμα {group.name}.</p></div>
    <div class="training-panel"><h3>ΠΡΟΓΡΑΜΜΑ ΠΡΟΠΟΝΗΣΕΩΝ</h3><p class="training-schedule">{group.training_schedule || 'Δεν έχει καταχωριστεί πρόγραμμα προπονήσεων.'}</p>
      {#if group.location_name || group.city}<div class="training-venue"><span>ΧΩΡΟΣ ΠΡΟΠΟΝΗΣΕΩΝ</span><strong>{[group.location_name, group.city].filter(Boolean).join(' · ')}</strong></div>{/if}
      <a href="/contact">Επικοινωνία με τον σύλλογο <span aria-hidden="true">↗</span></a>
    </div>
  </section>
  <section class="group-matches" id="matches" aria-labelledby="matches-heading"><p class="public-eyebrow">ΤΟ ΠΡΟΓΡΑΜΜΑ ΜΑΣ</p><h2 id="matches-heading">ΑΓΩΝΕΣ ΤΜΗΜΑΤΟΣ</h2>
    {#if upcoming.length}<Fixtures fixtures={upcoming} />{:else}<p class="empty-group">Δεν έχουν καταχωριστεί προσεχείς αγώνες για αυτό το τμήμα.</p>{/if}
  </section>
  <section class="group-matches group-results" id="results" aria-labelledby="results-heading"><h2 id="results-heading">ΑΠΟΤΕΛΕΣΜΑΤΑ</h2>
    {#if results.length}<Fixtures fixtures={results} />{:else}<p class="empty-group">Δεν έχουν καταχωριστεί αποτελέσματα για αυτό το τμήμα.</p>{/if}
  </section>
  <section class="academy-group-statistics public-section" id="statistics" aria-labelledby="stats-heading" data-testid="group-stats-section">
    <p class="public-eyebrow">ΣΤΑΤΙΣΤΙΚΑ ΠΟΥ ΕΧΟΥΝ ΔΗΜΟΣΙΕΥΤΕΙ</p><h2 id="stats-heading">ΣΤΑΤΙΣΤΙΚΑ ΑΘΛΗΤΩΝ</h2>
    <p class="group-stats-description">Ατομικά δημοσιευμένα στατιστικά αθλητών του τμήματος, όταν υπάρχουν. Δεν υπολογίζονται αυθαίρετα αποτελέσματα αγώνων.</p>
    {#if !data.groupSectionsAvailable}<p class="empty-group" role="status">Τα δημοσιευμένα στατιστικά δεν είναι προσωρινά διαθέσιμα.</p>
    {:else if data.groupPlayers.some(player=>player.statistics)}
      <!-- svelte-ignore a11y_no_noninteractive_tabindex (Keyboard scrolling for narrow tables) -->
      <div class="group-stats-scroll" tabindex="0" role="region" aria-label="Πίνακας δημοσιευμένων ατομικών στατιστικών">
        <table><thead><tr><th scope="col">ΑΘΛΗΤΗΣ</th><th scope="col">ΣΥΜΜΕΤΟΧΕΣ</th><th scope="col">ΓΚΟΛ</th><th scope="col">ΑΣΙΣΤ</th><th scope="col">ΛΕΠΤΑ</th></tr></thead>
        <tbody>{#each data.groupPlayers as player (player.id)}<tr><th scope="row">{player.name}</th>
          <td>{player.statistics?.appearances ?? '—'}</td><td>{player.statistics?.goals ?? '—'}</td><td>{player.statistics?.assists ?? '—'}</td><td>{player.statistics?.minutes_played ?? '—'}</td></tr>{/each}</tbody></table>
      </div>
    {:else}<p class="empty-group">Δεν έχουν δημοσιευτεί ατομικά στατιστικά για αυτό το τμήμα.</p>{/if}
  </section>
  <section class="academy-group-gallery public-section" id="gallery" aria-labelledby="gallery-heading" data-testid="group-gallery-section">
    <p class="public-eyebrow">ΣΤΙΓΜΕΣ ΤΟΥ ΤΜΗΜΑΤΟΣ</p><h2 id="gallery-heading">ΦΩΤΟΓΡΑΦΙΕΣ</h2>
    {#if !data.groupSectionsAvailable}<p class="empty-group" role="status">Η δημοσιευμένη γκαλερί δεν είναι προσωρινά διαθέσιμη.</p>
    {:else if data.groupGallery.length}
      <div class="academy-gallery-grid">{#each data.groupGallery as photo (photo.id)}<figure>
        <PublicImage src={photo.image_url} alt={photo.title} />
        <figcaption>{photo.title}</figcaption>
      </figure>{/each}</div>
    {:else}<p class="empty-group">Δεν έχουν δημοσιευτεί φωτογραφίες για αυτό το τμήμα.</p>{/if}
  </section>
</PublicShell>


<style>
  .academy-group-roster,.academy-group-statistics,.academy-group-gallery{scroll-margin-top:100px;background:#0c0d0e}
  .academy-group-roster h2,.academy-group-statistics h2,.academy-group-gallery h2{font:900 clamp(30px,3vw,48px)/1.06 'Fira Sans Extra Condensed',sans-serif;margin:8px 0 24px}
  .academy-group-coaches{display:flex;align-items:center;flex-wrap:wrap;gap:15px;border-block:1px solid #353535;margin-bottom:24px;padding:14px}
  .academy-group-coaches span{color:var(--public-gold);font-weight:800;font-size:12px}
  .academy-group-coaches strong{font-size:14px}
  .academy-roster-grid{display:grid;grid-template-columns:repeat(5,minmax(0,1fr));gap:15px}
  .academy-roster-player{display:block;background:#18191a;border:1px solid #585044;min-width:0;color:#fff;text-decoration:none}
  .academy-roster-player:hover,.academy-roster-player:focus-visible{border-color:var(--public-gold)}
  .academy-player-photo{position:relative;aspect-ratio:3/4;overflow:hidden;background:#111}
  .academy-player-photo :global(img){width:100%;height:100%;object-fit:cover}
  .academy-player-photo>span{position:absolute;left:12px;bottom:10px;color:var(--public-gold);font:900 28px 'Fira Sans Extra Condensed',sans-serif}
  .academy-player-text{padding:14px}
  .academy-player-text h3{font:800 21px/1.15 'Fira Sans Extra Condensed',sans-serif;overflow-wrap:anywhere}
  .academy-player-text p{font-size:12px;color:#c0c0c0;margin-top:9px}
  .group-stats-description{color:#c2c2c2;margin-bottom:20px;max-width:78ch;font-size:14px}
  .group-stats-scroll{overflow-x:auto;border:1px solid #585044}
  .group-stats-scroll:focus-visible{outline:2px solid var(--public-gold);outline-offset:4px}
  table{border-collapse:collapse;width:100%;min-width:500px}
  th,td{padding:15px 13px;text-align:left;border-bottom:1px solid #363636}
  thead th{font-size:12px;color:var(--public-gold)}
  tbody th{font-weight:600;font-size:14px}
  tbody td{font-size:14px}
  .academy-gallery-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:18px}
  .academy-gallery-grid figure{margin:0;background:#161616;border:1px solid #54514b}
  .academy-gallery-grid :global(img){width:100%;aspect-ratio:4/3;object-fit:cover}
  .academy-gallery-grid figcaption{font-size:13px;line-height:1.5;padding:12px;color:#e1e1e1}
  @media(max-width:1050px){.academy-roster-grid{grid-template-columns:repeat(3,minmax(0,1fr))}}
  @media(max-width:650px){.academy-roster-grid,.academy-gallery-grid{grid-template-columns:repeat(2,minmax(0,1fr));gap:9px}.academy-group-roster,.academy-group-statistics,.academy-group-gallery{padding:25px 15px}.academy-player-text h3{font-size:18px}.academy-gallery-grid figcaption{font-size:12px}}
  @media(max-width:390px){.academy-roster-grid{grid-template-columns:minmax(0,1fr) minmax(0,1fr)}}
</style>
