<script lang="ts">
  import PublicShell from '../../lib/public/PublicShell.svelte';
  import PublicImage from '../../lib/public/PublicImage.svelte';
  import Fixtures from '../../lib/public/Fixtures.svelte';
  import Unavailable from '../../lib/public/Unavailable.svelte';
  import type { PublicSectionPageData } from '../../lib/public/sections-types';
  import { academyGroupLink } from '../../lib/public/navigation';
  import { publicImages } from '../../lib/public/images';
  import OrganizationSponsorStrip from '../../lib/public/OrganizationSponsorStrip.svelte';
  import PublicIcon from '../../lib/public/PublicIcon.svelte';
  import type { PublicIconName } from '../../lib/public/icon-types';
  let { data }: { data: PublicSectionPageData } = $props();
  const academyBanner = $derived(
    data.site?.academy_groups.find(group=>group.organization_id==='lefteria_fc_academy'&&group.banner_url)?.banner_url
    || publicImages.hero);
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
    <div class="academy-intro-copy"><p class="public-eyebrow">ΛΕΥΤΕΡΙΑ FC</p><h1>ΑΚΑΔΗΜΙΑ</h1>
      <p class="academy-hero-motto">ΔΙΑΠΛΑΣΗ <span>·</span> ΑΝΑΠΤΥΞΗ <span>·</span> ΟΝΕΙΡΑ</p>
      <p class="academy-description">{data.site?.club?.description || 'Η Ακαδημία της Λευτεριάς FC είναι αφιερωμένη στην ανάπτυξη νέων ποδοσφαιριστών. Χτίζουμε χαρακτήρα, πειθαρχία και αγάπη για το άθλημα μέσα από ένα δομημένο πρόγραμμα προπόνησης.'}</p>
      <div class="academy-actions"><a class="public-button" href="/academy/registration" data-testid="academy-hero-register">ΕΓΓΡΑΦΗ <PublicIcon name="arrow" size={18} /></a><a class="public-button public-button-outline" href="/academy/groups" data-testid="academy-hero-groups">ΤΑ ΤΜΗΜΑΤΑ ΜΑΣ <PublicIcon name="arrow" size={18} /></a><a class="public-button public-button-outline" href="/contact">ΜΙΛΗΣΕ ΜΑΖΙ ΜΑΣ <PublicIcon name="arrow" size={18} /></a></div>
    </div>
    <nav class="public-subnav" aria-label="Ακαδημία"><a href="#explore">ΓΝΩΡΙΣΕ ΤΗΝ ΑΚΑΔΗΜΙΑ</a><a href="/academy/groups">ΗΛΙΚΙΑΚΑ ΤΜΗΜΑΤΑ</a><a href="#academy-fixtures">ΑΓΩΝΕΣ</a><a href="/news?organization_id=lefteria_fc_academy">ΝΕΑ ΑΚΑΔΗΜΙΑΣ ↗</a></nav>
  </section>
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
  {#if !data.available}<Unavailable />{:else}<section class="public-section" id="groups" aria-labelledby="academy-groups-title"><div class="section-heading"><h2 id="academy-groups-title">ΗΛΙΚΙΑΚΑ ΤΜΗΜΑΤΑ</h2></div><div class="public-group-grid">{#each data.site?.academy_groups ?? [] as group (group.id)}<article id={`group-${encodeURIComponent(group.id)}`} class="public-group-card">{#if group.banner_url}<PublicImage src={group.banner_url} alt="" />{/if}<div><p class="public-eyebrow">{group.age_range || group.season || 'ΑΚΑΔΗΜΙΑ'}</p><h3><a href={academyGroupLink(group)}>{group.name} <span aria-hidden="true">↗</span></a></h3>{#if group.description}<p>{group.description}</p>{/if}{#if group.city || group.location_name}<p class="group-location">{[group.city, group.location_name].filter(Boolean).join(' · ')}</p>{/if}{#if group.training_schedule}<h4>ΠΡΟΓΡΑΜΜΑ ΠΡΟΠΟΝΗΣΕΩΝ</h4><p class="preserve-lines">{group.training_schedule}</p>{/if}<a class="group-open" href={academyGroupLink(group)}>ΓΝΩΡΙΣΕ ΤΟ ΤΜΗΜΑ <span aria-hidden="true">↗</span></a></div></article>{:else}<p class="public-empty">Δεν έχουν καταχωριστεί ηλικιακά τμήματα.</p>{/each}</div></section><section class="public-section section-dark" id="academy-fixtures" aria-labelledby="academy-fixtures-title"><div class="section-heading"><h2 id="academy-fixtures-title">ΑΓΩΝΕΣ ΑΚΑΔΗΜΙΑΣ</h2></div>{#if data.site?.fixtures.length}<Fixtures fixtures={data.site.fixtures} />{:else}<p class="public-empty">Δεν έχουν καταχωριστεί αγώνες ακαδημίας.</p>{/if}</section>{/if}
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
  .academy-reviews-heading h2{font:900 clamp(29px,3vw,48px)/1.1 'Fira Sans Extra Condensed',sans-serif}
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
  .academy-stats-grid strong{color:#f6bb38;font:900 clamp(38px,4vw,65px)/1 'Fira Sans Extra Condensed',sans-serif}
  .academy-stats-grid span{color:#d2d2d2;font-size:13px;font-weight:700}
  .academy-philosophy{display:grid;grid-template-columns:minmax(0,1.08fr) minmax(0,.92fr);gap:45px;align-items:center;background:#101111}
  .academy-philosophy h2,.academy-enquiry h2{font:900 clamp(32px,3.5vw,58px)/1.1 'Fira Sans Extra Condensed',sans-serif}
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
</style>
