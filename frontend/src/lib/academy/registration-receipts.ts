/** Verified Academy intake confirmations only.
 * A receipt is an opaque handle for a committed pending registration, not
 * evidence of approval, invoicing or payment. Treat malformed/upstream-
 * duplicated receipts as unavailable rather than a misleading success.
 */
export type AcademyRegistrationReceipts={
  receipt:string;
  receipts:string[];
};

const ID=/^[0-9a-f]{32}$/;
export function parseAcademyRegistrationReceipts(raw:unknown):AcademyRegistrationReceipts|null {
  if(raw===null||typeof raw!=='object'||Array.isArray(raw))return null;
  const value=raw as Record<string,unknown>;
  const {receipt,receipts}=value;
  if(typeof receipt!=='string'||!ID.test(receipt)||!Array.isArray(receipts)||
     receipts.length<1||receipts.length>5||receipts[0]!==receipt||
     !receipts.every((id:unknown)=>typeof id==='string'&&ID.test(id))||
     new Set(receipts).size!==receipts.length)return null;
  return {receipt,receipts};
}
