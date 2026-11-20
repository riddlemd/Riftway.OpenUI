using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using OpenUI.Lang.Blazor;
using OpenUI.Lang.Runtime;

namespace Riftway.OpenUI.Samples;

/// <summary>One message in the conversation.</summary>
public sealed class ChatTurn
{
    public required string Role { get; init; }

    /// <summary>What the person wrote, or the program the model wrote (complete once <see cref="Done"/>).</summary>
    public string Text { get; set; } = "";

    /// <summary>For a message an action sent: the event's parameters and form values, as JSON.</summary>
    public string? Context { get; init; }

    /// <summary>For a reply: the stream the view draws. It replays, so a redraw never asks the model twice.</summary>
    public ReplayStream? Stream { get; init; }

    public bool Done { get; set; }

    public string? Failure { get; set; }
}

/// <summary>
/// The conversation: sends the person's messages and the program's actions to the model, and streams each
/// reply into a <see cref="ReplayStream"/> the view draws as it arrives.
/// </summary>
public sealed class ChatSession(OpenUIComponents components, IChatClient? client, string modelLabel)
{
    private readonly string _systemPrompt = OpenUISetup.SystemPrompt(components);
    private int _fakeReplies;

    public List<ChatTurn> Turns { get; } = [];

    /// <summary>Raised whenever <see cref="Turns"/> or a turn changes, on any thread.</summary>
    public event Action? Changed;

    /// <summary>The model in use, or the offline demo.</summary>
    public string ModelLabel => client is null ? "offline demo (no ANTHROPIC_API_KEY)" : modelLabel;

    public bool Busy { get; private set; }

    public Task SendAsync(string text, string? context = null)
    {
        if (Busy || string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;
        Busy = true;
        Turns.Add(new ChatTurn { Role = "user", Text = text, Context = context, Done = true });
        var reply = new ChatTurn { Role = "assistant", Stream = new ReplayStream() };
        var history = BuildHistory();
        Turns.Add(reply);
        Changed?.Invoke();
        return Task.Run(() => StreamReplyAsync(reply, history));
    }

    /// <summary>Replaces the conversation with one finished reply, to look at a program without a model.</summary>
    public void Show(string program)
    {
        var stream = new ReplayStream();
        stream.Write(program);
        stream.Complete();
        Turns.Clear();
        Turns.Add(new ChatTurn { Role = "assistant", Text = program, Stream = stream, Done = true });
        Changed?.Invoke();
    }

    /// <summary>Turns a program's action into the next message: <c>@ToAssistant</c> continues the conversation.</summary>
    public async Task HandleHostEventAsync(OpenUIHostEvent e)
    {
        using var json = Serialize(e);
        var root = json.RootElement;
        switch (e.Type)
        {
            case "continue_conversation":
                await SendAsync(e.HumanFriendlyMessage, root.GetRawText());
                break;
            case "open_url" when root.TryGetProperty("params", out var p) && p.TryGetProperty("url", out var url):
                // A link leaves the app for the system browser; inside the window it would replace the app.
                await Shell.OpenExternalAsync(url.GetString()!);
                break;
        }
    }

    private List<ChatMessage> BuildHistory()
    {
        var messages = new List<ChatMessage> { OpenUISetup.SystemMessage(_systemPrompt) };
        foreach (var turn in Turns.Where(t => t.Failure is null))
        {
            messages.Add(turn.Role == "user"
                ? new ChatMessage(ChatRole.User, turn.Context is null ? turn.Text : $"{turn.Text}\n\nContext: {turn.Context}")
                : new ChatMessage(ChatRole.Assistant, turn.Text));
        }

        return messages;
    }

    private async Task StreamReplyAsync(ChatTurn reply, List<ChatMessage> history)
    {
        var text = new StringBuilder();
        try
        {
            if (client is null)
            {
                foreach (var chunk in FakeReplies.Next(_fakeReplies++))
                {
                    await Task.Delay(15);
                    text.Append(chunk);
                    reply.Stream!.Write(chunk);
                }
            }
            else
            {
                await foreach (var update in client.GetStreamingResponseAsync(history))
                {
                    if (string.IsNullOrEmpty(update.Text)) continue;
                    text.Append(update.Text);
                    reply.Stream!.Write(update.Text);
                }
            }
        }
        catch (Exception ex)
        {
            reply.Failure = ex.Message;
        }
        finally
        {
            reply.Text = text.ToString();
            reply.Done = true;
            reply.Stream!.Complete();
            Busy = false;
            Changed?.Invoke();
        }
    }

    private static JsonDocument Serialize(OpenUIHostEvent e)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) e.WriteTo(writer);
        return JsonDocument.Parse(buffer.WrittenMemory);
    }
}

/// <summary>
/// A stream of text chunks that every reader sees from the start: what has arrived replays at once, then new
/// chunks follow until <see cref="Complete"/>.
/// </summary>
public sealed class ReplayStream : IAsyncEnumerable<string>
{
    private readonly Lock _gate = new();
    private readonly List<string> _chunks = [];
    private readonly List<Channel<string>> _readers = [];
    private bool _completed;

    public void Write(string chunk)
    {
        lock (_gate)
        {
            _chunks.Add(chunk);
            foreach (var r in _readers) r.Writer.TryWrite(chunk);
        }
    }

    public void Complete()
    {
        lock (_gate)
        {
            _completed = true;
            foreach (var r in _readers) r.Writer.TryComplete();
        }
    }

    public async IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<string>();
        lock (_gate)
        {
            foreach (var c in _chunks) channel.Writer.TryWrite(c);
            if (_completed) channel.Writer.TryComplete();
            else _readers.Add(channel);
        }

        try
        {
            await foreach (var chunk in channel.Reader.ReadAllAsync(cancellationToken)) yield return chunk;
        }
        finally
        {
            lock (_gate) _readers.Remove(channel);
        }
    }
}
