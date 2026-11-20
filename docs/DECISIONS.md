# Decisions

Short records of the main decisions. Newest last.

| # | Date | Decision | Why | Trade-off |
|---|---|---|---|---|
| 1 | 2026-11-13 | React + Vite on Vercel, ASP.NET Core (.NET 10) on Render, Postgres and Auth on Supabase | All have free tiers; the owner's stack is .NET; Supabase gives sign-in and Postgres in one place | Render free tier sleeps when idle |
| 2 | 2026-11-13 | API in MVC layers: Controllers -> Services -> Repositories, Dapper with parameterised SQL | Rules in one layer, SQL in another; easy to test and review | More files than a minimal API |
| 3 | 2026-11-13 | Integration tests against a real Postgres (Testcontainers) | Catches SQL, trigger and RLS mistakes that mocks hide | Tests need Docker and are slower |
| 4 | Before V3 (migration 008) | RLS on for every table and public roles revoked | The public Supabase key must never reach data; the API is the only way in | Every read goes through the API |
| 5 | 2026-11-19 | Invite-only access with per-program roles (coordinator, member) checked in services | The lead asked for several programs with their own coordinators; nobody should get in alone | No self sign-up; invites are shared by link, no emails |
| 6 | 2026-11-19 | Coordinators define the forms; values stored as JSON and checked by the server | Each program has different status questions; no migration per form change | Field-level reporting needs JSON queries |
| 7 | 2026-11-19 | Excel built on the server with ClosedXML, in the same layout as the training sheets | Coordinators already use those sheets; one correct file for everyone | Larger API image; 120-day cap on daily exports |
| 8 | 2026-11-19 | Contract-first: [PROGRAMS-V3.md](https://github.com/shr1-live/fde-tracker-apiv2/blob/main/docs/PROGRAMS-V3.md) written before code | API and web agents build in parallel against the same shapes | The contract must be kept up to date by hand |
| 9 | 2026-11-19 | V3 moved to its own monorepo (`web/` + `api/`) with both histories imported; training repos restored (split again on 2026-11-20, see 15) | Merging V3 into the training repos removed features from the live training site | Two products to look after |
| 10 | 2026-11-19 | Feed uses keyset (cursor) paging per sort | Stable pages while new posts arrive; fast at any depth | Cursors only work with the sort they were made for |
| 11 | 2026-11-19 | Like and comment counters kept by database triggers (migration 011) | "Most liked" and "Most discussed" sort with an index; counts stay right in the same transaction | Logic split between SQL and C# |
| 12 | 2026-11-19 | One design system ([DESIGN.md](https://github.com/shr1-live/fde-tracker-appv2/blob/main/DESIGN.md)); tokens only in `src/styles.css` of the frontend repository | The first V3 design was rejected; one source stops drift | Pages cannot use one-off colours |
| 13 | 2026-11-19 | AI cannot push to `develop` or `main`; the owner merges | Keeps a human check on everything that deploys | The owner is a bottleneck for merges |
| 14 | 2026-11-19 | Few, big pushes | Every push uses free Vercel and Render build quota | Larger pull requests to review |
| 15 | 2026-11-20 | Split the monorepo into two repositories: frontend `fde-tracker-appv2` (the old monorepo, renamed) and backend `fde-tracker-apiv2` | Production practice: separate deploys, separate CI, smaller blast radius, clearer ownership, backend and frontend can scale on their own; the hackathon guide asks for a clean engineering story | The contract must be changed in two places and kept in sync; shared docs are copied; a cross-cutting change needs two pull requests. History was kept with a subtree split and renames, so earlier commits stay visible in both repositories. Before the split, branch `main_backup_before_split` was created on the frontend repository at the same commit as `main` (`5d56f39`; the owner also made `main_backup_11102026` there), so the pre-split state can be restored |
