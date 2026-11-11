export type TrainingFee = {
  id:string;player_id:string;child_name:string;fee_month:string;
  amount_cents:number;currency:'EUR';status:'issued'|'paid'|'review_required';
  payment_status:string;checkout_url:string|null;due_date:string;
};
export type ParentTrainingFees = {
  fees:TrainingFee[];preferred_day:number;save_card_requested:boolean;
  automatic_collection_requested:boolean;automatic_collection_active:false;
  saved_card_available:false;
};

const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const month=/^20\d{2}-(?:0[1-9]|1[0-2])-01$/;
const date=/^20\d{2}-(?:0[1-9]|1[0-2])-(?:0[1-9]|[12]\d|3[01])$/;
const paymentStates=['not_created','initiating','ready','awaiting_settlement','paid','failed','review_required'];

export function payablHostedLink(url:unknown):url is string {
  if(typeof url!=='string'||url.length>2048)return false;
  try {
    const parsed=new URL(url);
    return parsed.protocol==='https:' && !parsed.username && !parsed.password &&
      parsed.port==='' && (parsed.hostname==='sandbox.payabl.com' ||
      parsed.hostname==='pay4.sandbox.payabl.com');
  } catch {return false;}
}

export function validTrainingFee(value:unknown):value is TrainingFee {
  if(!value||typeof value!=='object'||Array.isArray(value))return false;
  const f=value as Record<string,unknown>;
  return typeof f.id==='string'&&uuid.test(f.id) &&
    typeof f.player_id==='string'&&f.player_id.length>0&&f.player_id.length<=128 &&
    typeof f.child_name==='string'&&f.child_name.length<=200 &&
    typeof f.fee_month==='string'&&month.test(f.fee_month) &&
    typeof f.due_date==='string'&&date.test(f.due_date) &&
    typeof f.amount_cents==='number'&&Number.isSafeInteger(f.amount_cents) &&
      f.amount_cents>=1&&f.amount_cents<=100000000 &&
    f.currency==='EUR' &&
    (f.status==='issued'||f.status==='paid'||f.status==='review_required') &&
    typeof f.payment_status==='string'&&paymentStates.includes(f.payment_status) &&
    (f.checkout_url===null ||
       (f.status==='issued' && f.payment_status==='ready' && payablHostedLink(f.checkout_url)));
}

export function validParentTrainingFees(value:unknown):value is ParentTrainingFees {
  if(!value||typeof value!=='object'||Array.isArray(value))return false;
  const row=value as Record<string,unknown>;
  return Array.isArray(row.fees)&&row.fees.length<=100&&row.fees.every(validTrainingFee) &&
    typeof row.preferred_day==='number'&&Number.isInteger(row.preferred_day) &&
      row.preferred_day>=1&&row.preferred_day<=31 &&
    typeof row.save_card_requested==='boolean' &&
    typeof row.automatic_collection_requested==='boolean' &&
    row.automatic_collection_active===false&&row.saved_card_available===false;
}
