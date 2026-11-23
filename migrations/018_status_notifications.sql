-- Two more kinds of notification, for the people above whoever filled something in: a member's daily status or completed
-- program day tells the program's coordinators and the admin; a coordinator's tells the other coordinators and the admin.
alter table notifications drop constraint if exists notifications_kind_check;
alter table notifications add constraint notifications_kind_check
  check (kind in ('daily_missed', 'week_submitted', 'status_updated', 'day_completed'));
