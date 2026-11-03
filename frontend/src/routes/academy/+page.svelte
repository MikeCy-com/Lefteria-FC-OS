<script lang="ts">
  import PublicShell from '../../lib/public/PublicShell.svelte';
  import PublicImage from '../../lib/public/PublicImage.svelte';
  import Fixtures from '../../lib/public/Fixtures.svelte';
  import Unavailable from '../../lib/public/Unavailable.svelte';
  import type { PublicPageData } from '../../lib/public/types';
  import { academyGroupLink } from '../../lib/public/navigation';
  import PublicIcon from '../../lib/public/PublicIcon.svelte';
  import type { PublicIconName } from '../../lib/public/icon-types';
  let { data }: { data: PublicPageData } = $props();
  const destinations: { href: string; icon: PublicIconName; title: string; description: string }[] = [
    { href: '/academy/philosophy', icon: 'heart', title: 'Η ΦΙΛΟΣΟΦΙΑ ΜΑΣ', description: 'Γνώρισε τις αξίες και την προσέγγισή μας στην ανάπτυξη νέων παικτών.' },
    { href: '/academy/groups', icon: 'users', title: 'ΗΛΙΚΙΑΚΑ ΤΜΗΜΑΤΑ', description: 'Βρες το τμήμα που ταιριάζει στο παιδί σου και δες το πρόγραμμα προπονήσεών του.' },
    { href: '/team', icon: 'trophy', title: 'Η ΠΡΩΤΗ ΟΜΑΔΑ', description: 'Γνώρισε τους παίκτες μας και τον επόμενο σταθμό στο ποδοσφαιρικό ταξίδι.' },
    { href: '/partners?scope=academy', icon: 'shield', title: 'ΟΙ ΣΥΝΕΡΓΑΤΕΣ ΜΑΣ', description: 'Οι άνθρωποι και οι οργανισμοί που στηρίζουν την ακαδημία μας.' }
  ];
