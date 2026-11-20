using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenUI.Lang.Prompting;
using OpenUI.Lang.Runtime;

namespace Riftway.OpenUI.Samples;

/// <summary>
/// A karaoke night's data, exposed as the tools a program's Query and Mutation statements call. The sample
/// and the eval share it, so a program the eval scores is one the sample can run.
/// </summary>
public sealed class SampleTools
{
    private readonly Lock _gate = new();
    private readonly List<QueueEntry> _queue =
    [
        new("q1", "Dana", "Bohemian Rhapsody", "Queen", 1),
        new("q2", "Lee", "Dancing Queen", "ABBA", 3),
        new("q3", "Sam", "Africa", "Toto", 2),
        new("q4", "Kit", "Mr. Brightside", "The Killers", 0),
    ];

    private static readonly ImmutableArray<Song> Catalog =
    [
        new("s1", "Bohemian Rhapsody", "Queen", "rock", 1975, 354),
        new("s2", "Dancing Queen", "ABBA", "pop", 1976, 231),
        new("s3", "Africa", "Toto", "rock", 1982, 295),
        new("s4", "Mr. Brightside", "The Killers", "rock", 2004, 222),
        new("s5", "Total Eclipse of the Heart", "Bonnie Tyler", "pop", 1983, 330),
        new("s6", "Jolene", "Dolly Parton", "country", 1973, 161),
        new("s7", "Friends in Low Places", "Garth Brooks", "country", 1990, 258),
        new("s8", "Valerie", "Amy Winehouse", "soul", 2007, 219),
        new("s9", "Respect", "Aretha Franklin", "soul", 1967, 147),
        new("s10", "Livin' on a Prayer", "Bon Jovi", "rock", 1986, 249),
        new("s11", "Wannabe", "Spice Girls", "pop", 1996, 173),
        new("s12", "Take Me Home, Country Roads", "John Denver", "country", 1971, 190),
    ];

    private static readonly ImmutableArray<HourStat> Hours =
    [
        new("7 PM", 6, 4), new("8 PM", 11, 7), new("9 PM", 18, 12), new("10 PM", 22, 16), new("11 PM", 15, 9),
    ];

    public SampleTools()
    {
        Tools = new OpenUITools()
            .Add("listQueue", OpenUIToolEffect.Read,
                (NoArgs _, CancellationToken _) => ValueTask.FromResult(ListQueue()),
                SampleJson.Default.NoArgs, SampleJson.Default.QueueResult)
            .Add("searchSongs", OpenUIToolEffect.Read,
                (SearchArgs a, CancellationToken _) => ValueTask.FromResult(Search(a)),
                SampleJson.Default.SearchArgs, SampleJson.Default.SongsResult)
            .Add("nightStats", OpenUIToolEffect.Read,
                (NoArgs _, CancellationToken _) => ValueTask.FromResult(Stats()),
                SampleJson.Default.NoArgs, SampleJson.Default.StatsResult)
            .Add("addSinger", OpenUIToolEffect.Mutating,
                (AddArgs a, CancellationToken _) => ValueTask.FromResult(Add(a)),
                SampleJson.Default.AddArgs, SampleJson.Default.OkResult)
            .Add("removeEntry", OpenUIToolEffect.Mutating,
                (RemoveArgs a, CancellationToken _) => ValueTask.FromResult(Remove(a)),
                SampleJson.Default.RemoveArgs, SampleJson.Default.OkResult);
    }

    /// <summary>What the program's Query and Mutation statements call.</summary>
    public OpenUITools Tools { get; }

    public int QueueCount { get { lock (_gate) return _queue.Count; } }

