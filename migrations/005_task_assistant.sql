-- Day 3: an AI task assistant. The model only asks for actions; application code validates, checks permission,
-- asks for confirmation before anything destructive, runs the action and logs it.

create table if not exists assistant_tasks (
  id         uuid primary key default gen_random_uuid(),
  user_id    uuid not null references app_users(id) on update cascade on delete cascade,
  title      text not null check (char_length(title) between 1 and 140),
  due_date   date,
  priority   text not null default 'medium' check (priority in ('low', 'medium', 'high')),
  status     text not null default 'open' check (status in ('open', 'done')),
  notes      text check (notes is null or char_length(notes) <= 500),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);
create index if not exists assistant_tasks_user_idx on assistant_tasks (user_id, status, due_date);

-- Destructive actions wait here until the person confirms. Fail closed: a decided or expired row never runs again.
create table if not exists pending_actions (
  id         uuid primary key default gen_random_uuid(),
  user_id    uuid not null references app_users(id) on update cascade on delete cascade,
  tool       text not null check (char_length(tool) <= 40),
  arguments  jsonb not null,
  summary    text not null check (char_length(summary) <= 300),
  expires_at timestamptz not null,
  decided_at timestamptz,
  created_at timestamptz not null default now()
);
create index if not exists pending_actions_user_idx on pending_actions (user_id, created_at desc);

-- Every tool call, whatever happened to it.
create table if not exists tool_calls (
  id         bigserial primary key,
  user_id    uuid not null references app_users(id) on update cascade on delete cascade,
  tool       text not null check (char_length(tool) <= 40),
  arguments  jsonb,
  outcome    text not null check (outcome in ('ok', 'invalid', 'denied', 'failed', 'needs_confirmation', 'confirmed', 'cancelled', 'expired')),
  detail     text check (detail is null or char_length(detail) <= 300),
  latency_ms int,
  created_at timestamptz not null default now()
);
create index if not exists tool_calls_user_idx on tool_calls (user_id, created_at desc);
