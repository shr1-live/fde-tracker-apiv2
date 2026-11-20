-- One row per call to the LLM: lets us enforce a daily cap per person and watch latency, tokens and failures.
-- A row is reserved ('pending') before the model is called, so parallel or aborted requests still count against the cap.
-- Stores no prompt text and no candidate data.
create table if not exists llm_calls (
  id             bigserial primary key,
  user_id        uuid not null references app_users(id) on update cascade on delete cascade,
  model          text,
  prompt_version text not null,
  input_tokens   int,
  output_tokens  int,
  latency_ms     int,
  status         text not null check (status in ('pending', 'ok', 'invalid_output', 'provider_error', 'timeout', 'cancelled')),
  created_at     timestamptz not null default now()
);
create index if not exists llm_calls_user_day_idx on llm_calls (user_id, created_at desc);
