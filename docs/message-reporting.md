# Reporting to Homon from a script

Homon can watch a service by poking it (ping, HTTP). It can also watch something that only speaks
when it has something to say — a nightly backup, a storage array check, an agent — by letting that
thing **report in**. If a reporter says "I will report again within 25 hours" and nothing arrives,
its row on the dashboard goes red on its own.

All the judgement is on your side. Homon stores what you send, derives a status from it, and
watches the clock. It knows nothing about restic, exit codes or log formats.

See `docs/ARCHITECTURE.md` §3.25–§3.27 for why it is built this way.

## Set one up

1. Sign in, go to **Admin → Reporters**, and add one. Give it a name you will recognise on the
   dashboard.
2. Homon generates its identifier and mints the one API key paired with it. **The key is shown
   once.** Copy it somewhere the script can read.
3. Decide **Message visibility**. The default, *administrators only*, keeps message bodies off the
   dashboard — the right answer for anything that prints paths or hostnames. Choose *everyone who
   can read the dashboard* only for a reporter whose message is a one-line summary.
4. Go to **Admin → Probes**, add a probe of kind **Message**, and choose the reporter. That probe
   is what appears on the dashboard, in whatever group you put it in, with whatever name you give
   it.

Deleting a reporter is refused while a probe watches it; delete the probe first.

## The request

```bash
# The key lives in a file, read at call time. Never interpolate it on the command line: argv is
# visible in `ps` to every user on the host, and shell history outlives the terminal.
curl -fsS -X POST https://homon.example.invalid/api/v1/messages \
  -H "Authorization: Bearer $(cat /etc/homon/example-backup.key)" \
  -H 'Content-Type: application/json' \
  -d '{
        "name": "example-host backup",
        "description": "Nightly restic backup",
        "status": "success",
        "category": "backup",
        "recurrence": "PT25H",
        "message": "snapshot a1b2c3 — 412 files, 1.2 GiB in 41s"
      }'
```

`Content-Type: application/json` is not optional — it is the CSRF guard, and omitting it is a 415.
`X-Api-Key: hmn_…` works in place of the `Authorization` header if that is easier.

| Field | Required | What it is |
| --- | --- | --- |
| `name` | yes | What this reporter calls itself, at most 100 characters. Shown on the admin page. |
| `status` | no | `success`, `warning`, `failure`, `unknown` or `none`. Omitted means `none`. |
| `category` | no | A lower-case kebab-case slug, at most 40 characters — `backup`, `storage`, `nas-array`. Omitted means `other`. Any slug is accepted; Homon does not need teaching about a new one. |
| `message` | no | Free text. Never rejected for length: over 64 KiB, the **last** 64 KiB is kept behind a truncation marker. |
| `description` | no | A sentence about this message, at most 280 characters. |
| `recurrence` | no | An ISO 8601 duration, measured from when Homon receives the message. |
| `expectNextBy` | no | An absolute instant instead of a duration. Never both. |
| `identifier` | no | Your reporter's identifier. Only ever checked for a match — the key already says who you are — so it is a typo guard, and the wrong one is a 400. |

A `201` answers with what Homon understood, including the deadline it just set:

```json
{ "id": 42, "identifier": "9H4KQ2VBMTR4WXYZ", "status": "success", "category": "backup",
  "receivedAt": "2026-10-01T04:30:00+00:00", "nextExpectedAt": "2026-10-02T05:30:00+00:00",
  "recurrence": "PT25H", "truncated": false }
```

Log `nextExpectedAt` on the first run. It is the cheapest way to notice a wrong `recurrence` then
rather than during the first outage.

## What each status does

| `status` | On the dashboard | Counts as |
| --- | --- | --- |
| `success` | green, "Succeeded" | up |
| `warning` | orange, "Warning" | down, for the uptime figure |
| `failure` | red, "Failed" | down |
| `none` | green, "Reported" | up — it checked in, and claims nothing more |
| `unknown` | grey, "Unknown" | neither; no observation is recorded at all |

Whatever the status, **being late beats all of it**: past `nextExpectedAt` the row reads red and
"Overdue", because a success from three days ago says nothing about today.

## Recurrence

`PT25H` is 25 hours. `P1D` is a day. `PT90M` is ninety minutes. `P5Y` is five years.

`XmlConvert.ToTimeSpan` parses these, and its fixed-length rule is the one surprise worth knowing:
**a month counts as 30 days and a year as 365**. `P1M` is thirty days, not "the first of next
month". If you need a real calendar — "by 03:00 next Monday" — compute it yourself and send
`expectNextBy` with an absolute instant instead.

Declare a window **wider than your period**, with the tolerance baked in: `PT25H` for a nightly
job, not `P1D`. Homon adds no grace of its own, deliberately — you know how late is too late and it
does not.

**Omitting both fields means this reporter can never be overdue.** Only its own `failure` or
`warning` will ever take it off green. That is a legitimate choice for something that reports
irregularly, but it is the difference between monitoring and bookkeeping.

## A wrapper, end to end

```bash
#!/usr/bin/env bash
# Runs a backup and reports the outcome either way. The reporting must not be able to fail the
# backup, and a failed backup must still be reported — hence `set -o pipefail` without `-e`, and
# the explicit exit code capture.
set -uo pipefail

KEY_FILE=/etc/homon/example-backup.key
HOMON=https://homon.example.invalid

log=$(restic backup /srv/data 2>&1)
code=$?

if [ "$code" -eq 0 ]; then
  status=success
else
  status=failure
fi

# --argjson/--arg keeps the log out of the shell's quoting rules entirely; a backup log containing
# a quote or a newline would otherwise produce invalid JSON on exactly the night it matters.
jq -n --arg status "$status" --arg body "$log" \
  '{name: "example-host backup", status: $status, category: "backup",
    recurrence: "PT25H", message: $body}' \
  | curl -fsS -X POST "$HOMON/api/v1/messages" \
      -H "Authorization: Bearer $(cat "$KEY_FILE")" \
      -H 'Content-Type: application/json' \
      --data-binary @- \
  || logger -t example-backup "could not report to Homon"

exit "$code"
```

Put it behind a systemd timer or cron. One reporter per thing you want a separate row for: a host
that backs up *and* has an array to watch gets two reporters, two keys and two probes.

## When something is wrong

| Response | What it means |
| --- | --- |
| `401` | The key is unknown, revoked, or expired. Replace it on the reporter's page. |
| `403` | Either the key's scope is `read` (a reporter's own key is always read-write — this is a hand-minted key), or the key is not paired with a reporter at all. The second happens if the reporter was deleted while the script kept running: nothing will alert you about it, because no report is expected from a reporter that no longer exists. |
| `400` | The body is wrong; the `errors` object names the field. |
| `415` | The `Content-Type: application/json` header is missing. |
| `429` | More than 30 reports in five minutes from this key. |

A revoked key fails loudly in the right place — the probe goes overdue and red — but the *reason*
only appears on the reporter's admin page, as a revoked key. Check there when a reporter goes
quiet and the host looks healthy.

## Retention

Messages are kept **32 days**, then swept hourly. A reporter's most recent message is never
deleted, however old: it is the evidence that the reporter has gone silent, and the deadline it
missed.