    /// <summary>The same tools as the system prompt describes them.</summary>
    public static ImmutableArray<ToolDescriptor> Descriptors { get; } =
    [
        new("listQueue", "Tonight's singing queue, in order. waits is how many rounds the singer has waited.",
            Schema("""{"type":"object","properties":{}}"""),
            Schema("""{"type":"object","properties":{"rows":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"singer":{"type":"string"},"song":{"type":"string"},"artist":{"type":"string"},"waits":{"type":"number"}}}}}}""")),
        new("searchSongs", "Songs in the catalog. query matches title or artist; genre is rock, pop, country or soul; both optional.",
            Schema("""{"type":"object","properties":{"query":{"type":"string"},"genre":{"type":"string"}}}"""),
            Schema("""{"type":"object","properties":{"songs":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"title":{"type":"string"},"artist":{"type":"string"},"genre":{"type":"string"},"year":{"type":"number"},"seconds":{"type":"number"}}}}}}""")),
        new("nightStats", "Tonight's totals: songs sung per hour and how many by first-time singers, and songs queued per genre.",
            Schema("""{"type":"object","properties":{}}"""),
            Schema("""{"type":"object","properties":{"hours":{"type":"array","items":{"type":"object","properties":{"hour":{"type":"string"},"songs":{"type":"number"},"firstTimers":{"type":"number"}}}},"genres":{"type":"array","items":{"type":"object","properties":{"genre":{"type":"string"},"count":{"type":"number"}}}}}}""")),
        new("addSinger", "Adds a singer and song to the end of the queue.",
            Schema("""{"type":"object","properties":{"singer":{"type":"string"},"song":{"type":"string"},"artist":{"type":"string"}},"required":["singer","song"]}"""),
            Schema("""{"type":"object","properties":{"ok":{"type":"boolean"}}}""")),
        new("removeEntry", "Removes a queue entry by its id.",
            Schema("""{"type":"object","properties":{"id":{"type":"string"}},"required":["id"]}"""),
            Schema("""{"type":"object","properties":{"ok":{"type":"boolean"}}}""")),
    ];

    private QueueResult ListQueue() { lock (_gate) return new([.. _queue]); }

    private static SongsResult Search(SearchArgs a) => new(
    [
        .. Catalog.Where(s =>
            (string.IsNullOrWhiteSpace(a.Query)
             || s.Title.Contains(a.Query, StringComparison.OrdinalIgnoreCase)
             || s.Artist.Contains(a.Query, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(a.Genre) || s.Genre.Equals(a.Genre, StringComparison.OrdinalIgnoreCase))),
    ]);

    private StatsResult Stats()
    {
        lock (_gate)
        {
            var genres = _queue
                .Select(q => Catalog.FirstOrDefault(s => s.Title == q.Song)?.Genre ?? "other")
                .Concat(Catalog.Select(s => s.Genre))
                .GroupBy(g => g)
                .Select(g => new GenreCount(g.Key, g.Count()))
                .OrderByDescending(g => g.Count)
                .ToList();
            return new([.. Hours], genres);
        }
    }

    private OkResult Add(AddArgs a)
    {
        if (string.IsNullOrWhiteSpace(a.Singer) || string.IsNullOrWhiteSpace(a.Song)) return new(false);
        lock (_gate)
        {
            _queue.Add(new($"q{Guid.NewGuid():N}"[..9], a.Singer, a.Song, a.Artist ?? "", 0));
            return new(true);
        }
    }

    private OkResult Remove(RemoveArgs a)
    {
        lock (_gate) return new(_queue.RemoveAll(q => q.Id == a.Id) > 0);
    }

    private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement.Clone();
}

public sealed record QueueEntry(string Id, string Singer, string Song, string Artist, int Waits);
public sealed record Song(string Id, string Title, string Artist, string Genre, int Year, int Seconds);
public sealed record HourStat(string Hour, int Songs, int FirstTimers);
public sealed record GenreCount(string Genre, int Count);
public sealed record QueueResult(List<QueueEntry> Rows);
public sealed record SongsResult(List<Song> Songs);
public sealed record StatsResult(List<HourStat> Hours, List<GenreCount> Genres);
public sealed record NoArgs;
public sealed record SearchArgs(string? Query, string? Genre);
public sealed record AddArgs(string Singer, string Song, string? Artist);
public sealed record RemoveArgs(string Id);
public sealed record OkResult(bool Ok);

[JsonSerializable(typeof(QueueResult))]
[JsonSerializable(typeof(SongsResult))]
[JsonSerializable(typeof(StatsResult))]
[JsonSerializable(typeof(NoArgs))]
[JsonSerializable(typeof(SearchArgs))]
[JsonSerializable(typeof(AddArgs))]
[JsonSerializable(typeof(RemoveArgs))]
[JsonSerializable(typeof(OkResult))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class SampleJson : JsonSerializerContext;
