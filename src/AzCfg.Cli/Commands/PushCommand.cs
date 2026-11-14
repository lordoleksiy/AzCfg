using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>
/// Plans and applies the local files to <c>--env</c> (else the active environment, else <c>dev</c>). Conflicts refuse the push (exit 1).
/// A needed Key Vault is chosen interactively and saved in the binding. Confirmation is skipped by <c>--yes</c>, except for a protected
/// environment, which needs its name (prompt or <c>--confirm</c>); <c>--json</c> never prompts and throws when a confirmation or vault choice is missing.
/// Failed keys exit 1.
/// </summary>
public sealed class PushCommand(IProjectService projects, ISyncService sync, IUserStateStore state, IEnvironmentResolver resolver, IResourceDiscovery discovery, IAnsiConsole console)
    : EnvironmentCommandBase<PushSettings>(projects, state, resolver)
{
    public override async Task<int> ExecuteAsync(CommandContext context, PushSettings settings, CancellationToken ct)
    {
        var (binding, env) = await ResolveAsync(settings, ct);
        var plan = await sync.PlanPushAsync(binding, env, new PushSource.LocalFiles(), ct);

        if (plan.Conflicts.Count > 0)
        {
            return Refuse(settings, plan);
        }

        if (plan.Changes.Count == 0)
        {
            return Report(settings, new ApplyResult(0, []), "No changes.");
        }

        if (plan.RequiresVaultChoice)
        {
            if (settings.Json || settings.Yes)
            {
                throw new InvalidOperationException("A Key Vault must be chosen for new secrets; run 'azcfg push' without --json/--yes.");
            }

            var vault = console.Prompt(new SelectionPrompt<AzureResource>().Title("Key Vault for new secrets:").AddChoices(await discovery.FindKeyVaultsAsync(ct)).UseConverter(v => Markup.Escape(v.Name)));
            binding = binding with { VaultByEnvironment = new Dictionary<string, Uri>(binding.VaultByEnvironment) { [env.Name] = vault.Endpoint } };
            await State.SaveBindingAsync(binding, ct);
            plan = await sync.PlanPushAsync(binding, env, new PushSource.LocalFiles(), ct);
        }

        if (!settings.Json)
        {
            PlanView.Render(console, plan);
        }

        var confirmed = settings.Confirm;
        if (env.IsProtected)
        {
            confirmed ??= settings.Json
                ? throw new InvalidOperationException($"'{env.Name}' is protected; pass --confirm {env.Name}.")
                : console.Prompt(new TextPrompt<string>($"Type [bold]{Markup.Escape(env.Name)}[/] to apply:"));
        }
        else if (!Confirm(settings, "Apply these changes?"))
        {
            return Cancel();
        }

        var global = plan.TouchesGlobal && Confirm(settings, "[yellow]Global keys are shared by every service.[/] Apply them?");
        if (plan.TouchesGlobal && !global)
        {
            return Cancel();
        }

        var result = await sync.ApplyAsync(plan, new ApplyOptions(confirmed, global), ct);
        return Report(settings, result, $"Applied {result.Applied} change(s).");
    }

    private bool Confirm(PushSettings settings, string question) =>
        settings.Yes || (settings.Json ? throw new InvalidOperationException("--json requires --yes to confirm.") : console.Confirm(question, false));

    private int Cancel()
    {
        console.MarkupLine("Cancelled.");
        return 1;
    }

    private int Refuse(PushSettings settings, SyncPlan plan)
    {
        if (settings.Json)
        {
            console.Profile.Out.Writer.WriteLine(CliJson.Serialize(plan));
        }
        else
        {
            PlanView.Render(console, plan);
            console.MarkupLine("[red]Push refused: resolve the conflicts first.[/]");
        }

        return 1;
    }

    private int Report(PushSettings settings, ApplyResult result, string message)
    {
        if (settings.Json)
        {
            console.Profile.Out.Writer.WriteLine(CliJson.Serialize(result));
        }
        else
        {
            console.MarkupLine(message);
            result.Failed.ToList().ForEach(k => console.MarkupLineInterpolated($"[red]Failed:[/] {k}"));
        }

        return result.Failed.Count > 0 ? 1 : 0;
    }
}
