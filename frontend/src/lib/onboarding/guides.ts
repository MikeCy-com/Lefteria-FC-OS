import type { MobileRole, MobileTab } from '../mobile/navigation.ts';

export const ONBOARDING_VERSION = '2026.11.08.1';

export type WalkthroughStep = {
  id: string;
  title: string;
  description: string;
  destination?: string;
  destinationLabel?: string;
  state: 'ready' | 'planned' | 'verified';
};

type MobileIdentity = {
  role: MobileRole;
  first_name?: string | null;
  last_name?: string | null;
  birthday?: string | null;
};
type AdminIdentity = {
  role: string;
  name: string;
  organizations: readonly { id: string; name: string }[];
  permissions: readonly string[];
};

const mobileDest = (tab:MobileTab) => '/app?tab=' + encodeURIComponent(tab);
const step = (id:string,title:string,description:string,tab:MobileTab,
  state:WalkthroughStep['state']='ready'):WalkthroughStep =>
  ({id,title,description,destination:state==='planned'?undefined:mobileDest(tab),
    destinationLabel:state==='planned'?undefined:'Άνοιγμα',state});

/**
 * Every guide step must remain tied to a verified actual implementation.
 * A planned tab may be explained, but must never look like an operable task.
 * Increase ONBOARDING_VERSION when the workflow or permissions change.
 */
export function mobileGuide(identity:MobileIdentity):WalkthroughStep[] {
  const profileComplete = Boolean(identity.first_name?.trim() && identity.last_name?.trim() &&
    identity.birthday?.trim());
  const profile:WalkthroughStep={
    id:'profile',title:'Ολοκλήρωση προφίλ',
    description:'Έλεγξε το όνομα και επώνυμό σου και ολοκλήρωσε τα στοιχεία προφίλ. Η φωτογραφία είναι προαιρετική.',
    destination:mobileDest('profile'),destinationLabel:'Το προφίλ μου',
    state:profileComplete?'verified':'ready'
  };
  const guides:Record<MobileRole,WalkthroughStep[]> = {
    parent:[
      step('family','Παιδιά & σύνδεση οικογένειας','Έλεγξε τα επαληθευμένα παιδιά και τις εξουσιοδοτήσεις σου.','children'),
      step('calendar','Πρόγραμμα & διαθεσιμότητα','Βρες προπονήσεις, αγώνες και τις πραγματικές επιλογές συμμετοχής.','calendar'),
      step('chat','Μηνύματα ομάδας','Οι συνομιλίες και τα συνημμένα θα ενεργοποιηθούν όταν ολοκληρωθεί ο έλεγχος πρόσβασης.','chat','planned'),
      step('announcements','Ανακοινώσεις τμημάτων','Θα εμφανίζονται μόνο ανακοινώσεις για επιτρεπόμενες ομάδες.','announcements','planned')
    ],
    player:[
      step('calendar','Προπονήσεις & αγώνες','Δες τις επιτρεπόμενες δραστηριότητες του τμήματός σου.','calendar'),
      step('chat','Μηνύματα ομάδας','Η ομαδική επικοινωνία θα είναι διαθέσιμη με επαληθευμένη ανάθεση.','chat','planned'),
      step('announcements','Ενημερώσεις ομάδας','Θα διαβάζεις ανακοινώσεις μόνο των ομάδων σου.','announcements','planned')
    ],
    coach:[
      step('calendar','Το πρόγραμμά μου','Δες το πραγματικό ημερολόγιο του ρόλου σου.','calendar'),
      step('squad','Ανατεθειμένες ομάδες','Η ειδική προβολή ρόστερ προπονητή απαιτεί ακόμη ξεχωριστό έλεγχο ανάθεσης.','squad','planned'),
      step('chat','Μηνύματα & γονείς','Η συνομιλία προπονητή θα περιορίζεται στα τρέχοντα επιτρεπόμενα τμήματα.','chat','planned'),
      step('announcements','Ενημερώσεις τμημάτων','Δες ανακοινώσεις των τμημάτων που σου έχουν πραγματικά ανατεθεί.','announcements','planned')
    ],
    management:[
      step('calendar','Ημερολόγιο οργανισμού','Επίλεξε τον οργανισμό σου και δες το επιτρεπόμενο πρόγραμμα.','calendar'),
      step('teams','Ομάδες & μέλη','Ο πλήρης πίνακας διαχείρισης ομάδων μέσα στην εφαρμογή είναι υπό ανάπτυξη.','teams','planned'),
      step('announcements','Έγκριση ανακοινώσεων','Οι δημοσιεύσεις προς μέλη θα απαιτούν ρητή επιβεβαίωση και καταγραφή.','announcements','planned'),
      step('chat','Μηνύματα & επιβεβαιώσεις ανάγνωσης','Οι ομαδικές συνομιλίες και το «Διαβάστηκε από» δεν έχουν ακόμη ενεργοποιηθεί.','chat','planned')
    ],
    fan:[
      step('calendar','Αγωνιστικά νέα','Τα δημόσια νέα είναι διαθέσιμα από την ιστοσελίδα και όχι από ιδιωτικές ομάδες.','news','planned'),
      step('chat','Ιδιωτικά μηνύματα μελών','Οι ανακοινώσεις Ακαδημίας δεν είναι δημόσιες προς φιλάθλους.','chat','planned')
    ]
  };
  return [profile,...guides[identity.role]];
}

