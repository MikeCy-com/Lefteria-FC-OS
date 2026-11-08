import type { WebAdminSession } from './controller.ts';
import { hasAdminPermission } from './permissions.ts';
import { canReadClub } from './club-controller.ts';
import { canViewFirstTeamTrials } from './trials-access.ts';

type AdminUser = WebAdminSession['user'];
export type AdminSectionId = 'website' | 'crm' | 'pos' | 'finance' | 'football' | 'operations';
export interface AdminModule {
  id: string; name: string; description: string;
  status: 'live' | 'planned';
  href?: '/admin/club' | '/admin/news' | '/admin/trials' | '/admin/pos';
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
  { id: 'crm', name: 'CRM & επικοινωνία', description: 'Επαφές, ενδιαφέρον και επικοινωνία με οικογένειες.', modules: [
    { id: 'leads', name: 'Ενδιαφερόμενοι & επαφές', description: 'Απαιτεί ιδιωτικό API και πολιτική δεδομένων.', status: 'planned', scope: 'any', anyPermission: ['marketing:read'] },
    { id: 'registrations', name: 'Εγγραφές Ακαδημίας', description: 'Αιτήματα γονέων και κηδεμόνων.', status: 'planned', scope: 'academy', anyPermission: ['academy:read', 'academy:read_assigned'] },
    { id: 'messages', name: 'Μηνύματα', description: 'Επικοινωνία με μέλη και οικογένειες.', status: 'planned', scope: 'any', anyPermission: ['messages:read'] },
    { id: 'campaigns', name: 'Καμπάνιες & παραπομπές', description: 'Προώθηση και συνεργάτες.', status: 'planned', scope: 'any', anyPermission: ['marketing:read'] }
  ] },
  { id: 'pos', name: 'POS & κατάστημα', description: 'Σημεία πώλησης, κατάλογος και παραγγελίες.', modules: [
    { id: 'registers', name: 'Ταμεία & σημεία πώλησης', description: 'Χειριστές, βάρδιες και τοποθεσίες.', status: 'planned', scope: 'any', anyPermission: ['shop:read'] },
    { id: 'products', name: 'Κατάλογος προϊόντων', description: 'Επαληθευμένος κατάλογος και ελεγχόμενη διαχείριση κατηγοριών · χωρίς πωλήσεις.', status: 'live', href: '/admin/pos', scope: 'any', anyPermission: ['shop:read'] },
    { id: 'orders', name: 'Πωλήσεις & παραγγελίες', description: 'Συναλλαγές και ιστορικό.', status: 'planned', scope: 'any', anyPermission: ['shop:read', 'finance:read'] }
  ] },
  { id: 'finance', name: 'Οικονομικά', description: 'Χρεώσεις, εισπράξεις, έξοδα και αναφορές.', modules: [
    { id: 'charges', name: 'Χρεώσεις & εισπράξεις', description: 'Οφειλές και πληρωμές.', status: 'planned', scope: 'any', anyPermission: ['finance:read'] },
    { id: 'reports', name: 'Αναφορές & έξοδα', description: 'Οικονομικά ανά οργανισμό.', status: 'planned', scope: 'any', anyPermission: ['finance:read'] }
  ] },
  { id: 'football', name: 'Αγωνιστικό & Ακαδημία', description: 'Ομάδες, προπονήσεις, αγώνες και δοκιμαστικά.', modules: [
    { id: 'trials', name: 'Δοκιμαστικά πρώτης ομάδας', description: 'Επαληθευμένη κατάσταση κλειστών εγγραφών.', status: 'live', href: '/admin/trials', scope: 'first-team', anyPermission: ['settings:read', 'marketing:read'] },
    { id: 'teams', name: 'Ομάδες & παίκτες', description: 'Ρόστερ και συνδέσεις ομάδων.', status: 'planned', scope: 'any', anyPermission: ['teams:read', 'teams:read_assigned'] },
    { id: 'academy', name: 'Τμήματα Ακαδημίας', description: 'Τμήματα και ηλικιακές ομάδες.', status: 'planned', scope: 'academy', anyPermission: ['academy:read', 'academy:read_assigned'] },
    { id: 'schedule', name: 'Πρόγραμμα & παρουσίες', description: 'Προπονήσεις και ημερολόγιο.', status: 'planned', scope: 'any', anyPermission: ['training:read', 'training:read_assigned', 'attendance:read'] },
    { id: 'fixtures', name: 'Αγώνες & αποτελέσματα', description: 'Αγωνιστικό πρόγραμμα και ενημερώσεις.', status: 'planned', scope: 'first-team', anyPermission: ['fixtures:read', 'results:read'] }
  ] },
  { id: 'operations', name: 'Οργάνωση & ρυθμίσεις', description: 'Προσωπικό, υποδομές και ασφαλής πρόσβαση.', modules: [
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
      return item.anyPermission.some(permission => hasAdminPermission(user.permissions, permission));
    }).map(({ id, name, description, status, href }): AdminModule =>
      ({ id, name, description, status, ...(href ? { href } : {}) }));
    return modules.length ? [{ id: section.id, name: section.name, description: section.description, modules }] : [];
  });
}
