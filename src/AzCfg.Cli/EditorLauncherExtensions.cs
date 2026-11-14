namespace AzCfg.Cli;

public static class EditorLauncherExtensions
{
    /// <summary>Writes <paramref name="text"/> to a private temp .json file, opens it in the editor and returns the saved content. The file is always deleted.</summary>
    public static async Task<string> EditTextAsync(this IEditorLauncher editor, string text, CancellationToken ct)
    {
        var path = Path.Combine(Path.GetTempPath(), $"azcfg-{Guid.NewGuid():N}.json");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            await using (var stream = File.Open(path, options))
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(text.AsMemory(), ct);
            }

            await editor.EditAsync(path, ct);
            return await File.ReadAllTextAsync(path, ct);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
