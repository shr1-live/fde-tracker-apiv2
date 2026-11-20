# Day 1 evidence: LLM fundamentals and model selection

**Task:** a minimal LLM-backed endpoint, secure configuration, timeout and API error handling, latency and token logging, the same fixed prompt set run on two models, and a recommendation with reasons.

## Result

| Model | Passed (20 fixed prompts) | Median time | Input tokens | Output tokens | Errors |
|---|---|---|---|---|---|
| **qwen2.5:3b (recommended)** | **18 / 20** | 2.1 s | 1,442 | 387 | 0 |
| qwen2.5:1.5b | 16 / 20 | 1.1 s | 1,442 | 495 | 0 |

Both failed the SQL question and obeyed an injected "reply BANANA"; the 1.5b model invented the plot of a book that does not exist. Recommendation: qwen2.5:3b, because a wrong answer costs more than a slow one. Both run locally, so cost is measured in tokens; a hosted model has not been compared yet.

## Files

| What it proves | File |
|---|---|
| Comparison report and recommendation | [day1-local-v2-comparison.md](../evals/day1-local-v2-comparison.md) |
| Every answer, time and token count | [day1-local-v2-results.json](../evals/day1-local-v2-results.json) |
| The 20 fixed prompts and their scoring rules | [day1-prompts.json](../../evals/day1-prompts.json) |
| Endpoint | [AssistantController.cs](../../FdeTracker.Api/Controllers/AssistantController.cs) |
| Timeout, retries, latency and token capture | [LlmClient.cs](../../FdeTracker.Api/Services/Llm/LlmClient.cs) |
| Error categories (validation, permission, business, transient, rate limit) | [Errors.cs](../../FdeTracker.Api/Services/Llm/Errors.cs) · [LlmFailure.cs](../../FdeTracker.Api/Services/Llm/LlmFailure.cs) |
| Key only in environment variables | [AppOptions.cs](../../FdeTracker.Api/Common/AppOptions.cs) |
| Usage log (latency, tokens, outcome; no prompt text) | [002_llm_calls.sql](../../migrations/002_llm_calls.sql) |
| Tests: timeout, 429, 5xx, network, refused key, garbled answers, no key or provider text leaked | [LlmClientTests.cs](../../FdeTracker.Tests/LlmClientTests.cs) · [ResilienceTests.cs](../../FdeTracker.Tests/ResilienceTests.cs) |
| How to run the comparison | [README: Comparing models and prompt versions](../../README.md) |
