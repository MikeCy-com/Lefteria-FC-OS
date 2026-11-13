<script lang="ts">
  import PublicImage from './PublicImage.svelte';
  import PlayerStatistics from './PlayerStatistics.svelte';
  import type { SectionPlayer, SectionStaff } from './sections-types';
  let { player, staff }: { player?: SectionPlayer; staff?: SectionStaff } = $props();
  const person = $derived(player ?? staff!);
  const facts = $derived(player ? [['ΘΕΣΗ', player.position], ['ΗΛΙΚΙΑ', player.age], ['ΕΘΝΙΚΟΤΗΤΑ', player.nationality], ['ΥΨΟΣ', player.height], ['ΒΑΡΟΣ', player.weight], ['ΠΟΔΙ', player.preferred_foot]] : [['ΡΟΛΟΣ', staff?.role], ['ΕΘΝΙΚΟΤΗΤΑ', staff?.nationality]]);
</script>
<nav class="person-breadcrumb" aria-label="Διαδρομή σελίδας"><a href="/">ΑΡΧΙΚΗ</a><span>/</span><a href="/team">ΠΡΩΤΗ ΟΜΑΔΑ</a><span aria-hidden="true">/</span><a href={player ? '/team?tab=roster' : '/team?tab=staff'}>{player ? 'ΡΟΣΤΕΡ' : 'ΤΕΧΝΙΚΟ ΕΠΙΤΕΛΕΙΟ'}</a><span aria-hidden="true">/</span><span aria-current="page">{person.name}</span></nav>
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
<style>
  .person-breadcrumb{display:flex;flex-wrap:wrap;gap:10px;padding:24px 40px;font-size:13px;line-height:1.5;letter-spacing:.02em;color:#ddd;border-bottom:1px solid #ffffff15}.person-breadcrumb a{color:inherit}.person-breadcrumb a:hover{color:#f5a623}.person-breadcrumb [aria-current="page"]{color:#fff;font-weight:600;overflow-wrap:anywhere}
  .person-hero{display:grid;grid-template-columns:1.1fr 1fr;min-height:530px;background:radial-gradient(ellipse at right,#66502244,transparent 70%),#111;border-bottom:1px solid #ffffff1a}
  .person-identity{position:relative;padding:60px 40px;display:flex;flex-direction:column;align-items:flex-start;justify-content:center;isolation:isolate;overflow:hidden}.person-number{font-size:230px;font-weight:900;line-height:1;color:#f5a6231a;position:absolute;right:20px;top:20px;z-index:-1}
  h1{font-size:clamp(38px,5.3vw,80px);line-height:1.02;max-width:650px;margin:20px 0;overflow-wrap:anywhere}.person-role{font-size:19px;color:#ccc;margin:0 0 34px}.person-portrait{padding:25px;display:grid;place-items:center;background:linear-gradient(140deg,transparent,#f5a6230e)}.person-portrait :global(img){height:480px;width:100%;object-fit:contain;object-position:bottom}.person-portrait :global(img[data-fallback='true']){max-width:240px;height:240px;align-self:center}
  .person-content{max-width:1400px}.person-facts{display:flex;flex-wrap:wrap;gap:35px;border-bottom:1px solid #ffffff20;padding-bottom:32px;margin:0 0 38px}.person-facts div{min-width:100px;min-height:48px}dt{font-size:13px;line-height:1.5;color:#c7c7c7;letter-spacing:.03em}dd{font-size:19px;font-weight:600;margin:8px 0 0}.person-story{max-width:850px;margin-top:48px}.person-story p,.person-story li{font-size:17px;line-height:1.7;color:#d5d5d5;overflow-wrap:anywhere}
  @media(max-width:700px){.person-breadcrumb{padding:20px}.person-hero{grid-template-columns:1fr;min-height:0}.person-identity{padding:40px 22px}.person-number{font-size:180px}.person-portrait{padding:0 25px}.person-portrait :global(img){height:300px}.person-facts{gap:20px}.person-facts div{flex:1 1 125px}.person-story p,.person-story li{font-size:16px}}
</style>
