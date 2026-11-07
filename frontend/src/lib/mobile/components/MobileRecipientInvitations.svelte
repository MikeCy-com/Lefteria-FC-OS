<script lang="ts">
  import { untrack } from 'svelte';
  import type { components } from '../../api/schema';
  import type { AvailabilityStatus, MobileCalendar, CalendarLoadStatus, MobileCalendarItem } from '../calendar.ts';
  import MobileDelegatedCalendar from './MobileDelegatedCalendar.svelte';
  import './mobile-theme.css';

  type Inbox = components['schemas']['RecipientPendingInvitationsResult'];
  type DelegationAvailability = components['schemas']['FamilyDelegationAvailabilityDto'];
  interface Props {
    inbox: Inbox | null;
    status: 'idle' | 'loading' | 'ready' | 'error';
    error: string | null;
    busy?: boolean;
    onLoad: () => boolean | Promise<boolean>;
    onDecline?: (invitationId: string) => boolean | Promise<boolean>;
    decliningId?: string | null;
    declineError?: string | null;
    onAccept?: (invitationId: string) => boolean | Promise<boolean>;
    acceptingId?: string | null;
    acceptError?: string | null;
    acceptSuccess?: string | null;
    delegationAvailability?: DelegationAvailability | null;
    delegationStatus?: 'idle' | 'loading' | 'ready' | 'error';
    delegationError?: string | null;
    onLoadDelegation?: () => boolean | Promise<boolean>;
    delegatedCalendar?: MobileCalendar | null;
    delegatedCalendarStatus?: CalendarLoadStatus;
    delegatedCalendarError?: string | null;
    delegatedCalendarLoadingMore?: boolean;
    onLoadCalendar?: (month: string, append?: boolean) => boolean | Promise<boolean>;
    onRespondCalendar?: (item: MobileCalendarItem, status: AvailabilityStatus) => boolean | Promise<boolean>;
    calendarRespondingKey?: string | null;
    calendarRespondError?: string | null;
    calendarRespondSuccess?: string | null;
  }

  let { inbox, status, error, busy = false, onLoad,
    onDecline, decliningId = null, declineError = null,
    onAccept, acceptingId = null, acceptError = null, acceptSuccess = null,
    delegationAvailability = null, delegationStatus = 'idle', delegationError = null,
    onLoadDelegation,delegatedCalendar=null,delegatedCalendarStatus='idle',
    delegatedCalendarError=null,delegatedCalendarLoadingMore=false,
    onLoadCalendar,onRespondCalendar,calendarRespondingKey=null,
    calendarRespondError=null,calendarRespondSuccess=null }: Props = $props();
  const id = $props.id();
  let requested = $state(false);
  let delegationRequested = $state(false);
  let confirmingId = $state<string | null>(null);
  let confirmingAcceptId = $state<string | null>(null);

  async function confirmAccept(invitationId: string) {
    if (!onAccept || acceptingId || decliningId) return;
    if (await onAccept(invitationId)) confirmingAcceptId = null;
  }

  async function confirmDecline(invitationId: string) {
    if (!onDecline || decliningId) return;
    if (await onDecline(invitationId)) confirmingId = null;
  }

  // The mobile controller separately authorizes this request using the active
  // OTP-verified FAN session. Never issue a request during SSR.
  $effect(() => {
    if (!requested && status === 'idle' && !busy) {
      requested = true;
      untrack(() => { void onLoad(); });
    }
  });

  $effect(() => {
    if (!delegationRequested && delegationStatus === 'idle' && !busy && onLoadDelegation) {
      delegationRequested = true;
      untrack(() => { void onLoadDelegation(); });
    }
  });

  const label = (organization: string) =>
    organization === 'lefteria_fc_academy' ? 'Ακαδημία Λευτεριά FC' : 'Λευτεριά FC';
  const formatExpiry = (date: string) =>
    new Intl.DateTimeFormat('el-CY', {
      dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Nicosia'
    }).format(new Date(date));
</script>

