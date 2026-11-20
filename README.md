# Riftway.OpenUI

Generative UI for [Riftway](https://github.com/riddlemd/Riftway) apps. A model answers with
[OpenUI Lang](https://www.openui.com/docs/openui-lang) instead of text, and the app draws it as live Blazor
components while it streams. The program's queries and actions call C# methods in the app's own process, so
clicking a button that saves and refreshes data never goes back to the model.

> [!WARNING]
> **Experimental.** This repository is step 1: a sample app and an eval that measures how often models write
> programs the sample can draw cleanly. The `Riftway.OpenUI` package comes after, if the numbers hold up.

It builds on [openui-dotnet](https://github.com/DeepForgeTech/openui-dotnet) (the OpenUI Lang parser and Blazor
renderer for .NET) and Riftway's Blazor Hybrid. The model is called from the .NET main process through
`Microsoft.Extensions.AI`'s `IChatClient`; nothing about the model reaches the page.

## Layout

| Path | What it is |
| --- | --- |
| `samples/Riftway.OpenUISample` | A Riftway window with a chat: each reply streams in as components drawn with MudBlazor |
| `samples/Shared` | The sample's tools (a karaoke night's queue, song catalog and stats), the prompt options and the model client |
| `tools/OpenUIEval` | Asks a model a fixed set of prompts and scores each reply: does it draw, does it have errors |
| `tests/OpenUIEval.Tests` | Tests for the scorer |

## Run the sample

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```sh
dotnet run --project samples/Riftway.OpenUISample
```

With `ANTHROPIC_API_KEY` set it talks to Claude (`claude-sonnet-5-5`; set `RIFTWAY_OPENUI_MODEL` to change it).
Without it, it runs an offline demo with canned replies.

`--self-check <dir>` drives the offline demo through a full loop (a streamed program, a query, a mutation, an
action that sends the next message), saves screenshots to `<dir>` and exits non-zero on a failure.
`--render <dir>` draws every `.oui` in `<dir>` and saves a screenshot of each next to it, for checking eval replies
by eye.

## Run the eval

```sh
dotnet run --project tools/OpenUIEval -- generate --out results/sonnet --model claude-sonnet-5-5
dotnet run --project tools/OpenUIEval -- score --in results/sonnet
```

`generate` needs `ANTHROPIC_API_KEY` and writes one `<prompt>.r<run>.oui` per reply. `score` runs every `.oui` in a
folder through an OpenUI session with the sample's tools and writes `results.json` and `report.md`. A reply is
**clean** when it draws and has no errors (warnings don't count). `prompt <file>` writes the system prompt, for
generating replies some other way.

## License

MIT
