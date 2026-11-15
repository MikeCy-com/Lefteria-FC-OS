<script lang="ts">
  import PublicShell from '../../../lib/public/PublicShell.svelte';
  import PublicImage from '../../../lib/public/PublicImage.svelte';
  import { academyGroupLink } from '../../../lib/public/navigation';
  import { personPublicLink } from '../../../lib/public/public-urls';
  import { publicImages } from '../../../lib/public/images';
  import type { PublicSectionPageData } from '../../../lib/public/sections-types';
  let { data }: { data: PublicSectionPageData } = $props();
  const groups = $derived((data.site?.academy_groups ?? []).filter(group => group.organization_id === 'lefteria_fc_academy'));
  // Only backend-published public DTOs for a group which also appears in
  // the canonical administrator-approved public group directory.
  const publishedGroupIds = $derived(new Set(groups.map(group => group.id)));
  const publicPlayers = $derived((data.sections?.players ?? []).filter(player =>
    player.organization_id === 'lefteria_fc_academy' &&
    player.team_type === 'Academy' &&
    player.academy_group_ids.some(id => publishedGroupIds.has(id))));
  const groupPlayers = (id:string) => publicPlayers.filter(player => player.academy_group_ids.includes(id));
  const groupCoaches = (id:string) => (data.sections?.staff ?? []).filter(coach =>
    coach.organization_id === 'lefteria_fc_academy' && coach.academy_group_ids.includes(id));

</script>

