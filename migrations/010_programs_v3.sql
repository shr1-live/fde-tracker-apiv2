-- Programs V3 (docs/PROGRAMS-V3.md): several invite-only learning programs, each with its own plan (weeks and days),
-- its own status forms, members and coordinators, plus a shared feed. The old tables are not touched.
-- Idempotent: safe to run more than once. Every user reference follows a re-linked account (on update cascade).

create table if not exists lp_programs (
  id          uuid primary key default gen_random_uuid(),
  name        text not null unique check (char_length(name) between 2 and 80),
  description text null check (description is null or char_length(description) <= 600),
  starts_on   date null,
  created_at  timestamptz not null default now()
);

create table if not exists lp_members (
  program_id uuid not null references lp_programs(id) on delete cascade,
  user_id    uuid not null references app_users(id) on update cascade on delete cascade,
  role       text not null check (role in ('coordinator', 'member')),
  added_at   timestamptz not null default now(),
  primary key (program_id, user_id)
);
create index if not exists lp_members_user_idx on lp_members (user_id);

create table if not exists lp_invites (
  id          uuid primary key default gen_random_uuid(),
  email       text not null check (email = lower(email) and char_length(email) between 3 and 254),
  program_id  uuid not null references lp_programs(id) on delete cascade,
  role        text not null check (role in ('coordinator', 'member')),
  invited_by  uuid null references app_users(id) on update cascade on delete set null,
  created_at  timestamptz not null default now(),
  accepted_at timestamptz null,
  unique (email, program_id)
);
create index if not exists lp_invites_open_idx on lp_invites (email) where accepted_at is null;

create table if not exists lp_days (
  id              uuid primary key default gen_random_uuid(),
  program_id      uuid not null references lp_programs(id) on delete cascade,
  week            int not null check (week between 1 and 52),
  day             int not null check (day between 1 and 7),
  topic           text not null check (char_length(topic) between 1 and 140),
  topics_to_cover text not null default '' check (char_length(topics_to_cover) <= 4000),
  practical_task  text null check (practical_task is null or char_length(practical_task) <= 4000),
  resources       text null check (resources is null or char_length(resources) <= 2000),
  unique (program_id, week, day)
);

create table if not exists lp_form_fields (
  id          uuid primary key default gen_random_uuid(),
  program_id  uuid not null references lp_programs(id) on delete cascade,
  form        text not null check (form in ('daily', 'day')),
  key         text not null check (key ~ '^[a-z][a-z0-9_]{1,39}$'),
  label       text not null check (char_length(label) between 1 and 80),
  description text null check (description is null or char_length(description) <= 400),
  type        text not null check (type in ('text', 'longtext', 'number', 'rating', 'url')),
  required    boolean not null default false,
  position    int not null default 0,
  unique (program_id, form, key)
);

create table if not exists lp_daily_status (
  program_id uuid not null references lp_programs(id) on delete cascade,
  user_id    uuid not null references app_users(id) on update cascade on delete cascade,
  on_date    date not null,
  "values"   jsonb not null default '{}'::jsonb check (jsonb_typeof("values") = 'object'),
  updated_at timestamptz not null default now(),
  primary key (program_id, user_id, on_date)
);

create table if not exists lp_day_status (
  program_id   uuid not null references lp_programs(id) on delete cascade,
  user_id      uuid not null references app_users(id) on update cascade on delete cascade,
  day_id       uuid not null references lp_days(id) on delete cascade,
  "values"     jsonb not null default '{}'::jsonb check (jsonb_typeof("values") = 'object'),
  completed    boolean not null default false,
  completed_at timestamptz null,
  updated_at   timestamptz not null default now(),
  primary key (user_id, day_id)
);
create index if not exists lp_day_status_program_idx on lp_day_status (program_id, user_id);

create table if not exists feed_posts (
  id         uuid primary key default gen_random_uuid(),
  author_id  uuid not null references app_users(id) on update cascade on delete cascade,
  body       text null check (body is null or char_length(body) between 1 and 2000),
  url        text null check (url is null or (url ~ '^https://' and char_length(url) <= 500)),
  created_at timestamptz not null default now(),
  check (body is not null or url is not null)
);
create index if not exists feed_posts_time_idx on feed_posts (created_at desc, id desc);
create index if not exists feed_posts_author_idx on feed_posts (author_id, created_at desc);

