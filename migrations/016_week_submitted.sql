-- A second kind of notification: when a member or coordinator submits their week, the program's coordinators and the
-- admin hear about it. Several people submit the same week, so a notification is unique per subject person as well.
alter table notifications drop constraint if exists notifications_kind_check;
alter table notifications add constraint notifications_kind_check check (kind in ('daily_missed', 'week_submitted'));
alter table notifications add column if not exists subject_user_id uuid null references app_users(id) on update cascade on delete cascade;
drop index if exists notifications_once_idx;
create unique index if not exists notifications_once_idx
  on notifications (user_id, kind, program_id, on_date, coalesce(subject_user_id, '00000000-0000-0000-0000-000000000000'::uuid));
