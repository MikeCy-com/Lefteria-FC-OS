<script lang="ts">
  import LegalPage from '../../lib/public/LegalPage.svelte';
  import LegalSection from '../../lib/public/LegalSection.svelte';
  import type { PublicPageData } from '../../lib/public/types';
  let { data }: { data: PublicPageData } = $props();
</script>
<LegalPage {data} title="Πολιτική Απορρήτου" description="Πολιτική Απορρήτου και προστασία προσωπικών δεδομένων της Λευτεριά FC σύμφωνα με τον GDPR και την Κυπριακή νομοθεσία." icon="shield" testid="privacy-page">
{#snippet intro()}
<p>
            Η <strong>Λευτεριά FC</strong> σέβεται και προστατεύει τα προσωπικά σας δεδομένα.
            Η παρούσα Πολιτική Απορρήτου εξηγεί ποια δεδομένα συλλέγουμε, γιατί, πώς τα χρησιμοποιούμε και ποια είναι τα δικαιώματά σας
            σύμφωνα με τον Γενικό Κανονισμό Προστασίας Δεδομένων (ΕΕ 2016/679 — GDPR) και τον Νόμο 125(Ι)/2018 της Κυπριακής Δημοκρατίας.
          </p>
{/snippet}
<LegalSection title="1. Υπεύθυνος Επεξεργασίας" icon="user" testid="privacy-section-controller">
<p>
            <strong>Λευτεριά FC</strong><br />
            Λεμεσός, Κύπρος<br />
            Email: <a href="mailto:info@lefteriafc.cy">info@lefteriafc.cy</a>
          </p>
          <p>Για ερωτήσεις σχετικά με τα δεδομένα σας, επικοινωνήστε μαζί μας στην παραπάνω διεύθυνση.</p>
</LegalSection>
<LegalSection title="2. Ποια δεδομένα συλλέγουμε" icon="file" testid="privacy-section-data">
<p>Συλλέγουμε τα εξής δεδομένα ανάλογα με την αλληλεπίδρασή σας μαζί μας:</p>
          <ul>
            <li><strong>Στοιχεία επικοινωνίας:</strong> ονοματεπώνυμο, τηλέφωνο, email.</li>
            <li><strong>Στοιχεία παίκτη (για ακαδημία):</strong> ηλικία, ημερομηνία γέννησης, σχολείο, στοιχεία γονέων.</li>
            <li><strong>Ιατρικά δεδομένα (όπου απαιτείται):</strong> ιατρικό πιστοποιητικό, αλλεργίες, επείγουσα επαφή. Αυτά είναι ευαίσθητα και τα προστατεύουμε με ιδιαίτερη επιμέλεια.</li>
            <li><strong>Έγγραφα:</strong> ταυτότητα/διαβατήριο, πιστοποιητικά γέννησης, ασφαλιστική κάλυψη, δηλώσεις εγγραφής KOP.</li>
            <li><strong>Τεχνικά δεδομένα:</strong> διεύθυνση IP, τύπος συσκευής, cookies (ανώνυμα, μόνο μετά από συγκατάθεση).</li>
            <li><strong>Εικόνες/βίντεο:</strong> φωτογραφίες και βίντεο από αγώνες/προπονήσεις, μόνο μετά από ρητή γραπτή συγκατάθεση για ανηλίκους.</li>
          </ul>
</LegalSection>
<LegalSection title="3. Γιατί τα χρησιμοποιούμε (νομική βάση)" icon="lock" testid="privacy-section-purpose">
<ul>
            <li><strong>Εκτέλεση σύμβασης:</strong> εγγραφή, διαχείριση συνδρομής, έκδοση παραστατικών.</li>
            <li><strong>Έννομη υποχρέωση:</strong> εγγραφή στην Κυπριακή Ομοσπονδία Ποδοσφαίρου (KOP), λογιστικά αρχεία, φορολογικές υποχρεώσεις.</li>
            <li><strong>Έννομο συμφέρον:</strong> ασφάλεια εγκαταστάσεων, βελτίωση υπηρεσιών, στατιστική ανάλυση.</li>
            <li><strong>Συγκατάθεση:</strong> ενημερωτικά newsletters, marketing επικοινωνία, φωτογράφιση/βιντεοσκόπηση, μη αναγκαία cookies.</li>
          </ul>
</LegalSection>
<LegalSection title="4. Πόσο διατηρούμε τα δεδομένα" icon="shield" testid="privacy-section-retention">
<ul>
            <li>Δεδομένα μελών/παικτών: για όσο διαρκεί η συμμετοχή + 5 έτη μετά τη λήξη για λογιστικούς/νομικούς σκοπούς.</li>
            <li>Ιατρικά δεδομένα: 5 έτη μετά τη λήξη συμμετοχής ή σύμφωνα με ιατρικό απόρρητο.</li>
            <li>Εγγραφές leads (φόρμες ενδιαφέροντος): 24 μήνες από την τελευταία επικοινωνία εκτός αν αιτηθείτε διαγραφή νωρίτερα.</li>
            <li>Δεδομένα cookies: όπως ορίζεται στο cookie banner (τυπικά 12 μήνες).</li>
          </ul>
</LegalSection>
<LegalSection title="5. Τα δικαιώματά σας (GDPR)" icon="user" testid="privacy-section-rights">
<p>Έχετε τα εξής δικαιώματα, τα οποία μπορείτε να ασκήσετε δωρεάν μέσω email στο <a href="mailto:info@lefteriafc.cy">info@lefteriafc.cy</a>:</p>
          <ul>
            <li><strong>Πρόσβαση:</strong> ζητήστε αντίγραφο των δεδομένων σας.</li>
            <li><strong>Διόρθωση:</strong> ζητήστε ενημέρωση ανακριβών στοιχείων.</li>
            <li><strong>Διαγραφή («δικαίωμα στη λήθη»):</strong> ζητήστε διαγραφή όπου δεν υπάρχει έννομη υποχρέωση διατήρησης.</li>
            <li><strong>Περιορισμός επεξεργασίας:</strong> ζητήστε να «παγώσουμε» την επεξεργασία σε συγκεκριμένες περιπτώσεις.</li>
            <li><strong>Φορητότητα:</strong> λάβετε τα δεδομένα σας σε μηχαναγνώσιμη μορφή.</li>
            <li><strong>Εναντίωση:</strong> εναντιωθείτε σε επεξεργασία που βασίζεται σε έννομο συμφέρον ή marketing.</li>
            <li><strong>Ανάκληση συγκατάθεσης:</strong> ανά πάσα στιγμή, χωρίς να επηρεάζεται η νομιμότητα επεξεργασίας πριν την ανάκληση.</li>
          </ul>
          <p>
            Αν πιστεύετε ότι τα δικαιώματά σας δεν ικανοποιήθηκαν, μπορείτε να απευθυνθείτε στην
            <strong>Επίτροπο Προστασίας Δεδομένων Προσωπικού Χαρακτήρα Κύπρου</strong> (<a href="https://www.dataprotection.gov.cy" target="_blank" rel="noreferrer">dataprotection.gov.cy</a>).
          </p>
</LegalSection>
<LegalSection title="6. Ασφάλεια & Διαβίβαση" icon="trash" testid="privacy-section-security">
<p>
            Τα δεδομένα σας φυλάσσονται σε ασφαλή διακομιστή με κρυπτογράφηση εν κινήσει (HTTPS) και σε ηρεμία. Η πρόσβαση περιορίζεται σε εξουσιοδοτημένο προσωπικό. Δεν πωλούμε δεδομένα σε τρίτους.
          </p>
          <p>Χρησιμοποιούμε τους εξής τρίτους παρόχους (data processors):</p>
          <ul>
            <li><strong>Stripe</strong> (πληρωμές) — δεδομένα κάρτας δεν αποθηκεύονται από εμάς.</li>
            <li><strong>Twilio</strong> (SMS ειδοποιήσεις).</li>
            <li><strong>Meta / TikTok</strong> (social media posts — μόνο δημόσιο περιεχόμενο που έχετε ήδη εγκρίνει).</li>
            <li><strong>Google Fonts / Analytics</strong> — μόνο με τη συγκατάθεσή σας.</li>
          </ul>
</LegalSection>
<LegalSection title="7. Cookies" icon="cookie" testid="privacy-section-cookies">
<p>
            Χρησιμοποιούμε τεχνικά cookies απαραίτητα για τη λειτουργία της ιστοσελίδας. Για αναλυτικά cookies και cookies marketing ζητάμε τη συγκατάθεσή σας μέσω του cookie banner που εμφανίζεται στην πρώτη επίσκεψη. Μπορείτε να ανακαλέσετε τη συγκατάθεσή σας ανά πάσα στιγμή διαγράφοντας τα cookies του browser σας.
          </p>
</LegalSection>
<LegalSection title="8. Ανήλικοι" icon="user" testid="privacy-section-minors">
<p>
            Για ανηλίκους κάτω των 18 ετών (και ιδίως κάτω των 16 σύμφωνα με το άρθρο 8 GDPR), απαιτείται ρητή γραπτή συγκατάθεση γονέα/κηδεμόνα για κάθε επεξεργασία που δεν εμπίπτει σε έννομη υποχρέωση ή έννομο συμφέρον (π.χ. φωτογραφίες, marketing).
          </p>
</LegalSection>
<LegalSection title="9. Αλλαγές & Επικοινωνία" icon="mail" testid="privacy-section-contact">
<p>
            Η παρούσα Πολιτική μπορεί να αναθεωρείται περιοδικά. Για κάθε ουσιώδη αλλαγή θα σας ειδοποιήσουμε μέσω email ή ανακοίνωσης στην ιστοσελίδα.
          </p>
          <p>
            Για οποιοδήποτε ερώτημα, επικοινωνήστε στο <a href="mailto:info@lefteriafc.cy">info@lefteriafc.cy</a>.
          </p>
</LegalSection>
<div class="legal-other"><a href="/terms" data-testid="link-to-terms">Δείτε επίσης τους Όρους Χρήσης →</a></div>
</LegalPage>
