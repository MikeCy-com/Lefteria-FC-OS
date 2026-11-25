export type RegistrationQueue='action'|'all'|'pending'|'under_review'|
  'needs_information'|'approved'|'closed';
export interface RegistrationQueueRecord{
  id:string;
  status:string;
  applicant_first_name:string;
  applicant_last_name:string;
  guardian_name:string;
  onboarded_player_id?:string|null;
}

// Filter only the current server-authorized page; no cross-city counts,
// inferred parent OTP grants or client-side authorization decisions.
export function registrationNeedsAttention(row:RegistrationQueueRecord):boolean{
  return ['pending','under_review','needs_information'].includes(row.status)||
    (row.status==='approved'&&!row.onboarded_player_id);
}
export function registrationNextStep(row:RegistrationQueueRecord):string{
  switch(row.status){
    case 'pending':return 'Απαιτείται έναρξη ελέγχου της αίτησης.';
    case 'under_review':return 'Ελέγξτε τη συγκατάθεση και επιλέξτε επαληθευμένο τμήμα, αν επιτρέπεται.';
    case 'needs_information':return 'Αναμονή πρόσθετων στοιχείων από τον κηδεμόνα.';
    case 'approved':return row.onboarded_player_id
      ?'Η ένταξη στο ρόστερ καταγράφηκε. Ο έλεγχος κηδεμόνα και η πρόσβαση εφαρμογής είναι ξεχωριστά.'
      :'Η αίτηση εγκρίθηκε αλλά δεν έχει συνδεθεί με εγγραφή παίκτη.';
    case 'rejected':return 'Η αίτηση απορρίφθηκε. Διατηρείται για ιστορικό.';
    case 'withdrawn':return 'Η αίτηση αποσύρθηκε. Διατηρείται για ιστορικό.';
    default:return 'Επαληθεύστε την τρέχουσα κατάσταση πριν από αλλαγές.';
  }
}
export function filterRegistrations<T extends RegistrationQueueRecord>(
  rows:readonly T[],queue:RegistrationQueue,search:string
):T[]{
  const term=search.trim().toLocaleLowerCase('el-GR');
  return rows.filter(row=>{
    const matchQueue=queue==='all' ||
      (queue==='action'&&registrationNeedsAttention(row)) ||
      (queue==='closed'&&['withdrawn','rejected'].includes(row.status)) ||
      row.status===queue;
    if(!matchQueue)return false;
    return !term || [row.applicant_first_name+' '+row.applicant_last_name,
      row.guardian_name,row.id].some(text=>
      text.toLocaleLowerCase('el-GR').includes(term));
  });
}
export function registrationPageSummary(rows:readonly RegistrationQueueRecord[]){
  // These counts refer ONLY to the authorized current API page, never to
  // the organization, all children, or a family outside the user's grants.
  return {
    loaded:rows.length,
    needsAttention:rows.filter(registrationNeedsAttention).length,
    linked:rows.filter(r=>r.status==='approved'&&!!r.onboarded_player_id).length,
    awaitingGuardianInfo:rows.filter(r=>r.status==='needs_information').length
  };
}
