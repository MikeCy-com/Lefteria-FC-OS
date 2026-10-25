# ADR-0002: PostgreSQL is the rewrite system of record

- Status: accepted target direction; schema/importer design pending
- Date: 2026-10-25
- Context: the requested rewrite targets transactions and relationships in finance, memberships, academy and club operations. Production source uses MongoDB/Motor; no complete collection census or live dataset was inspected.
- Decision: the new application targets PostgreSQL with versioned migrations, constraints and transactions. EF Core/Npgsql is the proposed default; schema/provider choices remain subject to Phase 1 evidence. Old Mongo remains authoritative until approved cutover.
- Consequences: explicit relationships and transactional invariants when properly designed; high mapping, recovery, migration and operations cost.
- Constraints: stable legacy IDs, field-level transformations/provenance, checkpointed/idempotent imports, isolated rehearsals, reconciliation, no source writes, expand-and-contract. Decide bulk freeze versus change capture only after write patterns are known.
- Alternatives: keep Mongo permanently (does not meet selected target), blind document-to-table conversion (unsafe), immediate dual-write (consistency risk without proven need).
- Revisit if: verified requirements show PostgreSQL cannot meet a defined need; changing target needs explicit owner decision.
