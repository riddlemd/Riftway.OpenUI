namespace Riftway.OpenUI.Samples;

/// <summary>What the sample streams when no model is configured, so it runs (and self-checks) offline.</summary>
internal static class FakeReplies
{
    private const string Queue = """
        root = Stack([title, chart, tbl, form, ask])
        title = TextContent("Tonight's queue", "large-heavy")
        $singer = "Riley"
        $song = "Total Eclipse of the Heart"
        queue = Query("listQueue", {}, {rows: []})
        add = Mutation("addSinger", {singer: $singer, song: $song})
        chart = BarChart(queue.rows.singer, [Series("Rounds waited", queue.rows.waits)])
        tbl = Table([Col("Singer", queue.rows.singer), Col("Song", queue.rows.song), Col("Waited", queue.rows.waits)])
        form = Form("add", Buttons([Button("Add to queue", Action([@Run(add), @Run(queue), @Reset($singer, $song)]), "primary")]), [FormControl("Singer", Input("singer", "Name", "text", {required: true}, $singer)), FormControl("Song", Input("song", "Song", "text", {required: true}, $song))])
        ask = Buttons([Button("Ask the host", @ToAssistant("Who should sing next?"), "secondary")])

        """;

    private const string Offline = """
        root = Stack([card])
        card = Card([CardHeader("Offline demo"), TextContent("Set ANTHROPIC_API_KEY and restart to talk to a model. This reply is canned.")])

        """;

    /// <summary>The n-th canned reply, in chunks the size a model streams.</summary>
    public static IEnumerable<string> Next(int n)
    {
        var text = n == 0 ? Queue : Offline;
        for (var i = 0; i < text.Length; i += 24) yield return text.Substring(i, Math.Min(24, text.Length - i));
    }
}
