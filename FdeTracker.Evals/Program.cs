using System.Text.Json;
using FdeTracker.Evals;

// Usage: dotnet run --project FdeTracker.Evals -- day1 | day2 | day4 | day4-rescore
// Which models run is decided by evals/targets.json (any provider that offers the common chat-completions API).
// Settings come from environment variables, never from code:
//   EVAL_TARGETS   path to the targets file, default evals/targets.json
//   EVAL_ONLY      optional comma list of target names to run (default: all that can run)
//   EVAL_CASES     Day 4 case file inside evals/, default day4-cases.json
//   EVAL_RUN       label for this run, used in the output file names, default "local"
//   EVAL_TIMEOUT   seconds per call, default 180
//   EVAL_OUT       output folder, default docs/evals
//   plus one environment variable per hosted target holding its key (named in api_key_env)
var command = args.FirstOrDefault() ?? "day1";
var targetsPath = Environment.GetEnvironmentVariable("EVAL_TARGETS") ?? Path.Combine(AppContext.BaseDirectory, "evals", "targets.json");
var only = Environment.GetEnvironmentVariable("EVAL_ONLY")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var casesFile = Path.GetFileName(Environment.GetEnvironmentVariable("EVAL_CASES") ?? "day4-cases.json"); // which Day 4 case file to run, for example day4-holdout.json
var label = new string((Environment.GetEnvironmentVariable("EVAL_RUN") ?? "local").Where(ch => char.IsLetterOrDigit(ch) || ch == '-').ToArray());
if (label.Length == 0) label = "local";
var timeout = int.TryParse(Environment.GetEnvironmentVariable("EVAL_TIMEOUT"), out var t) && t > 0 ? t : 180;
var outDir = Environment.GetEnvironmentVariable("EVAL_OUT") ?? Path.Combine("docs", "evals");
Directory.CreateDirectory(outDir);

switch (command)
{
    case "day1":
        var targets = Targets.Load(targetsPath, only);
        if (targets.Count == 0) { Console.Error.WriteLine("No model can run. Check evals/targets.json and the key environment variables."); return 1; }
        var set = JsonSerializer.Deserialize<PromptSet>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "evals", "day1-prompts.json")))!;
        var results = await Day1Runner.Run(set, targets, timeout);
        var names = targets.Select(x => x.Name).ToArray();
        var json = Path.Combine(outDir, $"day1-{label}-results.json");
        var md = Path.Combine(outDir, $"day1-{label}-comparison.md");
        File.WriteAllText(json, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(md, Report.Day1(results, names, set, label, Targets.LoadPrices(targetsPath)));
        Console.WriteLine($"\nWrote {md}\nWrote {json}");
        break;
    case "day2":
        var day2Targets = Targets.Load(targetsPath, only);
        if (day2Targets.Count == 0) { Console.Error.WriteLine("No model can run. Check evals/targets.json and the key environment variables."); return 1; }
        var cases = JsonSerializer.Deserialize<Day2Cases>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "evals", "day2-cases.json")))!;
        var rows = await Day2Runner.Run(cases, day2Targets, timeout);
        var json2 = Path.Combine(outDir, $"day2-{label}-results.json");
        var md2 = Path.Combine(outDir, $"day2-{label}-comparison.md");
        File.WriteAllText(json2, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(md2, Day2Report.Make(rows, cases, label));
        Console.WriteLine($"\nWrote {md2}\nWrote {json2}");
        break;
    case "day4":
        var day4Targets = Targets.Load(targetsPath, only);
        if (day4Targets.Count == 0) { Console.Error.WriteLine("No model can run. Check evals/targets.json and the key environment variables."); return 1; }
        var set4 = JsonSerializer.Deserialize<Day4Set>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "evals", casesFile)))!;
        var raws4 = await Day4Runner.Call(set4, day4Targets, timeout);
        File.WriteAllText(Path.Combine(outDir, $"day4-{label}-raw.json"), JsonSerializer.Serialize(raws4, new JsonSerializerOptions { WriteIndented = true }));
        var rows4 = Day4Runner.Rescore(set4, raws4);
        var json4 = Path.Combine(outDir, $"day4-{label}-results.json");
        var md4 = Path.Combine(outDir, $"day4-{label}-comparison.md");
        File.WriteAllText(json4, JsonSerializer.Serialize(rows4, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(md4, Day4Report.Make(rows4, set4, label, casesFile));
        Console.WriteLine($"\nWrote {md4}\nWrote {json4}");
        break;
    case "day4-rescore":
        // Re-applies the current checks to the model replies saved by a day4 run. No model is called.
        var set4r = JsonSerializer.Deserialize<Day4Set>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "evals", casesFile)))!;
        var raws4r = JsonSerializer.Deserialize<List<Day4Raw>>(File.ReadAllText(Path.Combine(outDir, $"day4-{label}-raw.json")))!;
        var rows4r = Day4Runner.Rescore(set4r, raws4r);
        File.WriteAllText(Path.Combine(outDir, $"day4-{label}-results.json"), JsonSerializer.Serialize(rows4r, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(outDir, $"day4-{label}-comparison.md"), Day4Report.Make(rows4r, set4r, label, casesFile));
        Console.WriteLine($"Re-scored {raws4r.Count} replies for label {label}.");
        break;
    default:
        Console.Error.WriteLine($"Unknown command '{command}'. Available: day1, day2, day4, day4-rescore");
        return 1;
}
return 0;
