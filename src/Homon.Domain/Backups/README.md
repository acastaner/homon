# Backups — module slot

Not implemented yet. Plan 008.

**What it owns.** Proof that the backup jobs ran. A backup script (restic on a server,
today) finishes and `POST`s a report — job name, started/finished, success or failure, a
log excerpt — authenticated with an API key. The dashboard shows a card per job: last run,
outcome, and turns orange/red when a job is late or failed.

**Requirements.** API keys are minted and revoked by the administrator (the auth plumbing
for them is already in place: `Homon.Domain/Auth/ApiKey.cs`, `create-api-key`, the
`ApiKey` authentication scheme). A job is "late" when no report has arrived within its
expected interval plus a grace period.

**Shape.** `BackupJob` (Id, Name, ExpectedInterval, Grace), `BackupRun` (JobId,
StartedAt, FinishedAt, Succeeded, Summary, LogExcerpt, ReportedByKeyId).

**Where the rest lands.** `Homon.Api/Endpoints/BackupEndpoints.cs` (the report endpoint
requires the `ApiKey` policy), `ApiKeyEndpoints.cs` (admin CRUD),
`Homon.Web/src/pages/admin-api-keys-page.tsx` (already a placeholder), and a `curl` snippet
in the docs for the scripts to call.
