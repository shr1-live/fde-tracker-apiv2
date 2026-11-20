# Instructions for AI agents (backend)

These are the rules AI coding agents follow in this repository. The owner keeps a local copy (`CLAUDE.md`) and a local task list (`TASKS.md`); both are git-ignored. This file is the shareable version.

This is the **backend** of the Programs V3 product (invite-only learning programs: plan, daily and program status, Excel reports, social feed). The **frontend** is a separate repository, `shr1-live/fde-tracker-appv2` (local folder `D:/Downloads/fde-tracker-appv2`). The two talk only through the API contract in [docs/PROGRAMS-V3.md](docs/PROGRAMS-V3.md): change the contract here first, then the frontend follows.

## Rules

- Read the task list before work; update it after (tick items, recount, change "Last updated").
- Production product: precise, small, reviewed changes. No unrelated refactors.
- Free tiers only. Never ask for, print, log or commit a secret. Configuration is by environment variable only (`render.yaml` names them; values are set in the Render dashboard).
- Git flow: new branch from `develop` -> pull request into `develop` -> `develop` into `main` deploys (Render). Agents never push to `develop` or `main`; the owner merges.
- Few, big pushes: each push can build on Render. Commit locally; push once per finished, reviewed change.
- Per task: plan -> add tasks -> build -> self-review with the review agents -> push -> give compare links -> tick tasks.
- Security: validate every input on the server, parameterised SQL only, per-program permission checks in services (404 for a program you are not in, 403 for a role you lack), https-only links, rate limits, no stack traces in responses.
- Tests are required for every rule and every permission check (xUnit + Testcontainers; Docker must be running). Report the exact pass count; never call a run green that was not.
- Do not say "deployed" until the Render deploy of that commit and `/health` have been checked.

## Agents (`.claude/agents/`)

| Agent | Use it for |
|---|---|
| `architect` | Planning a change before code (read-only) |
| `backend-dev` | Controllers, services, repositories, migrations, xUnit tests |
| `qa-engineer` | Test runs, real-API checks, negative testing |
| `code-reviewer` | Diff review before a pull request (read-only) |
| `security-reviewer` | Auth, permissions, injection, secrets review (read-only) |
| `devops` | Render setup, CI, live checks |
| `docs-writer` | README, `docs/`, the API contract |
| `task-manager` | Task list, branches, compare links |
| `frontend-dev`, `ui-ux-designer` | Not used in this repository (they work in the frontend repository) |

## Guardrails for AI work

- `.claude/settings.json` denies reading `.env` and key files, force-pushes, pushes to `main` or `develop`, and `reset --hard`.
- `.claude/hooks/block-secrets.mjs` blocks any write that looks like a secret (private keys, cloud keys, tokens, connection strings with passwords, service-role JWTs).
- CI (`.github/workflows/ci.yml`) builds, runs the tests, checks for vulnerable packages and scans the whole history for secrets.
- A human reads, runs and merges every change. AI output is validated (tests, screenshots, real-API runs), not trusted.

## Docs

- Contract: [docs/PROGRAMS-V3.md](docs/PROGRAMS-V3.md)
- Product: [docs/PRODUCT.md](docs/PRODUCT.md)
- Architecture: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
- Standards: [docs/STANDARDS.md](docs/STANDARDS.md)
- How AI was used: [docs/AI-DEVELOPMENT.md](docs/AI-DEVELOPMENT.md)
- QA: [docs/QA.md](docs/QA.md)
- Security: [docs/SECURITY.md](docs/SECURITY.md)
- Delivery: [docs/DELIVERY.md](docs/DELIVERY.md)
- Decisions: [docs/DECISIONS.md](docs/DECISIONS.md)
- Disclosure of earlier work: [docs/DISCLOSURE.md](docs/DISCLOSURE.md)

The product-level docs (product, architecture, AI development, decisions, disclosure) are kept identical in both repositories; this repository is the source of truth for them.
