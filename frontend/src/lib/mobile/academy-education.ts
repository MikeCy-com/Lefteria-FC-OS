// Editorial starter content for the OS parent education hub.
// NOT claimed to be reviewed/published by the Lefteria FC Academy.
// No private child data or inferred age from an authenticated roster record.
export type EducationAudience = 'parent' | 'player';
export type EducationStage = '6-9' | '10-12' | '13-15' | '16-18';
export type EducationTopic = 'support' | 'psychology' | 'communication' | 'wellbeing';

export interface AcademyEducationArticle {
  readonly id: string;
  readonly audience: EducationAudience;
  readonly stage: EducationStage;
  readonly topic: EducationTopic;
  readonly title: string;
  readonly summary: string;
  readonly points: readonly string[];
  readonly discussion: string;
}

export const EDUCATION_STAGES: readonly {id: EducationStage; title: string; hint: string}[] = [
  {id:'6-9',title:'6–9 ετών',hint:'Παιχνίδι, χαρά και αίσθημα ασφάλειας'},
  {id:'10-12',title:'10–12 ετών',hint:'Συνεργασία, μάθηση και αυτοπεποίθηση'},
  {id:'13-15',title:'13–15 ετών',hint:'Αλλαγές, ταυτότητα και διαχείριση πίεσης'},
  {id:'16-18',title:'16–18 ετών',hint:'Αυτονομία, στόχοι και ισορροπία'}
];

export const EDUCATION_TOPICS: readonly {id: EducationTopic; title: string}[] = [
  {id:'support',title:'Ο ρόλος του γονέα'},
  {id:'psychology',title:'Ψυχολογία αθλητή'},
  {id:'communication',title:'Επικοινωνία'},
  {id:'wellbeing',title:'Ευεξία και ισορροπία'}
];

