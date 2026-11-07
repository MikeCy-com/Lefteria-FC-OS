import {redirect} from '@sveltejs/kit';
// Source path retained; OS does not yet offer the legacy profile workflow.
// Navigate only to a real public page. Never impersonate a completed transaction.
export function GET(): Response { redirect(307, '/login'); }
