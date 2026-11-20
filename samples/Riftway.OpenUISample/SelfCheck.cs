using Microsoft.Extensions.DependencyInjection;
using Riftway;

namespace Riftway.OpenUI.Samples;

/// <summary>
/// <c>--self-check [dir]</c>: drives the offline demo through one full loop and exits non-zero on a failure.
/// A message streams a program, the program's query reads the C# tools, a button runs a mutation and
/// re-queries, and another button's <c>@ToAssistant</c> becomes the next message. Screenshots go to dir.
/// </summary>
internal static class SelfCheck
{
    public static async Task<int> RunAsync(BrowserWindow window, IServiceProvider services, string dir)
    {
        var chat = services.GetRequiredService<ChatSession>();
        var data = services.GetRequiredService<SampleTools>();
        var failures = new List<string>();
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failures.Add(what);
        }

        Check(await WaitForAsync(window, "Send"), "the window loaded");
        await chat.SendAsync("Show me tonight's queue");
        Check(await WaitForAsync(window, "Bohemian Rhapsody"), "the streamed program drew the queue from the listQueue tool");
        Check(await CountAsync(window, "svg") > 0, "the bar chart drew");
        await SaveAsync(window, dir, "1-queue.png");

        var before = data.QueueCount;
        await ClickAsync(window, "add to queue");
        Check(await WaitForAsync(window, "Total Eclipse of the Heart"), "Add ran the addSinger mutation and re-ran the query");
        Check(data.QueueCount == before + 1, "the mutation reached the C# tools");

        await ClickAsync(window, "ask the host");
        Check(await WaitForAsync(window, "Who should sing next?"), "@ToAssistant became the next message");
        Check(await WaitForAsync(window, "Offline demo"), "the next reply streamed");
        Check(chat.Turns.Count == 4 && chat.Turns[2].Context is { Length: > 0 }, "the action's context went with the message");
        await SaveAsync(window, dir, "2-followup.png");

        Console.WriteLine(failures.Count == 0 ? "self-check passed" : $"self-check failed: {failures.Count}");
        return failures.Count == 0 ? 0 : 1;
    }

    /// <summary><c>--render &lt;dir&gt;</c>: draws every .oui in dir and saves a screenshot of each beside it.</summary>
    public static async Task<int> RenderAsync(BrowserWindow window, IServiceProvider services, string dir)
    {
        var chat = services.GetRequiredService<ChatSession>();
        if (!await WaitForAsync(window, "Send")) return 1;
        foreach (var file in Directory.EnumerateFiles(dir, "*.oui").Order(StringComparer.Ordinal))
        {
            chat.Show(await File.ReadAllTextAsync(file));
            // Long enough for the queries to answer and the charts to lay out.
            await Task.Delay(1500);
            await SaveAsync(window, dir, Path.GetFileNameWithoutExtension(file) + ".png");
            Console.WriteLine($"rendered {Path.GetFileName(file)}");
        }

        return 0;
    }

    private static async Task<string> TextAsync(BrowserWindow window) =>
        (await window.WebContents.ExecuteJavaScriptAsync("document.body.innerText"))?.ToString() ?? "";

    private static async Task<bool> WaitForAsync(BrowserWindow window, string text, int seconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if ((await TextAsync(window)).Contains(text, StringComparison.Ordinal)) return true;
            await Task.Delay(200);
        }

        return false;
    }

    private static async Task<int> CountAsync(BrowserWindow window, string selector) =>
        (await window.WebContents.ExecuteJavaScriptAsync($"document.querySelectorAll('{selector}').length"))?.GetValue<int>() ?? 0;

    private static Task ClickAsync(BrowserWindow window, string label) =>
        window.WebContents.ExecuteJavaScriptAsync(
            $"[...document.querySelectorAll('button')].find(b => b.innerText.trim().toLowerCase() === '{label}').click()", true);

    private static async Task SaveAsync(BrowserWindow window, string dir, string name)
    {
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, name), (await window.CapturePageAsync()).ToPng());
    }
}
