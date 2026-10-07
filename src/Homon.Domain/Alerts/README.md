# Alerts — module slot

**What it owns.** The pure shapes of alert email: `AlertSettings` (a singleton row — the Resend API key, protected; the sender; the recipients; the on/off switch), and `AlertNotification` (one row of the outbox, written when a probe goes down or comes back up). Sending lives in `Homon.Infrastructure/Alerts/` (the dispatcher and the message builder); the administrator sets it all up on the Alerts admin page. The outage itself is probe state: `Probe.DownSince` in `Monitoring/`. See `docs/ARCHITECTURE.md` §3.31.
