<script lang="ts">
  import { untrack } from 'svelte';
  import type { components } from '../../api/schema';
  import './mobile-theme.css';

  type Family = components['schemas']['ParentFamilyResult'];
  type OutgoingInvitations = components['schemas']['ParentOutgoingInvitationsResult'];

  interface Props {
    family: Family | null;
    status: 'idle' | 'loading' | 'ready' | 'error';
    error: string | null;
    busy?: boolean;
    onLoad: () => boolean | Promise<boolean>;
    onCalendar: (playerId: string) => void | Promise<void>;
    outgoingInvitations?: OutgoingInvitations | null;
    outgoingStatus?: 'idle' | 'loading' | 'ready' | 'error';
    outgoingError?: string | null;
    onLoadOutgoing?: () => boolean | Promise<boolean>;
    onCancelOutgoing?: (invitationId: string) => boolean | Promise<boolean>;
    outgoingCancellingId?: string | null;
    outgoingCancelError?: string | null;
  }

  let { family, status, error, busy = false, onLoad, onCalendar,
    outgoingInvitations = null, outgoingStatus = 'idle', outgoingError = null,
    onLoadOutgoing, onCancelOutgoing, outgoingCancellingId = null,
    outgoingCancelError = null }: Props = $props();
  let confirmingId = $state<string | null>(null);

  async function confirmCancel(id: string) {
    if (!onCancelOutgoing || outgoingCancellingId) return;
    if (await onCancelOutgoing(id)) confirmingId = null;
  }
  const id = $props.id();

  // A newly mounted parent view refreshes the canonical server relationship.
  // No locally cached player or browser-provided child ID is treated as a grant.
  $effect(() => {
    if (status === 'idle' && !busy) untrack(() => { void onLoad(); });
  });

  // Never load outgoing records while the guardian-child source is missing.
  // Recheck the current parent identity and guardian role in the controller.
  $effect(() => {
    if (status === 'ready' && family && outgoingStatus === 'idle' && onLoadOutgoing && !busy) {
      untrack(() => { void onLoadOutgoing(); });
    }
  });

  function organizationLabel(value: string): string {
    return value === 'lefteria_fc_academy' ? 'ΑΚΑΔΗΜΙΑ' : 'ΛΕΥΤΕΡΙΑ FC';
  }
  function teamTypeLabel(value: string): string {
    return value === 'Academy' ? 'Ακαδημία' : 'Πρώτη ομάδα';
  }
</script>

