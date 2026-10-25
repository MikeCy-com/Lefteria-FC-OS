<script lang="ts">
  import { tick } from 'svelte';
  import { ROLE_NAMES } from '../navigation.ts';
  import type { MobileRole } from '../navigation.ts';
  import type { FanRegistrationForm, MobileIdentityOption, MobileLoginStep, RememberDays, UiAction } from './component-types.ts';
  import MobileIcon from './MobileIcon.svelte';
  import './mobile-theme.css';

  interface Props {
    step?: MobileLoginStep;
    phone?: string;
    rememberDays?: RememberDays;
    identities?: readonly MobileIdentityOption[];
    detectedRole?: MobileRole | null;
    busy?: boolean;
    error?: string | null;
    resendSeconds?: number;
    onRequestOtp: (phone: string) => UiAction;
    onVerifyOtp: (code: string, rememberDays: RememberDays) => UiAction;
    onChooseRole: (role: MobileRole, rememberDays: RememberDays) => UiAction;
    onRegisterFan: (registration: FanRegistrationForm) => UiAction;
    onStepChange: (step: MobileLoginStep) => void;
  }

  let {
    step = 'phone', phone = $bindable(''), rememberDays = $bindable<RememberDays>(null),
    identities = [], detectedRole = null, busy = false, error = null, resendSeconds = 0,
    onRequestOtp, onVerifyOtp, onChooseRole, onRegisterFan, onStepChange
  }: Props = $props();
  const id = $props.id();
  let code = $state('');
  let firstName = $state('');
  let lastName = $state('');
  let birthday = $state('');
  let submitting = $state(false);
  let phoneInput = $state<HTMLInputElement>();
  let otpInput = $state<HTMLInputElement>();
  let firstNameInput = $state<HTMLInputElement>();
  let roleHeading = $state<HTMLHeadingElement>();
  const isBusy = $derived(busy || submitting);

  $effect(() => {
    const current = step;
    void tick().then(() => {
      if (current !== step) return;
      if (current === 'phone') phoneInput?.focus();
      if (current === 'otp') otpInput?.focus();
      if (current === 'fan_register') firstNameInput?.focus();
      if (current === 'choose_role') roleHeading?.focus();
    });
  });

  async function run(action: () => UiAction) {
    if (isBusy) return;
    submitting = true;
    try { await action(); } finally { submitting = false; }
  }

  function changeStep(next: MobileLoginStep) {
    if (isBusy) return;
    code = '';
    onStepChange(next);
  }

  function selectRemember(value: string) {
    rememberDays = value === '30' ? 30 : value === '60' ? 60 : value === '90' ? 90 : null;
  }
</script>

