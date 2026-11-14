namespace AzCfg.Cli;

public interface IEditorLauncher
{
    /// <summary>Opens <paramref name="path"/> in the user's editor and completes when the editor exits. Throws <see cref="InvalidOperationException"/> on a non-zero exit code.</summary>
    Task EditAsync(string path, CancellationToken ct);
}
