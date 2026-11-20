using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenUI.Lang.Blazor;
using OpenUI.Lang.Blazor.Mud;
using OpenUI.Lang.Prompting;

namespace Riftway.OpenUI.Samples;

/// <summary>What the sample and the eval share: the components, the prompt and the model client.</summary>
public static class OpenUISetup
{
    public const string DefaultModel = "claude-sonnet-5-5";

    /// <summary>The rules the host adds to OpenUI's prompt. Kept short: every token here is paid on every turn.</summary>
    public static PromptOptions PromptOptions { get; } = new()
    {
        Tools = SampleTools.Descriptors,
        Preamble = "You are the assistant inside a karaoke host's desktop app. Answer every request with an interface "
                   + "written in OpenUI Lang, built from the components below. Use the tools for the night's real data.",
        AdditionalRules =
        [
            "Reply with OpenUI Lang only: no prose and no code fences.",
            "Read live data with Query statements instead of writing the numbers yourself.",
        ],
    };

    /// <summary>Registers the MudBlazor component set, the one the sample draws with.</summary>
    public static IServiceCollection AddSampleOpenUI(this IServiceCollection services) =>
        services.AddOpenUIComponents<MudComponents>();

    public static string SystemPrompt(OpenUIComponents components) => components.SystemPrompt(PromptOptions);

    /// <summary>The model named by RIFTWAY_OPENUI_MODEL, else <see cref="DefaultModel"/>.</summary>
    public static string ModelId =>
        Environment.GetEnvironmentVariable("RIFTWAY_OPENUI_MODEL") is { Length: > 0 } m ? m : DefaultModel;

    /// <summary>An Anthropic client from ANTHROPIC_API_KEY, or null when it is not set.</summary>
    public static IChatClient? CreateChatClient(string? modelId = null)
    {
        var key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) return null;
        return new AnthropicClient { ApiKey = key }.AsIChatClient(modelId ?? ModelId, defaultMaxOutputTokens: 8000);
    }

    /// <summary>
    /// The system message, marked for prompt caching: the component reference is thousands of tokens and the
    /// same on every turn, so caching it makes later turns cheaper and faster.
    /// </summary>
    public static ChatMessage SystemMessage(string prompt) =>
        new(ChatRole.System, [new TextContent(prompt).WithCacheControl(new CacheControlEphemeral())]);
}