create table if not exists feed_likes (
  post_id    uuid not null references feed_posts(id) on delete cascade,
  user_id    uuid not null references app_users(id) on update cascade on delete cascade,
  created_at timestamptz not null default now(),
  primary key (post_id, user_id)
);

create table if not exists feed_comments (
  id         uuid primary key default gen_random_uuid(),
  post_id    uuid not null references feed_posts(id) on delete cascade,
  author_id  uuid not null references app_users(id) on update cascade on delete cascade,
  body       text not null check (char_length(body) between 1 and 1000),
  created_at timestamptz not null default now()
);
create index if not exists feed_comments_post_idx on feed_comments (post_id, created_at);
create index if not exists feed_comments_author_idx on feed_comments (author_id, created_at desc);

-- Seed: the FDE Training Program. It starts with Batch 1 when that batch exists, else on 2026-11-16.
insert into lp_programs (name, description, starts_on)
values ('FDE Training Program', 'Forward Deployed Engineer training: LLM applications from fundamentals to a delivered assistant.',
        coalesce((select starts_on from batches where lower(name) = 'batch 1' order by created_at limit 1), date '2026-11-16'))
on conflict (name) do nothing;

-- Week 1, Days 1-5, as in the "Status Report" sheet.
insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task)
select p.id, d.week, d.day, d.topic, d.topics_to_cover, d.practical_task
from lp_programs p, (values
  (1, 1, $v$LLM Fundamentals & Model Selection$v$,
   $v$• LLM architecture – high-level transformer concepts.
• Tokens, tokenization and context windows.
• System prompts, user prompts and assistant messages.
• Temperature and output generation parameters.
• Context length and token limitations.
• Model selection – capability, latency, cost and reliability.
• LLM APIs – request, response, streaming and error handling.
• Multimodal models – text, images and audio overview.
• Input/output token costs and usage tracking.
• Model limitations and hallucinations.$v$,
   $v$Practical task:
Build a minimal LLM-backed endpoint or service using any technology stack. Run the same fixed prompt set against at least two model options or configurations, where access permits.

Acceptance criteria:
Successful API integration; secure configuration; handles timeout and API errors; captures latency and token usage; compares outputs on a fixed test set; recommends a model with reasons.$v$),
  (1, 2, $v$Advanced Prompt Engineering$v$,
   $v$• Zero-shot, one-shot and few-shot prompting.
• Role prompting and instruction hierarchy.
• Prompt structure: context, task, constraints and expected output.
• Reasoning-oriented prompting techniques.
• Breaking complex tasks into smaller prompt steps.
• Providing examples and reference data in prompts.
• Prompt templates, variables and reusable components.
• Handling ambiguous requests and asking clarifying questions.
• Reducing prompt injection and instruction-conflict risks.
• Iterating, testing and improving prompts.$v$,
   $v$Practical task:
Build a recruitment candidate-analysis assistant. Compare at least three prompt versions using the same job description and candidate profiles.

Acceptance criteria:
Consistent output structure; prompt versions are stored; test cases are repeatable; missing information is identified rather than invented; output is grounded in supplied candidate evidence.$v$),
  (1, 3, $v$Structured Outputs & Tool Calling$v$,
   $v$• Why structured outputs are useful in LLM applications.
• JSON mode and schema-constrained responses.
• Defining and validating JSON schemas.
• Handling malformed, incomplete and unexpected outputs.
• Function/tool calling concepts and workflow.
• Defining tool names, descriptions and input parameters.
• Passing tool results back to the model.
• Multi-step tool calls and orchestration.
• Tool errors, retries, timeouts and fallback handling.
• Tool permissions and safe execution boundaries.$v$,
   $v$Practical task:
Build an AI task-management assistant that extracts task details into a schema and calls controlled create, list, and update tools. Require confirmation before destructive actions.

Acceptance criteria:
Schema validation; invalid arguments handled; tools execute only in application code; authentication and permission checks; tool failures handled; destructive actions require confirmation; execution is logged.$v$),
  (1, 4, $v$LLM Evaluation, Reliability & Guardrails$v$,
   $v$• Defining quality criteria and success metrics for LLM features.
• Creating representative test datasets and test cases.
• Evaluating accuracy, relevance, completeness and consistency.
• Using human review and automated evaluation.
• Testing edge cases and adversarial inputs.
• Identifying and reducing hallucinations.
• Input and output validation.
• Guardrails for unsafe, irrelevant or policy-violating responses.
• Monitoring latency, failures, token usage and cost.
• Regression testing prompts and model changes.$v$,
   $v$Practical task:
Create a 30-case evaluation set for the recruitment assistant: 10 normal, 5 incomplete, 5 ambiguous, 5 adversarial/prompt-injection, and 5 edge cases. Add guardrails and rerun the evaluation.

Acceptance criteria:
All test categories represented; expected behaviour defined; before-and-after results recorded; failure cases documented; prompt-injection tests do not override trusted instructions; no unsupported candidate claims.$v$),
  (1, 5, $v$End-to-End LLM Application$v$,
   $v$• Understanding the architecture of a basic LLM-powered application.
• Connecting an application to an LLM provider API.
• Managing API keys and environment configuration securely.
• Building the request/response flow from frontend to backend.
• Managing conversation history and context.
• Implementing streaming responses in the user interface.
• Handling loading states, errors, retries and rate limits.
• Logging requests and tracking token usage and cost.
• Testing the complete application workflow.
• Deploying and monitoring a basic LLM application.$v$,
   $v$Practical task:
Deliver an AI-powered recruitment assistant that analyses supplied candidate profiles against a job description, returns structured, evidence-based output, and uses at least two controlled tools or application functions.

Acceptance criteria:
Working end-to-end flow; structured and validated response; at least two tools/functions; input/output validation; failure handling; basic logs and token/latency tracking; tests, documentation, and a demo.$v$)
) as d(week, day, topic, topics_to_cover, practical_task)
where p.name = 'FDE Training Program'
on conflict (program_id, week, day) do nothing;

