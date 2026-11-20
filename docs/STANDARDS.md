# Engineering standards

These are the rules the code follows today, not goals. They cover the backend. The frontend rules are in the [frontend repository](https://github.com/shr1-live/fde-tracker-appv2/blob/main/docs/STANDARDS.md).

## API (this repository)

- **Layers:** Controllers -> Services -> Repositories.
  - Controllers handle HTTP only: read the request, call a service, return a view model.
  - Services hold the rules: validation, limits, per-program role checks, transactions.
  - Repositories hold all SQL (Dapper). One class per table group.
- **SQL:** parameterised only. No string-built SQL with user input. Search treats `%` and `_` as plain characters.
- **JSON:** snake_case in and out. Unknown fields are refused. Errors use `{ "error": "..." }` and never leak internals.
- **Validation:** every input is checked on the server (lengths, types, https-only links, date ranges), even when the web app checks it too.
- **Migrations:** numbered SQL files in `migrations/`, run once each at start-up. Each is idempotent (`if not exists`, guarded `alter`), turns RLS on and revokes the public roles for new tables.
- **Tests:** xUnit integration tests against a real Postgres (Testcontainers). Every rule and every permission check gets a test.

## Git and delivery

- New branch from `develop` for every change -> pull request into `develop` -> `develop` into `main` deploys.
- Nobody (and no AI agent) commits straight to `develop` or `main`. The owner merges.
- Few, big pushes. Every push starts a Render build on a free quota, so work is committed locally and pushed once per finished, reviewed change.
- Commit messages say what changed and why, in plain words.
- Secrets never go in the repo. Configuration is by environment variable only; names are in `render.yaml`, values are set in the Render dashboard.

## Writing

Plain English, short sentences, sentence case. Buttons say what happens ("Save status"). Docs state limits and what is not done.
