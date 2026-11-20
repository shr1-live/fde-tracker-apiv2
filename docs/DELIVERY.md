# Delivery (backend)

This page covers the backend repository. The frontend is deployed from its own repository: see its [delivery page](https://github.com/shr1-live/fde-tracker-appv2/blob/main/docs/DELIVERY.md).

## Environments

| Environment | Web | API | Database |
|---|---|---|---|
| Local | `npm run dev` on http://localhost:5173 | `dotnet run` on http://localhost:5077 | Postgres 16 in Docker |
| Production | https://fde-tracker-green.vercel.app (Vercel, from `main` of `fde-tracker-appv2`) | https://fde-tracker-v3-api.onrender.com (Render, from `main` of `fde-tracker-apiv2`) | Supabase Postgres |

There is no separate staging environment. `develop` is the integration branch in both repositories; Vercel builds preview links for frontend pull requests.

## Run locally

Needs the .NET 10 SDK and Docker. The exact commands are in the [README](../README.md#run-locally). In short:

1. Start Postgres in Docker.
2. Set `DATABASE_URL`, `SUPABASE_URL`, `CORS_ORIGINS`, `ASPNETCORE_URLS`, then `dotnet run --project FdeTracker.Api`. Migrations run at start-up.
3. Tests: `dotnet test` (Docker must be running).
4. To see the whole product, run the frontend from its repository with `VITE_API_URL` pointing at this API.

## Deploy (Render)

The root `render.yaml` is a blueprint for one free Docker web service, `fde-tracker-v3-api`, built from the root `Dockerfile` with a health check at `/health`. In Render: New -> Blueprint -> pick `fde-tracker-apiv2`.

Environment variables (names only; values live in the Render dashboard):

| Name | Set where | Notes |
|---|---|---|
| `DATABASE_URL` | Render dashboard | Secret |
| `SUPABASE_URL` | Render dashboard | Used to verify sign-in tokens |
| `LLM_API_KEY`, `LLM_MODEL` | Render dashboard | Optional; only the older assistant features use them |
| `ALLOWED_DOMAIN`, `ADMIN_EMAIL`, `FREE_SEATS`, `CORS_ORIGINS` | `render.yaml` | Not secret |
| `LLM_PROVIDER`, `LLM_BASE_URL`, `LLM_MAX_TOKENS`, `LLM_DAILY_CAP`, `ASSISTANT_SUMMARY` | `render.yaml` | Not secret |
| `RATE_LIMIT_PER_MINUTE`, `WRITE_LIMIT_PER_MINUTE` | Optional | Defaults 240 and 40 |

**Supabase.** Add the site address with `/**` to Authentication -> URL Configuration -> Redirect URLs, or sign-in links go to the Site URL instead.

## Deploy order

1. Backend first: merge, wait for Render to be live, check `/health`.
2. Then the frontend: merge, wait for Vercel to be ready, check sign-in against the live API.

Backend first, because the web app calls the API. A contract change goes out in that order. Remove a field only after the frontend no longer reads it.

## When the API address changes

1. Frontend repository: change `VITE_API_URL` in `.env.production` and the `connect-src` entry in `vercel.json`.
2. Backend: check `CORS_ORIGINS` still lists the web address.
3. Redeploy the frontend and check sign-in, `/health` and the headers.

## Migration note: the split into two repositories

Render's running service `fde-tracker-v3-api` was created from the old combined repository with Root Directory `api`. After the split it must be switched to the backend repository: Settings -> Build and Deploy -> Repository, pick `fde-tracker-apiv2`, and clear Root Directory (the `Dockerfile` and `render.yaml` are now at the repository root). Vercel project `fde-tracker-green` keeps the old repository, which is now the frontend (`fde-tracker-appv2`). Until the owner has done the Render switch and checked the live status of the new commit, the new layout is not deployed. This was not verified when this page was written.

## Planned move to the company domain

The plan is to move the web app and API to the company (55) domain. That needs, in one change:
1. the new web address in `CORS_ORIGINS` (Render, backend repository) and in the Supabase Redirect URLs;
2. the new API address in `.env.production` and in the CSP `connect-src` in `vercel.json` (frontend repository);
3. a check of sign-in, `/health` and the headers on the new addresses.

Not started yet.

## Release checklist

- [ ] Work is on a branch from `develop`; related changes grouped into one pull request.
- [ ] `dotnet test` passes (exact count recorded).
- [ ] If the contract changed: `docs/PROGRAMS-V3.md` is updated and a matching frontend pull request exists.
- [ ] `code-reviewer` and `security-reviewer` agents have read the diff; findings fixed or written down.
- [ ] New migration is idempotent and turns RLS on for new tables.
- [ ] No secrets in the diff.
- [ ] One push; the owner merges into `develop`, then `develop` into `main`.
- [ ] After the merge: the Render status of that commit is "success"; `/health` answers; signed-out calls return 401; CORS allows the web address.
