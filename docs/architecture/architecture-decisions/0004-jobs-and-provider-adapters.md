# ADR-0004: Durable jobs and provider-neutral integrations

- Status: accepted target direction; provider/job inventory pending
- Date: 2026-10-25
- Context: current reminders and provider effects run in the FastAPI lifecycle. Existing integrations include payments, SMS, push, social, backups, object storage, FFmpeg and AI libraries; exact call sites/retry semantics are not fully mapped.
- Decision: use .NET Worker Services for long-running/scheduled work as it migrates. First evaluate PostgreSQL outbox/job tables with leases, retries, idempotency and terminal failure visibility. Put Stripe, Twilio, Web Push/VAPID, Meta, Drive, storage and AI behind adapters. Keep FFmpeg. Retain Python only for mapped AI/ML/media tasks with concrete benefit.
- Consequences: durable observable work without adding Redis or a paid broker initially; queue availability depends on PostgreSQL. At-least-once delivery requires idempotent handlers and provider-safe replay.
- Alternatives: process-local fire-and-forget (loss/duplicate risk), premature broker (extra operations), rewrite all AI/media in C# (not justified).
- Revisit when: queue metrics exceed PostgreSQL design or verified workflow benefits from separate Python runtime.
