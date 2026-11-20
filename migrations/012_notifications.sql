-- In-app notifications. The first kind is the 10am "you missed a daily status" reminder: one row per person, program and
-- missed day, so running the reminder job twice (a restart, a second instance, the catch-up on sign-in) never sends twice.
create table if not exists notifications (
  id         uuid primary key default gen_random_uuid(),
  user_id    uuid not null references app_users(id) on update cascade on delete cascade,
  kind       text not null check (kind in ('daily_missed')),
  program_id uuid null references lp_programs(id) on delete cascade,
  on_date    date null,
  title      text not null check (char_length(title) between 1 and 200),
  body       text not null default '' check (char_length(body) <= 1000),
  link       text null check (link is null or (link ~ '^/' and char_length(link) <= 300)),
  created_at timestamptz not null default now(),
  read_at    timestamptz null,
  emailed_at timestamptz null
);
create unique index if not exists notifications_once_idx on notifications (user_id, kind, program_id, on_date);
create index if not exists notifications_user_time_idx on notifications (user_id, created_at desc);

-- Same lock as 008: only the API (the table owner) may touch it, never Supabase's public web API.
alter table public.notifications enable row level security;
do $$
begin
  if exists (select 1 from pg_roles where rolname = 'anon') then revoke all on table public.notifications from anon; end if;
  if exists (select 1 from pg_roles where rolname = 'authenticated') then revoke all on table public.notifications from authenticated; end if;
end $$;
