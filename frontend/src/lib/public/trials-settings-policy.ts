import type { components } from '../api/schema';

export type FirstTeamTrialSettings = components['schemas']['FirstTeamTrialsSettings'];

const sourceDefaults: FirstTeamTrialSettings = {
  open: false,
  headline: 'Ετοιμος να Ξεκινησεις το Ταξιδι σου;',
  subtitle: 'Γίνε μέλος της πρώτης ομάδας LEFTERIA FC και κάνε το πρώτο βήμα για να γίνεις επαγγελματίας ποδοσφαιριστής.',
  button_text: 'Δηλωσε Συμμετοχη για Δοκιμαστικα',
  closed_message: 'Οι εγγραφές για δοκιμαστικά είναι προσωρινά κλειστές. Παρακαλούμε δοκιμάστε ξανά σύντομα.'
};

// A future source-backed public read may customize harmless copy. It cannot
// open enrollment in this frontend until consent and submission are authorized.
// Do not allow a malformed upstream to inject excessive markup/content.
function safeText(value: unknown, fallback: string, max: number): string {
  return typeof value === 'string' && value.trim() === value &&
    value.length >= 1 && value.length <= max &&
    !/[\u0000-\u001f\u007f]/.test(value) ? value : fallback;
}

export function verifiedClosedTrialSettings(value: unknown): FirstTeamTrialSettings {
  const row = value && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown> : {};
  return {
    open: false,
    headline: safeText(row.headline, sourceDefaults.headline, 180),
    subtitle: safeText(row.subtitle, sourceDefaults.subtitle, 500),
    button_text: safeText(row.button_text, sourceDefaults.button_text, 100),
    closed_message: safeText(row.closed_message, sourceDefaults.closed_message, 500)
  };
}