<section class="recipient-invites" data-testid="recipient-pending-invitations"
  aria-labelledby={id + '-heading'}>
  <div class="recipient-heading">
    <p class="recipient-eyebrow">ΛΟΓΑΡΙΑΣΜΟΣ ΦΙΛΑΘΛΟΥ</p>
    <h2 id={id + '-heading'}>Εκκρεμείς προσκλήσεις</h2>
    <p>Καταχωρίσεις που αφορούν τον επιβεβαιωμένο αριθμό σου.</p>
  </div>

  {#if onLoadDelegation}
    <section class="recipient-delegation-status" aria-label="Κατάσταση οικογενειακής σύνδεσης"
      data-testid="recipient-delegation-status">
      <h3>Οικογενειακή σύνδεση</h3>
      {#if delegationStatus === 'idle' || delegationStatus === 'loading'}
        <p role="status">Έλεγχος σύνδεσης…</p>
      {:else if delegationStatus === 'error'}
        <p role="alert">{delegationError || 'Δεν είναι δυνατός ο έλεγχος σύνδεσης.'}</p>
      {:else if delegationAvailability?.has_active_delegation}
        <p role="status" data-testid="recipient-delegation-active">Υπάρχει ενεργή καταχώριση οικογενειακής σύνδεσης.</p>
        <p>Η πρόσβαση ως αθλητής δεν είναι ακόμη διαθέσιμη.</p>
      {:else}
        <p data-testid="recipient-delegation-inactive">Δεν υπάρχει ενεργή οικογενειακή σύνδεση.</p>
      {/if}
      <button type="button" class="recipient-refresh" disabled={busy || delegationStatus === 'loading'}
        onclick={() => void onLoadDelegation()} data-testid="recipient-delegation-refresh">Έλεγχος ξανά</button>
    </section>
  {/if}

  {#if onLoadCalendar && delegationStatus === 'ready' && delegationAvailability?.has_active_delegation}
    <MobileDelegatedCalendar calendar={delegatedCalendar} status={delegatedCalendarStatus}
      error={delegatedCalendarError} busy={busy}
      loadingMore={delegatedCalendarLoadingMore} onLoad={onLoadCalendar}
      onRespond={onRespondCalendar} respondingKey={calendarRespondingKey}
      respondError={calendarRespondError} respondSuccess={calendarRespondSuccess} />
  {/if}

  {#if status === 'loading' || status === 'idle'}
    <p class="recipient-notice" role="status" data-testid="recipient-invites-loading">
      Έλεγχος εκκρεμών καταχωρίσεων…
    </p>
  {:else if status === 'error'}
    <div class="recipient-notice recipient-error" role="alert" data-testid="recipient-invites-error">
      <p>{error || 'Οι καταχωρίσεις δεν είναι προσωρινά διαθέσιμες.'}</p>
      <button type="button" disabled={busy} onclick={() => void onLoad()}>Δοκιμή ξανά</button>
    </div>
  {:else if inbox && inbox.items.length === 0}
    <p class="recipient-notice" data-testid="recipient-invites-empty">
      Δεν υπάρχουν εκκρεμείς καταχωρίσεις για τον αριθμό σου.
    </p>
  {:else if inbox}
    <div class="recipient-list" data-testid="recipient-invites-list">
      {#each inbox.items as item (item.id)}
        <article class="recipient-card" data-testid="recipient-invite">
          <span class="recipient-card-label">ΣΕ ΑΝΑΜΟΝΗ</span>
          <h3>{label(item.organization_id)}</h3>
          <p>Ισχύει έως {formatExpiry(item.expires_at)}</p>
          <p class="recipient-note">Αυτή η καταχώριση δεν ενεργοποιεί πρόσβαση σε αθλητή.</p>
          {#if onAccept}
            {#if confirmingAcceptId === item.id}
              <div class="recipient-decline-confirm" data-testid="recipient-accept-confirm">
                <p>Να αποδεχτείς την οικογενειακή σύνδεση από {label(item.organization_id)};
                  Δεν δίνει ακόμα πρόσβαση ως αθλητής και μπορεί να ανακληθεί από τον γονέα.</p>
                <div class="recipient-decline-actions">
                  <button type="button" disabled={busy || !!acceptingId || !!decliningId}
                    onclick={() => { confirmingAcceptId = null; }} data-testid="recipient-accept-back">Όχι</button>
                  <button type="button" disabled={busy || !!acceptingId || !!decliningId}
                    onclick={() => void confirmAccept(item.id)} data-testid="recipient-accept-submit">
                    {acceptingId === item.id ? 'Αποδοχή…' : 'Ναι, αποδοχή'}
                  </button>
                </div>
              </div>
            {:else}
              <button type="button" class="recipient-refresh" disabled={busy || !!acceptingId || !!decliningId}
                onclick={() => { confirmingAcceptId = item.id; confirmingId = null; }}
                data-testid="recipient-accept-start">Αποδοχή οικογενειακής σύνδεσης</button>
            {/if}
          {/if}
          {#if onDecline}
            {#if confirmingId === item.id}
              <div class="recipient-decline-confirm" data-testid="recipient-decline-confirm">
                <p>Να απορριφθεί η εκκρεμής καταχώριση από {label(item.organization_id)}; Η ενέργεια δεν δίνει πρόσβαση σε αθλητή.</p>
                <div class="recipient-decline-actions">
                  <button type="button" disabled={busy || !!decliningId}
                    onclick={() => { confirmingId = null; }} data-testid="recipient-decline-back">Όχι</button>
                  <button type="button" disabled={busy || !!decliningId}
                    onclick={() => void confirmDecline(item.id)} data-testid="recipient-decline-submit">
                    {decliningId === item.id ? 'Απόρριψη…' : 'Ναι, απόρριψη'}
                  </button>
                </div>
              </div>
            {:else}
              <button type="button" class="recipient-decline-start"
                disabled={busy || !!decliningId}
                aria-label={'Απόρριψη εκκρεμούς πρόσκλησης από ' + label(item.organization_id)}
                onclick={() => { confirmingId = item.id; }}
                data-testid="recipient-decline-start">Απόρριψη καταχώρισης</button>
            {/if}
          {/if}
        </article>
      {/each}
    </div>
  {/if}

  {#if acceptError}
    <p class="recipient-notice recipient-error" role="alert" data-testid="recipient-accept-error">{acceptError}</p>
  {/if}
  {#if acceptSuccess}
    <p class="recipient-notice" role="status" data-testid="recipient-accept-success">{acceptSuccess}</p>
  {/if}

  {#if declineError}
    <p class="recipient-notice recipient-error" role="alert" data-testid="recipient-decline-error">{declineError}</p>
  {/if}

  <p class="recipient-development" data-testid="recipient-acceptance-unavailable">
    <strong>Under Developments</strong>
    Η σύνδεση δεν δημιουργεί λογαριασμό αθλητή. Η πρόσβαση ως αθλητής δεν είναι ακόμη διαθέσιμη.
    Δεν έχει αποσταλεί SMS από αυτές τις καταχωρίσεις.
  </p>
  <button type="button" class="recipient-refresh" disabled={busy || !!decliningId || !!acceptingId || status === 'loading'}
    onclick={() => void onLoad()} data-testid="recipient-invites-refresh">Ανανέωση</button>
</section>

<style>
  .recipient-invites { max-width: 850px; margin: 18px auto 0; padding: 22px;
    color: #f1f1f1; background: linear-gradient(135deg,#181a1a,#101212);
    border: 1px solid #3c3528; border-radius: 10px; }
  .recipient-heading { margin-bottom: 18px; }
  .recipient-delegation-status { padding: 15px; margin: 0 0 18px; background: #171d1c; border: 1px solid #47544a; border-radius: 8px; }
  .recipient-delegation-status h3 { font-size: 16px; margin: 0 0 8px; }
  .recipient-delegation-status p { font-size: 13px; line-height: 1.6; color: #c7d8ce; margin: 7px 0; }
  .recipient-eyebrow { color: #f5a623; font-size: 10px; font-weight: 700;
    letter-spacing: .1em; margin: 0 0 9px; }
  .recipient-heading h2 { font-size: clamp(21px,4vw,26px); line-height: 1.2; margin: 0 0 7px; }
  .recipient-heading>p:last-child { color: #afb5b6; font-size: 13px; line-height: 1.6; margin: 0; }
  .recipient-notice { background: #131616; border: 1px solid #2c3031; border-radius: 8px;
    padding: 16px; color: #d2d4d4; font-size: 13px; line-height: 1.6; }
  .recipient-error { border-color: #a85c53; }
  .recipient-error>p { margin: 0 0 13px; }
  .recipient-error button,.recipient-refresh { min-height: 44px; padding: 10px 15px;
    background: #2a2115; color: #ffcf62; border: 1px solid #84652f; border-radius: 7px; cursor: pointer; }
  .recipient-list { display: grid; gap: 12px; margin: 16px 0; }
  .recipient-card { padding: 18px; border: 1px solid #645027; border-radius: 10px;
    background: linear-gradient(115deg,#2c2315,#151719); }
  .recipient-card-label { color: #ffcf62; font-size: 10px; letter-spacing: .09em; font-weight: 700; }
  .recipient-card h3 { font-size: 19px; margin: 8px 0; }
  .recipient-card>p { color: #c5c6c4; font-size: 12px; margin: 6px 0; }
  .recipient-card .recipient-note { color: #cbbca1; }
  .recipient-decline-start { display:block; margin-top: 14px; min-height:44px; padding:10px 14px;
    color:#f7b9a3; background:#302018; border:1px solid #84533b; border-radius:7px; cursor:pointer; }
  .recipient-decline-confirm { margin-top:15px; padding:14px; border:1px solid #a45e46; border-radius:8px;
    background:#251a16; }
  .recipient-decline-confirm>p { margin:0 0 12px; color:#eac8bc; font-size:12px; line-height:1.6; }
  .recipient-decline-actions { display:flex; flex-wrap:wrap; gap:10px; }
  .recipient-decline-actions button { min-height:44px; padding:10px 15px; border-radius:7px;
    background:#221e1a; border:1px solid #7a6343; color:#f8dec3; cursor:pointer; }
  .recipient-decline-actions button:last-child { background:#421e19; border-color:#ac614e; color:#ffd5c9; }
  .recipient-decline-start:focus-visible,.recipient-decline-actions button:focus-visible {
    outline:2px solid #ffcf62; outline-offset:3px;
  }
  .recipient-development { margin: 17px 0; padding: 14px; border-top: 1px solid #393d3e;
    color: #b5b6b2; font-size: 12px; line-height: 1.7; }
  .recipient-development strong { display: block; color: #f5b847; margin-bottom: 4px; }
  .recipient-refresh { font-size: 12px; }
  button:focus-visible { outline: 2px solid #ffcf62; outline-offset: 3px; }
  button:disabled { cursor: default; opacity: .6; }
  @media(max-width:620px){.recipient-invites{padding:17px;}}
</style>
