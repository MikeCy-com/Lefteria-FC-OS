export type CashierStockItem={product_id:string;variant_id:string;product_name:string;variant_name:string;price_cents:number;available:number};
const key=(item:CashierStockItem)=>item.product_id+':'+item.variant_id;
export function filterCashierStock<T extends CashierStockItem>(items:readonly T[],query:string,availableOnly:boolean):T[]{
  const normalize=(text:string)=>text.normalize('NFD').replace(/\p{M}/gu,'').toLocaleLowerCase('el-CY');
  const term=normalize(query.trim().slice(0,100));
  return items.filter(item=>(!availableOnly||item.available>0)&&normalize(item.product_name+' '+item.variant_name).includes(term));
}
export function cashierBasketState<T extends CashierStockItem>(items:readonly T[],selected:Readonly<Record<string,number>>){
  const byKey=new Map(items.map(item=>[key(item),item]));
  const picked:{item:T;quantity:number}[]=[];
  const invalidKeys:string[]=[];
  for(const [id,quantity] of Object.entries(selected)){
    if(quantity===0)continue;
    const item=byKey.get(id);
    if(!item||!Number.isSafeInteger(quantity)||quantity<1||quantity>Math.min(99,item.available)){
      invalidKeys.push(id);continue;
    }
    picked.push({item,quantity});
  }
  // Preserve catalog order for existing idempotency fingerprints.
  picked.sort((a,b)=>items.indexOf(a.item)-items.indexOf(b.item));
  const estimate=picked.reduce((sum,row)=>sum+row.item.price_cents*row.quantity,0);
  const units=picked.reduce((sum,row)=>sum+row.quantity,0);
  return {picked,invalidKeys,estimate,units,valid:!invalidKeys.length&&picked.length>=1&&picked.length<=30&&estimate<=100000000};
}
