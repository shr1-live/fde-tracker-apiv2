using System.Reflection;
using System.Text.Json;

namespace FdeTracker.Api.Services.Lab;

/// <summary>
/// The Day 1 prompt set and the Day 2 cases, built into the API from the same files the evaluation tool reads
/// (evals/day1-prompts.json and evals/day2-cases.json), so the web lab and the evals always run the same cases.
/// </summary>
public sealed class LabData
{
    public PromptSet Day1 { get; }
    public Day2Cases Day2 { get; }

    public LabData()
    {
        Day1 = Load<PromptSet>("day1-prompts.json");
        Day2 = Load<Day2Cases>("day2-cases.json");
    }

    static T Load<T>(string file)
    {
        var name = "FdeTracker.Api.Lab." + file;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Missing built-in lab file {file}");
        return JsonSerializer.Deserialize<T>(stream) ?? throw new InvalidOperationException($"Empty built-in lab file {file}");
    }
}
