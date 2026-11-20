# Programs V3: contract

The tracker becomes a simple, invite-only platform for several learning programs (FDE, DevOps, ...).
This file is the contract between the backend (`fde-tracker-api`) and the web app. Both build against it.

## Roles

| Who | Where it is stored | Can do |
|---|---|---|
| Admin | `app_users.role = 'admin'` (the `ADMIN_EMAIL` account) | Everything, in every program. Creates programs. |
| Coordinator | `lp_members.role = 'coordinator'` for one program | Everything inside that program: plan, forms, invite members and other coordinators, change roles, see everyone's status and reports. |
| Member | `lp_members.role = 'member'` for one program | See the plan, tick days, fill their own daily and program status, see their own reports. |

One person can be a member of one program and a coordinator of another. Everyone signed in and approved can use the feed.

**Invite-only.** Nobody gets in alone. The admin email is always admin. Anyone else is approved only when an invite exists for their email (or they were already approved before V3). On sign-in, all open invites for that email become memberships. An unknown email stays `pending`, and the web app shows "You have not been invited yet". No emails are sent: the inviter shares the site link.

**Existing data (migration).** Approved non-admin users become members of the seeded FDE program. Users with role `reviewer` become its coordinators. The old tables and endpoints are not touched.

## Tables (migration `010_programs_v3.sql`, idempotent, RLS on and public roles revoked like 008)

- `lp_programs(id uuid pk, name text unique 2-80, description text <=600 null, starts_on date null, created_at)`
- `lp_members(program_id, user_id, role in ('coordinator','member'), added_at, pk(program_id,user_id))`
- `lp_invites(id uuid pk, email text lower, program_id, role, invited_by, created_at, accepted_at null, unique(email, program_id))`
- `lp_days(id uuid pk, program_id, week int 1-52, day int 1-7, topic text 1-140, topics_to_cover text <=4000, practical_task text <=4000 null, resources text <=2000 null, unique(program_id, week, day))`
- `lp_form_fields(id uuid pk, program_id, form in ('daily','day'), key text ^[a-z][a-z0-9_]{1,39}$, label 1-80, description <=400 null, type in ('text','longtext','number','rating','url'), required bool, position int, unique(program_id, form, key))`
- `lp_daily_status(program_id, user_id, on_date date, values jsonb, updated_at, pk(program_id,user_id,on_date))`
- `lp_day_status(program_id, user_id, day_id, values jsonb, completed bool, completed_at null, updated_at, pk(user_id, day_id))`
- `feed_posts(id uuid pk, author_id, body text <=2000 null, url text https <=500 null, created_at, edited_at null, like_count, comment_count, kind generated video|link|text, check(body or url))`. `011_feed_scale.sql` adds `edited_at`, the counters (kept by triggers on `feed_likes`/`feed_comments` in the same transaction, backfilled), the stored `kind` and one index per sort order and for kind and author.
- `feed_likes(post_id, user_id, created_at, pk(post_id,user_id))`
- `feed_comments(id uuid pk, post_id, author_id, body 1-1000, created_at)`

Seed: program **"FDE Training Program"**. Its start date is Batch 1's `starts_on` if present, else 2026-11-16. It has Week 1, Days 1-5 (topic, topics to cover and practical task exactly as in the Excel "Status Report" sheet), plus the default forms:

- daily form: `project` (text, "Project worked", "Project name and hours spent. Write None if you are not on a project."); `theory` (longtext, "Training theory", "Topics covered, completion status and hours spent."); `practical` (longtext, "Practical", "Practical task progress or status."). All are required.
- day form: `learned` (longtext, "What You Learn", "What you learned from this day's topics, in your own words."); `proof` (longtext, "Proof of Evidence", "Links (GitHub, document, demo) or a short note that shows the work."); `rating` (rating, "Rate (1-5)", the five rubric lines from the Readiness Guide). `learned` and `rating` are required when the day is marked completed.

## Value rules (server-checked)

`values` is a JSON object whose keys must be field keys of that form (unknown keys give 400). Types:

- `text`: string up to 500 characters
- `longtext`: string up to 4000 characters
- `number`: a number from 0 to 1000
- `rating`: an integer from 1 to 5
- `url`: https only, up to 500 characters

Empty strings count as missing. Required fields: daily status always. Day status only when `completed` is true. A daily date must be between 2020-01-01 and tomorrow.

## API (all under `/api`; signed in and approved, except `GET /api/me`)

JSON uses snake_case like the existing API. Errors use the existing `{ error }` shape.

**Me:** `GET /api/me` gains `programs: [{ id, name, role }]` (admin: every program, role `admin`) and `can_manage: bool` (admin, or coordinator anywhere).

