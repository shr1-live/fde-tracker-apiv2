-- The feed is per program: each post belongs to one program and only that program's people (and the admin) see it.
-- Posts made before this belong to the FDE Training Program, which they were all about.
alter table feed_posts add column if not exists program_id uuid null references lp_programs(id) on delete cascade;
update feed_posts set program_id = (select id from lp_programs where name = 'FDE Training Program' order by created_at limit 1)
where program_id is null;
create index if not exists feed_posts_program_time_idx on feed_posts (program_id, created_at desc, id desc);
