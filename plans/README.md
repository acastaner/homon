# Implementation plans

Numbered in one monotonic sequence; a number is never reused. Each plan is a file
`NNN-short-imperative-title.md` and records what it changed and what it decided; the
decisions themselves live in `docs/ARCHITECTURE.md`.

| Plan | Status | Title |
| --- | --- | --- |
| 001 | DONE | Scaffolding — solution, toolchain, plumbing, gate, Docker, docs |
| 002 | planned | Monitoring core and the ping probe |
| 003 | planned | HTTP/HTTPS probe |
| 004 | planned | SMB/CIFS probe (managed client) |
| 005 | planned | SNMP probe scaffold |
| 006 | planned | Links |
| 007 | planned | Pages and the WYSIWYG editor |
| 008 | planned | Backup reports and API-key administration |
| 009 | planned | Alerts by email |
| 010 | planned | Weather widget |
| 011 | planned | Family calendar widget |
| 012 | planned | Design pass (from `docs/design-brief.md`) |

`docs/MODULES.md` carries each planned module's requirements and the constraints already
known. The gate at plan 001: `./ci/run-ci.sh` → `PASS — web api e2e`.
