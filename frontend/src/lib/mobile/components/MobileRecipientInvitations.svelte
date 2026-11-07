<script lang="ts">
  import { untrack } from 'svelte';
  import type { components } from '../../api/schema';
  import './mobile-theme.css';

  type Inbox = components['schemas']['RecipientPendingInvitationsResult'];
  interface Props {
    inbox: Inbox | null;
    status: 'idle' | 'loading' | 'ready' | 'error';
    error: string | null;
    busy?: boolean;
    onLoad: () => boolean | Promise<boolean>;
    onDecline?: (invitationId: string) => boolean | Promise<boolean>;
    decliningId?: string | null;
    declineError?: string | null;
  }

  let { inbox, status, error, busy = false, onLoad, onDecline,
    decliningId = null, declineError = null }: Props = $props();
  let confirmingId = $state<string | null>(null);
  async function confirmDecline(id: string) {
    if (!onDecline || decliningId) return;
    if (await onDecline(id)) confirmingId = null;
  }
  const id = $props.id();
  let requested = $state(false);

  // The mobile controller separately authorizes this request using the active
  // OTP-verified FAN session. Never issue a request during SSR.
  $effect(() => {
    if (!requested && status === 'idle' && !busy) {
      requested = true;
      untrack(() => { void onLoad(); });
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
          {#if onDecline}
            {#if confirmingId === item.id}
              <div class="recipient-decline-confirm" data-testid="recipient-decline-confirm">
                <p>Να απορριφθεί αυτή η εκκρεμής καταχώριση;</p>
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
                onclick={() => { confirmingId = item.id; }}
                aria-label={'Απόρριψη πρόσκλησης για ' + label(item.organization_id)}
                data-testid="recipient-decline-start">Απόρριψη καταχώρισης</button>
            {/if}
          {/if}
        </article>
      {/each}
    </div>
  {/if}

  {#if declineError}
    <p class="recipient-notice recipient-error" role="alert" data-testid="recipient-decline-error">{declineError}</p>
  {/if}
  <p class="recipient-development" data-testid="recipient-acceptance-unavailable">
    <strong>Under Developments</strong>
    Η αποστολή πρόσκλησης, η αποδοχή και η ενεργοποίηση πρόσβασης δεν είναι ακόμη διαθέσιμες.
    Δεν έχει αποσταλεί SMS από αυτές τις καταχωρίσεις.
  </p>
  <button type="button" class="recipient-refresh" disabled={busy || status === 'loading'}
    onclick={() => void onLoad()} data-testid="recipient-invites-refresh">Ανανέωση</button>
</section>

<style>
  .recipient-invites { max-width: 850px; margin: 18px auto 0; padding: 22px;
    color: #f1f1f1; background: linear-gradient(135deg,#181a1a,#101212);
    border: 1px solid #3c3528; border-radius: 10px; }
  .recipient-heading { margin-bottom: 18px; }
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
  .recipient-development { margin: 17px 0; padding: 14px; border-top: 1px solid #393d3e;
    color: #b5b6b2; font-size: 12px; line-height: 1.7; }
  .recipient-development strong { display: block; color: #f5b847; margin-bottom: 4px; }
  .recipient-refresh { font-size: 12px; }
  .recipient-decline-start { min-height:44px; margin-top:12px; padding:9px 13px; border:1px solid #91594a; border-radius:7px; background:#2f1b16; color:#ffd2c4; cursor:pointer; }
  .recipient-decline-confirm { margin-top:13px; padding:13px; border:1px solid #aa654e; border-radius:7px; }
  .recipient-decline-confirm>p { margin:0 0 10px; color:#f0cabc; font-size:13px; }
  .recipient-decline-actions { display:flex; flex-wrap:wrap; gap:10px; }
  .recipient-decline-actions button { min-height:44px; padding:9px 13px; border:1px solid #795f45; background:#211d17; color:#f9d8a6; border-radius:6px; cursor:pointer; }
  .recipient-decline-actions button:last-child { border-color:#a75b48; background:#402019; color:#ffd5cb; }
  button:focus-visible { outline: 2px solid #ffcf62; outline-offset: 3px; }
  button:disabled { cursor: default; opacity: .6; }
  @media(max-width:620px){.recipient-invites{padding:17px;}}
</style>
