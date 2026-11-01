<script lang="ts">
  import type { PublicGroup,PublicSite } from './types';
  import { publicImages } from './images';
  import Icon from './PublicIcon.svelte';
  import PublicImage from './PublicImage.svelte';
  let { site, groups = [] }: { site: PublicSite | null; groups?: PublicGroup[] } = $props();
  const rows = $derived([
    ['academy','ΑΚΑΔΗΜΙΑ','Ηλικιακά τμήματα','/academy',groups.find(group => group.banner_url)?.banner_url || publicImages.academy],
    ['team','ΠΡΩΤΗ ΟΜΑΔΑ','Αγώνες & νέα','/team',site?.teams.find(team => team.banner_url)?.banner_url || site?.club?.stadium_image_url || publicImages.pitch],
    ['schedule','ΠΡΟΓΡΑΜΜΑ','Προπονήσεις & αγώνες','/team?tab=schedule',site?.club?.stadium_image_url || publicImages.academy],
    ['shop','ΚΑΤΑΣΤΗΜΑ','Επίσημα προϊόντα','/shop?tab=merchandise',publicImages.supporter]
  ]);
</script>
<section class="public-destinations" aria-label="Εξερεύνησε τη Λευτεριά" data-testid="public-destinations">{#each rows as [id,title,text,path,image]}<a href={path} class={`public-destination public-destination-${id}`} data-testid={`public-destination-${id}`}><PublicImage src={image} alt="" /><div><h2 data-testid={`public-destination-heading-${id}`}>{title}</h2><p>{text}</p></div><Icon name="arrow" size={24} /></a>{/each}</section>
