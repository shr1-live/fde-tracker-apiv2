-- Daily status as a timesheet: each person adds their own tasks for a week (rows) and logs hours plus a short note per day (cells).
-- A week starts on Monday. Submitting a week stamps it; changing it afterwards turns it back into a draft until submitted again.
create table if not exists lp_time_tasks (
  id         uuid primary key default gen_random_uuid(),
  program_id uuid not null references lp_programs(id) on delete cascade,
  user_id    uuid not null references app_users(id) on update cascade on delete cascade,
  week_start date not null check (extract(isodow from week_start) = 1),
  title      text not null check (char_length(title) between 1 and 120),
  position   int not null default 0,
  created_at timestamptz not null default now()
);
create index if not exists lp_time_tasks_week_idx on lp_time_tasks (program_id, user_id, week_start, position);

create table if not exists lp_time_cells (
  task_id uuid not null references lp_time_tasks(id) on delete cascade,
  on_date date not null,
  minutes int not null check (minutes between 0 and 1440),
  note    text null check (note is null or char_length(note) <= 500),
  primary key (task_id, on_date)
);
create index if not exists lp_time_cells_date_idx on lp_time_cells (on_date);

create table if not exists lp_time_weeks (
  program_id   uuid not null references lp_programs(id) on delete cascade,
  user_id      uuid not null references app_users(id) on update cascade on delete cascade,
  week_start   date not null,
  submitted_at timestamptz null,
  updated_at   timestamptz not null default now(),
  primary key (program_id, user_id, week_start)
);

-- Same lock as 008: only the API (the table owner) may touch these, never Supabase's public web API.
do $$
declare t text;
begin
  foreach t in array array['lp_time_tasks','lp_time_cells','lp_time_weeks']
  loop
    execute format('alter table public.%I enable row level security', t);
    if exists (select 1 from pg_roles where rolname = 'anon') then execute format('revoke all on table public.%I from anon', t); end if;
    if exists (select 1 from pg_roles where rolname = 'authenticated') then execute format('revoke all on table public.%I from authenticated', t); end if;
  end loop;
end $$;
