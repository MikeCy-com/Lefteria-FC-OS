# ADR-0001: Start as a modular monolith

- Status: accepted target direction; implementation pending
- Date: 2026-10-25
- Context: identity, academy, football, finance, commerce, content, social, media and notification flows are connected. No independent business-service deployment requirement is verified.
- Decision: use a modular monolith in ASP.NET Core/C#. Feature modules own domain rules, use cases, persistence and interfaces. API and Worker may be separate hosts in one codebase where lifecycle requires it. Start with one PostgreSQL cluster.
- Consequences: simpler self-hosted operations and local transactions; boundaries need code review and tests. Modules do not scale independently at first.
- Alternatives: one giant API composition file (repeats coupling), microservices (network/operations burden without evidence), a project per entity (unnecessary nesting).
- Revisit when: measured scaling, release, security or availability requirements justify an independently operated service.
