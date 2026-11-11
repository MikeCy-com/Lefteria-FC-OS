import type { WebAdminSession } from './controller.ts';
import { hasAdminPermission } from './permissions.ts';
import { canReadClub } from './club-controller.ts';
import { canViewFirstTeamTrials } from './trials-access.ts';

type AdminUser = WebAdminSession['user'];
export type AdminSectionId = 'website' | 'hub' | 'app' | 'crm' | 'pos' | 'finance' | 'football' | 'operations';
export interface AdminModule {
  id: string; name: string; description: string;
  status: 'live' | 'planned';
  href?: '/admin/club' | '/admin/news' | '/admin/trials' | '/admin/pos' | '/admin/groups' | '/admin/announcements' | '/admin/onboarding' | '/admin/leads' | '/admin/finance' | '/admin/attendance' | '/admin/evaluations' | '/admin/documents';
}
export interface AdminSection {
  id: AdminSectionId; name: string; description: string; modules: AdminModule[];
}
interface Entry extends AdminModule {
  scope: 'any' | 'academy' | 'first-team';
  anyPermission: readonly string[];
}
interface Section {
  id: AdminSectionId; name: string; description: string; modules: readonly Entry[];
}
const catalog: readonly Section[] = [
  { id: 'website', name: 'Ιστοσελίδα', description: 'Δημόσια παρουσία, περιεχόμενο και ταυτότητα συλλόγου.', modules: [
    { id: 'club', name: 'Προφίλ συλλόγου', description: 'Προβολή και επεξεργασία δημόσιων στοιχείων ανά οργανισμό.', status: 'live', href: '/admin/club', scope: 'any', anyPermission: ['settings:read'] },
    { id: 'news', name: 'Άρθρα & νέα', description: 'Πραγματική προβολή δημοσιευμένου περιεχομένου ανά οργανισμό. Η επεξεργασία παραμένει ανενεργή.', status: 'live', href: '/admin/news', scope: 'any', anyPermission: ['news:read'] },
    { id: 'sponsors', name: 'Χορηγοί & συνεργάτες', description: 'Δημόσιες συνεργασίες συλλόγου.', status: 'planned', scope: 'any', anyPermission: ['sponsors:read'] },
    { id: 'media', name: 'Βιβλιοθήκη πολυμέσων', description: 'Φωτογραφίες και άλμπουμ.', status: 'planned', scope: 'any', anyPermission: ['gallery:read'] },
    { id: 'social', name: 'Social Control Centre', description: 'Επίσημοι κοινωνικοί λογαριασμοί.', status: 'planned', scope: 'any', anyPermission: ['social:write'] }
  ] },
  { id: 'hub', name: 'HUB · Περιεχόμενο & συνδρομές', description: 'Ασφαλές εκπαιδευτικό περιεχόμενο για γονείς και αθλητές, με προαιρετική συνδρομή φιλάθλων και υποχρεωτική ανθρώπινη έγκριση.', modules: [
    { id: 'hub_editorial', name: 'AI πρόχειρα & έγκριση περιεχομένου', description: 'Κείμενα και εικαστικά από AI: επεξεργασία, έλεγχος προστασίας ανηλίκων, έγκριση διαχειριστή και μετά δημοσίευση. Η υπηρεσία παραγωγής και το API εκκρεμούν.', status: 'planned', scope: 'any', anyPermission: ['news:read','settings:read'] },
    { id: 'hub_subscriptions', name: 'Συνδρομές HUB φιλάθλων', description: 'Ξεχωριστή επαληθευμένη συνδρομή με επιλογές γονέα / αθλητή, χωρίς αλλαγή ρόλου ή δικαιώματα παιδιών. Η χρέωση και η επαλήθευση παρόχου εκκρεμούν.', status: 'planned', scope: 'any', anyPermission: ['finance:read','marketing:read'] }
  ] },
  { id: 'app', name: 'Εφαρμογή · Επικοινωνία', description: 'Μόνο η ιδιωτική εφαρμογή μελών: μηνύματα, ανακοινώσεις, ειδοποιήσεις και κανόνες ορατότητας. Δεν αφορά τη δημόσια ιστοσελίδα.', modules: [
    { id: 'announcements', name: 'Ανακοινώσεις εφαρμογής · πρόχειρα', description: 'Πρόχειρα για ομάδες και Ακαδημία. Η δημοσίευση και η προβολή στην εφαρμογή δεν έχουν ενεργοποιηθεί ακόμη.', status: 'live', href: '/admin/announcements', scope: 'any', anyPermission: ['teams:write','academy:write'] },
    { id: 'messages', name: 'Μηνύματα εφαρμογής', description: 'Ιδιωτικές συνομιλίες, ομαδικά μηνύματα, αντιδράσεις και επιβεβαιώσεις ανάγνωσης· σε υλοποίηση.', status: 'planned', scope: 'any', anyPermission: ['messages:read'] },
    { id: 'app_notifications', name: 'Ειδοποιήσεις εφαρμογής', description: 'Ειδοποιήσεις προς τρέχοντα και επιτρεπόμενα μέλη, με απόπειρες και επαναλήψεις.', status: 'planned', scope: 'any', anyPermission: ['messages:write'] },
    { id: 'app_media', name: 'Συνημμένα εφαρμογής', description: 'Προστατευμένα αρχεία και banner ανακοινώσεων. Καμία δημόσια ανάρτηση.', status: 'planned', scope: 'any', anyPermission: ['messages:write'] }
  ] },
  { id: 'crm', name: 'CRM & επαφές', description: 'Εγγραφές, οικογένειες, εξυπηρέτηση και ενδιαφερόμενοι· διακριτά από την επικοινωνία της εφαρμογής.', modules: [
    { id: 'leads', name: 'Ενδιαφερόμενοι Ακαδημίας', description: 'Ιδιωτικός κατάλογος αιτημάτων και ελεγχόμενη καταχώριση επικοινωνίας (χωρίς αυτόματη εγγραφή ή αποστολή).', status: 'live', href: '/admin/leads', scope: 'academy', anyPermission: ['marketing:read'] },
    { id: 'registrations', name: 'Εγγραφές Ακαδημίας', description: 'Αιτήματα γονέων και κηδεμόνων.', status: 'planned', scope: 'academy', anyPermission: ['academy:read', 'academy:read_assigned'] },
    { id: 'campaigns', name: 'Καμπάνιες & παραπομπές', description: 'Προώθηση και συνεργάτες.', status: 'planned', scope: 'any', anyPermission: ['marketing:read'] }
  ] },
  { id: 'pos', name: 'POS & κατάστημα', description: 'Σημεία πώλησης, κατάλογος και παραγγελίες.', modules: [
    { id: 'registers', name: 'Ταμεία & σημεία πώλησης', description: 'Χειριστές, βάρδιες και τοποθεσίες.', status: 'planned', scope: 'any', anyPermission: ['shop:read'] },
    { id: 'products', name: 'Κατάλογος προϊόντων', description: 'Επαληθευμένος κατάλογος και ελεγχόμενη διαχείριση κατηγοριών · χωρίς πωλήσεις.', status: 'live', href: '/admin/pos', scope: 'any', anyPermission: ['shop:read'] },
    { id: 'orders', name: 'Πωλήσεις & παραγγελίες', description: 'Συναλλαγές και ιστορικό.', status: 'planned', scope: 'any', anyPermission: ['shop:read', 'finance:read'] }
  ] },
  { id: 'finance', name: 'Οικονομικά', description: 'Χρεώσεις, εισπράξεις, έξοδα και αναφορές.', modules: [
    { id: 'payabl', name: 'Ακαδημία · Χρεώσεις, PDF & payabl. (sandbox)', description: 'Έκδοση επιλεγμένου μήνα ανά παιδί, καταχώριση μετρητών, λήψη αυθεντικού PDF και δοκιμαστικοί σύνδεσμοι Payabl με προστατευμένο Twilio SMS. Οι πραγματικές αυτόματες εισπράξεις, η παραγωγική διασύνδεση και οι πληρωμές προσωπικού παραμένουν ανενεργές.', status: 'live', href: '/admin/finance', scope: 'any', anyPermission: ['finance:read'] },
    { id: 'charges', name: 'Χρεώσεις & εισπράξεις', description: 'Οφειλές και πληρωμές.', status: 'planned', scope: 'any', anyPermission: ['finance:read'] },
    { id: 'reports', name: 'Αναφορές & έξοδα', description: 'Οικονομικά ανά οργανισμό.', status: 'planned', scope: 'any', anyPermission: ['finance:read'] }
  ] },
  { id: 'football', name: 'Αγωνιστικό & Ακαδημία', description: 'Ομάδες, προπονήσεις, αγώνες και δοκιμαστικά.', modules: [
    { id: 'trials', name: 'Δοκιμαστικά πρώτης ομάδας', description: 'Επαληθευμένη κατάσταση κλειστών εγγραφών.', status: 'live', href: '/admin/trials', scope: 'first-team', anyPermission: ['settings:read', 'marketing:read'] },
    { id: 'groups', name: 'Ομάδες & τμήματα', description: 'Πραγματικές ομάδες, μέλη και ελεγχόμενη δημιουργία τμημάτων ανά οργανισμό.', status: 'live', href: '/admin/groups', scope: 'any', anyPermission: ['teams:read', 'academy:read'] },
    { id: 'attendance', name: 'Παρουσίες προπονήσεων Ακαδημίας', description: 'Ιδιωτικό παρουσιολόγιο πραγματικής παρουσίας σε επιβεβαιωμένες προπονήσεις, ανεξάρτητο από RSVP. Μόνο εξουσιοδοτημένη διαχείριση.', status: 'live', href: '/admin/attendance', scope: 'academy', anyPermission: ['attendance:read'] },
    { id: 'documents', name: 'Προστατευμένα έγγραφα αθλητών', description: 'Ιδιωτική προβολή τύπων και ημερομηνιών λήξης· χωρίς πρόσβαση σε αρχεία.', status: 'live', href: '/admin/documents', scope: 'any', anyPermission: ['academy:read','teams:read'] },
    { id: 'evaluations', name: 'Αξιολογήσεις αθλητών', description: 'Περιορισμένη προβολή αξιολογήσεων ανά αθλητή.', status: 'live', href: '/admin/evaluations', scope: 'any', anyPermission: ['academy:read','teams:read'] },
    { id: 'assigned_groups', name: 'Τα ανατεθειμένα τμήματά μου', description: 'Ειδική προβολή προπονητή με ανατεθειμένα τμήματα· απαιτεί ξεχωριστή πολιτική ανάθεσης.', status: 'planned', scope: 'any', anyPermission: ['teams:read_assigned', 'academy:read_assigned'] },
    { id: 'schedule', name: 'Πρόγραμμα & παρουσίες', description: 'Προπονήσεις και ημερολόγιο.', status: 'planned', scope: 'any', anyPermission: ['training:read', 'training:read_assigned', 'attendance:read'] },
    { id: 'fixtures', name: 'Αγώνες & αποτελέσματα', description: 'Αγωνιστικό πρόγραμμα και ενημερώσεις.', status: 'planned', scope: 'first-team', anyPermission: ['fixtures:read', 'results:read'] }
  ] },
  { id: 'operations', name: 'Οργάνωση & ρυθμίσεις', description: 'Προσωπικό, υποδομές και ασφαλής πρόσβαση.', modules: [
    { id: 'onboarding', name: 'Οδηγός διαχειριστή', description: 'Οδηγίες πρώτης χρήσης για διαχειριστές, προπονητές και προσωπικό, με βήματα ανά δικαίωμα.', status: 'live', href: '/admin/onboarding', scope: 'any', anyPermission: ['settings:read','teams:read','academy:read','staff:read','shop:read','messages:read','news:read'] },
    { id: 'staff', name: 'Προσωπικό & προπονητές', description: 'Στελέχη και αναθέσεις.', status: 'planned', scope: 'any', anyPermission: ['staff:read'] },
    { id: 'facilities', name: 'Εγκαταστάσεις', description: 'Χώροι και πόροι.', status: 'planned', scope: 'any', anyPermission: ['facilities:read'] },
    { id: 'users', name: 'Χρήστες & δικαιώματα', description: 'Εξουσιοδοτήσεις λογαριασμών.', status: 'planned', scope: 'any', anyPermission: ['users:read', 'users:write'] },
    { id: 'organization', name: 'Ρυθμίσεις οργανισμών', description: 'Ρυθμίσεις εκτός δημόσιου προφίλ.', status: 'planned', scope: 'any', anyPermission: ['settings:read'] }
  ] }
];
// Navigation is presentation-only. Protected API endpoints MUST independently
// verify fresh session role and organization grants. Planned items never
// receive clickable URLs and never imply live POS, CRM or payment operations.
export function adminSectionsFor(user: AdminUser): AdminSection[] {
  const organizations = user.organizations.map(org => org.id);
  const assigned = organizations.some(id => id === 'lefteria_fc' || id === 'lefteria_fc_academy');
  return catalog.flatMap(section => {
    const modules = section.modules.filter(item => {
      if (!assigned) return false;
      if (item.scope === 'academy' && !organizations.includes('lefteria_fc_academy')) return false;
      if (item.scope === 'first-team' && !organizations.includes('lefteria_fc')) return false;
      if (item.id === 'club') return canReadClub(user);
      if (item.id === 'trials') return canViewFirstTeamTrials(user);
      if (item.id === 'documents' || item.id === 'evaluations') return ['manager','super_admin'].includes(user.role) &&
        ((organizations.includes('lefteria_fc') && hasAdminPermission(user.permissions,'teams:read')) ||
         (organizations.includes('lefteria_fc_academy') && hasAdminPermission(user.permissions,'academy:read')));
      if (item.id === 'onboarding') return true;
      if (item.id === 'attendance') return (
        ['manager','super_admin'].includes(user.role) &&
        organizations.includes('lefteria_fc_academy') &&
        hasAdminPermission(user.permissions,'attendance:read'));
      if (item.id === 'leads') return (['manager','super_admin'].includes(user.role) &&
        organizations.includes('lefteria_fc_academy') &&
        hasAdminPermission(user.permissions,'marketing:read'));
      if (item.id === 'announcements') return (['manager','super_admin'].includes(user.role) && (
        (organizations.includes('lefteria_fc') && hasAdminPermission(user.permissions,'teams:write')) ||
        (organizations.includes('lefteria_fc_academy') && hasAdminPermission(user.permissions,'academy:write'))));
      if (item.id === 'groups') return (
        (organizations.includes('lefteria_fc') && hasAdminPermission(user.permissions,'teams:read')) ||
        (organizations.includes('lefteria_fc_academy') && hasAdminPermission(user.permissions,'academy:read'))
      );
      return item.anyPermission.some(permission => hasAdminPermission(user.permissions, permission));
    }).map(({ id, name, description, status, href }): AdminModule =>
      ({ id, name, description, status, ...(href ? { href } : {}) }));
    return modules.length ? [{ id: section.id, name: section.name, description: section.description, modules }] : [];
  });
}
