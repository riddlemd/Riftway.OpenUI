using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using OpenUI.Lang.Blazor;
using Riftway;
using Riftway.Blazor;
using Riftway.OpenUI.Samples;

// --self-check [dir]: run the offline loop and exit with the result. --render <dir>: screenshot every .oui in dir.
var (mode, dir) = args switch
{
    ["--self-check", var d, ..] => ("self-check", d),
    ["--self-check", ..] => ("self-check", AppContext.BaseDirectory),
    ["--render", var d, ..] => ("render", d),
    _ => ("interactive", ""),
};
var scripted = mode != "interactive";

var app = RiftwayApp.Create();
var services = new ServiceCollection();
services.AddRiftwayBlazor();
services.AddMudServices();
services.AddSampleOpenUI();
services.AddSingleton<SampleTools>();
services.AddSingleton(sp => new ChatSession(
    sp.GetRequiredService<OpenUIComponents>(),
    // Scripted runs are offline, so they need no key and spend nothing.
    scripted ? null : OpenUISetup.CreateChatClient(),
    OpenUISetup.ModelId));
var provider = services.BuildServiceProvider();

app.Ready += (_, _) =>
{
    var window = new BrowserWindow(new BrowserWindowOptions { Width = 1100, Height = 900, Title = "Riftway + OpenUI" });
    window.LoadBlazor<App>(provider);
    if (!scripted) return;
    window.WebContents.ConsoleMessage += (_, e) => Console.WriteLine($"console {e.Level}: {e.Message}");
    _ = Task.Run(async () =>
    {
        var code = 1;
        try
        {
            code = mode == "render"
                ? await SelfCheck.RenderAsync(window, provider, dir)
                : await SelfCheck.RunAsync(window, provider, dir);
        }
        catch (Exception ex) { Console.WriteLine($"FAIL {ex}"); }
        Environment.ExitCode = code;
        app.Quit();
    });
};

return app.Run() is var exit && exit != 0 ? exit : Environment.ExitCode;
