<script lang="ts">
  import PublicImage from './PublicImage.svelte';
  import PlayerStatistics from './PlayerStatistics.svelte';
  import type { PublicSections, SectionPlayer, SectionStaff } from './sections-types';
  let { player, staff, gallery = [], season = null, returnTeamId = null }: {
    player?: SectionPlayer; staff?: SectionStaff; gallery?: PublicSections['gallery'];
    season?: string | null; returnTeamId?: string | null
  } = $props();
  const person = $derived(player ?? staff!);
  const nameParts=$derived(person.name.trim().split(/\s+/));
  const isAcademyPlayer = $derived(player?.organization_id === 'lefteria_fc_academy');
  const clubTeamLabel = $derived(
    player?.team_type === 'First Team' ? 'ΠΡΩΤΗ ΟΜΑΔΑ'
      : player?.team_type === 'Women' ? 'ΓΥΝΑΙΚΕΙΑ ΟΜΑΔΑ'
      : player?.team_type === 'Selected' ? 'ΕΠΙΛΕΓΜΕΝΗ ΟΜΑΔΑ'
      : player?.team_type || 'ΟΜΑΔΑ'
  );
  const teamQuery = $derived(returnTeamId &&
    (player?.organization_id === 'lefteria_fc' && player.team_id === returnTeamId ||
      staff?.organization_id === 'lefteria_fc' && staff.team_ids.includes(returnTeamId))
      ? '&team_id=' + encodeURIComponent(returnTeamId) : '');
  const facts = $derived(player ? [['ΘΕΣΗ', player.position], ['ΗΛΙΚΙΑ', player.age], ['ΕΘΝΙΚΟΤΗΤΑ', player.nationality], ['ΥΨΟΣ', player.height], ['ΒΑΡΟΣ', player.weight], ['ΠΟΔΙ', player.preferred_foot]] : [['ΡΟΛΟΣ', staff?.role], ['ΕΘΝΙΚΟΤΗΤΑ', staff?.nationality]]);
