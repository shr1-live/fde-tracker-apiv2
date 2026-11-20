# Product

FDE Tracker V3 is an invite-only web app for running several learning programs (FDE, DevOps, ...) in one place: a plan, daily and program status forms, reports with Excel download, and a shared feed.

## Problem

- Training coordinators track every learner in Excel sheets. Each learner fills a daily status row and a per-day "What You Learn / Proof / Rate" row by hand.
- The sheets live in many files. Nobody sees the whole program at a glance, and it is easy to miss who has not filled in a day.
- Weekly and program reports are built by hand, by copying rows between sheets.
- Learners share useful links and videos in chat, where they are lost after a day. There is no shared feed.

## Users and roles

| Role | Who | Can do |
|---|---|---|
| Admin | One account (set by `ADMIN_EMAIL` on the server) | Everything in every program. Creates programs. Approves or rejects people. |
| Coordinator | Set per program | Everything inside that program: plan, forms, invites, roles, everyone's status and reports. |
| Member | Set per program | See the plan, tick days, fill their own daily and program status, see their own reports. |

One person can be a member of one program and a coordinator of another. Everyone who is approved can use the feed. Nobody gets in without an invite.

## Main journeys

**Admin starts a program**
1. Signs in with the company email (Supabase magic link).
2. Opens Programs, creates "DevOps Training" with a start date.
3. Opens Manage and invite, invites a coordinator by email. No email is sent; the admin shares the site link.

**Coordinator sets up the program**
1. Signs in. The invite turns into a coordinator role.
2. Adds the plan: week, day, topic, topics to cover, optional practical task.
3. Edits the daily form and the day form (fields, labels, the (i) description, required or not).
4. Invites members by email.

**Member works through a day**
1. Signs in. Sees the program in the side panel.
2. Opens Plan, reads the day's topics and task.
3. Opens Daily status, fills the form for today, saves.
4. Opens Program status, fills "What You Learn", proof and a 1-5 rating, ticks the day completed.

**Coordinator reports**
1. Opens Reports, picks Daily or Program.
2. Sees one row per person; days not filled in show as "not started".
3. Downloads Excel. The file has the same layout as the training sheets (Summary + Status; Readiness Guide + one sheet per person).

**Anyone shares something**
1. Opens the Feed (the home page).
2. Posts text or a link. A YouTube link plays in place.
3. Others like and comment. The feed sorts by newest, most liked or most discussed, filters by kind, and searches text and links.

## Scope

In scope: invite-only access, programs, per-program roles, plan, two configurable forms, daily and program reports, Excel export, feed (posts, links, YouTube, likes, comments, sort, filter, search, edit, single post page), light and dark mode, phone layout.

Out of scope for now:
- Sending emails (invites are shared by link).
- Notifications and reminders.
- File uploads in the feed (links only).
- Bulk import from the old Excel sheets.
- The old tracker features (assistants, practice lab, study notes, old reports). They stay in the earlier repos; see [DISCLOSURE.md](DISCLOSURE.md).

## Success measures

| Measure | Target |
|---|---|
| A coordinator can see who has not filled in today | One page, no spreadsheet work |
| Weekly report time | Minutes (one download) instead of copying rows by hand |
| Excel layout | Opens in the same layout as the existing training sheets |
| Access | Only invited people get in; checked by tests |
| Feed use | Links shared in the feed instead of chat (not measured yet) |

The usage measures are not tracked in the app yet. They are checked by asking coordinators.
