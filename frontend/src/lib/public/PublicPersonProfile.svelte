<script lang="ts">
  import PublicImage from './PublicImage.svelte';
  import PlayerStatistics from './PlayerStatistics.svelte';
  import type { SectionPlayer, SectionStaff } from './sections-types';
  let { player, staff }: { player?: SectionPlayer; staff?: SectionStaff } = $props();
  const person = $derived(player ?? staff!);
  const facts = $derived(player ? [['ΘΕΣΗ', player.position], ['ΗΛΙΚΙΑ', player.age], ['ΕΘΝΙΚΟΤΗΤΑ', player.nationality], ['ΥΨΟΣ', player.height], ['ΒΑΡΟΣ', player.weight], ['ΠΟΔΙ', player.preferred_foot]] : [['ΡΟΛΟΣ', staff?.role], ['ΕΘΝΙΚΟΤΗΤΑ', staff?.nationality]]);
</script>
<section class="person-hero">
  <div class="person-identity"><p class="public-eyebrow">ΛΕΥΤΕΡΙΑ FC · {player ? 'ΠΡΩΤΗ ΟΜΑΔΑ' : 'ΤΕΧΝΙΚΟ ΕΠΙΤΕΛΕΙΟ'}</p>{#if player}<span class="person-number" aria-label={`Αριθμός φανέλας ${player.number}`}>{player.number}</span>{/if}<h1>{person.name}</h1>{#if player?.position || staff?.role}<p class="person-role">{player?.position ?? staff?.role}</p>{/if}<a class="public-button public-button-outline" href={player ? '/team?tab=roster' : '/team?tab=staff'}>← {player ? 'ΟΛΟ ΤΟ ΡΟΣΤΕΡ' : 'ΤΟ ΕΠΙΤΕΛΕΙΟ'}</a></div>
  <div class="person-portrait"><PublicImage src={person.image_url} alt={person.name} eager /></div>
</section>
<section class="public-section person-content">
  <dl class="person-facts">{#each facts as [label, value]}{#if value !== null && value !== undefined && value !== ''}<div><dt>{label}</dt><dd>{value}</dd></div>{/if}{/each}</dl>
  {#if player}<h2>ΣΤΑΤΙΣΤΙΚΑ</h2><PlayerStatistics statistics={player.statistics} />{/if}
  {#if person.bio}<section class="person-story"><p class="public-eyebrow">ΤΟ ΠΡΟΦΙΛ</p><h2>ΓΝΩΡΙΣΕ ΤΟΝ {player ? 'ΠΑΙΚΤΗ' : 'ΑΝΘΡΩΠΟ'}</h2><p class="preserve-lines">{person.bio}</p></section>{/if}
  {#if player?.previous_clubs.length}<section class="person-story"><h2>ΠΡΟΗΓΟΥΜΕΝΕΣ ΟΜΑΔΕΣ</h2><ul>{#each player.previous_clubs as club}<li>{club.club_name}</li>{/each}</ul></section>{/if}
  {#if staff?.qualifications.length}<section class="person-story"><h2>ΠΡΟΣΟΝΤΑ</h2><ul>{#each staff.qualifications as item}<li>{item}</li>{/each}</ul></section>{/if}
  {#if staff?.badges.length}<section class="person-story"><h2>ΔΙΠΛΩΜΑΤΑ</h2><ul>{#each staff.badges as item}<li>{item}</li>{/each}</ul></section>{/if}
  {#if staff?.coaching_philosophy}<section class="person-story"><h2>ΦΙΛΟΣΟΦΙΑ</h2><p class="preserve-lines">{staff.coaching_philosophy}</p></section>{/if}
  {#if staff?.previous_experience.length}<section class="person-story"><h2>ΕΜΠΕΙΡΙΑ</h2>{#each staff.previous_experience as item}<p>{[item.club,item.role,item.years].filter(Boolean).join(' · ')}</p>{/each}</section>{/if}
</section>
