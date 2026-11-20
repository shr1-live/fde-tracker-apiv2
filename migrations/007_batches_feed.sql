-- Batches (a group of learners who start together), a person's batch, and "recommended" picks on the shared feed.
-- Idempotent: safe to run more than once.

create table if not exists batches (
  id         uuid primary key default gen_random_uuid(),
  name       text not null unique check (char_length(name) between 2 and 60),
  starts_on  date not null,
  created_at timestamptz not null default now()
);

alter table app_users add column if not exists batch_id uuid null references batches(id) on delete set null;
create index if not exists app_users_batch_idx on app_users (batch_id);

alter table content_items add column if not exists recommended boolean not null default false;
alter table content_items add column if not exists recommended_at timestamptz null;
