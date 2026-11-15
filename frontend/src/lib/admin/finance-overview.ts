export type FinanceOwner = 'lefteria_fc' | 'lefteria_fc_academy';
export type MoneyCounts = {
  count: number;
  issued_cents: number;
  paid_cents?: number;
  collected_cents?: number;
  outstanding_cents: number;
  overdue_cents?: number;
  review_cents?: number;
  cancelled_count?: number;
};
export type FinanceOverview = {
  organization_id: FinanceOwner;
  currency: 'EUR';
  report_basis: 'all_time_separate_ledgers';
  environment: 'sandbox';
  generated_at: string;
  expenses_available: false;
  reconciliation_available: false;
  training_fees: { count: number; issued_cents: number; paid_cents: number;
    outstanding_cents: number; review_cents: number };
  general_charges: { count: number; cancelled_count: number; issued_cents: number;
    collected_cents: number; outstanding_cents: number; overdue_cents: number };
  unattached_payabl_requests: { count: number; settled_cents: number;
    pending_cents: number; review_cents: number };
};

const amount = (v: unknown): v is number =>
  typeof v === 'number' && Number.isSafeInteger(v) && v >= 0;

function numbers(value: unknown, keys: readonly string[]): value is Record<string, number> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const row = value as Record<string, unknown>;
  return keys.every(key => amount(row[key]));
}

/** Reject mismatched organizations, incomplete snapshots and impossible totals. */
export function verifiedFinanceOverview(
  value: unknown, owner: FinanceOwner
): value is FinanceOverview {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const row = value as Record<string, unknown>;
  if (row.organization_id !== owner || row.currency !== 'EUR' ||
      row.environment !== 'sandbox' || row.report_basis !== 'all_time_separate_ledgers' ||
      row.expenses_available !== false || row.reconciliation_available !== false ||
      typeof row.generated_at !== 'string' || !Number.isFinite(Date.parse(row.generated_at))) {
    return false;
  }
  const f = row.training_fees, c = row.general_charges, p = row.unattached_payabl_requests;
  if (!numbers(f, ['count','issued_cents','paid_cents','outstanding_cents','review_cents']) ||
      !numbers(c, ['count','cancelled_count','issued_cents','collected_cents',
        'outstanding_cents','overdue_cents']) ||
      !numbers(p, ['count','settled_cents','pending_cents','review_cents'])) return false;
  return f.issued_cents === f.paid_cents + f.outstanding_cents &&
    f.review_cents <= f.outstanding_cents &&
    c.issued_cents === c.collected_cents + c.outstanding_cents &&
    c.overdue_cents <= c.outstanding_cents;
}
