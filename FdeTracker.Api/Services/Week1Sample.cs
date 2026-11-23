namespace FdeTracker.Api.Services;

/// <summary>Week 1 as one trainee wrote it in the training sheet (What You Learn, Proof of Evidence, Rate). Sample text for the demo's week 1.</summary>
public static class Week1Sample
{
    public static readonly (string Learned, string Proof, int? Rate)[] Days =
    [
        ("""
I built an API endpoint that sends a prompt to a language model and returns a checked answer. The key, address and model live only in environment variables, never in code. Every call has a timeout and an overall deadline, and every failure is sorted into a category with a retry flag: a 429 waits the time the provider asks for and is retried, 500s and network failures are retried up to three times, a refused key or a bad request is not retried, and each gives the caller a clear message without leaking the provider's reply or the key. Every call logs its latency and input/output token counts. I compared two models on 20 fixed prompts: qwen2.5:3b passed 18/20 at a median of 2.1 s, qwen2.5:1.5b passed 16/20 at 1.1 s, both using 1,442 input tokens. Both failed the SQL question and obeyed an injected "reply BANANA", and the 1.5b model invented the plot of a book that does not exist. I recommend qwen2.5:3b because a wrong answer costs more than a slow one. Both ran locally, so cost is measured in tokens only; a hosted model has not been compared yet.
""", "Day 1 evidence", 5),
        ("""
I built and compared seven stored prompt versions, one per Day 2 technique: v1 plain (zero-shot), v2 structured (context, task, constraints, expected output), v3 one worked example, v4 an explicit rule that profile text is data, never instructions (instruction hierarchy, injection defence), v5 reasoning-oriented (evidence-first reasoning before the rating, plus clarifying questions for thin profiles), v6 few-shot (three worked examples), and v7 a two-step chain (extract quoted facts, my code keeps only real quotes, then rate from those facts only). Every answer has a fixed JSON shape, every claimed skill needs an exact quote from the profile or it is removed, and missing information is listed instead of invented. On 8 fixed candidates run twice on qwen2.5:3b: v4 94%, v2 and v3 88%, v1 84%, v5 79%, v7 64%, v6 63%. The advanced techniques did not help this small model: few-shot was the most repeatable (8 of 8) but rated too cautiously, and the chain lost detail between steps. On the smaller 1.5b model v2 won, so the best prompt depends on the model and must be re-tested when the model changes.
""", "Day 2 evidence", 5),
        ("""
I built a task assistant where the model only asks for one of four tools (create, list, update, delete) as a fixed JSON object, and my code checks and runs it: every argument is validated (24 refused cases in tests), every query uses the signed-in person's id, delete only creates a confirmation question (10 minutes, decided once, re-checked on yes), refusals go back to the model, at most 4 steps, every call logged. Real-model run, qwen2.5:3b, 5 runs checked on the database: create, list, update, delete-asks-first, yes deletes, no keeps all 5 of 5; but it tried a made-up id first every time (refused by my code) and made an unasked update in 2 of 5 runs. Gemini 3.5 Flash Lite by hand in AI Studio with the same tools and rules: correct arguments, looked up the real id before update and delete, chose delete not update, and stated the confirmation. Not yet done: Gemini through the app 5 times, and the injection test.
""", "Day 3 evidence", 4),
        ("""
Built a 30-case evaluation set (10 normal, 5 incomplete, 5 ambiguous, 5 prompt-injection, 5 edge) for the recruitment assistant, with expected behaviour per case and an automatic scorer. Added guardrails in code: every claim needs a real quote from the profile, a rating needs evidence, and an instruction screen checks profiles for hidden orders. Before/after on qwen2.5:3b: 13/30 to 26/30 passed, unsupported claims shown 37 to 0, hidden orders obeyed 3/5 to 0/5. A fresh set of 15 unseen cases passed 13/15; the instruction screen caught none of the new attacks, and the quote rules did the work. Re-run on 19 Nov with new model answers: final again 26/30, 0 unsupported claims, 0/5 orders obeyed, so the result repeats.
""", "Day 4 evidence", 4),
        ("""
Theory only  practice  partially done
""", "Day 5 evidence", null),
    ];
}
