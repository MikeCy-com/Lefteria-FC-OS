export type AttendanceRow={
  rsvp:'going'|'not_going'|'maybe'|'no_response'|null;
  attendance:'present'|'absent'|null;
};
export type AttendanceSummary={
  total:number;responses:{going:number;not_going:number;maybe:number;no_response:number};
  actual:{present:number;absent:number;not_recorded:number};
};
export function summarizeAcademyAttendance(rows:readonly AttendanceRow[]):AttendanceSummary{
  const result:AttendanceSummary={total:rows.length,
    responses:{going:0,not_going:0,maybe:0,no_response:0},
    actual:{present:0,absent:0,not_recorded:0}};
  for(const row of rows){
    const response=row.rsvp??'no_response';
    result.responses[response]++;
    result.actual[row.attendance??'not_recorded']++;
  }
  return result;
}
