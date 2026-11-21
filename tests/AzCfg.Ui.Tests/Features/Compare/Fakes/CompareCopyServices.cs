using AzCfg.Core.Security;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Sync;
using AzCfg.Ui.Features.Compare;
using AzCfg.Ui.Features.Project;
using AzCfg.Ui.Tests.Features.Browse;
using AzCfg.Ui.Tests.Features.Browse.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Tests.Features.Compare.Fakes;

/// <summary>What <see cref="CompareCopier"/> needs: the real <see cref="DraftService"/> behind a call log (export = <c>Db:Host</c>), unlock state and protection flow.</summary>
internal static class CompareCopyServices
{
    public static RecordingDraftService Add(IServiceCollection services)
    {
        var sync = new FakeSyncService { ExportJson = BrowseFixtures.EditJson(("Db:Host", "h1")) };
        var drafts = new RecordingDraftService(new DraftService(sync, new HmacSecretFingerprinter(new byte[32])));
        services.AddSingleton<IDraftService>(drafts);
        services.AddScoped<ProtectedUnlockState>();
        services.AddScoped<ProtectionFlow>();
        services.AddScoped<CompareCopier>();
        return drafts;
    }
}
