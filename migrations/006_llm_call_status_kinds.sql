-- The model client now sorts failures into finer kinds (rate limit, server error, network, refused key...), and the usage log records which one it was.
-- The old check only knew a few statuses, so logging the new kinds would have failed. Widen it.
alter table llm_calls drop constraint if exists llm_calls_status_check;
alter table llm_calls add constraint llm_calls_status_check check (status in (
  'pending', 'ok', 'invalid_output', 'provider_error', 'timeout', 'cancelled',
  'rate_limited', 'server_error', 'network', 'auth', 'bad_request', 'too_large', 'model_not_found', 'content_blocked', 'bad_format', 'truncated'));
