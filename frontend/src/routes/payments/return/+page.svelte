<svelte:head>
  <title>Πληρωμή σε επαλήθευση | Λευτεριά FC</title>
  <meta name="robots" content="noindex,nofollow" />
</svelte:head>

<main class="payment-return" data-testid="payabl-public-return">
  <img src="/mobile/club-logo.png" width="78" height="88" alt="Λευτεριά FC" />
  <p class="eyebrow">ΛΕΥΤΕΡΙΑ FC · ΠΛΗΡΩΜΕΣ</p>
  <h1>Το αίτημα πληρωμής βρίσκεται σε επαλήθευση</h1>
  <p>Η επιστροφή από τη σελίδα πληρωμής δεν αποτελεί επιβεβαίωση εξόφλησης. Ο σύλλογος ελέγχει την επίσημη επιβεβαίωση του παρόχου.</p>
  <p>Για πληροφορίες σχετικά με την κατάσταση της συναλλαγής σας, επικοινωνήστε με τη γραμματεία του συλλόγου.</p>
  <a href="/">Επιστροφή στην αρχική</a>
</main>

<style>
  .payment-return{min-height:75vh;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:1rem;margin:auto;max-width:40rem;padding:4rem 1.5rem;text-align:center;font-family:'Fira Sans Extra Condensed',system-ui,sans-serif}
  .payment-return img{object-fit:contain}
  .payment-return h1{font-size:clamp(2rem,5vw,3.3rem);line-height:1.12;margin:0}
  .payment-return p{font-size:1.1rem;line-height:1.5}
  .payment-return .eyebrow{font-weight:700;letter-spacing:.12em}
  .payment-return a{padding:.75rem 1.5rem;border-radius:.6rem;border:2px solid currentColor;font-weight:700}
</style>