const adminStep=(id:string,title:string,description:string,destination?:string,
  state:WalkthroughStep['state']='ready'):WalkthroughStep=>({
    id,title,description,
    ...(state==='planned'?{}:{destination,destinationLabel:'Άνοιγμα'}),
    state
  });

export function adminGuide(user:AdminIdentity):WalkthroughStep[] {
  const organizations = user.organizations.filter(o =>
    o.id==='lefteria_fc' || o.id==='lefteria_fc_academy');
  const permissions=new Set(user.permissions);
  const all=permissions.has('*:*');
  const has=(name:string)=>all || permissions.has(name) ||
    permissions.has(name.split(':')[0]+':*');
  const profile:WalkthroughStep=adminStep('account','Επιβεβαίωσε τον λογαριασμό σου',
    'Δες το όνομα, τον ρόλο και τις τρέχουσες δυνατότητές σου. Η επεξεργασία στοιχείων admin δεν είναι ακόμη διαθέσιμη.',
    '/admin#account',user.name.trim()?'ready':'planned');
  const organization:WalkthroughStep=adminStep('organizations','Έλεγχος οργανισμών και δικαιωμάτων',
    'Έλεγξε ότι εμφανίζονται μόνο οι οργανισμοί που σου έχουν ανατεθεί. Η πρόσβαση επιβεβαιώνεται ξανά στον διακομιστή.',
    '/admin#access',organizations.length?'verified':'ready');
  const result=[profile,organization];
  if(has('teams:read') || has('academy:read'))
    result.push(adminStep('groups','Ομάδες, παίκτες & προπονητές',
      'Διάβασε το τρέχον ρόστερ. Οι αλλαγές μελών και προπονητών απαιτούν δικαιώματα εγγραφής.',
      '/admin/groups'));
  if((user.role==='manager' || user.role==='super_admin') &&
      (has('teams:write') || has('academy:write')))
    result.push(adminStep('drafts','Πρόχειρες ανακοινώσεις εφαρμογής',
      'Σύνταξε ένα πρόχειρο για αληθινό τμήμα. Η αποστολή και δημοσίευση δεν έχουν ενεργοποιηθεί.',
      '/admin/announcements'));
  if(has('shop:read'))
    result.push(adminStep('pos','Κατάλογος POS','Δες τις κατηγορίες και τα προϊόντα. Δεν υπάρχει ενεργό checkout.',
      '/admin/pos'));
  if(has('news:read'))
    result.push(adminStep('news','Δημόσια νέα ιστοσελίδας','Διάκρινε τη δημόσια ιστοσελίδα από τις ιδιωτικές ανακοινώσεις της εφαρμογής.',
      '/admin/news'));
  if(['coach','assistant_coach'].includes(user.role) && result.length<=2)
    result.push(adminStep('assigned','Ομάδες που σου έχουν ανατεθεί',
      'Η προβολή προπονητή θα ενεργοποιηθεί μετά την εφαρμογή της πολιτικής πρόσβασης στα τμήματα.',
      undefined,'planned'));
  return result;
}
