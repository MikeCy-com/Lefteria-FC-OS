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
  {#if !data.available}<Unavailable />{:else}<section class="public-section" id="groups" aria-labelledby="academy-groups-title"><div class="section-heading"><h2 id="academy-groups-title">ΗΛΙΚΙΑΚΑ ΤΜΗΜΑΤΑ</h2></div><div class="public-group-grid">{#each data.site?.academy_groups ?? [] as group (group.id)}<article id={`group-${encodeURIComponent(group.id)}`} class="public-group-card">{#if group.banner_url}<PublicImage src={group.banner_url} alt="" />{/if}<div><p class="public-eyebrow">{group.age_range || group.season || 'ΑΚΑΔΗΜΙΑ'}</p><h3><a href={academyGroupLink(group)}>{group.name} <span aria-hidden="true">↗</span></a></h3>{#if group.description}<p>{group.description}</p>{/if}{#if group.city || group.location_name}<p class="group-location">{[group.city, group.location_name].filter(Boolean).join(' · ')}</p>{/if}{#if group.training_schedule}<h4>ΠΡΟΓΡΑΜΜΑ ΠΡΟΠΟΝΗΣΕΩΝ</h4><p class="preserve-lines">{group.training_schedule}</p>{/if}<a class="group-open" href={academyGroupLink(group)}>ΓΝΩΡΙΣΕ ΤΟ ΤΜΗΜΑ <span aria-hidden="true">↗</span></a></div></article>{:else}<p class="public-empty">Δεν έχουν καταχωριστεί ηλικιακά τμήματα.</p>{/each}</div></section><section class="public-section section-dark" id="academy-fixtures" aria-labelledby="academy-fixtures-title"><div class="section-heading"><h2 id="academy-fixtures-title">ΑΓΩΝΕΣ ΑΚΑΔΗΜΙΑΣ</h2></div>{#if data.site?.fixtures.length}<Fixtures fixtures={data.site.fixtures} />{:else}<p class="public-empty">Δεν έχουν καταχωριστεί αγώνες ακαδημίας.</p>{/if}</section>{/if}
</PublicShell>

