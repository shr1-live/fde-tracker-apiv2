# Security (backend)

This page covers the API and the database. The browser side (CSP, headers, XSS and link rules) is in the [frontend repository](https://github.com/shr1-live/fde-tracker-appv2/blob/main/docs/SECURITY.md).

## Threat model (summary)

| Threat | Control |
|---|---|
| Someone outside the company signs in | Supabase Auth only; the API checks the email domain (`ALLOWED_DOMAIN`) and requires an invite |
| A forged or expired token | The API verifies every JWT against the Supabase issuer and signing keys |
| A member reads another member's status or reports | Per-program role checks in services; 403 for another person's data; tested |
| A coordinator acts outside their program, or undoes an admin decision | Role checked per program; invites never lift a rejection; nobody changes their own role; the admin cannot be removed |
| SQL injection | Parameterised SQL only (Dapper); search treats `%` and `_` as text |
| Bad or oversized input | Server-side validation of every field; bodies capped at 64 KB; unknown JSON fields refused |
| Script injection in the feed | The server accepts only https links; text is escaped by the web app (see the frontend repository) |
| Abuse and floods | Rate limits: 240 requests per minute per address, 40 writes per minute per person; 30 posts and 200 comments per person per day |
| Direct database access with the public key | RLS is on for every table and the `anon` and `authenticated` roles are revoked; all data goes through the API |
| Other sites calling the API | CORS allow-list (`CORS_ORIGINS`) |
| Clickjacking, content injection | The web host sets the page headers (see the frontend repository); the API sends its own security headers |
| Leaked secrets | No secrets in the repo; Render values are `sync: false`; values are set in the Render dashboard only; errors never echo internals |
| Spreadsheet formula injection | Excel cells are written as plain values (text or number), never as formulas |

## Known gaps

- No dedicated test yet proves that a value starting with `=` stays text in the Excel file.
- No audit screen for program changes (who invited whom, who changed a form); the old audit log covers older admin actions only.
- Rate limits are per server instance and reset on restart.
- CI (`.github/workflows/ci.yml`: build and tests, vulnerable-package check, gitleaks secret scan), Dependabot and a local hook that blocks secrets in agent edits (`.claude/hooks/block-secrets.mjs`) exist in the repository. They have not been checked on GitHub from this split yet.
- Render free tier and Supabase free tier have no uptime promise.

## Reporting a problem

Do not open a public issue for a security problem. Contact the repository owner directly through GitHub (`shr1-live`) and describe what you found and how to repeat it. Please do not test against real users' data.