<section class="family-page" aria-labelledby={id + '-title'} data-testid="mobile-family">
  <header class="family-heading">
    <p>ΟΙΚΟΓΕΝΕΙΑ · ΛΕΥΤΕΡΙΑ FC</p>
    <h1 id={id + '-title'}>Τα παιδιά μου</h1>
    <span>Επαληθευμένες συνδέσεις γονέα και αθλητών</span>
  </header>

  {#if status === 'loading' || status === 'idle'}
    <div class="family-notice" role="status" data-testid="family-loading">Φόρτωση οικογενειακών στοιχείων…</div>
  {:else if status === 'error'}
    <div class="family-notice family-error" role="alert" data-testid="family-unavailable">
      <p>{error ?? 'Τα οικογενειακά στοιχεία δεν είναι προσωρινά διαθέσιμα.'}</p>
      <button type="button" disabled={busy} onclick={() => void onLoad()}>Δοκιμή ξανά</button>
    </div>
  {:else if family}
    {#if family.children.length === 0}
      <p class="family-notice" data-testid="family-empty">Δεν υπάρχουν επαληθευμένες συνδέσεις παιδιών για αυτόν τον λογαριασμό.</p>
    {:else}
      <div class="family-children" data-testid="family-children">
        {#each family.children as child (child.id)}
          <article class="family-child" data-testid="family-child">
            <div class="family-child-avatar" aria-hidden="true">{child.name.trim().charAt(0).toLocaleUpperCase('el-GR')}</div>
            <div class="family-child-body">
              <p class="family-organization">{organizationLabel(child.organization_id)}</p>
              <h2>{child.name}</h2>
              <p>{teamTypeLabel(child.team_type)}</p>
              <button type="button" disabled={busy} onclick={() => void onCalendar(child.id)}
                class="family-calendar-link" aria-label={`Πρόγραμμα για ${child.name}`} data-testid="family-child-calendar">
                Το πρόγραμμα <span aria-hidden="true">→</span>
              </button>
            </div>
          </article>
        {/each}
      </div>
    {/if}
    {#if onLoadOutgoing}
      <section class="family-invitations" aria-labelledby={id + '-outgoing'} data-testid="family-outgoing-invitations">
        <h2 id={id + '-outgoing'}>Εκκρεμείς προσκλήσεις</h2>
        {#if outgoingStatus === 'idle' || outgoingStatus === 'loading'}
          <p class="family-outgoing-status" role="status" data-testid="family-outgoing-loading">Έλεγχος εκκρεμών αιτημάτων…</p>
        {:else if outgoingStatus === 'error'}
          <div class="family-outgoing-error" role="alert" data-testid="family-outgoing-error">
            <p>{outgoingError || 'Δεν είναι δυνατή η φόρτωση των προσκλήσεων.'}</p>
            <button type="button" disabled={busy} onclick={() => void onLoadOutgoing()}>Δοκιμή ξανά</button>
          </div>
        {:else if outgoingInvitations}
          {#if outgoingInvitations.items.length === 0}
            <p class="family-outgoing-status" data-testid="family-outgoing-empty">Δεν υπάρχουν εκκρεμείς καταχωρίσεις.</p>
          {:else}
            <div class="family-outgoing-list" data-testid="family-outgoing-list">
              {#each outgoingInvitations.items.filter(item => family.children.some(child => child.id === item.player_id && child.organization_id === item.organization_id)) as item (item.id)}
                {@const child = family.children.find(child => child.id === item.player_id)}
                <article class="family-outgoing-row" data-testid="family-outgoing-row">
                  <strong>{child?.name ?? 'Παιδί'}</strong>
                  <p>Καταχώριση σε αναμονή · {new Intl.DateTimeFormat('el-CY', { dateStyle: 'medium', timeZone: 'Asia/Nicosia' }).format(new Date(item.expires_at))}</p>
                  <span>Δεν έχει αποσταλεί πρόσκληση ή SMS.</span>
                  {#if onCancelOutgoing}
                    {#if confirmingId === item.id}
                      <div class="family-cancel-confirm" data-testid="family-cancel-confirm">
                        <p>Να ακυρωθεί η εκκρεμής καταχώριση για {child?.name ?? 'το παιδί'};</p>
                        <div class="family-cancel-actions">
                          <button type="button" disabled={busy || !!outgoingCancellingId}
                            onclick={() => { confirmingId = null; }} data-testid="family-cancel-back">Όχι</button>
                          <button type="button" disabled={busy || !!outgoingCancellingId}
                            onclick={() => void confirmCancel(item.id)} data-testid="family-cancel-submit">
                            {outgoingCancellingId === item.id ? 'Ακύρωση…' : 'Ναι, ακύρωση'}
                          </button>
                        </div>
                      </div>
                    {:else}
                      <button type="button" class="family-cancel-start"
                        disabled={busy || !!outgoingCancellingId}
                        onclick={() => { confirmingId = item.id; }}
                        aria-label={'Ακύρωση εκκρεμούς πρόσκλησης για ' + (child?.name ?? 'παιδί')}
                        data-testid="family-cancel-start">Ακύρωση καταχώρισης</button>
                    {/if}
                  {/if}
                </article>
              {/each}
            </div>
          {/if}
        {/if}
        {#if outgoingCancelError}
          <div class="family-outgoing-error" role="alert" data-testid="family-cancel-error">{outgoingCancelError}</div>
        {/if}
        <div class="family-invitations-pending">
          <p>Under Developments</p>
          <span>Η αποστολή προσκλήσεων, η επιβεβαίωση τηλεφώνου και η ενεργοποίηση πρόσβασης θα γίνουν διαθέσιμες μετά από τους απαιτούμενους ελέγχους.</span>
        </div>
      </section>
    {:else}
      <div class="family-invitations" data-testid="family-invitations-unavailable">
        <h2>Οικογενειακές ρυθμίσεις</h2>
        <p>Under Developments</p>
        <span>Οι προσκλήσεις, η σύνδεση νέου παιδιού και η ανάκληση πρόσβασης θα ενεργοποιηθούν μετά τον έλεγχο ταυτότητας και ασφάλειας.</span>
      </div>
    {/if}
  {/if}
</section>

<style>
  .family-page { max-width: 850px; margin: 0 auto; color: #f4f4f4; }
  .family-heading { margin: 0 0 26px; }
  .family-heading > p { color: #ffca46; font-size: 11px; font-weight: 700; letter-spacing: .10em; margin: 0 0 8px; }
  .family-heading h1 { font-family: 'Fira Sans Extra Condensed', sans-serif; font-size: clamp(31px, 6vw, 43px); line-height: 1.1; margin: 0 0 8px; }
  .family-heading span { font-size: 14px; color: #adb2b3; }
  .family-notice { padding: 22px; border: 1px solid #303333; border-radius: 8px; background: #141717; color: #c5c8c9; line-height: 1.6; }
  .family-error { border-color: #bc5c4f; }
  .family-error p { margin: 0 0 16px; }
  .family-error button, .family-calendar-link { min-height: 44px; border: 1px solid #a77520; padding: 10px 16px; background: #251d11; color: #ffca46; border-radius: 5px; cursor: pointer; font-weight: 600; }
  .family-error button:disabled, .family-calendar-link:disabled { opacity: .55; cursor: wait; }
  .family-error button:focus-visible, .family-calendar-link:focus-visible { outline: 2px solid #ffca46; outline-offset: 3px; }
  .family-children { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 270px), 1fr)); gap: 14px; }
  .family-child { min-width: 0; display: flex; align-items: start; gap: 15px; padding: 21px; border: 1px solid #38332a; border-radius: 10px; background: linear-gradient(145deg, #1c1c1b, #101313); }
  .family-child-avatar { display: grid; place-items: center; flex: 0 0 48px; width: 48px; height: 48px; background: #332612; border: 1px solid #8b681e; border-radius: 50%; color: #ffca46; font-size: 21px; font-weight: 700; }
  .family-child-body { min-width: 0; flex: 1; overflow-wrap: anywhere; }
  .family-organization { margin: 1px 0 7px; color: #ffca46; font-size: 11px; font-weight: 700; }
  .family-child h2 { font-family: 'Fira Sans Extra Condensed', sans-serif; font-size: 25px; margin: 0 0 7px; }
  .family-child-body > p:not(.family-organization) { font-size: 13px; color: #c2c5c6; margin: 0 0 14px; }
  .family-calendar-link { padding: 10px 13px; font-size: 13px; }
  .family-invitations { margin-top: 22px; padding: 22px; border: 1px solid #343435; border-radius: 8px; background: #121415; }
  .family-invitations h2 { font-size: 20px; margin: 0 0 8px; }
  .family-outgoing-list { display: grid; gap: 10px; margin: 14px 0 20px; }
  .family-outgoing-row { border: 1px solid #4e3b20; background: #211a10; padding: 14px; border-radius: 8px; }
  .family-outgoing-row strong { color: #f6f6f6; font-size: 14px; }
  .family-outgoing-row p { color: #ffca46; font-size: 12px; margin: 7px 0; }
  .family-outgoing-row span { color: #bcb4a7; font-size: 12px; }
  .family-outgoing-status { color: #b8bdbe; font-size: 13px; line-height: 1.65; }
  .family-outgoing-error { border: 1px solid #934840; border-radius: 6px; padding: 12px; }
  .family-outgoing-error button { min-height: 44px; border: 1px solid #a77520; background: #251d11; color: #ffca46; border-radius: 5px; cursor: pointer; padding: 9px 14px; }
  .family-invitations-pending { margin-top: 15px; border-top: 1px solid #303333; padding-top: 16px; }
  .family-cancel-start { display: block; margin-top: 14px; min-height: 44px; border: 1px solid #82513c; border-radius: 6px; background: #302018; padding: 10px 14px; color: #f7b9a3; cursor: pointer; font-size: 12px; }
  .family-cancel-confirm { margin-top: 14px; padding: 12px; border: 1px solid #925844; border-radius: 6px; }
  .family-cancel-confirm p { margin: 0 0 10px; color: #f0d0c5; font-size: 12px; }
  .family-cancel-actions { display: flex; flex-wrap: wrap; gap: 10px; }
  .family-cancel-actions button { min-height: 44px; padding: 9px 14px; border: 1px solid #775c3f; border-radius: 6px; color: #f7e1ba; background: #201c16; cursor: pointer; }
  .family-cancel-actions button:last-child { border-color: #ae5f49; background: #3c1c17; color: #ffd0c5; }
  .family-cancel-actions button:disabled,.family-cancel-start:disabled { opacity: .6; cursor: wait; }
  .family-cancel-start:focus-visible,.family-cancel-actions button:focus-visible { outline: 2px solid #ffca46; outline-offset: 3px; }

  .family-invitations p { margin: 0 0 8px; font-size: 13px; color: #ffca46; font-weight: 700; }
  .family-invitations span { font-size: 13px; color: #a6abac; line-height: 1.65; }
</style>