</script>
{#if player}
  <nav class="person-breadcrumbs" aria-label="Διαδρομή προφίλ">
    <a href="/">ΑΡΧΙΚΗ</a><span aria-hidden="true">/</span>
    <a href={isAcademyPlayer ? '/academy/groups' : '/team?tab=roster'+teamQuery}>
      {isAcademyPlayer ? 'ΑΚΑΔΗΜΙΑ' : 'ΠΡΩΤΗ ΟΜΑΔΑ'}
    </a><span aria-hidden="true">/</span>
    <span aria-current="page">{person.name}</span>
  </nav>
{/if}
<section class="person-hero" class:player-profile-hero={!!player} data-testid={player ? "player-hero" : undefined}>
  <div class="person-identity"><p class="public-eyebrow">ΛΕΥΤΕΡΙΑ FC · {player ? (isAcademyPlayer ? 'ΑΚΑΔΗΜΙΑ' : clubTeamLabel) : 'ΤΕΧΝΙΚΟ ΕΠΙΤΕΛΕΙΟ'}</p>{#if player}<span class="person-number" aria-label={`Αριθμός φανέλας ${player.number}`}>{player.number}</span>{/if}<h1 data-testid={player ? 'player-name' : undefined}>
  {#if player}
    {#if nameParts.length>1}<span>{nameParts.slice(0,-1).join(' ')}</span>{/if}
    <span class="person-name-highlight">{nameParts.at(-1)}</span>
  {:else}{person.name}{/if}
</h1>{#if player?.position || staff?.role}<p class="person-role">{player?.position ?? staff?.role}</p>{/if}<a class="public-button public-button-outline" href={isAcademyPlayer ? '/academy/groups' : (player ? '/team?tab=roster' + teamQuery : '/team?tab=staff' + teamQuery)}>← {isAcademyPlayer ? 'ΤΑ ΤΜΗΜΑΤΑ ΤΗΣ ΑΚΑΔΗΜΙΑΣ' : (player ? 'ΟΛΟ ΤΟ ΡΟΣΤΕΡ' : 'ΤΟ ΕΠΙΤΕΛΕΙΟ')}</a></div>
  <div class="person-portrait" data-testid={player ? 'player-hero-photo' : undefined}><PublicImage src={person.image_url} alt={person.name} eager /></div>
  {#if player}
    <aside class="person-stat-bars" data-testid="player-stat-bars" aria-label="Δημοσιευμένα στατιστικά παίκτη">
      {#each [
        {label:'ΣΥΜΜΕΤΟΧΕΣ',value:player.statistics?.appearances,max:Math.max(30,Number(player.statistics?.appearances??0))},
        {label:'ΓΚΟΛ',value:player.statistics?.goals,max:Math.max(15,Number(player.statistics?.goals??0))},
        {label:'ΑΣΙΣΤ',value:player.statistics?.assists,max:Math.max(10,Number(player.statistics?.assists??0))},
        {label:'ΛΕΠΤΑ',value:player.statistics?.minutes_played,max:Math.max(2700,Number(player.statistics?.minutes_played??0))}
      ] as item (item.label)}
        {#if item.value!==null&&item.value!==undefined}
          <div class="person-stat-line">
            <div class="person-stat-label"><span>{item.label}</span><strong>{item.value}</strong></div>
            <div class="person-stat-track" aria-hidden="true">
              <span style:width={Math.min(100,Math.max(0,Math.round(Number(item.value)/item.max*100)))+'%'}></span>
            </div>
          </div>
        {/if}
      {/each}
    </aside>
  {/if}
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
  {#if player}<section id="player-statistics"><h2>ΣΤΑΤΙΣΤΙΚΑ</h2><PlayerStatistics statistics={player.statistics} />{#if !isAcademyPlayer}<a href={'/team?tab=statistics' + teamQuery}>Στατιστικά ομάδας →</a>{/if}</section>{/if}
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

<style>
  .person-breadcrumbs{display:flex;gap:10px;align-items:center;flex-wrap:wrap;
    padding:14px clamp(20px,4vw,58px);font-size:12px;color:#bbb;background:#080909}
  .person-breadcrumbs a{color:var(--public-gold);text-decoration:none}
  .person-breadcrumbs a:focus-visible{outline:2px solid var(--public-gold);outline-offset:4px}
  :global(.public-site) .person-name-highlight{display:block;color:var(--public-gold)}
  /* Source PlayerProfilePage.jsx: photograph / name / published stat rails.
     The underlying shared staff profile remains unchanged. */
  :global(.public-site) .player-profile-hero {
    grid-template-columns:minmax(190px,280px) minmax(0,1fr) minmax(190px,280px);
    align-items:end;gap:clamp(20px,3vw,42px);
    padding:clamp(28px,4vw,54px) clamp(22px,5vw,70px);
    min-height:390px;max-width:100%;background:
      linear-gradient(90deg,#050606eb,#080909bd 55%,#090a0aeb),
      radial-gradient(ellipse at 50% 35%,#5b47243c,transparent 68%),#080909;
  }
  :global(.public-site) .player-profile-hero .person-portrait{
    grid-column:1;grid-row:1;padding:0;background:transparent;align-self:end;
  }
  :global(.public-site) .player-profile-hero .person-portrait :global(img){
    width:100%;height:315px;object-fit:contain;object-position:center bottom;
  }
  :global(.public-site) .player-profile-hero .person-identity{
    grid-column:2;grid-row:1;min-width:0;padding:12px 0 18px;align-items:flex-start;
  }
  :global(.public-site) .player-profile-hero .person-number{
    position:static;z-index:auto;color:var(--public-gold);
    font:800 clamp(48px,5vw,76px)/1 'Fira Sans Extra Condensed',sans-serif;
  }
  :global(.public-site) .player-profile-hero .person-identity h1{
    font:italic 800 clamp(32px,3.4vw,51px)/1.08 'Fira Sans Extra Condensed',sans-serif;
    margin:8px 0 12px;text-transform:uppercase;
  }
  :global(.public-site) .player-profile-hero .person-role{
    margin:0 0 20px;color:var(--public-gold);font-weight:700;font-size:16px;
  }
  :global(.public-site) .player-profile-hero .person-stat-bars{
    grid-column:3;grid-row:1;min-width:0;padding-bottom:24px;
    display:grid;gap:18px;align-self:end;
  }
  .person-stat-line{display:grid;gap:7px}
  .person-stat-label{display:flex;justify-content:space-between;gap:12px;align-items:baseline;color:#d8d8d8;font-size:12px;font-weight:600}
  .person-stat-label strong{color:var(--public-gold)}
  .person-stat-track{height:6px;border-radius:5px;background:#252525;overflow:hidden}
  .person-stat-track>span{display:block;height:100%;background:var(--public-gold);border-radius:5px}
  @media(max-width:1100px){
    :global(.public-site) .player-profile-hero{
      grid-template-columns:minmax(160px,250px) minmax(0,1fr);gap:24px;
    }
    :global(.public-site) .player-profile-hero .person-stat-bars{
      grid-column:1/-1;grid-row:2;grid-template-columns:repeat(2,minmax(0,1fr));padding-bottom:0;
    }
  }
  @media(max-width:650px){
    :global(.public-site) .player-profile-hero{display:flex;flex-direction:column;align-items:stretch;padding:24px 20px;gap:12px}
    :global(.public-site) .player-profile-hero .person-portrait{order:0;align-self:center;width:min(100%,240px)}
    :global(.public-site) .player-profile-hero .person-portrait :global(img){height:270px}
    :global(.public-site) .player-profile-hero .person-identity{order:1;align-items:center;text-align:center;padding:0}
    :global(.public-site) .player-profile-hero .person-identity h1{font-size:clamp(32px,9vw,42px)}
    :global(.public-site) .player-profile-hero .person-stat-bars{order:2;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px;padding:14px 0 0}
  }
</style>
