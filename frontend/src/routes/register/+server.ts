import {redirect} from '@sveltejs/kit';
// Source path retained; OS does not yet offer the legacy register workflow.
// Navigate only to a real public page. Never impersonate a completed transaction.
export function GET(): Response { redirect(307, '/academy/registration'); }