**Programs**
- `GET /api/programs`: programs I belong to (admin: all), as `[{ id, name, description, starts_on, my_role, member_count }]`
- `POST /api/programs` (admin): `{ name, description?, starts_on? }`
- `PATCH /api/programs/{id}` (coordinator/admin): `{ name?, description?, starts_on? }`
- `GET /api/programs/{id}` (member+): `{ id, name, description, starts_on, my_role, days: [{ id, week, day, topic, topics_to_cover, practical_task, resources }], forms: { daily: Field[], day: Field[] } }`, where Field = `{ key, label, description, type, required }`
- `POST /api/programs/{id}/days` (coordinator/admin): `{ week, day, topic, topics_to_cover, practical_task?, resources? }`. A duplicate week+day gives 409.
- `PATCH /api/programs/{id}/days/{dayId}`, `DELETE /api/programs/{id}/days/{dayId}` (coordinator/admin)
- `PUT /api/programs/{id}/forms/{form}` (coordinator/admin; form is `daily` or `day`): `{ fields: Field[] }` replaces the list in order, 1-20 fields, keys unique
- `GET /api/programs/{id}/members` (coordinator/admin): `{ members: [{ user_id, email, name, role }], invites: [{ id, email, role, created_at }] }`
- `POST /api/programs/{id}/invites` (coordinator/admin): `{ email, role }`. If the user exists, they are added at once and approved (`{ status: "added" }`); otherwise the invite is stored (`{ status: "invited" }`). Re-inviting updates the role.
- `DELETE /api/programs/{id}/invites/{inviteId}` (coordinator/admin)
- `PATCH /api/programs/{id}/members/{userId}` `{ role }`; `DELETE /api/programs/{id}/members/{userId}` (coordinator/admin). Nobody can change or remove themselves; the admin account cannot be removed.

**Status**
- `GET /api/programs/{id}/daily?user_id=&from=&to=`: a member gets only their own (asking for another user gives 403). A coordinator or admin gets anyone in the program. Returns `[{ user_id, on_date, values, updated_at }]`.
- `PUT /api/programs/{id}/daily/{yyyy-mm-dd}` (member of the program, own): `{ values }`
- `GET /api/programs/{id}/day-status?user_id=`: same visibility rule. Returns `[{ user_id, day_id, values, completed, updated_at }]`.
- `PUT /api/programs/{id}/day-status/{dayId}` (own): `{ values, completed }`

**Reports** (a member sees only themselves; a coordinator or admin sees everyone in the program)
- `GET /api/programs/{id}/reports/daily?from=&to=` returns `{ fields, people: [{ user_id, name, email }], dates: [...], cells: [{ user_id, on_date, values }] }`. The default range is the program start (or 30 days ago) to today, and the maximum is 120 days.
- `GET /api/programs/{id}/reports/program` returns `{ days, fields, people: [{ user_id, name, email, rows: [{ day_id, values, completed }] }] }`
- `GET /api/programs/{id}/reports/daily.xlsx?from=&to=` is the same layout as the "FDE 30-Day Daily Status" sheet:
  - sheet `Summary`: the daily form (number, field, description) and the 1-5 rating rubric
  - sheet `Status`: column A is the name, then one column per working day "Day N" (N counted from the program start, Monday to Friday), with a "Weekly rating" column after every 5 days (left empty for the coordinator). Each cell holds `Label: value` lines.
- `GET /api/programs/{id}/reports/program.xlsx` is the same layout as the "Training Program Status Report" sheet:
  - sheet `Readiness Guide`: the rubric
  - one sheet per person (name, at most 31 characters, unique). Columns: Week, Topic, Topics to Cover, Practical Task, then each day-form field label, then Completed. The header has a dark-blue fill, white bold text and wrapped cells, and "Week N" appears on the first row of each week.
- Filename: `<program-slug>-daily-<from>-<to>.xlsx` / `<program-slug>-status-report.xlsx`, with the correct content type.

**Feed** (everyone approved)
- `GET /api/feed?sort=new|top|discussed&kind=all|video|link|text&mine=true&q=<text>&cursor=<opaque>&limit=20` (limit 1-50, default 20) returns `{ items: Post[], next_cursor: string|null }`. Post is `{ id, author: { id, name }, body, url, created_at, edited_at, like_count, liked_by_me, comment_count, comments: [{ id, author: { id, name }, body, created_at }] }`; comments come oldest first, the last 20.
  - sort `new` (default) = created_at desc, id desc; `top` = like_count desc, then new; `discussed` = comment_count desc, then new.
  - Paging is keyset: pass `next_cursor` back as `cursor` with the same sort and filters; it is null on the last page. A cursor made for another sort, or changed by hand, is a 400.
  - kind `video` = a YouTube link (host youtube.com or youtu.be, any subdomain), `link` = any other link, `text` = no link. `mine=true` = only my posts. `q` (at most 100 characters, blank = none) is a literal, case-insensitive search in the text and the link (`%` and `_` are plain characters).
  - Bad sort, kind, mine, limit, q or cursor: 400.
- `GET /api/feed/{id}` returns one Post with up to the last 50 comments (oldest first); 404 if missing.
- `PATCH /api/feed/{id}` `{ body?, url? }` author only (the admin may delete others' posts but not edit them; 403 for everyone else, 404 if missing). A field left out keeps its value, a field sent as null or empty is cleared; the result must still have text or a link, with the same rules as a new post (https link up to 500, text up to 2000). Sets `edited_at` and returns the Post.
- `POST /api/feed` `{ body?, url? }`, at least one; limit 30 posts per user per day
- `DELETE /api/feed/{id}` (author or admin)
- `POST /api/feed/{id}/like` toggles and returns `{ like_count, liked_by_me }`
- `POST /api/feed/{id}/comments` `{ body }`; limit 200 per user per day
- `DELETE /api/feed/comments/{commentId}` (author or admin)

## Out of scope (removed from the web app, kept in the `main_backup_me` branch)

Assistants, task assistant, practice lab, backlog, study notes, week pages, my plan, downloads, submission review, view-as switch, old reports and old dashboard. Their backend endpoints stay as they are for now.
