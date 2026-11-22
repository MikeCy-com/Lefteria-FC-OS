/**
 * Academy training season: September 1–June 30 (Cyprus local calendar).
 * These are deterministic estimates, NOT automatically issued invoices.
 * A verified guardian/payer, active approved placement and finance controls
 * must be checked again before any payable obligation is created.
 */
export const ACADEMY_SEASON_START_MONTH=9;
export const ACADEMY_SEASON_END_MONTH=6;
export const ACADEMY_SEASON_MONTHS=10;
export const ACADEMY_REGISTRATION_EUR=20;
export const ACADEMY_KIT_EUR=140;
export const ACADEMY_MANDATORY_PRINTING_EUR=10;
export const ACADEMY_CARD_PACKET_ESTIMATE_EUR=5;

export type AcademyMonthQuote={month:string;amountEur:number;prorated:boolean};
export type AcademySeasonQuote={
  season:string;children:number;monthlyFamilyRateEur:number;months:AcademyMonthQuote[];
  registrationEur:number;kitEur:number;printingEur:number;
  remainingTuitionEur:number;initialEstimatedEur:number;
};

export function academyFamilyRate(children:number):number {
  if (!Number.isSafeInteger(children)||children<1||children>20)
    throw new RangeError('Invalid family child count');
  return children===1?60:100+(children-2)*50;
}
function parseLocalDate(value:string):{year:number;month:number;day:number} {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value))
    throw new RangeError('Expected local YYYY-MM-DD');
  const [year,month,day]=value.split('-').map(Number);
  const d=new Date(Date.UTC(year,month-1,day));
  if(d.getUTCFullYear()!==year||d.getUTCMonth()+1!==month||
     d.getUTCDate()!==day)
    throw new RangeError('Invalid date');
  return {year,month,day};
}
export function academySeasonQuote(
  children:number,enrollmentLocalDate:string,seasonStartYear:number
):AcademySeasonQuote {
  if (!Number.isSafeInteger(seasonStartYear)||seasonStartYear<2020||seasonStartYear>2100)
    throw new RangeError('Invalid season');
  const start=seasonStartYear*100+9,end=(seasonStartYear+1)*100+6;
  const when=parseLocalDate(enrollmentLocalDate);
  const key=when.year*100+when.month;
  if(key>end)throw new RangeError('Enrollment after season end');
  const familyRate=academyFamilyRate(children);
  const months:AcademyMonthQuote[]=[];
  for(let index=0;index<ACADEMY_SEASON_MONTHS;index++){
    const year=seasonStartYear+(index>=4?1:0);
    const month=index<4?9+index:index-3;
    if(year*100+month<key)continue;
    const prorated=year===when.year&&month===when.month&&when.day>15;
    months.push({
      month:`${year}-${String(month).padStart(2,'0')}`,
      amountEur:prorated?familyRate/2:familyRate,prorated
    });
  }
  const registrationEur=children*ACADEMY_REGISTRATION_EUR;
  const kitEur=children*ACADEMY_KIT_EUR;
  const printingEur=children*ACADEMY_MANDATORY_PRINTING_EUR;
  const remainingTuitionEur=months.reduce((total,m)=>total+m.amountEur,0);
  return {
    season:`${seasonStartYear}/${seasonStartYear+1}`,children,
    monthlyFamilyRateEur:familyRate,months,registrationEur,kitEur,
    printingEur,remainingTuitionEur,
    initialEstimatedEur:(months[0]?.amountEur??0)+registrationEur+kitEur+printingEur
  };
}
