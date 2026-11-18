using AzCfg.Ui.Application.State;
using AzCfg.Ui.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Tests.Composition;

public sealed class UiRegistrationExtensionsTests
{
    private sealed class Contributor : ICommandContributor
    {
        public IEnumerable<UiCommand> Commands => [];
    }

    [Fact]
    public void AddTab_DuplicateKind_Throws()
    {
        var services = new ServiceCollection().AddTab(TabKind.Settings, typeof(object));
        Assert.Throws<InvalidOperationException>(() => services.AddTab(TabKind.Settings, typeof(string)));
    }

    [Fact]
    public void AddProjectMode_DuplicateMode_Throws()
    {
        var services = new ServiceCollection().AddProjectMode(ProjectMode.Browse, typeof(object));
        Assert.Throws<InvalidOperationException>(() => services.AddProjectMode(ProjectMode.Browse, typeof(string)));
    }

    [Fact]
    public void AddSidebarSection_DuplicateId_Throws()
    {
        var services = new ServiceCollection().AddSidebarSection("explorer", "Explorer", typeof(object), 0);
        Assert.Throws<InvalidOperationException>(() => services.AddSidebarSection("explorer", "Other", typeof(string), 1));
    }

    [Fact]
    public void Add_DistinctKeys_RegistersAll()
    {
        using var provider = new ServiceCollection()
            .AddTab(TabKind.Settings, typeof(object))
            .AddTab(TabKind.Welcome, typeof(object))
            .AddProjectMode(ProjectMode.Browse, typeof(object))
            .AddProjectMode(ProjectMode.Sync, typeof(object))
            .AddSidebarSection("a", "A", typeof(object), 0)
            .AddSidebarSection("b", "B", typeof(object), 1)
            .AddStatusBarItem(typeof(object), StatusBarAlignment.Left, 0)
            .AddStatusBarItem(typeof(object), StatusBarAlignment.Left, 0)
            .BuildServiceProvider();
        Assert.Equal(2, provider.GetServices<TabRegistration>().Count());
        Assert.Equal(2, provider.GetServices<ProjectModeRegistration>().Count());
        Assert.Equal(2, provider.GetServices<SidebarSectionRegistration>().Count());
        Assert.Equal(2, provider.GetServices<StatusBarItemRegistration>().Count());
    }

    [Fact]
    public void AddCommands_RegistersScopedContributor()
    {
        var descriptor = Assert.Single(new ServiceCollection().AddCommands<Contributor>());
        Assert.Equal((typeof(ICommandContributor), typeof(Contributor), ServiceLifetime.Scoped),
            (descriptor.ServiceType, descriptor.ImplementationType, descriptor.Lifetime));
    }
}
