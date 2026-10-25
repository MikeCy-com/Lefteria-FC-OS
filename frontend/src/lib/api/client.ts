import createClient from 'openapi-fetch';
import type { paths } from './schema';

export function createApiClient(options: { baseUrl: string; fetch?: typeof globalThis.fetch }) {
  return createClient<paths>({
    baseUrl: options.baseUrl,
    fetch: options.fetch ?? globalThis.fetch,
    cache: 'no-store'
  });
}
