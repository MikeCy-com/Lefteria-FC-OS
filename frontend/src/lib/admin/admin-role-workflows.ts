import type {WebAdminSession} from './controller.ts';
import {adminSectionsFor} from './module-catalog.ts';
import {hasAdminPermission} from './permissions.ts';

type AdminUser=WebAdminSession['user'];
export type AdminRoleAction={
  id:string;title:string;description:string;href:string;
  area:'academy'|'finance'|'operations'|'content'|'team';
};

// This is an affordance list, never an authorization grant. We intersect
// role-specific advice with the current server-issued module catalog and
// organization permissions. Planned modules and orphan routes are excluded.
const ACTIONS:Readonly<Record<string,Omit<AdminRoleAction,'href'>>>={
  registrations:{id:'registrations',title:'Αιτήσεις & κηδεμόνες',
    description:'Έλεγχος αιτήσεων, εκκρεμοτήτων και επαλήθευσης πριν από ένταξη.',
    area:'academy'},
  groups:{id:'groups',title:'Αθλητές & τμήματα',
    description:'Άνοιγμα επαληθευμένων ομάδων, συμμετοχών και αναθέσεων.',
    area:'academy'},
  territories:{id:'territories',title:'Πόλεις & γήπεδα',
    description:'Κοινά γήπεδα και αντίπαλοι ανά πόλη, ομάδες ανά τοποθεσία.',
    area:'team'},
  staff:{id:'staff',title:'Προπονητές & αναθέσεις',
    description:'Ορατοί προπονητές και αναθέσεις εντός των εξουσιοδοτημένων οργανισμών.',
    area:'team'},
  payabl:{id:'payabl',title:'Οικονομικός έλεγχος',
    description:'Επαληθευμένα παραστατικά και οικονομικές αναφορές. Οι πληρωμές απαιτούν ιδιαίτερη εξουσιοδότηση.',
    area:'finance'},
  registers:{id:'registers',title:'Ταμείο & βάρδια',
    description:'Άνοιγμα του προστατευμένου POS και των επιτρεπόμενων εργασιών.',
    area:'finance'},
  communications:{id:'communications',title:'Επικοινωνία μελών',
    description:'Καταστάσεις επικοινωνίας και επιτρεπόμενες ανακοινώσεις.',
    area:'operations'},
  announcements:{id:'announcements',title:'Ανακοινώσεις',
    description:'Διαχείριση εσωτερικών ανακοινώσεων ανά επιτρεπόμενο οργανισμό.',
    area:'operations'},
  leads:{id:'leads',title:'Αιτήματα ενδιαφέροντος',
    description:'Επαληθευμένες επαφές και κατάσταση παρακολούθησης.',
    area:'academy'},
  pages:{id:'pages',title:'Σελίδες ιστοσελίδας',
    description:'Επεξεργασία του περιεχομένου με τα υφιστάμενα εργαλεία δημοσίευσης.',
    area:'content'},
  news:{id:'news',title:'Άρθρα & νέα',
    description:'Επεξεργασία άρθρων και πρόχειρων, ανά εξουσιοδοτημένο οργανισμό.',
    area:'content'},
  hub_editorial:{id:'hub_editorial',title:'HUB & εγκρίσεις',
    description:'Εκπαιδευτικό περιεχόμενο με ανθρώπινο έλεγχο.',
    area:'content'},
  sponsors:{id:'sponsors',title:'Χορηγοί',
    description:'Διαχείριση περιεχομένου και εγκεκριμένων συνεργασιών.',
    area:'content'},
  users:{id:'users',title:'Χρήστες & δικαιώματα',
    description:'Διαχείριση λογαριασμών με τις προστασίες του Super Admin.',
    area:'operations'},
  onboarding:{id:'onboarding',title:'Οδηγός εργασιών',
    description:'Οδηγίες ασφαλούς πρόσβασης και βήματα πρώτης χρήσης.',
    area:'operations'},
  club:{id:'club',title:'Στοιχεία συλλόγου',
    description:'Ενημέρωση επαληθευμένων στοιχείων οργανισμού.',
    area:'operations'},
};

const BY_ROLE:Readonly<Record<string,readonly string[]>>={
  super_admin:['registrations','groups','payabl','territories','staff',
    'users','communications','announcements','leads','pages','news','registers','onboarding'],
  manager:['registrations','groups','territories','staff','communications',
    'announcements','leads','registers','onboarding'],
  accountant:['payabl','onboarding'],
  cashier:['registers'],
  editor:['news','pages','hub_editorial','sponsors','club'],
  marketing:['leads','news','pages','sponsors','hub_editorial'],
  coach:['groups','onboarding'],
  matchday_editor:['groups','announcements','onboarding']
};

export function adminRoleActions(user:AdminUser):AdminRoleAction[]{
  const permitted=new Map(adminSectionsFor(user).flatMap(section=>section.modules)
    .filter(module=>module.status==='live'&&module.href)
    .map(module=>[module.id,module.href!] as const));
  const haveAcademy=user.organizations.some(org=>org.id==='lefteria_fc_academy');
  const actions:AdminRoleAction[]=[];
  const links=new Set<string>();
  for(const id of BY_ROLE[user.role]??[]){
    const href=permitted.get(id),item=ACTIONS[id];
    if(!href||!item)continue;
    // Distinguish an organization-wide roster lookup from exact group read.
    // Player CRM only exists for managers with Academy write scope.
    const target=id==='groups'&&haveAcademy&&
      ['manager','super_admin'].includes(user.role)&&
      hasAdminPermission(user.permissions,'academy:write')
      ?'/admin/groups?organization_id=lefteria_fc_academy&view=players':href;
    // Only a verified Academy registration page accepts the review-queue hint.
    const finalHref=id==='registrations'?'/admin/academy-office?queue=action':target;
    if(links.has(finalHref))continue;
    links.add(finalHref);
    actions.push({...item,href:finalHref});
    if(actions.length>=6)break;
  }
  // An unassigned coach, marketing or matchday account gets no manufactured
  // privileges: the profile/access page remains available separately.
  return actions;
}

export function adminRoleIntro(role:string):string{
  switch(role){
    case 'super_admin':return 'Διοίκηση · έλεγχος πρόσβασης και λειτουργιών σε εξουσιοδοτημένους οργανισμούς.';
    case 'manager':return 'Διαχείριση μόνο των εξουσιοδοτημένων ομάδων, πόλεων και τοποθεσιών σου.';
    case 'accountant':return 'Οικονομική εποπτεία, χωρίς αυτόματη άδεια είσπραξης ή αλλαγής τιμολογίου.';
    case 'cashier':return 'Πωλήσεις και βάρδιες μόνο στα επιτρεπόμενα ταμεία.';
    case 'editor':return 'Περιεχόμενο και αναθεωρήσεις μόνο στα διαθέσιμα εργαλεία.';
    case 'marketing':return 'Εξουσιοδοτημένα εργαλεία περιεχομένου, χωρίς πρόσβαση σε δεδομένα παιδιών.';
    case 'coach':return 'Οι αναθέσεις προπονητή ελέγχονται ξεχωριστά από τον λογαριασμό διαχειριστή.';
    case 'matchday_editor':return 'Εργαλεία αγώνων μόνο μετά από επαληθευμένη ανάθεση.';
    default:return 'Οι διαθέσιμες ενέργειες προκύπτουν από τα τρέχοντα δικαιώματά σου.';
  }
}