</script>
<svelte:head><title>ΑΚΑΔΗΜΙΑ | ΛΕΥΤΕΡΙΑ FC</title><meta name="description" content="Η ακαδημία της Λευτεριάς FC. Γνώρισε τα ηλικιακά τμήματα και το πρόγραμμα αγώνων." /></svelte:head>
<PublicShell site={data.site} branding={data.branding} organization="lefteria_fc_academy" navigationGroups={data.navigationGroups}>
  <section class="public-page-heading academy-intro" data-testid="academy-landing-page">
    <div class="academy-intro-copy"><p class="public-eyebrow">ΑΚΑΔΗΜΙΑ ΛΕΥΤΕΡΙΑ FC</p><h1>ΛΕΥΤΕΡΙΑ FC<br /><em>ΑΚΑΔΗΜΙΑ</em></h1>
      <p class="academy-description">{data.site?.club?.description || 'Η Ακαδημία της Λευτεριάς FC είναι αφιερωμένη στην ανάπτυξη νέων ποδοσφαιριστών. Χτίζουμε χαρακτήρα, πειθαρχία και αγάπη για το άθλημα μέσα από ένα δομημένο πρόγραμμα προπόνησης.'}</p>
      <div class="academy-actions"><a class="public-button" href="/academy/groups">ΤΑ ΤΜΗΜΑΤΑ ΜΑΣ <PublicIcon name="arrow" size={18} /></a><a class="public-button public-button-outline" href="/contact">ΜΙΛΗΣΕ ΜΑΖΙ ΜΑΣ <PublicIcon name="arrow" size={18} /></a></div>
    </div>
    <nav class="public-subnav" aria-label="Ακαδημία"><a href="#explore">ΓΝΩΡΙΣΕ ΤΗΝ ΑΚΑΔΗΜΙΑ</a><a href="/academy/groups">ΗΛΙΚΙΑΚΑ ΤΜΗΜΑΤΑ</a><a href="#academy-fixtures">ΑΓΩΝΕΣ</a><a href="/news?organization_id=lefteria_fc_academy">ΝΕΑ ΑΚΑΔΗΜΙΑΣ ↗</a></nav>
  </section>
  <section class="public-section academy-explore" id="explore" aria-labelledby="explore-title"><div class="public-section-heading"><h2 id="explore-title">ΕΞΕΡΕΥΝΗΣΕ ΤΗΝ <em>ΑΚΑΔΗΜΙΑ</em></h2></div>
    <div class="academy-destinations">{#each destinations as item (item.href)}<a href={item.href} class="academy-destination"><PublicIcon name={item.icon} size={28} /><h3>{item.title}</h3><p>{item.description}</p><span>ΠΕΡΙΣΣΟΤΕΡΑ <PublicIcon name="arrow" size={18} /></span></a>{/each}</div>
  </section>
  {#if !data.available}<Unavailable />{:else}<section class="public-section" id="groups"><div class="section-heading"><h2>ΗΛΙΚΙΑΚΑ ΤΜΗΜΑΤΑ</h2></div><div class="public-group-grid">{#each data.site?.academy_groups ?? [] as group (group.id)}<article id={`group-${encodeURIComponent(group.id)}`} class="public-group-card">{#if group.banner_url}<PublicImage src={group.banner_url} alt="" />{/if}<div><p class="public-eyebrow">{group.age_range || group.season || 'ΑΚΑΔΗΜΙΑ'}</p><h3><a href={academyGroupLink(group)}>{group.name} <span aria-hidden="true">↗</span></a></h3>{#if group.description}<p>{group.description}</p>{/if}{#if group.city || group.location_name}<p class="group-location">{[group.city, group.location_name].filter(Boolean).join(' · ')}</p>{/if}{#if group.training_schedule}<h4>ΠΡΟΓΡΑΜΜΑ ΠΡΟΠΟΝΗΣΕΩΝ</h4><p class="preserve-lines">{group.training_schedule}</p>{/if}<a class="group-open" href={academyGroupLink(group)}>ΓΝΩΡΙΣΕ ΤΟ ΤΜΗΜΑ <span aria-hidden="true">↗</span></a></div></article>{:else}<p class="public-empty">Δεν έχουν καταχωριστεί ηλικιακά τμήματα.</p>{/each}</div></section><section class="public-section section-dark" id="academy-fixtures"><div class="section-heading"><h2>ΑΓΩΝΕΣ ΑΚΑΔΗΜΙΑΣ</h2></div>{#if data.site?.fixtures.length}<Fixtures fixtures={data.site.fixtures} />{:else}<p class="public-empty">Δεν έχουν καταχωριστεί αγώνες ακαδημίας.</p>{/if}</section>{/if}
</PublicShell>

<style>
  .academy-intro { background:radial-gradient(ellipse at 95% 0,#3e2e1738,transparent 65%),#111; }
  .academy-intro-copy { max-width:850px; padding:18px 0 44px; }
  .academy-intro h1 { margin:0 0 24px; font-size:clamp(42px,5vw,68px); line-height:1.02; }
  .academy-intro em,.academy-explore em { color:var(--public-gold); font-style:normal; }
  .academy-description { max-width:680px; color:#c5c5c5; font-size:18px; line-height:1.65; }
  .academy-actions { display:flex; flex-wrap:wrap; gap:14px; margin-top:30px; }
  .academy-destinations { display:grid; grid-template-columns:repeat(4,minmax(0,1fr)); gap:18px; }
  .academy-destination { display:flex; flex-direction:column; align-items:flex-start; padding:26px; border:1px solid #303030; background:#141414; text-decoration:none; border-radius:6px; }
  .academy-destination:hover { border-color:var(--public-gold); background:#1c1914; }
  .academy-destination :global(svg) { color:var(--public-gold); }
  .academy-destination h3 { font-size:25px; line-height:1.15; margin:24px 0 12px; }
  .academy-destination p { color:#b6b6b6; font-size:15px; line-height:1.65; margin:0 0 24px; }
  .academy-destination > span { display:flex; align-items:center; justify-content:space-between; gap:18px; width:100%; margin-top:auto; padding-top:18px; border-top:1px solid #ffffff18; color:var(--public-gold); font-size:12px; font-weight:600; }
  .public-group-card h3 a { color:inherit; text-decoration:none; }
  .public-group-card h3 a:hover { color:var(--public-gold); }
  .group-open { display:flex; gap:20px; align-items:center; justify-content:space-between; margin-top:24px; padding-top:16px; border-top:1px solid #ffffff24; color:var(--public-gold); font-weight:700; text-decoration:none; }
  .group-open:focus-visible,.public-group-card h3 a:focus-visible { outline:2px solid var(--public-gold); outline-offset:5px; }
  @media(max-width:1199px) { .academy-destinations { grid-template-columns:repeat(2,minmax(0,1fr)); } }
  @media(max-width:599px) { .academy-destinations { grid-template-columns:1fr; } .academy-intro-copy { padding:4px 0 28px; } .academy-description { font-size:16px; } .academy-destination { padding:22px; } }
</style>
