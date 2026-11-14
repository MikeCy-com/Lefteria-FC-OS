<script lang="ts">
  import PublicShell from '../../../lib/public/PublicShell.svelte';
  import Icon from '../../../lib/public/PublicIcon.svelte';
  import type { PublicPageData } from '../../../lib/public/types';

  let { data }: { data: PublicPageData } = $props();
  const steps = [
    { label: 'Παίκτης', icon: 'user' },
    { label: 'Γονέας', icon: 'users' },
    { label: 'Ιατρικά', icon: 'heart' },
    { label: 'Όροι', icon: 'file' },
    { label: 'Πληρωμή', icon: 'bag' }
  ] as const;
</script>

<svelte:head>
  <title>Έντυπο Εγγραφής | ΛΕΥΤΕΡΙΑ FC</title>
  <meta name="robots" content="noindex" />
</svelte:head>

<PublicShell site={data.site} branding={data.branding} organization="lefteria_fc_academy" navigationGroups={data.navigationGroups}>
  <div class="academy-registration" data-testid="registration-page">
    <div class="registration-wrap">
      <header class="registration-heading">
        <p class="registration-badge">Ακαδημία Ποδοσφαίρου</p>
        <h1>Έντυπο <span>Εγγραφής</span></h1>
        <p>Συμπληρώστε τη φόρμα για να εγγράψετε τον αθλητή στην ακαδημία.</p>
      </header>
      <ol class="registration-steps" aria-label="Βήματα εγγραφής" data-testid="step-indicator">
        {#each steps as step, index (step.label)}
          <li aria-current={index === 0 ? 'step' : undefined}>
            <span class="registration-step-icon"><Icon name={step.icon} size={16} /></span>
            <span>{step.label}</span>
          </li>
        {/each}
      </ol>
      <section class="registration-card" aria-labelledby="registration-player" data-testid="form-card">
        <h2 id="registration-player">Στοιχεία Παίκτη</h2>
        <p>Συμπληρώστε τα βασικά στοιχεία του αθλητή.</p>
        <fieldset disabled aria-describedby="registration-preview-note" data-testid="step-1">
          <legend class="sr-only">Στοιχεία Παίκτη</legend>
          <label>Όνομα <span>*</span><input placeholder="π.χ. Γιώργος" data-testid="player-first-name" /></label>
          <label>Επώνυμο <span>*</span><input placeholder="π.χ. Παπαδόπουλος" data-testid="player-last-name" /></label>
          <label>Ημερομηνία Γέννησης <span>*</span><input type="date" data-testid="player-dob" /></label>
          <label>Φύλο <span>*</span><select data-testid="player-gender"><option>Επιλέξτε...</option><option>Αγόρι</option><option>Κορίτσι</option></select></label>
          <label class="registration-address">Διεύθυνση Κατοικίας <span>*</span><input placeholder="π.χ. Λεωφ. Μακαρίου 25" data-testid="player-address" /></label>
          <label>Πόλη <span>*</span><input placeholder="π.χ. Λευκωσία" data-testid="player-city" /></label>
          <label>Τ.Κ.<input placeholder="π.χ. 1060" data-testid="player-postal" /></label>
        </fieldset>
        <p id="registration-preview-note">Προεπισκόπηση. Δεν συλλέγουμε προσωπικά δεδομένα σε αυτή τη σελίδα.</p>
        <a class="public-button" href="https://lefteriafc.cy/academy/registration">Άνοιγμα επίσημης φόρμας <Icon name="arrow" size={17} /></a>
      </section>
    </div>
  </div>
</PublicShell>
