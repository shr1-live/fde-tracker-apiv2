using System.Text.RegularExpressions;

namespace FdeTracker.Api.Services.Llm;

/// <summary>
/// Two plain functions that run in our own code (never in the model): find known skills in a text, and score a match.
/// They give an independent answer to compare with what the model says.
/// </summary>
public static class SkillTools
{
    // canonical name -> spellings to look for. Ordinary English words ("rest", "spring", "node", "prompt") are NOT aliases on their own.
    static readonly Dictionary<string, string[]> Vocabulary = new()
    {
        ["C#"] = ["c#", "csharp"], [".NET"] = [".net", "dotnet", "asp.net"], ["Java"] = ["java"], ["Spring Boot"] = ["spring boot"],
        ["Python"] = ["python"], ["Node.js"] = ["node.js", "nodejs"], ["JavaScript"] = ["javascript"], ["TypeScript"] = ["typescript"],
        ["React"] = ["react", "react.js", "reactjs"], ["Angular"] = ["angular"], ["SQL"] = ["sql"], ["PostgreSQL"] = ["postgresql", "postgres"], ["MongoDB"] = ["mongodb"],
        ["Docker"] = ["docker"], ["Kubernetes"] = ["kubernetes", "k8s"], ["AWS"] = ["aws"], ["Azure"] = ["azure"], ["GCP"] = ["gcp", "google cloud"],
        ["REST APIs"] = ["restful", "rest api", "rest apis"], ["GraphQL"] = ["graphql"], ["Git"] = ["git", "github"], ["CI/CD"] = ["ci/cd", "continuous integration"],
        ["Testing"] = ["unit test", "unit tests", "unit testing", "xunit", "pytest"], ["LLM"] = ["llm", "llms", "large language model", "large language models"],
        ["RAG"] = ["rag", "retrieval-augmented generation"], ["Prompt engineering"] = ["prompt engineering"],
        ["Vector databases"] = ["vector database", "vector databases", "vector db"], ["Machine learning"] = ["machine learning"],
        ["Linux"] = ["linux"], ["Communication"] = ["communication skills"], ["Leadership"] = ["leadership", "team lead"],
    };

    // Built once: one compiled pattern per skill.
    static readonly (string Skill, Regex Pattern)[] Patterns = Vocabulary
        .Select(kv => (kv.Key, new Regex(
            "(?<![a-z0-9+#.])(?:" + string.Join("|", kv.Value.Select(Regex.Escape)) + ")(?![a-z0-9+#])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250))))
        .ToArray();

    public static bool IsKnownSkill(string skill) => Vocabulary.Keys.Contains(skill, StringComparer.OrdinalIgnoreCase);

    public static List<string> ExtractSkills(string text)
    {
        var lower = text.ToLowerInvariant();
        return Patterns.Where(p => p.Pattern.IsMatch(lower)).Select(p => p.Skill).OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    public static (List<string> Matched, List<string> Missing, int Score) ScoreMatch(IReadOnlyCollection<string> required, IReadOnlyCollection<string> found)
    {
        var have = found.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matched = required.Where(have.Contains).ToList();
        var missing = required.Where(r => !have.Contains(r)).ToList();
        var score = required.Count == 0 ? 0 : (int)Math.Round(100.0 * matched.Count / required.Count);
        return (matched, missing, score);
    }
}
