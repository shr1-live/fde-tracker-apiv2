-- Feed at scale (docs/PROGRAMS-V3.md, Feed): edits, stored like/comment counters, a stored post kind and indexes for every sort.
-- Safe to run more than once. No new tables (feed_posts, feed_likes and feed_comments already have RLS on from 010).

alter table feed_posts add column if not exists edited_at timestamptz null;
alter table feed_posts add column if not exists like_count int not null default 0;
alter table feed_posts add column if not exists comment_count int not null default 0;

-- video = a YouTube link (youtube.com or youtu.be, any subdomain), link = any other link, text = no link.
-- The host must be followed by a port, path, query, fragment or the end, so "https://youtube.com@evil.example" is a plain link.
alter table feed_posts add column if not exists kind text generated always as (
  case
    when url is null then 'text'
    when url ~* '^https://([a-z0-9-]+\.)*(youtube\.com|youtu\.be)(:[0-9]+)?([/?#]|$)' then 'video'
    else 'link'
  end) stored;

-- The counters change in the same transaction as the like, unlike, comment or delete (including cascades when a person is removed).
create or replace function feed_likes_counter() returns trigger language plpgsql as $$
begin
  if tg_op = 'INSERT' then
    update feed_posts set like_count = like_count + 1 where id = new.post_id;
  else
    update feed_posts set like_count = greatest(like_count - 1, 0) where id = old.post_id;
  end if;
  return null;
end $$;

create or replace function feed_comments_counter() returns trigger language plpgsql as $$
begin
  if tg_op = 'INSERT' then
    update feed_posts set comment_count = comment_count + 1 where id = new.post_id;
  else
    update feed_posts set comment_count = greatest(comment_count - 1, 0) where id = old.post_id;
  end if;
  return null;
end $$;

drop trigger if exists feed_likes_count on feed_likes;
create trigger feed_likes_count after insert or delete on feed_likes for each row execute function feed_likes_counter();
drop trigger if exists feed_comments_count on feed_comments;
create trigger feed_comments_count after insert or delete on feed_comments for each row execute function feed_comments_counter();

-- The public roles have no business calling these.
revoke all on function feed_likes_counter() from public;
revoke all on function feed_comments_counter() from public;

-- Backfill (and repair) the counters from the real rows.
update feed_posts p set
  like_count = (select count(*)::int from feed_likes l where l.post_id = p.id),
  comment_count = (select count(*)::int from feed_comments c where c.post_id = p.id);

-- One index per sort order (new uses feed_posts_time_idx from 010), plus the kind filter and "my posts".
create index if not exists feed_posts_top_idx on feed_posts (like_count desc, created_at desc, id desc);
create index if not exists feed_posts_discussed_idx on feed_posts (comment_count desc, created_at desc, id desc);
create index if not exists feed_posts_kind_idx on feed_posts (kind, created_at desc, id desc);
create index if not exists feed_posts_author_time_idx on feed_posts (author_id, created_at desc, id desc);
create index if not exists feed_comments_post_time_idx on feed_comments (post_id, created_at desc, id desc);
