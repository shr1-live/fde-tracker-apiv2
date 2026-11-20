-- Submissions: a person hands in a task (for example a Week 1 day) with proof, and reviewers approve it or ask for changes.
-- One submission per person per task; proofs are https links or small files; every review decision is kept.

create table if not exists submissions (
  id           uuid primary key default gen_random_uuid(),
  user_id      uuid not null references app_users(id) on update cascade on delete cascade,
  task_key     text not null check (task_key ~ '^[a-z0-9-]{3,60}$'),
  note         text check (note is null or char_length(note) <= 2000),
  status       text not null default 'draft' check (status in ('draft', 'submitted', 'changes_requested', 'approved')),
  submitted_at timestamptz,
  reviewed_at  timestamptz,
  created_at   timestamptz not null default now(),
  updated_at   timestamptz not null default now(),
  unique (user_id, task_key)
);
create index if not exists submissions_queue_idx on submissions (status, submitted_at);

create table if not exists submission_items (
  id            uuid primary key default gen_random_uuid(),
  submission_id uuid not null references submissions(id) on delete cascade,
  kind          text not null check (kind in ('link', 'file')),
  label         text not null check (char_length(label) between 1 and 120),
  url           text check (url is null or (url ~ '^https://' and char_length(url) <= 500)),
  file_name     text check (file_name is null or char_length(file_name) <= 200),
  content_type  text check (content_type is null or char_length(content_type) <= 100),
  size_bytes    int  check (size_bytes is null or size_bytes between 1 and 2097152),
  data          bytea,
  created_at    timestamptz not null default now(),
  check ((kind = 'link' and url is not null and data is null) or (kind = 'file' and data is not null and url is null))
);
create index if not exists submission_items_sub_idx on submission_items (submission_id);

create table if not exists submission_reviews (
  id            uuid primary key default gen_random_uuid(),
  submission_id uuid not null references submissions(id) on delete cascade,
  reviewer_id   uuid not null references app_users(id) on update cascade,
  decision      text not null check (decision in ('approve', 'request_changes')),
  comment       text check (comment is null or char_length(comment) <= 2000),
  created_at    timestamptz not null default now()
);
create index if not exists submission_reviews_sub_idx on submission_reviews (submission_id, created_at desc);
