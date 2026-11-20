# Quality and testing (backend)

AI agents wrote and ran most tests. The owner checked the numbers, looked at screenshots and asked for runs against the real API.

## Layers

| Layer | Tool | Size | Runs |
|---|---|---|---|
| API integration tests | xUnit + Testcontainers (a real Postgres per run) | 264 tests pass (`dotnet test`) | Before every push; needs Docker |
| Review | `code-reviewer` and `security-reviewer` agents | Last round: 0 serious findings, 4 small ones fixed | Before every push |
| Live checks | curl and browser | `/health`, 401 when signed out, CORS, security headers, served bundle | After a deploy |

The browser checks live in the frontend repository. See the [frontend QA page](https://github.com/shr1-live/fde-tracker-appv2/blob/main/docs/QA.md).

## What the API tests cover

- **Permissions (negative tests):** a member reading another person's status gets 403; a person outside a program gets 403 or 404; only coordinators and the admin can change plans, forms and members; only the author can edit a post; nobody can change or remove themselves; the admin account cannot be removed.
- **Invite-only:** unknown emails stay pending; invites become memberships at sign-in; an invite never lifts a rejection.
- **Validation:** unknown form keys, wrong types, values out of range, http (not https) links, dates outside 2020-01-01 to tomorrow, too-long text; bad feed sort, kind, limit, search or cursor give 400.
- **Edge cases:** keyset paging with equal counts and equal times; a cursor from another sort; `%` and `_` in search are literal; days with nothing filled in show as not started; Excel files read back (file name, sheets, layout); the daily post limit (30).
- **Data rules:** counters stay right after likes and comments are added and removed; RLS is on and public roles are revoked for every table.


## How AI output was checked

- Tests are run every time; the exact pass count is reported, never "should pass".
- A mocked run is not enough: real-API runs from the frontend found a bug the mocks hid (typed text wiped on a slow load).
- When a test failed, the cause was found before the test was changed (for example, the UTC date bug was in the test, not the app).

## Not automated

- CI (`.github/workflows/ci.yml`) builds the API, runs the tests, checks packages and scans for secrets. It has not been checked on GitHub from this split yet.
- No load test. Feed paging and indexes were designed for scale but not measured under load.
- No test opens the Excel files in Excel itself; tests read them back with ClosedXML.
- Supabase sign-in emails are checked by hand.
