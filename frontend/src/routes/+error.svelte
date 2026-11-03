<script lang="ts">
  import { page } from '$app/state';
  import '../lib/public/public.css';
  const missing = $derived(page.status === 404);
</script>

<svelte:head><title>{missing ? 'Η σελίδα δεν βρέθηκε' : 'Προσωρινό πρόβλημα'} | ΛΕΥΤΕΡΙΑ FC</title><meta name="robots" content="noindex" /></svelte:head>
<main class="recovery">
  <a class="brand" href="/" data-sveltekit-reload><img src="/club/logo512.png" alt="" /><span>ΛΕΥΤΕΡΙΑ FC<small>ΙΔΡ. 2024</small></span></a>
  <section aria-labelledby="recovery-title">
    <p class="eyebrow">ΛΕΥΤΕΡΙΑ FC · {page.status}</p>
    <h1 id="recovery-title">{missing ? 'ΔΕΝ ΒΡΗΚΑΜΕ ΑΥΤΗ ΤΗ ΣΕΛΙΔΑ.' : 'ΑΣ ΤΟ ΔΟΚΙΜΑΣΟΥΜΕ ΞΑΝΑ.'}</h1>
    <p>{missing ? 'Ο σύνδεσμος μπορεί να έχει αλλάξει. Επίλεξε μια ενότητα για να συνεχίσεις.' : 'Η σελίδα δεν φορτώθηκε σωστά. Μπορείς να τη φορτώσεις ξανά ή να επιστρέψεις στην αρχική.'}</p>
    <div class="actions">{#if !missing}<button type="button" onclick={() => window.location.reload()}>Φόρτωση ξανά ↗</button>{/if}<a href="/" data-sveltekit-reload>Στην αρχική →</a></div>
    <nav aria-label="Συνέχεια πλοήγησης"><a href="/academy" data-sveltekit-reload>Ακαδημία</a><a href="/team" data-sveltekit-reload>Πρώτη ομάδα</a><a href="/news" data-sveltekit-reload>Νέα</a><a href="/admin/login" data-sveltekit-reload>Διαχείριση συλλόγου</a></nav>
  </section>
</main>

<style>
  .recovery{min-height:100svh;background:radial-gradient(ellipse at top right,#352807 0,transparent 48%),#0b0b0b;color:#f4f1e9;padding:clamp(24px,6vw,88px);font-family:'Fira Sans',sans-serif;box-sizing:border-box}.brand{display:flex;align-items:center;gap:14px;color:inherit;text-decoration:none;font-weight:800}.brand img{width:52px;height:52px}.brand small{display:block;color:#eebd48;font-size:11px;letter-spacing:.16em;margin-top:5px}section{max-width:860px;margin:clamp(64px,12vh,140px) auto 0}.eyebrow{font-size:12px;font-weight:700;letter-spacing:.15em;color:#eebd48}h1{font-family:'Fira Sans Extra Condensed',sans-serif;font-size:clamp(38px,6vw,80px);line-height:1.06;margin:22px 0}section>p:not(.eyebrow){max-width:620px;line-height:1.8;color:#c7c6c0;font-size:18px}.actions{display:flex;flex-wrap:wrap;gap:12px;margin:32px 0}.actions a,.actions button{padding:16px 24px;border:1px solid #eebd48;background:#eebd48;color:#111;font:700 15px 'Fira Sans',sans-serif;text-decoration:none;cursor:pointer;min-height:48px;box-sizing:border-box}nav{display:flex;flex-wrap:wrap;gap:24px;padding-top:30px;border-top:1px solid #35332c}nav a{color:#e1dfd6;text-underline-offset:5px}a:focus-visible,button:focus-visible{outline:3px solid #fff;outline-offset:5px}
</style>
