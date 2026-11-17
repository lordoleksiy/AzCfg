namespace AzCfg.Cli.Tests.Scenario;

/// <summary>An editor that saves the file unchanged.</summary>
public sealed class NoOpEditorLauncher : IEditorLauncher
{
    public Task EditAsync(string path, CancellationToken ct) => Task.CompletedTask;
}
