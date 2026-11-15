using AzCfg.Core.Abstractions;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>
/// Opens the remote config of <c>--env</c> as JSON in <c>$VISUAL</c>/<c>$EDITOR</c> (a random temp file, always deleted), then plans and applies the
/// edit like <c>push</c>; an unchanged file means "No changes". <c>--json</c> only prints the export JSON and starts no editor. Secrets appear only as <c>{"@kv": name}</c>.
/// </summary>
public sealed class EditCommand(IProjectService projects, ISyncService sync, IUserStateStore state, IEnvironmentResolver resolver, IResourceDiscovery discovery, IAnsiConsole console, IEditorLauncher editor)
    : PushCommandBase<EditSettings>(projects, sync, state, resolver, discovery, console)
{
    public override async Task<int> ExecuteAsync(CommandContext context, EditSettings settings, CancellationToken ct)
    {
        var (binding, env) = await ResolveAsync(settings, ct);
        var exported = await Sync.ExportJsonAsync(binding, env, ct);
        if (settings.Json)
        {
            Console.Profile.Out.Writer.WriteLine(exported);
            return 0;
        }

        var edited = await editor.EditTextAsync(exported, ct);

        if (edited == exported)
        {
            Console.MarkupLine("No changes.");
            return 0;
        }

        return await PushAsync(settings, binding, env, new PushSource.EditedJson(edited), ct);
    }
}
