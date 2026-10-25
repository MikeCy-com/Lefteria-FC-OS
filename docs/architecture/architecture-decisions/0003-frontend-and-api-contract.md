# ADR-0003: SvelteKit/TypeScript with OpenAPI-generated client

- Status: accepted target direction; route and contract details pending
- Date: 2026-10-25
- Context: current React/JavaScript serves public, CMS and role-specific PWA/TWA journeys. Owner selected SvelteKit/TypeScript and a consistent API contract.
- Decision: use SvelteKit + TypeScript. ASP.NET Core publishes versioned OpenAPI; CI generates the TypeScript client from that contract. Generated files have one source and are not hand-edited.
- Compatibility: preserve public URLs, Greek UX, redirects, /app paths, query tabs, notification destinations and old API clients until inventory, migration and deprecation evidence permit change.
- Consequences: typed contracts and route structure; SSR adds Node runtime, cache/auth decisions and PWA/TWA validation. Pin generator and check drift.
- Alternatives: keep React/JS (does not meet selected target), duplicate hand-written types (contract drift), rewrite URLs all at once (unnecessary breakage).
- Revisit when: staging shows SvelteKit/adapter fails hosting, performance, security or PWA requirements.
