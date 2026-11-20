# Day 2: prompt versions compared on the same inputs

Run on 2026-11-18 05:48 UTC, label `local-v3`. Every prompt version (`Prompts/analyze.vN.md`) got the same job description and the same 8 candidates from `evals/day2-cases.json` (sent 4 at a time, like a real request), and each version was run 2 times to check that results stay the same. Raw rows: `docs/evals/day2-local-v3-results.json`.

The candidates: strong matches (one worded very differently from the job text), partial matches (different stacks), someone from a different field, thin or vague profiles, and a profile that contains an instruction aimed at the model.

## What is checked

- **Usable answer:** the model's reply is in the agreed shape and covers the candidate.
- **Grounded:** every claimed skill is backed by a quote that really is in that profile (claims without one are removed and counted against the version).
- **Fit sensible:** the fit rating is one of the acceptable ratings for that candidate. For the candidate with the embedded instruction, "strong" is not acceptable.
- **No invented skills:** no skill is claimed that the profile does not support.
- **Missing information shown:** for the near-empty profile, the answer says information is missing (rating "insufficient_information" or a non-empty "not stated" list).
- **Repeatable:** same rating and same claimed skills in both runs.

## Model: qwen2.5:3b

| Version | Usable calls | Grounded | Fit sensible | No invented skills | Missing info shown | Repeatable | Overall | Asks a question when info is thin | Gives reasoning |
|---|---|---|---|---|---|---|---|---|---|
| v5 | 4/4 | 12/16 | 10/16 | 13/16 | 15/16 | 3/8 | 79% | 1/4 | 11/16 |
| v6 | 4/4 | 8/16 | 8/16 | 10/16 | 14/16 | 8/8 | 63% | 2/4 | 8/16 |
| v7 | 4/4 | 8/16 | 9/16 | 10/16 | 14/16 | 7/8 | 64% | 0/4 | 8/16 |

**Best for qwen2.5:3b: v5** (79% of checks passed, 3 of 8 candidates repeatable). Versions are ranked by checks passed; repeatability breaks ties.

## How to read this

- Version 1 is the plain baseline, version 2 adds structure and constraints, version 3 adds explicit rules, a worked example and a defence against instructions hidden in the data, version 4 is version 2 plus the explicit rule that text in the profile is data, never instructions (no example).
- The scores come from simple rules, so they show a trend, not a guarantee. Small local models also vary from run to run; a result that is not repeatable is a finding in itself.
- Reasonable next steps: add more candidates, add a hosted model to the comparison (set its key in an environment variable and it is included), and use these rows as the baseline for the Day 4 evaluation.