{#snippet rememberChoice()}
  <div class="remember-choice">
    <label for={`${id}-remember`}>Διάρκεια σύνδεσης</label>
    <select id={`${id}-remember`} value={rememberDays === null ? '' : String(rememberDays)}
      onchange={(event) => selectRemember(event.currentTarget.value)} disabled={isBusy} data-testid="remember-days">
      <option value="">24 ώρες</option><option value="30">30 ημέρες</option>
      <option value="60">60 ημέρες</option><option value="90">90 ημέρες</option>
    </select>
    <p>Επιλέξτε μεγαλύτερη διάρκεια μόνο σε προσωπική συσκευή.</p>
  </div>
{/snippet}

<section class="lf-mobile-theme login-page" aria-busy={isBusy} data-testid="mobile-login">
  <div class="login-content">
    <header class="brand">
      <img src="/mobile/club-logo.png" alt="" width="112" height="112" />
      <h1>ΛΕΥΤΕΡΙΑ FC</h1>
      <p>Μία Ομάδα <span aria-hidden="true">•</span> Ένα Όνειρο</p>
    </header>

    <div class="login-card">
      {#if error}<p class="error" role="alert" id={`${id}-error`} data-testid="login-error">{error}</p>{/if}

      {#if step === 'phone'}
        <form onsubmit={(event) => { event.preventDefault(); void run(() => onRequestOtp(phone.trim())); }}
          aria-describedby={error ? `${id}-error` : undefined} data-testid="phone-step">
          <fieldset disabled={isBusy}>
            <label for={`${id}-phone`}>Αριθμός τηλεφώνου</label>
            <div class="phone-field">
              <MobileIcon name="phone" size={19} />
              <input bind:this={phoneInput} id={`${id}-phone`} type="tel" autocomplete="tel" bind:value={phone}
                placeholder="+357 99 123456" required maxlength="40" aria-describedby={`${id}-phone-help`} data-testid="phone-input" />
            </div>
            <p class="hint" id={`${id}-phone-help`}>Εισάγετε τον αριθμό που είναι καταχωρημένος στη Λευτεριά FC.</p>
            {@render rememberChoice()}
            <button class="primary" type="submit" data-testid="request-otp-btn">
              {isBusy ? 'Αποστολή…' : 'Αποστολή κωδικού'}<MobileIcon name="arrow" size={20} />
            </button>
          </fieldset>
          <div class="signup-prompt">
            <p>Δεν είσαι μέλος της Λευτεριά FC;</p>
            <button type="button" class="text-button gold" disabled={isBusy} onclick={() => changeStep('fan_register')} data-testid="fan-register-link">Εγγράψου τώρα →</button>
          </div>
        </form>
      {:else if step === 'fan_register'}
        <form onsubmit={(event) => { event.preventDefault(); void run(() => onRegisterFan({ firstName: firstName.trim(), lastName: lastName.trim(), phone: phone.trim(), birthday })); }} data-testid="fan-register-step">
          <h2>Γίνε φίλος της Λευτεριάς</h2>
          <p class="intro">Νέα, αγώνες, προβλέψεις και περισσότερα.</p>
          <fieldset disabled={isBusy}>
            <label for={`${id}-first-name`}>Όνομα</label>
            <input bind:this={firstNameInput} id={`${id}-first-name`} autocomplete="given-name" bind:value={firstName} required maxlength="100" data-testid="fan-first-name-input" />
            <label for={`${id}-last-name`}>Επώνυμο</label>
            <input id={`${id}-last-name`} autocomplete="family-name" bind:value={lastName} required maxlength="100" data-testid="fan-last-name-input" />
            <label for={`${id}-fan-phone`}>Τηλέφωνο</label>
            <input id={`${id}-fan-phone`} type="tel" autocomplete="tel" bind:value={phone} placeholder="+357 99 123456" required maxlength="40" data-testid="fan-phone-input" />
            <label for={`${id}-birthday`}>Ημερομηνία γέννησης</label>
            <input id={`${id}-birthday`} type="date" autocomplete="bday" bind:value={birthday} required data-testid="fan-birthday-input" />
            <button class="primary" type="submit" data-testid="fan-register-submit">{isBusy ? 'Εγγραφή…' : 'Εγγραφή'}<MobileIcon name="arrow" size={20} /></button>
          </fieldset>
          <button type="button" class="text-button back" disabled={isBusy} onclick={() => changeStep('phone')} data-testid="fan-back-btn"><MobileIcon name="back" size={17} />Πίσω στη σύνδεση</button>
        </form>
      {:else if step === 'otp'}
        <form onsubmit={(event) => { event.preventDefault(); void run(() => onVerifyOtp(code, rememberDays)); }} data-testid="otp-step">
          <div class="step-icon"><MobileIcon name="shield" size={25} /></div>
          <h2>Εισάγετε τον κωδικό</h2>
          <p class="intro">Στάλθηκε στο <strong>{phone}</strong></p>
          {#if detectedRole}<span class="role-pill">{ROLE_NAMES[detectedRole]}</span>{/if}
          <fieldset disabled={isBusy}>
            <label class="code-label" for={`${id}-code`}>Εξαψήφιος κωδικός SMS</label>
            <input bind:this={otpInput} id={`${id}-code`} class="otp-code" type="text" inputmode="numeric" autocomplete="one-time-code"
              value={code} oninput={(event) => { code = event.currentTarget.value.replace(/[^0-9]/g, '').slice(0, 6); event.currentTarget.value = code; }}
              pattern="[0-9]{6}" minlength="6" maxlength="6" required data-testid="otp-input" />
            {@render rememberChoice()}
            <button class="primary" type="submit" data-testid="verify-otp-btn">{isBusy ? 'Επαλήθευση…' : 'Σύνδεση'}<MobileIcon name="arrow" size={20} /></button>
          </fieldset>
          <div class="otp-actions">
            <button type="button" class="text-button" disabled={isBusy} onclick={() => changeStep('phone')} data-testid="back-to-phone">Αλλαγή αριθμού</button>
            {#if resendSeconds > 0}<span aria-live="polite">Νέος κωδικός σε {resendSeconds}s</span>
            {:else}<button type="button" class="text-button gold" disabled={isBusy} onclick={() => { code = ''; void run(() => onRequestOtp(phone.trim())); }} data-testid="resend-otp">Αποστολή ξανά</button>{/if}
          </div>
        </form>
      {:else}
        <div data-testid="choose-role-step">
          <div class="step-icon"><MobileIcon name="shield" size={25} /></div>
          <h2 bind:this={roleHeading} tabindex="-1">Πώς θέλεις να συνδεθείς;</h2>
          <p class="intro">Επίλεξε έναν από τους διαθέσιμους ρόλους σου.</p>
          {@render rememberChoice()}
          <div class="identity-options">
            {#each identities as identity (identity.role)}
              <button class="identity-option" type="button" disabled={isBusy} onclick={() => void run(() => onChooseRole(identity.role, rememberDays))} data-testid={`choose-role-${identity.role}`}>
                <span><strong>{ROLE_NAMES[identity.role]}</strong><small>{identity.name}</small></span><MobileIcon name="arrow" size={19} />
              </button>
            {/each}
          </div>
          <button type="button" class="text-button back" disabled={isBusy} onclick={() => changeStep('otp')} data-testid="choose-role-back"><MobileIcon name="back" size={17} />Πίσω</button>
        </div>
      {/if}
    </div>
  </div>
  <footer>ΛΕΥΤΕΡΙΑ FC © {new Date().getFullYear()}</footer>
</section>

<style>
  .login-page { min-height:100dvh; display:flex; flex-direction:column; padding:env(safe-area-inset-top,0px) 24px env(safe-area-inset-bottom,0px); background:radial-gradient(ellipse at 15% 0%,#f5a62312,transparent 42%),radial-gradient(ellipse at 100% 100%,#174b3020,transparent 45%),var(--lf-bg); }
  .login-content { width:100%; max-width:380px; flex:1; align-content:center; margin:0 auto; padding:40px 0 28px; }
  .brand { text-align:center; margin-bottom:32px; }
  .brand img { display:block; object-fit:contain; margin:0 auto 16px; filter:drop-shadow(0 7px 20px #f5a62328); }
  .brand h1 { margin:0; font-size:36px; line-height:1.1; letter-spacing:.045em; }
  .brand p { margin:10px 0 0; color:var(--lf-gold); font-size:11px; font-weight:600; text-transform:uppercase; letter-spacing:.24em; }
  .brand p span { color:#626262; padding:0 6px; }
  .login-card { width:100%; }
  h2 { margin:0 0 6px; text-align:center; font-size:25px; line-height:1.2; }
  .intro { margin:0 0 22px; text-align:center; color:var(--lf-muted); font-size:13px; }
  .intro strong { color:#fff; font-weight:500; }
  fieldset { min-width:0; margin:0; padding:0; border:0; }
  label { display:block; margin:17px 0 7px; color:#c3c5c7; font-size:13px; }
  input,select { width:100%; min-height:49px; padding:12px 14px; border:1px solid var(--lf-line); border-radius:8px; background:#151718; color:#fff; }
  input::placeholder { color:#696e72; }
  .phone-field { position:relative; }
  .phone-field :global(svg) { position:absolute; top:15px; left:14px; color:#8f9498; pointer-events:none; }
  .phone-field input { padding-left:43px; font-size:18px; }
  .hint,.remember-choice p { margin:7px 0 0; color:#8b9094; font-size:11px; line-height:1.5; }
  .remember-choice { margin:21px 0; }
  .remember-choice label { margin-top:0; }
  .remember-choice select { min-height:44px; padding:9px 12px; font-size:13px; }
  button { cursor:pointer; }
  .primary { display:flex; width:100%; min-height:49px; align-items:center; justify-content:center; gap:10px; margin-top:22px; border:1px solid var(--lf-gold); border-radius:8px; background:linear-gradient(110deg,#ffcd47,#f5a623); color:#090909; font-size:15px; font-weight:700; }
  .primary:hover:not(:disabled) { background:var(--lf-gold-light); }
  .text-button { min-height:44px; padding:8px 0; border:0; background:transparent; color:#b0b4b6; font-size:13px; }
  .text-button:hover:not(:disabled) { color:#fff; }
  .text-button.gold { color:var(--lf-gold); }
  .signup-prompt { margin-top:25px; padding-top:18px; border-top:1px solid #202223; text-align:center; }
  .signup-prompt p { margin:0; color:#92979a; font-size:12px; }
  .signup-prompt button { font-weight:600; }
  .error { margin:0 0 20px; padding:12px 14px; border:1px solid #ef444440; border-radius:8px; background:#ef444411; color:#fca5a5; font-size:13px; line-height:1.5; overflow-wrap:anywhere; }
  .step-icon { display:grid; width:52px; height:52px; place-items:center; margin:0 auto 16px; border-radius:50%; background:#f5a62314; color:var(--lf-gold); }
  .role-pill { display:block; width:fit-content; margin:-9px auto 22px; padding:4px 12px; border-radius:20px; background:#f5a62314; color:var(--lf-gold); font-size:12px; }
  .code-label { text-align:center; margin-top:22px; }
  .otp-code { height:60px; text-align:center; padding:8px 0 8px .48em; font-family:var(--lf-heading); font-size:32px; letter-spacing:.48em; }
  .otp-actions { display:flex; gap:12px; align-items:center; justify-content:space-between; margin-top:10px; }
  .otp-actions span { color:#8b9094; font-size:12px; }
  .back { display:flex; align-items:center; justify-content:center; gap:7px; width:100%; margin-top:10px; }
  .identity-options { display:grid; gap:10px; }
  .identity-option { display:flex; align-items:center; justify-content:space-between; gap:12px; width:100%; min-height:76px; padding:14px 16px; border:1px solid var(--lf-line); border-radius:8px; background:linear-gradient(120deg,#151819,#101112); color:var(--lf-gold); text-align:left; }
  .identity-option:hover:not(:disabled) { border-color:var(--lf-gold); background:#1a1c1d; }
  .identity-option strong { display:block; color:#fff; font-size:14px; font-weight:600; }
  .identity-option small { display:block; margin-top:3px; color:var(--lf-muted); font-size:12px; }
  footer { padding:18px 0 22px; text-align:center; color:#686d70; font-size:11px; }
  @media (max-width:360px) { .login-page{padding-right:18px;padding-left:18px}.brand img{width:96px;height:96px}.brand{margin-bottom:24px}.login-content{padding-top:28px} }
  @media (prefers-reduced-motion:reduce) { *,*::before,*::after { transition:none!important; } }
</style>
