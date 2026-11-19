using AzCfg.Core.Abstractions;
using AzCfg.Core.Mapping;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>
/// Opens the remote config of <c>--env</c> as JSON in <c>$VISUAL</c>/<c>$EDITOR</c> (a random temp file, always deleted), then plans and applies the
/// edit like <c>push</c>; an unchanged file means "No changes". With <c>--set/--unset/--secret</c> (optionally <c>--global</c>) the export is patched
/// with <see cref="EditJsonPatch"/> instead and no editor starts. Both modes plan with the export as <c>BaseJson</c>, so Azure changes made after the
/// export are not reverted. <c>--json</c> without patch flags only prints the export JSON. Secrets appear only as <c>{"@kv": name}</c>; values are never accepted.
/// </summary>
public sealed class EditCommand(IProjectService projects, ISyncService sync, IUserStateStore state, IEnvironmentResolver resolver, IResourceDiscovery discovery, IAnsiConsole console, IEditorLauncher editor)
    : PushCommandBase<EditSettings>(projects, sync, state, resolver, discovery, console)
{
    public override async Task<int> ExecuteAsync(CommandContext context, EditSettings settings, CancellationToken ct)
    {
        var (binding, env) = await ResolveAsync(settings, ct);
        var exported = await Sync.ExportJsonAsync(binding, env, ct);
        if (settings.Json && !settings.HasPatch)
        {
            Console.Profile.Out.Writer.WriteLine(exported);
            return 0;
        }

        var edited = settings.HasPatch ? Patch(exported, settings) : await editor.EditTextAsync(exported, ct);

        if (!settings.HasPatch && edited == exported)
        {
            Console.MarkupLine("No changes.");
            return 0;
        }

        return await PushAsync(settings, binding, env, new PushSource.EditedJson(edited, BaseJson: exported), ct);
    }

    /// <summary>Applies <c>--unset</c>, <c>--set</c>, <c>--secret</c> (in that order) to the export; secret names are references, never values.</summary>
    private static string Patch(string exported, EditSettings settings)
    {
        var scope = settings.Global ? ConfigScope.Global : ConfigScope.Service;
        var json = exported;
        foreach (var key in settings.Unset)
        {
            json = EditJsonPatch.Remove(json, new ScopedKey(scope, key));
        }

        foreach (var (key, value) in settings.Set.Select(Split))
        {
            json = EditJsonPatch.SetPlain(json, new ScopedKey(scope, key), value);
        }

        foreach (var (key, name) in settings.Secret.Select(Split))
        {
            json = EditJsonPatch.SetSecretReference(json, new ScopedKey(scope, key), name);
        }

        return json;
    }

    private static (string Key, string Value) Split(string pair)
    {
        var i = pair.IndexOf('=', StringComparison.Ordinal);
        return (pair[..i], pair[(i + 1)..]);
    }
}
