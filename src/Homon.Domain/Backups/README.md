# Backups — module slot, retired

**Superseded by the Messaging module (plan 021), 2026-10-01.** Nothing compiles from this folder
and nothing will; it is kept so that a reader following a reference to it lands here rather than
nowhere.

What this slot was for — "proof that the backup jobs ran" — is done, generically. A backup script
reports to `POST /api/v1/messages` with its own API key, exactly as planned, but Homon no longer
models a *backup job*: it models a **reporter** that files **messages**, and a backup is one with
`category: backup`. The same mechanism carries a NAS array check, a certificate expiry sweep or
anything else a script knows the answer to, with no new code and no migration.

Plan 008's load-bearing decisions survived the generalisation and are carried over by name in plan
021: the reporter must exist before it can report, bodies are truncated at 64 KiB and never
rejected, lateness is derived at read time rather than tracked as its own state, and the report
endpoint is rate-limited per key. What did not survive was the backup-shaped part — `BackupJob`,
`BackupRun`, `ExpectedInterval` and `Grace` — because the reporter declares its own deadline and
knows its own tolerance better than Homon can guess them.

Read instead:

- `src/Homon.Domain/Messaging/README.md` — what the module owns.
- `docs/ARCHITECTURE.md` §3.25–§3.27 — the decisions and what was rejected.
- `docs/message-reporting.md` — what to put in a script.