-- The default forms.
insert into lp_form_fields (program_id, form, key, label, description, type, required, position)
select p.id, f.form, f.key, f.label, f.description, f.type, f.required, f.position
from lp_programs p, (values
  ('daily', 'project', 'Project worked', 'Project name and hours spent. Write None if you are not on a project.', 'text', true, 0),
  ('daily', 'theory', 'Training theory', 'Topics covered, completion status and hours spent.', 'longtext', true, 1),
  ('daily', 'practical', 'Practical', 'Practical task progress or status.', 'longtext', true, 2),
  ('day', 'learned', 'What You Learn', 'What you learned from this day''s topics, in your own words.', 'longtext', true, 0),
  ('day', 'proof', 'Proof of Evidence', 'Links (GitHub, document, demo) or a short note that shows the work.', 'longtext', false, 1),
  ('day', 'rating', 'Rate (1-5)', $v$1 = Needs significant guidance; cannot yet complete the task independently.
2 = Understands some concepts but needs frequent help to implement.
3 = Can implement the task with some guidance and review.
4 = Can independently implement, test, and troubleshoot the task.
5 = Can independently deliver and validate a customer-ready solution, explain trade-offs, and handle realistic edge cases.$v$, 'rating', true, 2)
) as f(form, key, label, description, type, required, position)
where p.name = 'FDE Training Program'
on conflict (program_id, form, key) do nothing;

-- Existing people: approved non-admin users join the FDE program; reviewers become its coordinators.
insert into lp_members (program_id, user_id, role)
select p.id, u.id, case when u.role = 'reviewer' then 'coordinator' else 'member' end
from lp_programs p join app_users u on u.access_status = 'approved' and u.role <> 'admin'
where p.name = 'FDE Training Program'
on conflict (program_id, user_id) do nothing;

-- Same lock-down as 008: these tables are for the API only, never for Supabase's public web API.
do $$
declare t text;
begin
  foreach t in array array['lp_programs','lp_members','lp_invites','lp_days','lp_form_fields','lp_daily_status','lp_day_status',
                           'feed_posts','feed_likes','feed_comments']
  loop
    execute format('alter table public.%I enable row level security', t);
    if exists (select 1 from pg_roles where rolname = 'anon') then execute format('revoke all on table public.%I from anon', t); end if;
    if exists (select 1 from pg_roles where rolname = 'authenticated') then execute format('revoke all on table public.%I from authenticated', t); end if;
  end loop;
end $$;
