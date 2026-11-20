-- FDE Learning Tracker: core schema (Postgres). Idempotent: safe to run more than once.

create table if not exists stacks (
  id    text primary key,
  label text not null,
  sort  int  not null default 0
);
insert into stacks (id, label, sort) values
  ('dotnet', '.NET (C#)', 1), ('node', 'Node.js / MERN', 2), ('java', 'Java / Spring Boot', 3), ('python', 'Python', 4)
on conflict (id) do nothing;

create table if not exists app_users (
  id            uuid primary key,                       -- the auth provider's user id
  email         text not null unique,
  display_name  text check (display_name is null or char_length(display_name) <= 80),
  stack_id      text references stacks(id),
  role          text not null default 'member' check (role in ('member', 'reviewer', 'admin')),
  access_status text not null default 'pending' check (access_status in ('pending', 'approved', 'rejected')),
  created_at    timestamptz not null default now(),
  last_seen_at  timestamptz not null default now()
);

create table if not exists topics (
  id          serial primary key,
  slug        text not null unique check (slug ~ '^[a-z0-9-]{2,40}$'),
  title       text not null,
  description text,
  sort        int  not null default 0
);
insert into topics (slug, title, description, sort) values
  ('fde-foundations',   'FDE foundations',        'Problem discovery, solution design, APIs, data, quality and security basics', 1),
  ('llm-integration',   'LLM integration',        'Calling models through APIs, structured output, provider switching', 2),
  ('prompt-engineering','Prompt engineering',     'System prompts, structured outputs, multi-step prompting', 3),
  ('rag',               'RAG',                    'Retrieval-augmented generation, citations, evaluation of retrieval', 4),
  ('vector-databases',  'Vector databases',       'Embeddings, chunking, semantic search, relevance', 5),
  ('agents',            'Agents and tool calling','Agent loops, tools, human approval', 6),
  ('mcp',               'MCP',                    'Model Context Protocol servers and clients', 7),
  ('evaluation',        'AI evaluation',          'Hallucination, accuracy, guardrails, CI checks', 8),
  ('deployment',        'Deployment and monitoring','Containers, cloud, observability, security', 9),
  ('fine-tuning',       'Fine-tuning',            'LoRA/QLoRA, when to fine-tune versus RAG', 10)
on conflict (slug) do nothing;

create table if not exists content_items (
  id           uuid primary key default gen_random_uuid(),
  topic_id     int  not null references topics(id),
  stack_id     text references stacks(id),               -- null = general (everyone sees it)
  kind         text not null check (kind in ('article', 'video', 'link', 'note')),
  title        text not null check (char_length(title) between 3 and 140),
  summary      text check (summary is null or char_length(summary) <= 400),
  body_md      text check (body_md is null or char_length(body_md) <= 20000),
  url          text check (url is null or (url ~ '^https://' and char_length(url) <= 500)),
  author_id    uuid not null references app_users(id) on update cascade,
  status       text not null default 'draft'
               check (status in ('draft', 'in_review', 'changes_requested', 'approved', 'rejected')),
  submitted_at timestamptz,
  approved_at  timestamptz,
  created_at   timestamptz not null default now(),
  updated_at   timestamptz not null default now()
);
create index if not exists content_feed_idx on content_items (approved_at desc, id desc) where status = 'approved';
create index if not exists content_topic_idx on content_items (topic_id, stack_id) where status = 'approved';
create index if not exists content_author_idx on content_items (author_id, created_at desc);
create index if not exists content_queue_idx on content_items (submitted_at) where status = 'in_review';

create table if not exists reviews (
  id          uuid primary key default gen_random_uuid(),
  content_id  uuid not null references content_items(id) on delete cascade,
  reviewer_id uuid not null references app_users(id) on update cascade,
  decision    text not null check (decision in ('approve', 'request_changes', 'reject')),
  comment     text check (comment is null or char_length(comment) <= 2000),
  created_at  timestamptz not null default now()
);
create index if not exists reviews_content_idx on reviews (content_id, created_at desc);

create table if not exists programs (
  id          uuid primary key default gen_random_uuid(),
  slug        text not null unique check (slug ~ '^[a-z0-9-]{2,60}$'),
  title       text not null check (char_length(title) between 2 and 120),
  description text check (description is null or char_length(description) <= 600),
  visibility  text not null default 'private' check (visibility in ('public', 'private')),
  owner_id    uuid references app_users(id) on update cascade on delete cascade,   -- null = system program
  created_at  timestamptz not null default now(),
  check ((visibility = 'public' and owner_id is null) or (visibility = 'private' and owner_id is not null))
);

create table if not exists program_items (
  id          uuid primary key default gen_random_uuid(),
  program_id  uuid not null references programs(id) on delete cascade,
  position    int  not null check (position >= 0),
  title       text not null check (char_length(title) between 1 and 140),
  description text check (description is null or char_length(description) <= 600),
  content_id  uuid references content_items(id) on delete set null,
  unique (program_id, position) deferrable initially deferred
);

create table if not exists progress (
  user_id         uuid not null references app_users(id) on update cascade on delete cascade,
  program_item_id uuid not null references program_items(id) on delete cascade,
  done_at         timestamptz not null default now(),
  primary key (user_id, program_item_id)
);

create table if not exists audit_log (
  id         bigserial primary key,
  actor_id   uuid,
  action     text not null,
  target     text,
  detail     jsonb,
  created_at timestamptz not null default now()
);

-- The company six-week programme as a public system program.
insert into programs (slug, title, description, visibility, owner_id) values
  ('fde-six-week', 'FDE six-week programme', 'The standard company FDE programme: one expected output per week.', 'public', null)
on conflict (slug) do nothing;

insert into program_items (program_id, position, title, description)
select p.id, v.pos, v.title, v.descr from programs p
cross join (values
  (0, 'Week 1: Baseline and FDE foundations', 'Baseline assessment; problem discovery, solution design, APIs, data, quality and security fundamentals'),
  (1, 'Week 2: LLM engineering', 'Working application feature using an LLM, structured outputs and tool calling'),
  (2, 'Week 3: RAG and vector databases', 'Document-based assistant with retrieval, relevant sources and citations'),
  (3, 'Week 4: Agents and MCP', 'Multi-step agent or workflow connected to tools or APIs, with appropriate human oversight'),
  (4, 'Week 5: Agentic development and production readiness', 'AI-assisted feature delivery with tests, review, security and deployment practices'),
  (5, 'Week 6: Individual capstone and evaluation', 'Demonstrable end-to-end solution, technical documentation and final presentation')
) as v(pos, title, descr)
where p.slug = 'fde-six-week'
  and not exists (select 1 from program_items i where i.program_id = p.id);
