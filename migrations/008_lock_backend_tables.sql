-- The backend's tables live in the same Supabase project as the website. Supabase's public web API (PostgREST) can reach any
-- table in the public schema with the public key every browser has, unless row level security is on. These tables are only for
-- the API: turn RLS on with NO policies (so the public API sees nothing) and take away the public roles' rights as well.
-- The API logs in as the tables' owner, which is not affected by RLS, so it keeps working. The website's own tables
-- (profiles, entries, access_requests) keep their own rules and are not touched here.
do $$
declare t text;
begin
  foreach t in array array['stacks','app_users','topics','content_items','reviews','programs','program_items','progress','audit_log',
                           'llm_calls','submissions','submission_items','submission_reviews','assistant_tasks','pending_actions',
                           'tool_calls','batches','schema_migrations']
  loop
    if to_regclass('public.' || t) is not null then
      execute format('alter table public.%I enable row level security', t);
      if exists (select 1 from pg_roles where rolname = 'anon') then execute format('revoke all on table public.%I from anon', t); end if;
      if exists (select 1 from pg_roles where rolname = 'authenticated') then execute format('revoke all on table public.%I from authenticated', t); end if;
    end if;
  end loop;
end $$;
