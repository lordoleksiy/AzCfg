using System.Text.Json.Nodes;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>
/// Opens the service keys present in <c>FROM</c> but missing in <c>TO</c> as JSON in the editor (secrets as <c>{"@kv": name}</c>, never values), then plans and applies
/// the edit into <c>TO</c> like <c>push</c>. No new keys means "No new keys." (exit 0). <c>--json</c> only prints the export; <c>--in</c> skips export and editor.
/// Throws <see cref="InvalidOperationException"/> for an environment outside the profile.
/// </summary>
public sealed class PromoteCommand(IProjectService projects, ISyncService sync, IUserStateStore state, IEnvironmentResolver resolver, IResourceDiscovery discovery, IAnsiConsole console, IEditorLauncher editor)
    : PushCommandBase<PromoteSettings>(projects, sync, state, resolver, discovery, console)
{
    protected override string? RequestedEnv(PromoteSettings settings) => settings.To;

    public override async Task<int> ExecuteAsync(CommandContext context, PromoteSettings settings, CancellationToken ct)
    {
        var (binding, to) = await ResolveAsync(settings, ct);
        var from = await FindEnvironmentAsync(settings.From, ct);
        var json = settings.In is { } file ? await File.ReadAllTextAsync(file, ct) : await Sync.ExportPromoteJsonAsync(binding, from, to, ct);

        if (settings.In is null)
        {
            if (settings.Json)
            {
                Console.Profile.Out.Writer.WriteLine(json);
                return 0;
            }

            if (JsonNode.Parse(json) is JsonObject { Count: 0 })
            {
                Console.MarkupLine("No new keys.");
                return 0;
            }

            json = await editor.EditTextAsync(json, ct);
        }

        return await PushAsync(settings, binding, to, new PushSource.Promote(json, from.Name), ct);
    }
}
