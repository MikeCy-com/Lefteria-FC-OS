<script lang="ts">
  import PublicShell from '../../lib/public/PublicShell.svelte';
  import PublicImage from '../../lib/public/PublicImage.svelte';
  import Fixtures from '../../lib/public/Fixtures.svelte';
  import Unavailable from '../../lib/public/Unavailable.svelte';
  import type { PublicSectionPageData } from '../../lib/public/sections-types';
  import { academyGroupLink } from '../../lib/public/navigation';
  import { buildAcademyTerritoryDirectory } from '../../lib/public/academy-territory-directory';
  import type { PublicGroup } from '../../lib/public/types';
  import { publicImages } from '../../lib/public/images';
  import OrganizationSponsorStrip from '../../lib/public/OrganizationSponsorStrip.svelte';
  import PublicIcon from '../../lib/public/PublicIcon.svelte';
  import type { PublicIconName } from '../../lib/public/icon-types';
  let { data }: { data: PublicSectionPageData & { editorialLead: string|null } } = $props();
  const academyBanner = $derived(
    data.site?.academy_groups.find(group=>group.organization_id==='lefteria_fc_academy'&&group.banner_url)?.banner_url
    || publicImages.academy);
  const academyStats = $derived(data.sections?.academy_stats?.organization_id === 'lefteria_fc_academy' ? data.sections.academy_stats : null);
  // The public sections endpoint is already publication-allowlisted. Do not
  // fetch private CRM reviews, invent aggregate ratings, or borrow club reviews.
  const academyReviews = $derived((data.sections?.reviews ?? [])
    .filter(row => row.organization_id === 'lefteria_fc_academy' &&
      Number.isInteger(Number(row.rating)) && Number(row.rating) >= 1 && Number(row.rating) <= 5)
    .map(row => ({...row,rating:Number(row.rating)}))
    .toSorted((a,b) => (b.published_at ?? '').localeCompare(a.published_at ?? '') || a.id.localeCompare(b.id)));
  const averageRating = $derived(academyReviews.length
    ? new Intl.NumberFormat('el-GR',{maximumFractionDigits:1}).format(
        academyReviews.reduce((sum,row)=>sum+row.rating,0)/academyReviews.length)
    : null);
  const scopedGroups = $derived((data.site?.academy_groups ?? []).filter(group => group.organization_id === 'lefteria_fc_academy'));
  // One verified, shared hierarchy for both Academy landing and directory.
  // Imported legacy text is unassigned until an administrator verifies IDs.
  const publishedGroupIds = $derived(new Set(scopedGroups.map(group => group.id)));
  const academyFixtures = $derived((data.site?.fixtures ?? []).filter(fixture =>
    fixture.organization_id === 'lefteria_fc_academy' &&
    typeof fixture.academy_group_id === 'string' &&
    publishedGroupIds.has(fixture.academy_group_id)));
  const academyTerritory = $derived(buildAcademyTerritoryDirectory(scopedGroups));
  const academyCities = $derived(academyTerritory.cities);
  const academyLocationsCount = $derived(academyCities.reduce(
    (count,city) => count + city.locations.length, 0));
  const academySeasons = $derived([...new Set(scopedGroups.map(group=>group.season).filter(Boolean))].slice(0,2));
  const academyAges = $derived([...new Set(scopedGroups.map(group=>group.age_range).filter(Boolean))].slice(0,3));
  const destinations: { href: string; icon: PublicIconName; title: string; description: string }[] = [
    { href: '/academy/philosophy', icon: 'heart', title: 'Η ΦΙΛΟΣΟΦΙΑ ΜΑΣ', description: 'Γνώρισε τις αξίες και την προσέγγισή μας στην ανάπτυξη νέων παικτών.' },
    { href: '/academy/groups', icon: 'users', title: 'ΗΛΙΚΙΑΚΑ ΤΜΗΜΑΤΑ', description: 'Βρες το τμήμα που ταιριάζει στο παιδί σου και δες το πρόγραμμα προπονήσεών του.' },
    { href: '/academy/registration', icon: 'trophy', title: 'ΕΓΓΡΑΦΕΣ', description: 'Δες πώς μπορείς να εγγράψεις έναν αθλητή στην Ακαδημία μας.' },
    { href: '/partners?scope=academy', icon: 'shield', title: 'ΟΙ ΣΥΝΕΡΓΑΤΕΣ ΜΑΣ', description: 'Οι άνθρωποι και οι οργανισμοί που στηρίζουν την ακαδημία μας.' }
  ];
