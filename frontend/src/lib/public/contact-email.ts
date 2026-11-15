/** Safely compose, but do not silently submit, a public contact email. */
export function safeContactEmail(value: unknown): string | null {
  if (typeof value !== 'string' || value.length > 254) return null;
  return /^[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,24}$/.test(value) ? value : null;
}

export type ContactEmailDraft = {
  name: string; replyEmail: string; organizationLabel: string;
  subject: string; message: string;
};
export function contactEmailDraftHref(recipient: unknown,draft: ContactEmailDraft): string | null {
  const email = safeContactEmail(recipient);
  if (!email) return null;
  const name = draft.name.trim().slice(0,120);
  const replyEmail = safeContactEmail(draft.replyEmail.trim());
  const topic = draft.subject.trim().slice(0,120);
  const message = draft.message.trim().slice(0,2000);
  if (!name || !replyEmail || !topic || !message) return null;
  const body = [
    'Ονοματεπώνυμο: ' + name,
    'Email απάντησης: ' + replyEmail,
    'Ενότητα: ' + draft.organizationLabel.trim().slice(0,80),'',
    message
  ].join('\n');
  return 'mailto:' + email + '?subject='
    + encodeURIComponent(topic + ' · ' + draft.organizationLabel)
    + '&body=' + encodeURIComponent(body);
}
