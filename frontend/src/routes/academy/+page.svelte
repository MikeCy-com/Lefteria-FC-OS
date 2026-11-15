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
  const destinations: { href: string; icon: PublicIconName; title: string; description: string }[] = [
    { href: '/academy/philosophy', icon: 'heart', title: 'Η ΦΙΛΟΣΟΦΙΑ ΜΑΣ', description: 'Γνώρισε τις αξίες και την προσέγγισή μας στην ανάπτυξη νέων παικτών.' },
    { href: '/academy/groups', icon: 'users', title: 'ΗΛΙΚΙΑΚΑ ΤΜΗΜΑΤΑ', description: 'Βρες το τμήμα που ταιριάζει στο παιδί σου και δες το πρόγραμμα προπονήσεών του.' },
    { href: '#academy-fixtures', icon: 'trophy', title: 'ΑΓΩΝΕΣ ΑΚΑΔΗΜΙΑΣ', description: 'Δες το αγωνιστικό πρόγραμμα των τμημάτων της Ακαδημίας μας.' },
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
      <div class="academy-actions"><a class="public-button" href="/academy/groups">ΤΑ ΤΜΗΜΑΤΑ ΜΑΣ <PublicIcon name="arrow" size={18} /></a><a class="public-button public-button-outline" href="/contact">ΜΙΛΗΣΕ ΜΑΖΙ ΜΑΣ <PublicIcon name="arrow" size={18} /></a></div>
    </div>
    <nav class="public-subnav" aria-label="Ακαδημία"><a href="#explore">ΓΝΩΡΙΣΕ ΤΗΝ ΑΚΑΔΗΜΙΑ</a><a href="/academy/groups">ΗΛΙΚΙΑΚΑ ΤΜΗΜΑΤΑ</a><a href="#academy-fixtures">ΑΓΩΝΕΣ</a><a href="/news?organization_id=lefteria_fc_academy">ΝΕΑ ΑΚΑΔΗΜΙΑΣ ↗</a></nav>
  </section>
  <section class="public-section academy-explore" id="explore" aria-labelledby="explore-title"><div class="public-section-heading"><h2 id="explore-title">ΕΞΕΡΕΥΝΗΣΕ ΤΗΝ <em>ΑΚΑΔΗΜΙΑ</em></h2></div>
    <div class="academy-destinations">{#each destinations as item (item.href)}<a href={item.href} class="academy-destination"><PublicIcon name={item.icon} size={28} /><h3>{item.title}</h3><p>{item.description}</p><span>ΠΕΡΙΣΣΟΤΕΡΑ <PublicIcon name="arrow" size={18} /></span></a>{/each}</div>
  </section>
  {#if !data.available}<Unavailable />{:else}<section class="public-section" id="groups" aria-labelledby="academy-groups-title"><div class="section-heading"><h2 id="academy-groups-title">ΗΛΙΚΙΑΚΑ ΤΜΗΜΑΤΑ</h2></div><div class="public-group-grid">{#each data.site?.academy_groups ?? [] as group (group.id)}<article id={`group-${encodeURIComponent(group.id)}`} class="public-group-card">{#if group.banner_url}<PublicImage src={group.banner_url} alt="" />{/if}<div><p class="public-eyebrow">{group.age_range || group.season || 'ΑΚΑΔΗΜΙΑ'}</p><h3><a href={academyGroupLink(group)}>{group.name} <span aria-hidden="true">↗</span></a></h3>{#if group.description}<p>{group.description}</p>{/if}{#if group.city || group.location_name}<p class="group-location">{[group.city, group.location_name].filter(Boolean).join(' · ')}</p>{/if}{#if group.training_schedule}<h4>ΠΡΟΓΡΑΜΜΑ ΠΡΟΠΟΝΗΣΕΩΝ</h4><p class="preserve-lines">{group.training_schedule}</p>{/if}<a class="group-open" href={academyGroupLink(group)}>ΓΝΩΡΙΣΕ ΤΟ ΤΜΗΜΑ <span aria-hidden="true">↗</span></a></div></article>{:else}<p class="public-empty">Δεν έχουν καταχωριστεί ηλικιακά τμήματα.</p>{/each}</div></section><section class="public-section section-dark" id="academy-fixtures" aria-labelledby="academy-fixtures-title"><div class="section-heading"><h2 id="academy-fixtures-title">ΑΓΩΝΕΣ ΑΚΑΔΗΜΙΑΣ</h2></div>{#if data.site?.fixtures.length}<Fixtures fixtures={data.site.fixtures} />{:else}<p class="public-empty">Δεν έχουν καταχωριστεί αγώνες ακαδημίας.</p>{/if}</section>{/if}
  <OrganizationSponsorStrip organization="lefteria_fc_academy" sponsors={data.sections?.sponsors ?? []} />
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
</style>
