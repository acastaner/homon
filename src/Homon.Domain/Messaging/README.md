# Messaging — module slot

Implemented. Plan 021. Supersedes the `Backups` slot (plan 008).

**What it owns.** The ingestion point for push reports. A reporter — a shell script, a cron
job, an agent on another host — `POST`s one JSON document to `/api/v1/messages` with its API
key: what it is called, what it did, the text that proves it, and when it intends to report
again. This module owns the two entities, the vocabularies, the recurrence contract, the
32-day retention, and the derivation that turns the newest message into a probe's state.

**What it deliberately does not own.** Any knowledge of what a reporter *does*. There is no
restic in here, no exit codes, no log parsing, no notion of a "job". The reporter decides
whether it succeeded and how long it should be trusted; Homon stores that and watches the
clock. A backup is `category: backup` and nothing more.

**The monitor is not here either.** It is `ProbeKind.Message` in `Monitoring/` — a `Probe`
row whose `Host` holds the reporter's identifier. That is what buys probe groups, display
order, pause, the observation history, the uptime ratio, the dashboard sections and (when
plan 009 lands) email alerting, with no second status model to keep in step. See
`docs/ARCHITECTURE.md` §3.25.

**Shape.**

- `Reporter` — `Identifier` (generated, immutable, unique), `Name` (unique, case-insensitively),
  `Description`, `BodyVisibility`, `ApiKeyId` (one key, one reporter).
- `Message` — append-only: `Status`, `Category`, `Body`, `Truncated`, `ReceivedAt`,
  `NextExpectedAt`, `RecurrenceDeclaration`, `ReportedByKeyId`. A correction is a second
  message, never an edit.
- `MessageProbeEvaluator.Evaluate(MessageSnapshot?, now)` — the pure derivation, and the seam
  plan 009 reads. Overdue beats the reported status.
- `MessageRecurrence.Resolve(...)` — an ISO 8601 duration or an absolute instant, into one
  deadline.
- `MessageBody.Truncate(...)` — the last 64 KiB, never a refusal.
- `ReporterIdentifier.New()` — the generated handle.

**Where the rest lives.** `Homon.Infrastructure/Messaging/` (the probe runner, the retention
sweep, the options), `Homon.Infrastructure/Persistence/Configurations/{Reporter,Message}Configuration.cs`,
`Homon.Api/Endpoints/{Message,Reporter,ApiKey}Endpoints.cs`,
`Homon.Web/src/pages/admin-reporters-page.tsx`, and `docs/message-reporting.md` for the people
writing the scripts.

**Known constraints.**

- A message body is reader-visible only if its reporter says so, and then only its latest, and
  then only its first 2000 characters. The full body has exactly one route, and it is
  administrator-only.
- A reporter that declares no recurrence can never be overdue — only failed. The admin page has
  to say so, or it looks monitored when it is not.
- The retention sweep never deletes a reporter's newest message, however old. Deleting it would
  take `NextExpectedAt` with it and turn a loud red "overdue by 40 days" into a quiet grey
  "no report received yet".
- A reporter cannot be deleted while a probe names its identifier.
