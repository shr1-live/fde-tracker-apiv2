# Release plan: from feature branches to a deployed, verified Week 1 project

Written 2026-11-17. It says what is left, who does each step, the exact order, how to check each step worked, and how to undo it. Everything here uses free tiers. No secret is ever written into a file, a commit, a chat or a log: keys live only in environment variables and in the hosting dashboards.

## 1. Where things stand

| Piece | State |
|---|---|
| Backend (`fde-tracker-api`, branch `feature/evals-multi-provider`) | Built, 170 tests pass, pushed. **25 commits, 88 files, ahead of `develop` by those 25 and behind by none.** It already contains every earlier backend branch (`llm-assistant`, `evals-day1-model-comparison`, `dev-api-docs`), so **one pull request carries all of it**. |
| Frontend (`FDE-Learniing-Tracker`, branch `feature/ui-redesign`) | Built, type-checks, builds, accessibility-audited, pushed. 13 commits ahead of `develop`; `develop` has one commit the branch lacks (your merge of PR #6) and the two merge **cleanly** (checked). One pull request. |
| Standalone Day 1 repo (`D:\Downloads\fde-llm-day1`) | Built, 102 tests pass, committed on `develop`. **Has no GitHub home yet**, so its 5 commits exist only on this computer. |
| Deployment image | Built from the real `Dockerfile` and run against a database: health OK, starts in Production, applies migration `006` on first start, ships the prompts and migrations, refuses unauthenticated calls, sends the security headers. |
| `render.yaml` | Fixed in this change: it did not list the model settings, so a deployed API would have answered "not set up". |
| Hosted-model proof (Gemini or Claude) | **Not done.** No key on this machine. |
| Demo video | **Not recorded.** The script exists (`docs/DEMO.md`, now about 10 minutes). |

## 2. Who does what

| Step | Owner | Claude |
|---|---|---|
| Set the Gemini key in your terminal | yes (a key must never be pasted anywhere) | never sees it |
| Run the hosted-model comparisons | runs nothing: tells Claude "set" | runs Days 1, 2, 4 and the fresh set, updates the report and the ratings from the real numbers |
| Create the empty GitHub repo for Day 1 | yes (Claude cannot create repos) | pushes `main` and `develop` once the URL is given |
| Raise and merge the two pull requests | yes (the GitHub login Claude has is read-only) | wrote the descriptions below |
| Render service, environment variables, Vercel setting | yes (your accounts) | wrote the exact values and the checks |
| Record the demo video | yes | wrote the script and the shot list |
| Final ratings in the sheet | yes | gives the exact text and says when a rating may honestly go up |

## 3. The plan, in order

### Phase A: the hosted-model check (about 30 minutes of your time, then Claude)
Why first: it is the only thing that can raise Days 1 to 4, and what it shows may change a decision in phase B.

1. Create a key in Google AI Studio (free).
2. In **your own terminal** (never in a chat or a file): `set GEMINI_API_KEY=<your key>`.
3. Standalone repo, with the API running against Gemini (settings in its README): `powershell -File scripts/smoke.ps1`. Expected: all checks pass, and the conversation remembers the name.
4. Tracker repo, same terminal:
   ```
   set EVAL_ONLY=gemini
   set EVAL_RUN=gemini
   set EVAL_TIMEOUT=300
   dotnet run --project FdeTracker.Evals -- day1
   dotnet run --project FdeTracker.Evals -- day2
   dotnet run --project FdeTracker.Evals -- day4
   set EVAL_CASES=day4-holdout.json
   set EVAL_RUN=gemini-holdout
   dotnet run --project FdeTracker.Evals -- day4
   ```
   That is about 125 model calls. The free tier allows only a few requests a minute, so expect it to take 10 to 20 minutes; the client waits what Google asks for. If it hits the daily quota, run the rest the next day: the runs are independent.
5. Tell Claude "key set, runs done". Claude then: copies the results into the report page, rewrites the Day 1 to 4 explanations from the real numbers, re-checks whether prompt v4 or v2 should be the default **on Gemini**, adds the real cost line, and says which ratings may honestly go up.

**Exit check:** four new files under `docs/evals/` (`day1-gemini-*`, `day2-gemini-*`, `day4-gemini-*`, `day4-gemini-holdout-*`) and no run marked as errors. **Undo:** none needed; it only adds files.

### Phase B: three decisions (5 minutes)
| Decision | Options | Recommendation |
|---|---|---|
| D1. Summary shown to users | `ASSISTANT_SUMMARY=model` (the model's wording) or `code` (built from the checked fields) | `code` if the assistant will read profiles from people you do not trust; it closed the planted-word attack at the cost of plainer wording. Set it in the Render dashboard; no code change. |
| D2. Default prompt | v4 (best on the 3B model) or v2 | Decide **after** phase A: if Gemini ranks them differently, follow Gemini, because that is the model you will run. |
| D3. Day 1 repo name | `fde-llm-day1` or your choice | Keep `fde-llm-day1`. |

### Phase C: push the Day 1 repo (5 minutes)
1. On GitHub create an **empty** repo (no README, no licence) and send Claude the URL.
2. Claude adds it as `origin` and pushes `main` and `develop`.

**Exit check:** the repo shows the README with the error table and the 102-test line. **Undo:** delete the repo.

### Phase D: the two pull requests (20 minutes)
Order: backend first, frontend second (the new frontend pages need the backend routes; the reverse order only shows "Could not reach the service" until the backend is up).

For each: GitHub > Pull requests > New > base `develop`, compare the feature branch, paste the description from section 6, check the diff once, **Merge (a merge commit, not squash)**, so the history stays readable.

Review checklist for the reader (10 minutes per PR): the file list matches the description; no key, address or password appears anywhere (search the diff for `key`, `secret`, `password`); migrations `002` to `006` only add tables or widen a check (nothing is dropped); the tests ran green (the PR shows the numbers).

**Exit check:** both PRs merged, `develop` builds on Vercel. **Undo:** `git revert -m 1 <merge commit>` on `develop` (a revert PR), nothing is lost.

### Phase E: deploy (about 60 minutes, free tiers)
Do this **from `develop`**, and only merge `develop` into `main` after phase F passes (that is the "deployment merge" your rules reserve for you).

**E1. Database (Supabase, free).** Use the existing project's connection string (the "Session pooler" or "Direct connection" string, with the password). It goes only into Render's dashboard. Migrations `001` to `006` are applied automatically by the API at its first start; `006` only widens a check on `llm_calls.status`, so it cannot lose data. Note: a free Supabase project pauses after about a week without use; open the dashboard to wake it.

**E2. Backend (Render, free).** Render > New > Blueprint > pick `fde-tracker-api`, branch `develop`. `render.yaml` supplies the non-secret values; you fill in only these in the dashboard:

| Setting | Where the value comes from |
|---|---|
| `DATABASE_URL` | the Supabase connection string from E1 |
| `SUPABASE_URL` | Supabase > Project settings > API > Project URL |
| `CORS_ORIGINS` | the exact origin of the site, for example `https://fde-learniing-tracker.vercel.app` (no trailing slash; add more origins separated by commas) |
| `LLM_API_KEY` | the Gemini key (paste it only into this dashboard field) |
| `LLM_MODEL` | a current free-tier model name from AI Studio, for example the one you used in phase A |
| `ASSISTANT_SUMMARY` | `model` (in the file) or `code`, per decision D1 |

Already set by `render.yaml`: `ALLOWED_DOMAIN`, `ADMIN_EMAIL`, `FREE_SEATS`, `LLM_PROVIDER=openai`, `LLM_BASE_URL` (Gemini's compatible address), `LLM_MAX_TOKENS`, `LLM_DAILY_CAP`. Optional tuning (defaults are fine): `LLM_TIMEOUT_SECONDS`, `LLM_TOTAL_TIMEOUT_SECONDS`, `LLM_MAX_ATTEMPTS`, `LLM_TEMPERATURE`, `LLM_TOP_P`, `TASK_MESSAGE_DEADLINE_SECONDS`, `DB_COMMAND_TIMEOUT_SECONDS`, `RATE_LIMIT_PER_MINUTE`, `WRITE_LIMIT_PER_MINUTE`.

**E3. Frontend (Vercel).** Project > Settings > Environment Variables: `VITE_API_URL` = the Render address (for example `https://fde-tracker-api.onrender.com`, no trailing slash), in Production (and Preview if previews should work). The existing `VITE_SUPABASE_URL`, `VITE_SUPABASE_ANON_KEY` and `VITE_ALLOWED_DOMAIN` stay. Redeploy (a variable change needs a new build).

**E4. Sign-in settings (Supabase).** Authentication > URL Configuration: Site URL and Redirect URL = the Vercel address. Sign in once as the admin (`ADMIN_EMAIL`) so the admin account exists.

**Undo:** in Render, Deploys > pick the previous deploy > Rollback; in Vercel, Deployments > promote the previous one. The database change is additive, so rolling the code back does not need a database rollback.

### Phase F: verify the deployment (30 minutes)
Run these in order on the real site. Anything unexpected: stop, note the exact message, tell Claude.

| # | Check | Expected |
|---|---|---|
| 1 | Open `https://<render address>/health` | `{"ok":true}` (the first call after idle takes about 30 seconds) |
| 2 | Render logs, first start | "Applied migration 006_llm_call_status_kinds.sql", then "Application started" |
| 3 | Open the site, sign in as admin | the dashboard; the menu shows Week 1 tasks, Task assistant, Week 1 report, Review submissions |
| 4 | Week 1 report | **Live status** shows your days (not "Could not reach the service") |
| 5 | Assistant: the first two candidates from `evals/day2-cases.json` | answers with quotes, speed and tokens; a Gemini model name in the run details |
| 6 | Assistant: the profile with "ignore all previous instructions" | rated low, summary replaced by the fixed sentence, a warning shown |
| 7 | Task assistant: add a task, then delete it and press Yes | the task appears and then disappears; the log shows `needs_confirmation` then `confirmed` |
| 8 | Submit proof for a day as a second (member) account | "Waiting for review"; the reviewer account sees it and can approve or send it back |
| 9 | Download PDF on the report | a print window; the saved PDF has the Live status filled in |
| 10 | Provoke a rate limit (press Analyse five times quickly with a free key) | a clear "limiting requests, try in N seconds" message, never a blank page |
| 11 | Browser developer tools, Network, any API call | no key or secret in any request, response or the page source |

**Exit check:** all eleven pass. **Then**, and only then, open a pull request `develop` into `main` (the deployment merge) and merge it; point Render and Vercel at `main` if they are not already.

### Phase G: the demo video (30 minutes)
Record from the **deployed** site with a free recorder (Windows Game Bar `Win+G`, or OBS). Follow `docs/DEMO.md` (about 10 minutes, seven short parts). Use made-up candidates only; blur nothing you would not show a reviewer; never show the Render or Gemini dashboards with a key visible. Upload as an **unlisted** link and add it to the README and the sheet. If the host is asleep, wake it with the health URL a minute before recording.

### Phase H: the sheet
Use the final numbers from phase A. Current honest ratings: Day 1 = 4, Day 2 = 4, Day 3 = 3, Day 4 = 3, Day 5 = 4. A rating may honestly go up only when its evidence exists:
- **Day 1 to 5**: the Gemini comparison exists, with its real cost line, **and** the project runs deployed (phase F passed).
- **Day 3 to 4**: Gemini (or another hosted model) completed the delete-with-confirmation flow in the task assistant in phase F check 7.
- **Day 4 to 4**: the Gemini fresh-set result is at least as good as the baseline claims, or its failures are explained.
- **Day 5 to 5** additionally needs the demo video.
Otherwise leave the rating; an honest 4 survives a question, an unproven 5 does not.

## 4. Risks and what to do

| Risk | Likelihood | What happens | What to do |
|---|---|---|---|
| Free Render service sleeps | certain | the first request after 15 minutes idle takes about 30 seconds | the app already says so with a Try again button; wake it before a demo |
| Gemini free quota (per minute / per day) | likely | 429 with a wait | the client waits up to 30 s; longer waits are shown to the user; split big evaluation runs across days |
| Free Gemini data use | certain | Google may use what is sent to improve its products | test with made-up profiles only; do not send real candidate data on a free key |
| Supabase free project paused | possible | the API cannot reach its database | open the Supabase dashboard to wake it; then restart the Render service |
| `CORS_ORIGINS` wrong or with a trailing slash | common | the site shows "Could not reach the service" while `/health` works | set the exact origin; redeploy |
| Migration `006` on the production database | low | adds a wider check on a status column | additive; no rollback needed |
| A wrong Gemini model name | possible | clear 502 "configured model was not found, tell the administrator" | fix `LLM_MODEL` and redeploy |
| Real-model weakness (task assistant) | known | a small model could not finish the delete flow | phase F check 7 with Gemini decides; if it fails, say so in the report |

## 5. Definition of done
- Both pull requests merged into `develop`, then `develop` merged into `main`.
- The Day 1 repo is on GitHub.
- Gemini results exist for Days 1, 2 and 4 and the fresh set, and the report page shows them.
- All eleven deployment checks pass.
- The demo video link is in the README and the sheet.
- The sheet's ratings match the evidence rules in phase H.

## 6. Pull request descriptions (copy and paste)

### Backend: `feature/evals-multi-provider` into `develop`

**Title:** Week 1: LLM assistant, evaluation tool, submissions and review, task assistant, guardrails

**What this adds** (25 commits; everything earlier backend branches carried is included, so they need no PRs of their own):
- **Assistant** (`POST /api/assistant/analyze`): provider-neutral model client (OpenAI-compatible, including Gemini, and Claude); versioned prompts v1 to v4; every claim needs an exact quote that is really in the profile; two in-code functions as an independent check; per-person daily limit; usage log with latency and tokens (no prompt text).
- **Errors**: failures are sorted into validation / permission / business / transient / rate limit with a retryable flag; retries honour the provider's wait (Retry-After or Gemini's hint) with backoff and an overall deadline; one error shape.
- **Task assistant** (`/api/task-assistant`): safe tool calling (validated arguments, owner-scoped, delete needs confirmation, every call logged), an overall deadline per message.
- **Submissions and review**: proof as links or files (checked by content, size and count limits), learner/reviewer/admin roles enforced on the server.
- **Guardrails**: instruction screen, "strong" must match our own skill check, optional code-built summary (`ASSISTANT_SUMMARY=code`).
- **Evaluation tool** (`FdeTracker.Evals`): Day 1, 2 and 4 runners, a 30-case set and a separate fresh set, re-scoring of saved answers, a cost column.
- **Docs**: README, `docs/WEEK1-REPORT.md`, `docs/DEMO.md`, `docs/RELEASE-PLAN.md`, generated reports under `docs/evals/`.

**Database:** migrations `002` to `006` are applied automatically at start; they add tables and widen one check (`llm_calls.status`); nothing is dropped.
**New settings** (all optional except the model's three): see README "Settings". Nothing secret is in the repo; `render.yaml` lists names only.
**Tests:** 170 pass (`dotnet test`, needs Docker for the database tests).
**Known limits, said plainly:** only small local models have been evaluated here (the Gemini check is phase A of `docs/RELEASE-PLAN.md`); the new instruction screen caught none of the fresh attacks (the older quote rules did the work); the task assistant's delete flow was not completed by the small local model (proven by scripted tests only).
**Review focus:** `Services/Llm/*`, `Services/Tasks/*`, `Controllers/Submission*`, migrations, `render.yaml`.

### Frontend: `feature/ui-redesign` into `develop`

**Title:** Week 1: generic programme pages, task assistant, report with live status and PDF, plan page in cards

**What this adds** (13 commits on top of PR #6):
- **Programme as data**: weeks, days, criteria and expected proof are data (`src/data/programme.ts`); `/week/:n`, the review page and the dashboard read from it, so a new week is a data entry.
- **Task assistant page** (`/task-assistant`): message box, actions taken, confirm card, task list, execution log.
- **Week 1 report**: live status from the server (learner: own days; reviewer: queue), Day 2 and Day 4 results (two runs), downloadable PDF, honest "not yet" notes per day.
- **Ten-day plan day page** restructured into cards (resource cards without raw URLs).
- Accessibility audited (axe, light and dark, desktop and phone): no violations.

**Needs the backend PR first** for the new pages to show data. **No new secrets.** The development-only test-account switcher and the admin preview are removed from production builds (checked in the built bundle).
**Review focus:** `src/pages/TaskAssistant.tsx`, `src/components/LiveStatus.tsx`, `src/components/Day4Results.tsx`, `src/data/programme.ts`.
