using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenUI.Lang.Blazor;
using Riftway.OpenUI.Eval;
using Riftway.OpenUI.Samples;

// Measures how often a model writes OpenUI Lang the sample can draw cleanly.
//   prompt <file>                              write the system prompt, for generating replies elsewhere
//   generate --out <dir> [--model m] [--runs n] ask the model (needs ANTHROPIC_API_KEY); one <id>.r<n>.oui per reply
//   score --in <dir>                           score every .oui in <dir>; writes results.json and report.md
var services = new ServiceCollection().AddSampleOpenUI().BuildServiceProvider();
var components = services.GetRequiredService<OpenUIComponents>();
var systemPrompt = OpenUISetup.SystemPrompt(components);
var prompts = JsonSerializer.Deserialize<List<EvalPrompt>>(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "prompts.json")), JsonOptions.Web)!;

return args switch
{
    ["prompt", var file] => WritePrompt(file),
    ["generate", .. var rest] => await GenerateAsync(Option(rest, "--out") ?? Usage(), Option(rest, "--model") ?? OpenUISetup.ModelId,
        int.Parse(Option(rest, "--runs") ?? "1", System.Globalization.CultureInfo.InvariantCulture)),
    ["score", .. var rest] => await ScoreAsync(Option(rest, "--in") ?? Usage()),
    _ => Fail("usage: prompt <file> | generate --out <dir> [--model m] [--runs n] | score --in <dir>"),
};

int WritePrompt(string file)
{
    File.WriteAllText(file, systemPrompt);
    Console.WriteLine($"wrote {file}: {systemPrompt.Length} chars, {prompts.Count} prompts in prompts.json");
    return 0;
}

async Task<int> GenerateAsync(string outDir, string model, int runs)
{
    using var client = OpenUISetup.CreateChatClient(model);
    if (client is null) return Fail("ANTHROPIC_API_KEY is not set");
    Directory.CreateDirectory(outDir);
    var log = new List<GenerationRecord>();
    for (var run = 1; run <= runs; run++)
    {
        foreach (var p in prompts)
        {
            var watch = Stopwatch.StartNew();
            var response = await client.GetResponseAsync([OpenUISetup.SystemMessage(systemPrompt), new(ChatRole.User, p.Prompt)]);
            watch.Stop();
            await File.WriteAllTextAsync(Path.Combine(outDir, $"{p.Id}.r{run}.oui"), response.Text);
            var usage = response.Usage;
            log.Add(new(p.Id, run, model, watch.ElapsedMilliseconds, usage?.InputTokenCount, usage?.OutputTokenCount,
                usage?.CachedInputTokenCount));
            Console.WriteLine($"{p.Id} r{run}: {watch.ElapsedMilliseconds} ms, out {usage?.OutputTokenCount} tok, cached in {usage?.CachedInputTokenCount}");
        }
    }

    await File.WriteAllTextAsync(Path.Combine(outDir, "generation.json"), JsonSerializer.Serialize(log, JsonOptions.Indented));
    return 0;
}

async Task<int> ScoreAsync(string inDir)
{
    var scorer = new Scorer(components.Library);
    var rows = new List<ScoredReply>();
    foreach (var file in Directory.EnumerateFiles(inDir, "*.oui").Order(StringComparer.Ordinal))
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var result = await scorer.ScoreAsync(await File.ReadAllTextAsync(file));
        rows.Add(new(name.Split('.')[0], name, result));
    }

    if (rows.Count == 0) return Fail($"no .oui files in {inDir}");
    await File.WriteAllTextAsync(Path.Combine(inDir, "results.json"), JsonSerializer.Serialize(rows, JsonOptions.Indented));
    var report = Report(inDir, rows);
    await File.WriteAllTextAsync(Path.Combine(inDir, "report.md"), report);
    Console.WriteLine(report);
    return 0;
}

static string Report(string inDir, List<ScoredReply> rows)
{
    var valid = rows.Count(r => r.Result.Valid);
    var renders = rows.Count(r => r.Result.Renders);
    var sb = new StringBuilder();
    sb.AppendLine($"# OpenUI eval: {Path.GetFileName(Path.TrimEndingDirectorySeparator(inDir))}");
    sb.AppendLine();
    sb.AppendLine($"- Replies: {rows.Count}");
    sb.AppendLine($"- Clean (draws, no errors): {valid} ({Pct(valid, rows.Count)})");
    sb.AppendLine($"- Draws something: {renders} ({Pct(renders, rows.Count)})");
    sb.AppendLine($"- Uses a Query: {rows.Count(r => r.Result.UsesQuery)}");
    sb.AppendLine();
    var codes = rows.SelectMany(r => r.Result.Errors).GroupBy(e => e.Code).OrderByDescending(g => g.Count()).ToList();
    if (codes.Count > 0)
    {
        sb.AppendLine("| Error code | Count |");
        sb.AppendLine("| --- | --- |");
        foreach (var g in codes) sb.AppendLine($"| `{g.Key}` | {g.Count()} |");
        sb.AppendLine();
    }

    sb.AppendLine("| Reply | Clean | Draws | Query | Statements | Errors |");
    sb.AppendLine("| --- | --- | --- | --- | --- | --- |");
    foreach (var r in rows)
    {
        var errors = string.Join("; ", r.Result.Errors.Select(e => $"`{e.Code}` {e.Statement}: {e.Message}".Replace("|", "\\|", StringComparison.Ordinal)));
        sb.AppendLine($"| {r.File} | {Mark(r.Result.Valid)} | {Mark(r.Result.Renders)} | {Mark(r.Result.UsesQuery)} | {r.Result.Statements} | {errors} |");
    }

    return sb.ToString();
}

static string Pct(int n, int of) => $"{100.0 * n / of:0}%";
static string Mark(bool b) => b ? "yes" : "no";
static string? Option(string[] rest, string name) => Array.IndexOf(rest, name) is var i and >= 0 && i + 1 < rest.Length ? rest[i + 1] : null;
static string Usage() => throw new ArgumentException("missing a required option; run with no arguments for usage");
static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 2;
}

internal sealed record EvalPrompt(string Id, string Prompt);
internal sealed record ScoredReply(string PromptId, string File, ScoreResult Result);
internal sealed record GenerationRecord(string PromptId, int Run, string Model, long Milliseconds, long? InputTokens, long? OutputTokens, long? CachedInputTokens);

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    public static readonly JsonSerializerOptions Indented = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