export const ACADEMY_EDUCATION_ARTICLES: readonly AcademyEducationArticle[] = [
  {
    id:'parent-6-9-joy',audience:'parent',stage:'6-9',topic:'support',
    title:'Η χαρά προηγείται του αποτελέσματος',
    summary:'Σε αυτές τις ηλικίες, το ποδόσφαιρο είναι πρώτα παιχνίδι και ένας χώρος όπου το παιδί μπορεί να αισθάνεται ότι ανήκει.',
    points:[
      'Ρώτησε «Τι απόλαυσες σήμερα;» αντί για το σκορ ή τα γκολ.',
      'Επιβράβευσε την προσπάθεια, τη συνεργασία και την περιέργεια.',
      'Άφησε τον προπονητή να καθοδηγεί την προπόνηση και απόφυγε οδηγίες από την εξέδρα.'
    ],
    discussion:'Ποιο ήταν το πιο διασκεδαστικό μέρος της προπόνησης;'
  },
  {
    id:'player-6-9-mistakes',audience:'player',stage:'6-9',topic:'psychology',
    title:'Τα λάθη είναι μέρος του παιχνιδιού',
    summary:'Δεν χρειάζεται να πετυχαίνεις κάθε πάσα. Όταν δοκιμάζεις κάτι καινούριο, μαθαίνεις.',
    points:[
      'Δοκίμασε ξανά μια δύσκολη κίνηση χωρίς να βιάζεσαι.',
      'Πες «Μπράβο» σε συμπαίκτη που προσπαθεί.',
      'Όταν κάτι σε στενοχωρεί, μίλησε σε προπονητή ή σε έναν ενήλικα που εμπιστεύεσαι.'
    ],
    discussion:'Τι καινούριο δοκίμασες, ακόμη κι αν δεν πέτυχε;'
  },
  {
    id:'parent-10-12-voice',audience:'parent',stage:'10-12',topic:'communication',
    title:'Άκου την εμπειρία του παιδιού',
    summary:'Η ανοιχτή επικοινωνία βοηθά το παιδί να μοιραστεί ενθουσιασμό, ανησυχίες και φιλίες χωρίς φόβο κριτικής.',
    points:[
      'Άκου πρώτα και ζήτησε άδεια πριν δώσεις συμβουλή.',
      'Μην συγκρίνεις το παιδί με άλλους αθλητές ή με αδέλφια.',
      'Για ζητήματα χρόνου συμμετοχής, συζήτησε ήρεμα με τον προπονητή εκτός αγώνα.'
    ],
    discussion:'Τι θα ήθελες να καταλαβαίνω καλύτερα για την ομάδα σου;'
  },
  {
    id:'player-10-12-confidence',audience:'player',stage:'10-12',topic:'psychology',
    title:'Η αυτοπεποίθηση μεγαλώνει με την εξάσκηση',
    summary:'Μπορείς να βελτιώνεσαι στον δικό σου ρυθμό. Δεν χρειάζεται να είσαι ο καλύτερος σε όλα.',
    points:[
      'Επίλεξε έναν μικρό στόχο που αφορά τη δική σου προσπάθεια.',
      'Παρατήρησε μια δεξιότητα που βελτιώθηκε μέσα στην εβδομάδα.',
      'Θυμήσου ότι η αξία σου δεν εξαρτάται από το αποτέλεσμα ενός αγώνα.'
    ],
    discussion:'Ποιο μικρό βήμα σε έκανε περήφανο σήμερα;'
  },
  {
    id:'parent-13-15-pressure',audience:'parent',stage:'13-15',topic:'psychology',
    title:'Υποστήριξη χωρίς υπερβολική πίεση',
    summary:'Στην εφηβεία οι αλλαγές είναι πολλές. Χρειάζεται χώρος για απορίες, διαφορετικά συναισθήματα και προσωπικές επιλογές.',
    points:[
      'Απόφυγε την κριτική αμέσως μετά από έναν δύσκολο αγώνα.',
      'Ρώτησε πώς θέλει να το υποστηρίξεις και σεβάσου την απάντηση.',
      'Αν η πίεση ή η στενοχώρια επιμένουν, αναζήτησε βοήθεια από κατάλληλο επαγγελματία με το παιδί.'
    ],
    discussion:'Πότε σε βοηθά να μιλάμε για έναν αγώνα και πότε προτιμάς λίγο χρόνο;'
  },
  {
    id:'player-13-15-emotions',audience:'player',stage:'13-15',topic:'wellbeing',
    title:'Διαχειρίζομαι τα συναισθήματα στον αγώνα',
    summary:'Η απογοήτευση, το άγχος και ο ενθουσιασμός είναι ανθρώπινα. Μπορείς να τα αναγνωρίζεις και να βρίσκεις τρόπους να συνεχίζεις.',
    points:[
      'Όταν νιώθεις πίεση, επικεντρώσου στην επόμενη μικρή ενέργεια του παιχνιδιού.',
      'Μίλησε με προπονητή ή έναν έμπιστο ενήλικα για ό,τι σε απασχολεί.',
      'Η ξεκούραση, οι φίλοι και το σχολείο έχουν σημασία όσο και το ποδόσφαιρο.'
    ],
    discussion:'Τι σε βοηθά να αισθάνεσαι υποστήριξη όταν έχεις μια δύσκολη μέρα;'
  },
  {
    id:'parent-16-18-independence',audience:'parent',stage:'16-18',topic:'support',
    title:'Περισσότερη αυτονομία, σταθερή υποστήριξη',
    summary:'Ο μεγαλύτερος έφηβος μπορεί να συμμετέχει περισσότερο στις αποφάσεις για τους στόχους του, χωρίς να χάνει τη στήριξη της οικογένειας.',
    points:[
      'Συζήτησε στόχους που θέτει ο ίδιος ο αθλητής και όχι μόνο οι ενήλικες.',
      'Βοήθησε στην οργάνωση σχολείου, ξεκούρασης και προπονήσεων χωρίς να απαιτείται τελειότητα.',
      'Δώσε χώρο για άλλα ενδιαφέροντα και για το ενδεχόμενο αλλαγής αθλητικών προτεραιοτήτων.'
    ],
    discussion:'Πώς θα ήθελες να συμμετέχω στις αποφάσεις για το ποδόσφαιρό σου;'
  },
  {
    id:'player-16-18-balance',audience:'player',stage:'16-18',topic:'wellbeing',
    title:'Στόχοι με ισορροπία',
    summary:'Οι αθλητικές φιλοδοξίες μπορούν να συνυπάρχουν με τη μάθηση, τη φιλία, την ανάπαυση και την προσωπική σου ζωή.',
    points:[
      'Χώρισε έναν μεγάλο στόχο σε μικρά, ρεαλιστικά βήματα.',
      'Συζήτησε τον φόρτο σου με προπονητή και οικογένεια αν νιώθεις ότι πιέζεσαι.',
      'Μην ορίζεις την ταυτότητα ή την αξία σου μόνο από την απόδοση.'
    ],
    discussion:'Ποιο μέρος της ζωής σου χρειάζεται περισσότερο χώρο αυτή την εβδομάδα;'
  }
];

export function academyEducationFor(
  stage: EducationStage,
  audience: EducationAudience | 'all' = 'all'
): readonly AcademyEducationArticle[] {
  return ACADEMY_EDUCATION_ARTICLES.filter(article =>
    article.stage === stage && (audience === 'all' || audience === article.audience));
}
