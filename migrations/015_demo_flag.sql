-- Demo data: programs the admin loads to show the product, and removes again in one go. Demo people have
-- @demo.local emails, so they can never sign in and never get email.
alter table lp_programs add column if not exists is_demo boolean not null default false;