</script>
<svelte:head><title>ΑΚΑΔΗΜΙΑ | ΛΕΥΤΕΡΙΑ FC</title><meta name="description" content="Η ακαδημία της Λευτεριάς FC. Γνώρισε τα ηλικιακά τμήματα και το πρόγραμμα αγώνων." /></svelte:head>
<PublicShell site={data.site} branding={data.branding} organization="lefteria_fc_academy" navigationGroups={data.navigationGroups}>
  <section class="public-page-heading academy-intro" data-testid="academy-landing-page">
    <PublicImage src={academyBanner} alt="" class="academy-hero-image" />
    <div class="academy-intro-copy"><p class="public-eyebrow">ΑΚΑΔΗΜΙΑ ΛΕΥΤΕΡΙΑ FC</p><h1>ΛΕΥΤΕΡΙΑ FC <span class="academy-title-gold">ΑΚΑΔΗΜΙΑ</span></h1>
      <p class="academy-hero-motto">ΔΙΑΠΛΑΣΗ <span>·</span> ΑΝΑΠΤΥΞΗ <span>·</span> ΟΝΕΙΡΑ</p>
      <p class="academy-description">{data.editorialLead ?? (data.site?.club?.description || 'Η Ακαδημία της Λευτεριάς FC είναι αφιερωμένη στην ανάπτυξη νέων ποδοσφαιριστών. Χτίζουμε χαρακτήρα, πειθαρχία και αγάπη για το άθλημα μέσα από ένα δομημένο πρόγραμμα προπόνησης.')}</p>
      <div class="academy-actions"><a class="public-button" href="/academy/registration" data-testid="academy-hero-register">ΕΓΓΡΑΦΗ <PublicIcon name="arrow" size={18} /></a><a class="public-button public-button-outline" href="/academy/groups" data-testid="academy-hero-groups">ΤΟ ΠΡΟΓΡΑΜΜΑ ΜΑΣ <PublicIcon name="arrow" size={18} /></a><a class="public-button public-button-outline" href="/contact">ΜΙΛΗΣΕ ΜΑΖΙ ΜΑΣ <PublicIcon name="arrow" size={18} /></a></div>
    </div>

  </section>
  <section class="academy-program-overview" aria-labelledby="academy-program-title">
    <h2 id="academy-program-title">ΤΟ ΠΡΟΓΡΑΜΜΑ ΜΑΣ</h2>
    <p>Ανάπτυξη με σεβασμό στο παιδί, την ηλικία και τις ανάγκες του κάθε αθλητή.</p>
    <div class="academy-program-facts">
      <div><PublicIcon name="pin" size={27} /><span>ΠΟΛΕΙΣ / ΤΟΠΟΘΕΣΙΕΣ <strong>{academyCities.length
            ? `${academyCities.length} ${academyCities.length === 1 ? 'πόλη' : 'πόλεις'} · ${academyLocationsCount} ${academyLocationsCount === 1 ? 'τοποθεσία' : 'τοποθεσίες'}`
            : 'Καμία επιβεβαιωμένη τοποθεσία'}</strong></span></div>
      <div><PublicIcon name="users" size={27} /><span>ΗΛΙΚΙΕΣ <strong>{academyAges.join(' · ') || 'Αναμένεται δημοσίευση'}</strong></span></div>
      <div><PublicIcon name="calendar" size={27} /><span>ΠΡΟΓΡΑΜΜΑ <strong>{academySeasons.join(' · ') || 'Αναμένεται δημοσίευση'}</strong></span></div>
    </div>
  </section>
  
  <section class="academy-age-groups public-section" id="groups" aria-labelledby="academy-groups-title" data-testid="academy-age-groups">
    {#snippet ageCard(group: PublicGroup)}
      <article class="academy-age-card">
        <PublicImage src={group.banner_url || publicImages.academy} alt="" />
        <div class="academy-age-caption">
          <h5><a href={academyGroupLink(group)}>{group.name} <span aria-hidden="true">↗</span></a></h5>
          <strong>{group.age_range || 'ΑΚΑΔΗΜΙΑ'}</strong>
          {#if group.season}<span>{group.season}</span>{/if}
          <a class="academy-card-arrow" href={academyGroupLink(group)} aria-label={`Προβολή ${group.name}`}>→</a>
        </div>
        {#if group.description || group.training_schedule}
          <details class="academy-age-details">
            <summary>Περισσότερες πληροφορίες</summary>
            {#if group.description}<p>{group.description}</p>{/if}
            {#if group.training_schedule}<p class="preserve-lines">{group.training_schedule}</p>{/if}
          </details>
        {/if}
      </article>
    {/snippet}
    <div class="academy-age-heading">
      <h2 id="academy-groups-title">ΤΑ ΤΜΗΜΑΤΑ ΜΑΣ</h2>
      <a href="/academy/groups">ΔΕΙΤΕ ΟΛΑ ΤΑ ΤΜΗΜΑΤΑ <PublicIcon name="arrow" size={17}/></a>
    </div>
    {#if academyCities.length || academyTerritory.unassigned.length}
      {#if academyCities.length > 1}
        <nav class="academy-landing-city-index" aria-label="Πόλεις Ακαδημίας" data-testid="academy-landing-city-navigation">
          {#each academyCities as city (city.id)}
            <a href={`#academy-landing-city-${city.slug}`}>{city.name}</a>
          {/each}
        </nav>
      {/if}
      {#each academyCities as city (city.id)}
        <section class="academy-city-block" aria-labelledby={`academy-landing-city-${city.slug}`} data-testid="academy-landing-verified-city">
          <div class="academy-city-heading"><h3 class="academy-city-label" id={`academy-landing-city-${city.slug}`}>{city.name}</h3>
            <a href={`/academy/groups#academy-city-${city.slug}`}>ΤΜΗΜΑΤΑ ΠΟΛΗΣ <PublicIcon name="arrow" size={15} /></a>
          </div>
          {#each city.locations as location (location.id)}
            <section class="academy-location-block" aria-labelledby={`academy-landing-location-${city.slug}-${location.slug}`} data-testid="academy-landing-verified-location">
              <h4 id={`academy-landing-location-${city.slug}-${location.slug}`}>{location.name}</h4>
              <div class="academy-age-grid">
                {#each location.groups as group (group.id)}
                  {@render ageCard(group)}
                {/each}
              </div>
            </section>
          {/each}
        </section>
      {/each}
      {#if academyTerritory.unassigned.length}
        <section class="academy-unassigned-block" aria-labelledby="academy-landing-unassigned" data-testid="academy-landing-unassigned-groups">
          <h3 id="academy-landing-unassigned">ΤΜΗΜΑΤΑ ΧΩΡΙΣ ΕΠΙΒΕΒΑΙΩΜΕΝΗ ΤΟΠΟΘΕΣΙΑ</h3>
          <p>Τα δημοσιευμένα τμήματα εμφανίζονται εδώ μέχρι να αντιστοιχιστούν από τη διοίκηση σε επίσημη πόλη και τοποθεσία.</p>
          <h4 class="sr-only">Δημοσιευμένα τμήματα χωρίς τοποθεσία</h4>
          <div class="academy-age-grid">
            {#each academyTerritory.unassigned as group (group.id)}
              {@render ageCard(group)}
            {/each}
          </div>
        </section>
      {/if}
    {:else}
      <div class="academy-published-empty">
        <p>Τα ηλικιακά τμήματα και οι τοποθεσίες δεν έχουν δημοσιευτεί ακόμη.</p>
        <a href="/academy/groups" class="public-button public-button-outline">ΠΛΗΡΟΦΟΡΙΕΣ ΤΜΗΜΑΤΩΝ <PublicIcon name="arrow" size={17}/></a>
      </div>
    {/if}
  </section>

  <section class="academy-philosophy public-section" aria-labelledby="academy-philosophy-title" data-testid="academy-development-philosophy">
    <div class="academy-philosophy-copy"><p class="public-eyebrow">ΦΙΛΟΣΟΦΙΑ ΑΝΑΠΤΥΞΗΣ</p>
      <h2 id="academy-philosophy-title">ΜΕΓΑΛΩΝΟΥΜΕ ΜΑΖΙ.<br/><em>ΜΕΣΑ ΚΑΙ ΕΞΩ ΑΠΟ ΤΟ ΓΗΠΕΔΟ.</em></h2>
      <p>Η ανάπτυξη κάθε παιδιού είναι πάνω από ένα αποτέλεσμα. Δίνουμε έμφαση στην τεχνική, την τακτική κατανόηση, τη συνεργασία και την ψυχολογία της ηλικίας του.</p>
      <ul><li>Ατομική ανάπτυξη πάνω από τα αποτελέσματα στα νεανικά τμήματα.</li>
        <li>Προπόνηση ανάλογα με την ηλικία και τις ανάγκες του κάθε παιδιού.</li>
        <li>Ισορροπία εκπαίδευσης, αθλητισμού και ψυχικής ανθεκτικότητας.</li>
        <li>Σταδιακή εξέλιξη και πορεία προς την πρώτη ομάδα.</li></ul>
      <a class="public-button public-button-outline" href="/academy/philosophy">ΔΕΣ ΤΗ ΦΙΛΟΣΟΦΙΑ ΜΑΣ <PublicIcon name="arrow" size={18} /></a>
    </div>
    <div class="academy-philosophy-art"><PublicImage src={publicImages.pitch} alt="Ποδοσφαιρικό γήπεδο" /></div>
  </section>
  <section class="academy-reference-fees" aria-labelledby="academy-fees-heading">
    <div class="academy-fees-art" aria-hidden="true"><PublicImage src={publicImages.academy} alt="" /></div>
    <div class="academy-fees-copy"><h2 id="academy-fees-heading">ΓΙΝΕ ΜΕΛΟΣ<br/>ΤΗΣ ΑΚΑΔΗΜΙΑΣ ΜΑΣ</h2>
      <p>Τα ισχύοντα τέλη εγγραφής και προπονήσεων επιβεβαιώνονται από την Ακαδημία κατά την αίτηση. Δεν πραγματοποιείται χρέωση σε αυτή τη σελίδα.</p>
      <a class="academy-fees-action" href="/academy/registration">ΚΑΝΕ ΕΓΓΡΑΦΗ ΤΩΡΑ <PublicIcon name="arrow" size={19}/></a>
    </div>
  </section>

  <nav class="public-subnav" aria-label="Ακαδημία"><a href="#explore">ΓΝΩΡΙΣΕ ΤΗΝ ΑΚΑΔΗΜΙΑ</a><a href="/academy/groups">ΗΛΙΚΙΑΚΑ ΤΜΗΜΑΤΑ</a><a href="#academy-fixtures">ΑΓΩΝΕΣ</a><a href="/news?organization_id=lefteria_fc_academy">ΝΕΑ ΑΚΑΔΗΜΙΑΣ ↗</a></nav>
  <section class="public-section academy-explore" id="explore" aria-labelledby="explore-title"><div class="public-section-heading"><h2 id="explore-title">ΕΞΕΡΕΥΝΗΣΕ ΤΗΝ <em>ΑΚΑΔΗΜΙΑ</em></h2></div>
    <div class="academy-destinations">{#each destinations as item (item.href)}<a href={item.href} class="academy-destination"><PublicIcon name={item.icon} size={28} /><h3>{item.title}</h3><p>{item.description}</p><span>ΠΕΡΙΣΣΟΤΕΡΑ <PublicIcon name="arrow" size={18} /></span></a>{/each}</div>
  </section>
  <section class="academy-key-figures public-section" aria-labelledby="academy-figures-heading" data-testid="academy-stats">
    <div class="section-heading"><h2 id="academy-figures-heading">Η ΑΚΑΔΗΜΙΑ ΣΕ ΑΡΙΘΜΟΥΣ</h2></div>
    <div class="academy-stats-grid">
      <div><strong>{academyStats ? String(academyStats.age_groups) : '—'}</strong><span>ΗΛΙΚΙΑΚΑ ΤΜΗΜΑΤΑ</span></div>
      <div><strong>{academyStats ? String(academyStats.athletes) : '—'}</strong><span>ΑΘΛΗΤΕΣ</span></div>
      <div><strong>{academyStats?.trainings_per_week ?? '—'}</strong><span>ΠΡΟΠΟΝΗΣΕΙΣ / ΕΒΔΟΜΑΔΑ</span></div>
      <div><strong>{academyStats?.dedication_pct != null ? academyStats.dedication_pct + '%' : '—'}</strong><span>ΑΦΟΣΙΩΣΗ</span></div>
    </div>
    {#if !academyStats}<p class="public-empty">Τα επίσημα στατιστικά της Ακαδημίας δεν έχουν δημοσιευτεί ακόμη.</p>{/if}
  </section>
  {#if !data.available}<Unavailable />{:else}<section class="public-section section-dark" id="academy-fixtures" aria-labelledby="academy-fixtures-title"><div class="section-heading"><h2 id="academy-fixtures-title">ΑΓΩΝΕΣ ΑΚΑΔΗΜΙΑΣ</h2></div>{#if academyFixtures.length}<Fixtures fixtures={academyFixtures} />{:else}<p class="public-empty">Δεν έχουν καταχωριστεί αγώνες ακαδημίας.</p>{/if}</section>{/if}
  <section class="academy-enquiry public-section" data-testid="academy-enquiry-section">
    <div><p class="public-eyebrow">ΕΝΔΙΑΦΕΡΕΣΑΙ;</p><h2>ΔΟΚΙΜΑΣΤΙΚΑ & ΕΓΓΡΑΦΕΣ</h2>
      <p>Γνώρισε από κοντά τη Λευτεριά FC Academy και το προπονητικό της περιβάλλον. Δες τις διαθέσιμες πληροφορίες εγγραφής ή επικοινώνησε με τον σύλλογο για το πρόγραμμα του παιδιού σου.</p>
    </div>
    <div class="academy-enquiry-actions"><a class="public-button" href="/academy/registration" data-testid="academy-registration-cta-btn">ΕΓΓΡΑΦΗ ΑΘΛΗΤΗ <PublicIcon name="arrow" size={18} /></a>
      <a class="public-button public-button-outline" href="/contact">ΡΩΤΗΣΕ ΜΑΣ <PublicIcon name="arrow" size={18} /></a></div>
  </section>
  <OrganizationSponsorStrip organization="lefteria_fc_academy" sponsors={data.sections?.sponsors ?? []} />
  {#if academyReviews.length}
    <section class="public-section academy-public-reviews" data-testid="academy-public-reviews"
      aria-labelledby="academy-reviews-heading">
      <div class="academy-reviews-heading">
        <div><p class="public-eyebrow">ΟΙ ΑΝΘΡΩΠΟΙ ΤΗΣ ΛΕΥΤΕΡΙΑΣ</p>
          <h2 id="academy-reviews-heading">ΤΙ ΛΕΝΕ ΟΙ ΓΟΝΕΙΣ & ΑΘΛΗΤΕΣ</h2>
          <p>Δημοσιευμένες αξιολογήσεις της Ακαδημίας</p></div>
        <div class="academy-reviews-summary" data-testid="academy-published-rating-summary">
          <strong>{averageRating}/5</strong><span>{academyReviews.length} δημοσιευμένες {academyReviews.length === 1 ? 'αξιολόγηση' : 'αξιολογήσεις'}</span>
        </div>
      </div>
      <div class="academy-review-grid">
        {#each academyReviews.slice(0,3) as review (review.id)}
          <blockquote class="academy-review-card">
            <div class="academy-review-stars">
              <span aria-hidden="true">{'★'.repeat(review.rating)}{'☆'.repeat(5-review.rating)}</span>
              <span class="sr-only">Βαθμολογία {review.rating} από 5</span>
            </div>
            {#if review.title}<h3>{review.title}</h3>{/if}
            <p class="academy-review-body">{review.body}</p>
            <footer><strong>{review.author_name}</strong>{#if review.author_role}<span>{review.author_role}</span>{/if}</footer>
            {#if review.response}<div class="academy-review-response"><strong>ΑΠΑΝΤΗΣΗ ΑΚΑΔΗΜΙΑΣ</strong><p>{review.response}</p></div>{/if}
          </blockquote>
        {/each}
      </div>
    </section>
  {/if}
</PublicShell>


<style>
  :global(.public-site[data-route="/academy"]) .academy-intro {
    position:relative;isolation:isolate;
    min-height:clamp(390px,36vw,600px);
    display:flex;flex-direction:column;justify-content:flex-end;
    padding:clamp(38px,4.5vw,72px) clamp(18px,4.6vw,78px) 0;
    overflow:hidden;background:#090b0c;
    border-bottom:1px solid #a57b2c;
  }
  :global(.public-site[data-route="/academy"]) .academy-intro>.academy-hero-image {
    position:absolute;inset:0;z-index:-2;
    width:100%;height:100%;object-fit:cover;object-position:center 42%;
  }
  :global(.public-site[data-route="/academy"]) .academy-intro::before {
    content:"";pointer-events:none;position:absolute;inset:0;z-index:-1;
    background:linear-gradient(90deg,#080909f3 0%,#080909d4 24%,#08090955 59%,#08090910 100%),
      linear-gradient(0deg,#080909e8,transparent 33%);
  }
  :global(.public-site[data-route="/academy"]) .academy-intro-copy{
    max-width:min(100%,650px);padding:0 0 clamp(34px,5vw,62px);
    position:relative;z-index:1;
  }
  :global(.public-site[data-route="/academy"]) .academy-intro .public-eyebrow{
    font-size:clamp(20px,1.8vw,32px);font-weight:800;font-style:italic;
  }
  :global(.public-site[data-route="/academy"]) .academy-intro h1{
    font:900 clamp(64px,7vw,126px)/.91 'Fira Sans Extra Condensed',sans-serif;
    letter-spacing:-.025em;font-style:italic;margin:0;
    text-shadow:0 7px 24px #000c;
  }
  .academy-hero-motto{
    font:800 clamp(17px,1.9vw,30px)/1.22 'Fira Sans Extra Condensed',sans-serif;
    color:#fff;margin:15px 0 20px;
  }
  .academy-hero-motto span{color:#ffc547}
  :global(.public-site[data-route="/academy"]) .academy-description{
    max-width:53ch;color:#eee;font-size:clamp(15px,1.15vw,19px);
    line-height:1.5;text-shadow:0 2px 10px #000;
  }
  :global(.public-site[data-route="/academy"]) .academy-intro .public-subnav{
    position:relative;z-index:1;display:flex;align-items:center;
    flex-wrap:wrap;gap:0;border-top:1px solid #a57b2c;
    border-bottom:0;max-width:100%;
  }
  :global(.public-site[data-route="/academy"]) .academy-intro .public-subnav a{
    min-height:48px;display:inline-flex;align-items:center;
    border:0;border-bottom:3px solid transparent;
    padding:10px 17px;font-weight:800;
  }
  :global(.public-site[data-route="/academy"]) .academy-intro .public-subnav a:hover,
  :global(.public-site[data-route="/academy"]) .academy-intro .public-subnav a:focus-visible{
    background:#f6bb3817;border-bottom-color:#f6bb38;
  }
  @media(max-width:700px){
    :global(.public-site[data-route="/academy"]) .academy-intro{
      min-height:490px;padding:32px 17px 0;justify-content:flex-end;
    }
    :global(.public-site[data-route="/academy"]) .academy-intro>.academy-hero-image{
      object-position:60% center;
    }
    :global(.public-site[data-route="/academy"]) .academy-intro::before{
      background:linear-gradient(0deg,#080909f8 0%,#080909e4 40%,#08090930 85%);
    }
    :global(.public-site[data-route="/academy"]) .academy-intro .public-subnav{
      overflow-x:auto;flex-wrap:nowrap;white-space:nowrap;
      scrollbar-width:thin;
    }
    :global(.public-site[data-route="/academy"]) .academy-intro .public-subnav a{
      font-size:12px;padding:12px;flex:none;
    }
    :global(.public-site[data-route="/academy"]) .academy-intro-copy{padding-bottom:25px;}
  }

  .academy-public-reviews{background:#0a0a0a;border-top:1px solid #65501f}
  .academy-reviews-heading{display:flex;align-items:center;justify-content:space-between;gap:24px;margin-bottom:28px}
  .academy-reviews-heading h2{font:900 clamp(25px,2.5vw,40px)/1.12 'Fira Sans Extra Condensed',sans-serif}
  .academy-reviews-heading p:not(.public-eyebrow){color:#bdbdbd;margin-top:8px}
  .academy-reviews-summary{display:flex;flex-direction:column;align-items:flex-end;gap:5px;flex-shrink:0}
  .academy-reviews-summary strong{font:900 36px/1 'Fira Sans Extra Condensed',sans-serif;color:#ffce65}
  .academy-reviews-summary span{color:#bdbdbd;font-size:12px}
  .academy-review-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:18px}
  .academy-review-card{display:flex;flex-direction:column;gap:14px;min-width:0;padding:27px 24px;margin:0;background:#181818;border:1px solid #5e4d2d}
  .academy-review-stars{color:#ffca57;font-size:23px;letter-spacing:.06em}
  .academy-review-card h3{font:800 24px/1.15 'Fira Sans Extra Condensed',sans-serif}
  .academy-review-body{font-size:15px;line-height:1.7;color:#ececec;overflow-wrap:anywhere}
  .academy-review-card footer{margin-top:auto;border-top:1px solid #444;padding-top:14px;display:flex;flex-direction:column;gap:5px}
  .academy-review-card footer strong{color:#fff;font-size:14px}
  .academy-review-card footer span{color:#bbb;font-size:12px}
  .academy-review-response{padding:14px;background:#101010;border-left:3px solid #e5b94e}
  .academy-review-response strong{font-size:11px;letter-spacing:.05em;color:#f6bb38}
  .academy-review-response p{margin-top:6px;color:#ccc;font-size:13px;line-height:1.6}
  @media(max-width:1000px){.academy-review-grid{grid-template-columns:repeat(2,minmax(0,1fr))}}
  @media(max-width:650px){.academy-review-grid{grid-template-columns:1fr}.academy-reviews-heading{align-items:flex-start;flex-direction:column}.academy-reviews-summary{align-items:flex-start}.academy-review-card{padding:21px 18px}}
  .academy-key-figures{background:#090a0b;border-block:1px solid #3c3528}
  .academy-stats-grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:20px}
  .academy-stats-grid>div{padding:25px 15px;border-left:2px solid #c99732;background:#151515;display:flex;flex-direction:column;gap:7px}
  .academy-stats-grid strong{color:#f6bb38;font:900 clamp(32px,3.3vw,52px)/1 'Fira Sans Extra Condensed',sans-serif}
  .academy-stats-grid span{color:#d2d2d2;font-size:13px;font-weight:700}
  .academy-philosophy{display:grid;grid-template-columns:minmax(0,1.08fr) minmax(0,.92fr);gap:45px;align-items:center;background:#101111}
  .academy-philosophy h2,.academy-enquiry h2{font:900 clamp(27px,2.75vw,45px)/1.12 'Fira Sans Extra Condensed',sans-serif}
  .academy-philosophy em{color:#f6bb38}
  .academy-philosophy-copy>p:not(.public-eyebrow),.academy-enquiry p:not(.public-eyebrow){line-height:1.7;color:#d0d0d0;max-width:67ch;margin:20px 0}
  .academy-philosophy ul{padding:0;list-style:none;display:grid;gap:14px;margin:24px 0}
  .academy-philosophy li{border-left:3px solid #e7b341;padding-left:15px;font-size:15px;line-height:1.5;color:#e7e7e7}
  .academy-philosophy-art{overflow:hidden;min-height:300px;align-self:stretch}
  .academy-philosophy-art :global(img){width:100%;height:100%;object-fit:cover}
  .academy-enquiry{background:linear-gradient(130deg,#12110d,#080808);border-block:1px solid #80662b;display:flex;justify-content:space-between;align-items:center;gap:30px}
  .academy-enquiry-actions{display:flex;flex-wrap:wrap;gap:15px;flex-shrink:0}
  @media(max-width:900px){.academy-stats-grid{grid-template-columns:repeat(2,minmax(0,1fr))}.academy-philosophy{grid-template-columns:1fr}.academy-philosophy-art{max-height:330px}.academy-enquiry{flex-direction:column;align-items:start}}
  @media(max-width:500px){.academy-key-figures,.academy-philosophy,.academy-enquiry{padding:30px 18px}.academy-stats-grid{gap:9px}.academy-stats-grid>div{padding:17px 9px}.academy-stats-grid span{font-size:11px}.academy-enquiry-actions{width:100%;flex-direction:column}.academy-enquiry-actions>a{justify-content:center}}

  /* Academy source-image composition: short visual hero, factual programme
     strip, city→location age tiles, player development, membership banner.
     Operational stats, fixtures, editorial destinations remain below. */
  :global(.public-site[data-route="/academy"]) .academy-intro{
    min-height:clamp(320px,21vw,385px);
    padding:24px clamp(24px,3.5vw,54px) 0;
    justify-content:flex-end;
  }
  :global(.public-site[data-route="/academy"]) .academy-intro-copy{
    max-width:min(100%,540px);padding-bottom:22px;
  }
  :global(.public-site[data-route="/academy"]) .academy-intro .public-eyebrow{font-size:19px;margin-bottom:6px}
  :global(.public-site[data-route="/academy"]) .academy-intro h1{
    font-size:clamp(44px,4.6vw,74px);line-height:.93;letter-spacing:-.025em;
  }
  .academy-hero-motto{font-size:clamp(17px,1.75vw,25px);margin:10px 0}
  :global(.public-site[data-route="/academy"]) .academy-description{
    max-width:48ch;font-size:clamp(13px,1vw,15px);line-height:1.44;margin:10px 0 14px;
  }
  :global(.public-site[data-route="/academy"]) .academy-actions{gap:9px}
  :global(.public-site[data-route="/academy"]) .academy-actions .public-button{min-height:40px;padding:10px 15px;font-size:13px}
  .academy-program-overview{
    display:grid;grid-template-columns:minmax(205px,.8fr) minmax(220px,1fr) minmax(0,2.2fr);
    align-items:center;gap:20px;padding:15px clamp(24px,3vw,45px);
    min-height:75px;background:#0c0d0d;border-block:1px solid #564524;
  }
  .academy-program-overview h2{
    margin:0;white-space:nowrap;font:900 clamp(20px,1.8vw,28px)/1 'Fira Sans Extra Condensed',sans-serif;
  }
  .academy-program-overview h2{display:flex;align-items:center;gap:12px;white-space:normal}
  .academy-program-overview h2::before{content:"";display:block;flex:0 0 5px;height:24px;background:#f6bb38;transform:skew(-18deg)}
  .academy-program-overview>p{margin:0;max-width:53ch;font-size:12px;line-height:1.45;color:#ddd}
  .academy-program-facts{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));align-items:stretch}
  .academy-program-facts>div{
    display:flex;align-items:center;gap:8px;padding:0 10px;
    border-left:1px solid #777;font-family:'Fira Sans Extra Condensed',sans-serif;
  }
  .academy-program-facts :global(svg){color:#f6bb38;flex-shrink:0}
  .academy-program-facts>div>span{font-size:12px;font-weight:800}
  .academy-program-facts strong{display:block;font:400 11px/1.4 'Fira Sans',sans-serif;color:#ddd;overflow-wrap:anywhere}
  .academy-age-groups{background:#101111;padding:18px clamp(20px,2.7vw,43px) 22px}
  .academy-age-heading{display:flex;align-items:center;justify-content:space-between;gap:15px;border-bottom:1px solid #3c3429;padding-bottom:12px}
  .academy-age-heading h2{font:900 clamp(20px,1.6vw,26px)/1 'Fira Sans Extra Condensed',sans-serif;margin:0}
  .academy-age-heading>a{display:flex;align-items:center;gap:9px;color:#ffcb4a;font:800 13px 'Fira Sans Extra Condensed',sans-serif;text-decoration:none}
  .academy-landing-city-index{
    display:flex;align-items:center;flex-wrap:wrap;gap:9px;
    padding:14px 0 5px;margin:0;
  }
  .academy-landing-city-index a{
    display:inline-flex;align-items:center;min-height:44px;padding:10px 16px;
    border:1px solid #876b38;border-radius:0 8px 0 5px;
    background:linear-gradient(140deg,#242117,#141515);color:#f9d68d;
    font:800 14px/1.2 'Fira Sans Extra Condensed',sans-serif;
    text-decoration:none;
  }
  .academy-landing-city-index a:is(:hover,:focus-visible){
    color:#080909;background:#f6bb38;border-color:#f6bb38;
  }
  .academy-landing-city-index a:focus-visible{outline:3px solid #fff;outline-offset:3px}
  .academy-city-block{padding:8px 0 12px;border-bottom:1px solid #34322b;scroll-margin-top:calc(var(--public-header,64px) + 14px)}
  .academy-city-heading{display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:10px}
  .academy-city-heading a{
    display:inline-flex;align-items:center;gap:7px;min-height:44px;
    color:#f6bb38;font:800 13px/1.2 'Fira Sans Extra Condensed',sans-serif;
    text-decoration:none;
  }
  .academy-city-heading a:is(:hover,:focus-visible){color:#fff;text-decoration:underline;text-underline-offset:3px}
  .academy-city-heading a:focus-visible{outline:2px solid #f6bb38;outline-offset:3px}
  .academy-city-label{scroll-margin-top:calc(var(--public-header,64px) + 14px)}
  .academy-location-block{padding:10px 0 6px;scroll-margin-top:calc(var(--public-header,64px) + 14px)}
  .academy-location-block>h4{
    display:flex;gap:8px;align-items:center;
    font:800 17px/1.3 'Fira Sans Extra Condensed',sans-serif;
    color:#f3f2ed;margin:0 0 11px;
  }
  .academy-location-block>h4::before{
    content:"";width:4px;height:1.05em;flex:none;
    transform:skew(-16deg);background:#f6bb38;
  }
  .academy-age-card:focus-within{border-color:#f6bb38;box-shadow:0 0 0 1px #f6bb38}
  .academy-age-card :is(a,summary):focus-visible{outline:3px solid #fff;outline-offset:3px}
  .academy-unassigned-block{
    padding:20px 18px;margin:18px 0 4px;
    background:linear-gradient(130deg,#1e1a14,#111313);
    border:1px solid #8c7549;border-left:4px solid #f6bb38;
  }
  .academy-unassigned-block h3{
    margin:0 0 8px;color:#f6bb38;
    font:800 clamp(19px,1.75vw,26px)/1.16 'Fira Sans Extra Condensed',sans-serif;
  }
  .academy-unassigned-block>p{font-size:13px;line-height:1.6;color:#ded8cc;max-width:75ch;margin:0 0 16px}
  .academy-city-block{margin-top:12px}
  .academy-city-label{font:800 14px 'Fira Sans Extra Condensed',sans-serif;color:#f5b835;margin:0 0 9px}
  .academy-location-block{margin:0 0 10px}
  .academy-location-block>h4{font-size:12px;margin:0 0 7px;color:#ececec}
  .academy-age-grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:14px}
  .academy-age-card{min-width:0;position:relative;overflow:hidden;border:1px solid #62605b;background:#121212;min-height:143px}
  .academy-age-card>:global(img){position:absolute;inset:0;width:100%;height:100%;object-fit:cover;filter:brightness(.72)}
  .academy-age-card::before{content:"";pointer-events:none;position:absolute;inset:0;z-index:1;background:linear-gradient(90deg,#050606f2,transparent 83%),linear-gradient(0deg,#0506069a,transparent 45%)}
  .academy-age-caption{position:relative;z-index:2;min-height:143px;padding:12px 14px 9px;display:flex;flex-direction:column;align-items:flex-start}
  .academy-age-caption h5{font:900 clamp(29px,2.3vw,40px)/1 'Fira Sans Extra Condensed',sans-serif;margin:0 0 4px}
  .academy-age-caption h5>a{color:#fff;text-decoration:none}
  .academy-age-caption h5 span{font-size:16px;color:#ffca45}
  .academy-age-caption strong{font:900 15px 'Fira Sans Extra Condensed',sans-serif;color:#ffbf3c}
  .academy-age-caption>span{font:500 12px 'Fira Sans',sans-serif;color:#fff}
  .academy-card-arrow{display:grid;place-items:center;margin-top:auto;width:34px;height:26px;border:1px solid #fff;color:#fff!important;text-decoration:none}
  .academy-age-details{position:relative;z-index:2;padding:7px 14px;font-size:12px;background:#0b0b0bf5}
  .academy-age-details summary{cursor:pointer;color:#ffca45}
  .academy-age-details p{margin:6px 0;color:#ddd}
  .academy-published-empty{display:flex;align-items:center;justify-content:space-between;gap:15px;padding:18px 0}
  .academy-published-empty p{color:#bbb;font-size:13px;margin:0}
  :global(.public-site[data-route="/academy"]) .academy-philosophy{
    min-height:218px;padding:0;background:#0b0c0c;gap:0;
    grid-template-columns:minmax(0,1fr) minmax(0,.44fr);
    border-top:1px solid #62552f;
  }
  .academy-philosophy-copy{padding:22px clamp(22px,3vw,42px)}
  .academy-philosophy h2{font-size:clamp(22px,2.15vw,34px)}
  .academy-philosophy-copy>p:not(.public-eyebrow){font-size:13px;line-height:1.5;margin:10px 0}
  .academy-philosophy ul{grid-template-columns:repeat(2,minmax(0,1fr));gap:7px 14px;margin:12px 0}
  .academy-philosophy li{font-size:12px;padding-left:10px}
  .academy-philosophy-art{min-height:200px;max-height:340px;clip-path:polygon(8% 0,100% 0,100% 100%,0 100%)}
  .academy-reference-fees{
    display:grid;grid-template-columns:minmax(0,.42fr) minmax(0,1fr);
    min-height:132px;margin:0;padding:0;border-top:1px solid #f6bb38;
    background:linear-gradient(110deg,#ffb82d,#f5ae1e);color:#070707;
  }
  .academy-fees-art{position:relative;overflow:hidden;clip-path:polygon(0 0,100% 0,90% 100%,0 100%)}
  .academy-fees-art :global(img){width:100%;height:100%;object-fit:cover;filter:saturate(.8)}
  .academy-fees-copy{display:flex;align-items:center;justify-content:space-between;gap:26px;padding:22px 28px 22px 10px}
  .academy-fees-copy h2{font:italic 900 clamp(28px,2.8vw,47px)/.97 'Fira Sans Extra Condensed',sans-serif;margin:0;flex-shrink:0}
  .academy-fees-copy p{font-size:12px;line-height:1.4;max-width:34ch;margin:0}
  .academy-fees-action{display:inline-flex;align-items:center;gap:10px;min-height:43px;white-space:nowrap;padding:10px 13px;background:#090909;color:#fff;text-decoration:none;font:800 15px 'Fira Sans Extra Condensed',sans-serif}
  :global(.public-site[data-route="/academy"])>.public-content .public-subnav{padding-inline:clamp(20px,2.7vw,42px)}
  @media(max-width:1200px){
    .academy-program-overview{grid-template-columns:1fr;gap:12px}
    .academy-program-facts{max-width:100%}
    .academy-fees-copy{flex-wrap:wrap}
    .academy-age-grid{grid-template-columns:repeat(2,minmax(0,1fr))}
  }
  @media(max-width:700px){
    :global(.public-site[data-route="/academy"]) .academy-intro{min-height:430px;padding:23px 17px 0}
    :global(.public-site[data-route="/academy"]) .academy-intro h1{font-size:46px}
    .academy-program-overview{padding:20px 16px;gap:10px}
    .academy-program-facts{grid-template-columns:1fr;gap:9px}
    .academy-program-facts>div{padding:8px 0;border-left:0;border-top:1px solid #444}
    .academy-age-groups{padding:20px 16px}
    .academy-landing-city-index{
      flex-wrap:nowrap;overflow-x:auto;padding:12px 1px;
      scrollbar-width:thin;overscroll-behavior-inline:contain;
      scroll-padding-inline:12px;
    }
    .academy-landing-city-index a{flex:none}
    .academy-city-heading{align-items:flex-start}
    .academy-unassigned-block{padding:18px 14px;margin-inline:0}
    .academy-age-grid{grid-template-columns:1fr}
    .academy-age-heading{align-items:flex-start;flex-direction:column}
    .academy-age-card{min-height:170px}
    .academy-age-caption{min-height:170px}
    :global(.public-site[data-route="/academy"]) .academy-philosophy{grid-template-columns:1fr}
    .academy-philosophy ul{grid-template-columns:1fr}
    .academy-philosophy-art{clip-path:none;max-height:230px}
    .academy-reference-fees{grid-template-columns:1fr}
    .academy-fees-art{min-height:130px;max-height:180px;clip-path:none}
    .academy-fees-copy{padding:20px;gap:18px}
    .academy-published-empty{align-items:flex-start;flex-direction:column}
  }

</style>

<style>
  /* Original AcademyLandingPage.jsx pairs the club name with a gold Academy
     wordmark, rather than displaying Academy as an oversized solo heading. */
  :global(.public-site[data-route="/academy"]) .academy-title-gold{color:var(--public-gold)}
</style>
