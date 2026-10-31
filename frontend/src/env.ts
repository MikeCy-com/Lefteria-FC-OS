import { defineEnvVars } from '@sveltejs/kit/env';

export const variables = defineEnvVars({
  INTERNAL_API_ORIGIN: {
    public: false,
    static: false,
    schema: (value: string | undefined) => value || 'http://api:8080',
    description: 'Server-only internal ASP.NET API origin; browser credentials are never forwarded.'
  }
});
