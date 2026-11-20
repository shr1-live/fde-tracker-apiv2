namespace FdeTracker.Api.Services.Llm;

/// <summary>
/// The stored prompt versions (Prompts/analyze.vN.md). Keeping every version in the repo lets us compare them on the same test cases.
/// v1 to v4 are single prompts; v5 asks for evidence-first reasoning and clarifying questions; v6 adds three worked examples (few-shot);
/// v7 is a two-step chain: step 1 (extract.v1.md) pulls quoted facts out of the profiles, step 2 (judge.v1.md) rates from those facts only.
/// </summary>
public sealed class PromptLibrary
{
    public const string DefaultVersion = "v4"; // best overall for the model in use (qwen2.5:3b, 94%) in docs/evals/day2-local-v2-comparison.md; v2 was best on the smaller 1.5b model, so re-check when the model changes
    public static readonly string[] Versions = ["v1", "v2", "v3", "v4", "v5", "v6", "v7"];
    /// <summary>The version that runs as two model calls.</summary>
    public const string ChainVersion = "v7";
    public static bool IsChain(string version) => version == ChainVersion;

    readonly Dictionary<string, string> _prompts;

    public string Extract { get; }
    public string Judge { get; }

    public PromptLibrary()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Prompts");
        string Read(string name) => File.ReadAllText(Path.Combine(dir, name)).Trim();
        _prompts = Versions.Where(v => !IsChain(v)).ToDictionary(v => v, v => Read($"analyze.{v}.md"));
        Extract = Read("extract.v1.md");
        Judge = Read("judge.v1.md");
    }

    /// <summary>The system prompt of a single-prompt version. The chain has two (<see cref="Extract"/> and <see cref="Judge"/>).</summary>
    public string System(string version) => _prompts[version];
}
