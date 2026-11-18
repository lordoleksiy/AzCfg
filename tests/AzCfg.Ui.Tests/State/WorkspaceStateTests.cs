using AzCfg.Core.Model;
using AzCfg.Ui.Application.Environments;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Workspace;
using static AzCfg.Ui.Tests.State.StateFixtures;

namespace AzCfg.Ui.Tests.State;

public sealed class WorkspaceStateTests
{
    private static WorkspaceTree Tree(ProjectNode inSolution, ProjectNode loose) =>
        new("C:/src", [new SolutionNode(new SolutionInfo("C:/src/All.slnx", "All", SolutionFormat.Slnx, [inSolution.Id]), [inSolution])], [loose]);

    [Fact]
    public void FindProject_InSolutionOrLoose_ReturnsNode()
    {
        var a = Node("A");
        var b = Node("B");
        var state = new WorkspaceState();
        state.SetTree(Tree(a, b));
        Assert.Same(a, state.FindProject(a.Id));
        Assert.Same(b, state.FindProject(b.Id));
        Assert.Null(state.FindProject("C:/src/X/X.csproj"));
    }

    [Fact]
    public void FindProject_NoTree_ReturnsNull() => Assert.Null(new WorkspaceState().FindProject("x"));

    [Fact]
    public void UpdateBinding_ReplacesNodeInTreeAndRaisesChanged()
    {
        var a = Node("A");
        var b = Node("B");
        var state = new WorkspaceState();
        state.SetTree(Tree(a, b));
        var raised = 0;
        state.Changed += () => raised++;
        var binding = Binding();
        state.UpdateBinding(b.Id, binding);
        state.UpdateBinding(a.Id, binding);
        Assert.Equal(binding, state.FindProject(a.Id)!.Binding);
        Assert.Equal(binding, state.Tree!.LooseProjects[0].Binding);
        Assert.Equal(binding, state.Tree.Solutions[0].Projects[0].Binding);
        Assert.Equal(2, raised);
    }

    [Fact]
    public void SetEnvironmentsAndSelectProject_StoreValuesAndRaiseChanged()
    {
        var state = new WorkspaceState();
        var raised = 0;
        state.Changed += () => raised++;
        var snapshot = new EnvironmentsSnapshot(null, []);
        state.SetEnvironments(snapshot);
        state.SelectProject("p");
        Assert.Equal((snapshot, "p"), (state.Environments, state.SelectedProjectId));
        Assert.Equal(2, raised);
    }
}
