using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>Plans a <see cref="PushSource"/> and applies it with the confirmations described on <see cref="PushCommand"/>.</summary>
public abstract class PushCommandBase<TSettings>(IProjectService projects, ISyncService sync, IUserStateStore state, IEnvironmentResolver resolver, IResourceDiscovery discovery, IAnsiConsole console)
    : EnvironmentCommandBase<TSettings>(projects, state, resolver) where TSettings : PushSettings
{
    protected IAnsiConsole Console { get; } = console;

    protected ISyncService Sync { get; } = sync;

    protected async Task<int> PushAsync(TSettings settings, ProjectBinding binding, ConfigEnvironment env, PushSource source, CancellationToken ct)
    {
        var plan = await Sync.PlanPushAsync(binding, env, source, ct);

        if (plan.Conflicts.Count > 0)
        {
            return Refuse(settings, plan);
        }

        if (plan.Changes.Count == 0)
        {
            var exit = Report(settings, new ApplyResult(0, []), "No changes.");
            if (!settings.Json)
            {
                WarningsView.Render(Console, plan.Warnings);
            }

            return exit;
        }

        if (plan.RequiresVaultChoice)
        {
            if (settings.Json || settings.Yes)
            {
                throw new InvalidOperationException("A Key Vault must be chosen for new secrets; run interactively without --json/--yes.");
            }

            var vault = Console.Prompt(new SelectionPrompt<AzureResource>().Title("Key Vault for new secrets:").AddChoices(await discovery.FindKeyVaultsAsync(ct)).UseConverter(v => Markup.Escape(v.Name)));
            binding = binding with { VaultByEnvironment = new Dictionary<string, Uri>(binding.VaultByEnvironment) { [env.Name] = vault.Endpoint } };
            await State.SaveBindingAsync(binding, ct);
            plan = await Sync.PlanPushAsync(binding, env, source, ct);
        }

        if (!settings.Json)
        {
            PlanView.Render(Console, plan);
        }

        var confirmed = settings.Confirm;
        if (env.IsProtected)
        {
            confirmed ??= settings.Json
                ? throw new InvalidOperationException($"'{env.Name}' is protected; pass --confirm {env.Name}.")
                : Console.Prompt(new TextPrompt<string>($"Type [bold]{Markup.Escape(env.Name)}[/] to apply:"));
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

        var result = await Sync.ApplyAsync(plan, new ApplyOptions(confirmed, global), ct);
        return Report(settings, result, $"Applied {result.Applied} change(s).");
    }

    private bool Confirm(TSettings settings, string question) =>
        settings.Yes || (settings.Json ? throw new InvalidOperationException("--json requires --yes to confirm.") : Console.Confirm(question, false));

    private int Cancel()
    {
        Console.MarkupLine("Cancelled.");
        return 1;
    }

    private int Refuse(TSettings settings, SyncPlan plan)
    {
        if (settings.Json)
        {
            Console.Profile.Out.Writer.WriteLine(CliJson.Serialize(plan));
        }
        else
        {
            PlanView.Render(Console, plan);
            Console.MarkupLine("[red]Push refused: resolve the conflicts first.[/]");
        }

        return 1;
    }

    private int Report(TSettings settings, ApplyResult result, string message)
    {
        if (settings.Json)
        {
            Console.Profile.Out.Writer.WriteLine(CliJson.Serialize(result));
        }
        else
        {
            Console.MarkupLine(message);
            result.Failed.ToList().ForEach(k => Console.MarkupLineInterpolated($"[red]Failed:[/] {k}"));
        }

        return result.Failed.Count > 0 ? 1 : 0;
    }
}
