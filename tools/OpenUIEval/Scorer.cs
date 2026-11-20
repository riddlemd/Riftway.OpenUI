using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Library;
using OpenUI.Lang.Runtime;
using Riftway.OpenUI.Samples;

namespace Riftway.OpenUI.Eval;

/// <summary>How one model reply fared once parsed, validated and run against the sample tools.</summary>
/// <param name="Renders">The program has a root component to draw.</param>
/// <param name="Errors">Every error that is not a warning, parse and runtime alike.</param>
/// <param name="UsesQuery">The program reads live data through at least one Query.</param>
public sealed record ScoreResult(bool Renders, IReadOnlyList<ScoredError> Errors, bool UsesQuery, int Statements)
{
    /// <summary>Clean: it draws and nothing went wrong.</summary>
    public bool Valid => Renders && Errors.Count == 0;
}

public sealed record ScoredError(string Code, string Message, string? Statement);

/// <summary>Scores a reply the way the app would see it: a session loads it and runs its queries.</summary>
/// <param name="library">The components replies are checked against.</param>
/// <param name="tools">Makes the tools for one reply; the sample tools when null.</param>
public sealed class Scorer(OpenUILibrary library, Func<IOpenUIToolProvider>? tools = null)
{
    public async Task<ScoreResult> ScoreAsync(string reply)
    {
        // Fresh tools per reply, so one reply's mutations can't change what the next one reads.
        var provider = tools?.Invoke() ?? new SampleTools().Tools;
        await using var session = OpenUISession.Create(library, new OpenUISessionOptions { Tools = provider });
        session.Load(reply);
        await session.WhenIdleAsync().ConfigureAwait(false);
        var view = session.Current;
        var errors = view.Errors
            .Where(e => !e.IsWarning)
            .Select(e => new ScoredError(e.Code.Value, e.Message, e.StatementId))
            .ToList();
        return new ScoreResult(view.Root is not null, errors, view.Queries.Count > 0, view.Document.Statements.Length);
    }
}
