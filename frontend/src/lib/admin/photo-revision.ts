import { writable } from 'svelte/store';

// In-memory render invalidation only. Neither private image bytes nor
// admin credentials are persisted in local/session storage.
export const adminPhotoRevision = writable(0);
