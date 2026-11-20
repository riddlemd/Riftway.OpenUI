using Microsoft.Extensions.DependencyInjection;
using OpenUI.Lang.Blazor;
using OpenUI.Lang.Library;
using OpenUI.Lang.Runtime;
using Riftway.OpenUI.Samples;
using Xunit;

namespace Riftway.OpenUI.Eval.Tests;

public sealed class ScorerTests
{
    private static readonly OpenUILibrary Library = new ServiceCollection().AddSampleOpenUI().BuildServiceProvider()
        .GetRequiredService<OpenUIComponents>().Library;

    private static readonly Scorer Scorer = new(Library);

    [Fact]
    public async Task Given_a_program_of_library_components_reading_a_real_tool_When_scored_Then_it_is_clean_and_uses_a_query()
    {
        // Given
        const string reply = """
            root = Card([title, tbl])
            title = TextContent("Tonight's queue", "large-heavy")
            queue = Query("listQueue", {}, {rows: []})
            tbl = Table([Col("Singer", queue.rows.singer), Col("Song", queue.rows.song)])
            """;

        // When
        var result = await Scorer.ScoreAsync(reply);

        // Then
        Assert.True(result.Valid);
        Assert.True(result.UsesQuery);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Given_a_component_the_library_does_not_have_When_scored_Then_it_draws_but_is_not_clean()
    {
        // Given
        const string reply = """
            root = Card([title, Fancy("x")])
            title = TextContent("Hi")
            """;

        // When
        var result = await Scorer.ScoreAsync(reply);

        // Then
        Assert.True(result.Renders);
        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Code == "unknown-component");
    }

    [Fact]
    public async Task Given_a_query_to_a_tool_that_does_not_exist_When_scored_Then_the_runtime_error_counts()
    {
        // Given
        const string reply = """
            root = Card([title])
            title = TextContent("Hi")
            q = Query("noSuchTool", {}, {rows: []})
            """;

        // When
        var result = await Scorer.ScoreAsync(reply);

        // Then
        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Code == "tool-not-found");
    }

    [Fact]
    public async Task Given_a_tool_that_fails_only_after_it_has_started_When_scored_Then_the_scorer_waits_and_counts_the_failure()
    {
        // Given
        var slowFailing = new OpenUITools().Add("listQueue", OpenUIToolEffect.Read, async (_, ct) =>
        {
            await Task.Delay(100, ct);
            throw new InvalidOperationException("the queue is unavailable");
        });
        var scorer = new Scorer(Library, () => slowFailing);
        const string reply = """
            root = Stack([tbl])
            queue = Query("listQueue", {}, {rows: []})
            tbl = Table([Col("Singer", queue.rows.singer)])
            """;

        // When
        var result = await scorer.ScoreAsync(reply);

        // Then
        Assert.False(result.Valid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task Given_prose_with_no_statements_When_scored_Then_nothing_draws()
    {
        // Given
        const string reply = "Here is your interface!";

        // When
        var result = await Scorer.ScoreAsync(reply);

        // Then
        Assert.False(result.Renders);
        Assert.False(result.Valid);
    }

    [Fact]
    public async Task Given_a_program_with_no_root_statement_that_falls_back_to_its_first_stack_When_scored_Then_the_warning_does_not_count()
    {
        // Given
        const string reply = """
            page = Stack([title])
            title = TextContent("Hi")
            """;

        // When
        var result = await Scorer.ScoreAsync(reply);

        // Then
        Assert.True(result.Valid);
    }
}
