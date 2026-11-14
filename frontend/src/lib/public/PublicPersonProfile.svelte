<script lang="ts">
  import PublicImage from './PublicImage.svelte';
  import PlayerStatistics from './PlayerStatistics.svelte';
  import type { PublicSections, SectionPlayer, SectionStaff } from './sections-types';
  let { player, staff, gallery = [], season = null }: { player?: SectionPlayer; staff?: SectionStaff; gallery?: PublicSections['gallery']; season?: string | null } = $props();
  const person = $derived(player ?? staff!);
  const facts = $derived(player ? [['ΘΕΣΗ', player.position], ['ΗΛΙΚΙΑ', player.age], ['ΕΘΝΙΚΟΤΗΤΑ', player.nationality], ['ΥΨΟΣ', player.height], ['ΒΑΡΟΣ', player.weight], ['ΠΟΔΙ', player.preferred_foot]] : [['ΡΟΛΟΣ', staff?.role], ['ΕΘΝΙΚΟΤΗΤΑ', staff?.nationality]]);
</script>
<section class="person-hero">
  <div class="person-identity"><p class="public-eyebrow">ΛΕΥΤΕΡΙΑ FC · {player ? (player.team_type === 'Academy' ? 'ΑΚΑΔΗΜΙΑ' : 'ΠΡΩΤΗ ΟΜΑΔΑ') : 'ΤΕΧΝΙΚΟ ΕΠΙΤΕΛΕΙΟ'}</p>{#if player}<span class="person-number" aria-label={`Αριθμός φανέλας ${player.number}`}>{player.number}</span>{/if}<h1>{person.name}</h1>{#if player?.position || staff?.role}<p class="person-role">{player?.position ?? staff?.role}</p>{/if}<a class="public-button public-button-outline" href={player ? '/team?tab=roster' : '/team?tab=staff'}>← {player ? 'ΟΛΟ ΤΟ ΡΟΣΤΕΡ' : 'ΤΟ ΕΠΙΤΕΛΕΙΟ'}</a></div>
  <div class="person-portrait"><PublicImage src={person.image_url} alt={person.name} eager /></div>
</section>
{#if player}
  <nav class="profile-links" aria-label="Ενότητες προφίλ">
    <a href="#player-overview">Επισκόπηση</a>
    <a href="#player-statistics">Στατιστικά</a>
    {#if player.bio || player.previous_clubs.length}<a href="#player-biography">Βιογραφικό</a>{/if}
    {#if gallery.length}<a href="#player-gallery">Φωτογραφίες</a>{/if}
  </nav>
{/if}
<section id="player-overview" class="public-section person-content">
  <dl class="person-facts">{#if season}<div><dt>Σεζόν</dt><dd>{season}</dd></div>{/if}{#each facts as [label, value]}{#if value !== null && value !== undefined && value !== ''}<div><dt>{label}</dt><dd>{value}</dd></div>{/if}{/each}</dl>
  {#if player}<section id="player-statistics"><h2>ΣΤΑΤΙΣΤΙΚΑ</h2><PlayerStatistics statistics={player.statistics} /><a href="/team?tab=statistics">Στατιστικά πρώτης ομάδας →</a></section>{/if}
  {#if person.bio}<section id={player ? 'player-biography' : undefined} class="person-story"><p class="public-eyebrow">ΤΟ ΠΡΟΦΙΛ</p><h2>ΓΝΩΡΙΣΕ ΤΟΝ {player ? 'ΠΑΙΚΤΗ' : 'ΑΝΘΡΩΠΟ'}</h2><p class="preserve-lines">{person.bio}</p></section>{/if}
  {#if player?.previous_clubs.length}<section id={player.bio ? undefined : 'player-biography'} class="person-story"><h2>ΠΡΟΗΓΟΥΜΕΝΕΣ ΟΜΑΔΕΣ</h2><ul>{#each player.previous_clubs as club}<li>{club.club_name}</li>{/each}</ul></section>{/if}
    {#if gallery.length}
    <section class="person-story" id="player-gallery"><h2>Φωτογραφίες</h2>
      <div class="player-gallery">{#each gallery as photo (photo.id)}
        <figure><PublicImage src={photo.image_url} alt={photo.title} /><figcaption>{photo.title}</figcaption></figure>
      {/each}</div>
    </section>
  {/if}
  {#if staff?.qualifications.length}<section class="person-story"><h2>ΠΡΟΣΟΝΤΑ</h2><ul>{#each staff.qualifications as item}<li>{item}</li>{/each}</ul></section>{/if}
  {#if staff?.badges.length}<section class="person-story"><h2>ΔΙΠΛΩΜΑΤΑ</h2><ul>{#each staff.badges as item}<li>{item}</li>{/each}</ul></section>{/if}
  {#if staff?.coaching_philosophy}<section class="person-story"><h2>ΦΙΛΟΣΟΦΙΑ</h2><p class="preserve-lines">{staff.coaching_philosophy}</p></section>{/if}
  {#if staff?.previous_experience.length}<section class="person-story"><h2>ΕΜΠΕΙΡΙΑ</h2>{#each staff.previous_experience as item}<p>{[item.club,item.role,item.years].filter(Boolean).join(' · ')}</p>{/each}</section>{/if}
</section>