<svelte:head><title>ΗΛΙΚΙΑΚΑ ΤΜΗΜΑΤΑ | ΛΕΥΤΕΡΙΑ FC</title><meta name="description" content="Τα ηλικιακά τμήματα της ακαδημίας Λευτεριά FC, οι χώροι και τα προγράμματα προπονήσεών τους." /></svelte:head>
<PublicShell site={data.site} branding={data.branding} organization="lefteria_fc_academy" navigationGroups={data.navigationGroups}>
  <section class="academy-intro" data-testid="academy-groups-hero">
    <div class="academy-groups-hero-image"><PublicImage src={publicImages.academy} alt="" eager /></div>
    <div class="academy-groups-hero-copy">
    <p class="public-eyebrow">ΑΚΑΔΗΜΙΑ ΛΕΥΤΕΡΙΑ FC</p><h1>ΜΙΚΡΑ ΒΗΜΑΤΑ.<br /><em>ΜΕΓΑΛΑ ΟΝΕΙΡΑ.</em></h1>
    <p class="intro-copy">Γνώρισε τα τμήματά μας και βρες τις πληροφορίες προπονήσεων και αγώνων για κάθε ηλικιακή ομάδα.</p>
    <nav class="academy-section-links" aria-label="Ακαδημία"><a href="/academy">Η ακαδημία μας</a><a href="/academy/philosophy">Η φιλοσοφία μας</a><a href="/contact">Επικοινωνία</a></nav>
    </div>
  </section>
  <section class="academy-directory" aria-labelledby="groups-heading">
    <div class="directory-heading"><h2 id="groups-heading">ΤΑ ΤΜΗΜΑΤΑ ΜΑΣ</h2><span>{groups.length} {groups.length === 1 ? 'τμήμα' : 'τμήματα'}</span></div>
    <div class="academy-cards">{#each groups as group (group.id)}
      <a class="academy-card" href={academyGroupLink(group)} data-testid="academy-group-card">
        <div class="card-visual">{#if group.banner_url}<PublicImage src={group.banner_url} alt="" />{:else}<span class="group-monogram" aria-hidden="true">{group.name}</span>{/if}<span class="card-arrow" aria-hidden="true">↗</span></div>
        <div class="card-content"><p class="public-eyebrow">{group.age_range || 'ΑΚΑΔΗΜΙΑ'}{group.season ? ` · ${group.season}` : ''}</p><h3>{group.name}</h3>
          {#if group.description}<p class="card-description">{group.description}</p>{/if}
          {#if group.city || group.location_name}<p class="card-location">{[group.city, group.location_name].filter(Boolean).join(' · ')}</p>{/if}
          {#if groupCoaches(group.id).length}
            <p class="card-coaches" data-testid="academy-public-coach">ΠΡΟΠΟΝΗΤΗΣ: {groupCoaches(group.id).map(coach=>coach.name).join(' · ')}</p>
          {/if}
          {#if group.training_schedule}<p class="card-training">{group.training_schedule}</p>{/if}
          <span class="card-action">ΓΝΩΡΙΣΕ ΤΟ ΤΜΗΜΑ <span aria-hidden="true">→</span></span>
        </div>
      </a>
    {:else}<p class="public-empty">Δεν έχουν καταχωριστεί ηλικιακά τμήματα.</p>{/each}</div>
  </section>
  <section class="academy-groups-philosophy public-section" aria-labelledby="academy-groups-philosophy-title" data-testid="academy-groups-philosophy">
    <div class="academy-groups-philosophy-copy">
      <p class="public-eyebrow">ΦΙΛΟΣΟΦΙΑ ΑΝΑΠΤΥΞΗΣ</p>
      <h2 id="academy-groups-philosophy-title">Η ΑΝΑΠΤΥΞΙΑΚΗ ΜΑΣ <em>ΦΙΛΟΣΟΦΙΑ</em></h2>
      <p>Στην Ακαδημία της Λευτεριά FC πιστεύουμε στην ολιστική ανάπτυξη των νέων αθλητών. Η τεχνική κατάρτιση, η τακτική κατανόηση, η φυσική κατάσταση και η ψυχική ανθεκτικότητα εξελίσσονται μαζί.</p>
      <ul>
        <li>Ατομική εξέλιξη πάνω από τα αποτελέσματα σε επίπεδο νέων.</li>
        <li>Μεθοδολογίες προπόνησης προσαρμοσμένες στην ηλικία.</li>
        <li>Ισορροπία εκπαίδευσης και ποδοσφαίρου.</li>
        <li>Σταδιακή πορεία προς την πρώτη ομάδα.</li>
      </ul>
      <a href="/academy/philosophy" class="public-button public-button-outline">ΜΑΘΕ ΤΗ ΦΙΛΟΣΟΦΙΑ ΜΑΣ ↗</a>
    </div>
    <div class="academy-groups-public-players" data-testid="academy-groups-player-previews">
      <h3>ΟΙ ΑΘΛΗΤΕΣ ΤΩΝ ΤΜΗΜΑΤΩΝ ΜΑΣ</h3>
      {#if !data.sectionsAvailable}
        <p class="public-empty" role="status">Τα δημοσιευμένα στοιχεία αθλητών δεν είναι προσωρινά διαθέσιμα.</p>
      {:else}
        {#each groups as group (group.id)}
          {#if groupPlayers(group.id).length}
            <div class="academy-group-preview">
              <div class="academy-group-preview-heading">
                <a href={academyGroupLink(group)}>{group.name}</a>
                <span>{group.age_range ?? ''}</span>
              </div>
              <div class="academy-group-preview-players">
                {#each groupPlayers(group.id).slice(0,6) as player (player.id)}
                  <a href={personPublicLink('player',player)} class="academy-player-preview" data-testid="academy-published-player">
                    <span>{player.position || 'ΑΘΛΗΤΗΣ'}</span>
                    <strong>{player.name}</strong>
                  </a>
                {/each}
              </div>
              {#if groupPlayers(group.id).length > 6}
                <a class="academy-more-group" href={academyGroupLink(group)}>ΟΛΟ ΤΟ ΡΟΣΤΕΡ ↗</a>
              {/if}
            </div>
          {/if}
        {/each}
        {#if !publicPlayers.length}<p class="public-empty">Δεν έχουν δημοσιευτεί αθλητές για τα διαθέσιμα τμήματα.</p>{/if}
      {/if}
    </div>
  </section>
  <section class="academy-groups-registration public-section" data-testid="academy-groups-registration">
    <p class="public-eyebrow">ΑΚΑΔΗΜΙΑ ΛΕΥΤΕΡΙΑ FC</p>
    <h2>ΕΝΤΥΠΟ <em>ΕΓΓΡΑΦΗΣ</em></h2>
    <p>Δες τη διαδικασία εγγραφής και γίνε μέρος της οικογένειας της Ακαδημίας.</p>
    <a href="/academy/registration" class="public-button" data-testid="academy-registration-cta-btn">ΕΓΓΡΑΦΗ ΑΘΛΗΤΗ ↗</a>
  </section>
</PublicShell>


<style>
  .academy-groups-hero-image{position:absolute;inset:0;pointer-events:none;overflow:hidden;z-index:0}
  .academy-groups-hero-image :global(img){width:100%;height:100%;object-fit:cover;opacity:.38}
  .academy-groups-hero-image::after{content:"";position:absolute;inset:0;background:linear-gradient(90deg,#060607ed,#0606078c 72%,#0606075c)}
  .academy-groups-hero-copy{position:relative;z-index:1}
  .card-coaches,.card-training{font-size:13px;line-height:1.55;color:#cacaca;margin:16px 0;overflow-wrap:anywhere}
  .card-coaches{color:#e7c47f;font-weight:700}
  .card-training{border-left:2px solid #e7b644;padding-left:11px;white-space:pre-line}
  .academy-groups-philosophy{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr);gap:clamp(28px,5vw,70px);background:#0b0b0b;border-block:1px solid #4b4027}
  .academy-groups-philosophy h2,.academy-groups-registration h2{font:900 clamp(30px,3.5vw,56px)/1.06 'Fira Sans Extra Condensed',sans-serif;margin:12px 0 25px}
  .academy-groups-philosophy h2 em,.academy-groups-registration h2 em{font-style:normal;color:#f6bb38}
  .academy-groups-philosophy-copy>p:not(.public-eyebrow){color:#dadada;line-height:1.75;font-size:15px;max-width:68ch}
  .academy-groups-philosophy-copy ul{padding:0;list-style:none;display:grid;gap:15px;margin:26px 0}
  .academy-groups-philosophy-copy li{padding-left:17px;border-left:3px solid #f6bb38;color:#eee;font-size:15px;line-height:1.55}
  .academy-groups-public-players h3{font:800 25px/1.15 'Fira Sans Extra Condensed',sans-serif;margin-bottom:18px}
  .academy-group-preview{border-top:1px solid #5d4b2b;padding:15px 0 20px}
  .academy-group-preview-heading{display:flex;flex-wrap:wrap;gap:10px;align-items:center;margin-bottom:14px}
  .academy-group-preview-heading a{color:#f6bb38;font:800 22px 'Fira Sans Extra Condensed',sans-serif}
  .academy-group-preview-heading span{font-size:12px;color:#aaa}
  .academy-group-preview-players{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:9px}
  .academy-player-preview{display:flex;flex-direction:column;gap:7px;min-height:76px;padding:12px;border:1px solid #393939;background:#161616;text-decoration:none;overflow-wrap:anywhere}
  .academy-player-preview span{font-size:11px;color:#f6bb38}
  .academy-player-preview strong{font-size:14px;color:#fff}
  .academy-player-preview:hover,.academy-player-preview:focus-visible{border-color:#f6bb38}
  .academy-more-group{display:inline-block;margin-top:15px;color:#e9be52;font-size:13px;font-weight:800}
  .academy-groups-registration{text-align:center;background:#101010;border-top:1px solid #4d3c1e}
  .academy-groups-registration p:not(.public-eyebrow){color:#d1d1d1;line-height:1.6;margin:0 auto 22px;max-width:60ch}
  .academy-groups-registration .public-button{display:inline-flex;min-height:48px;align-items:center}
  @media(max-width:900px){.academy-groups-philosophy{grid-template-columns:1fr}}
  @media(max-width:520px){.academy-groups-philosophy,.academy-groups-registration{padding:28px 16px}.academy-group-preview-players{grid-template-columns:repeat(2,minmax(0,1fr))}}
</style>
