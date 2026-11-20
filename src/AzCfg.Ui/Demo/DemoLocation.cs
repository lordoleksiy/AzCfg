namespace AzCfg.Ui.Demo;

/// <summary>The temp folder of one demo run: <c>&lt;root&gt;/Shop</c> is the sample workspace, <c>&lt;root&gt;/state</c> the UI's own files.</summary>
public sealed record DemoLocation(string Root)
{
    public string Workspace => Path.Combine(Root, "Shop");

    public string StateRoot => Path.Combine(Root, "state");

    /// <summary>
    /// Reserves <c>&lt;baseDirectory&gt;/azcfg-demo/&lt;yyyyMMdd-HHmmss&gt;</c> (a numeric suffix when taken) by atomically creating a
    /// marker file in it, so two runs started in the same second never share a folder.
    /// </summary>
    public static DemoLocation Reserve(string baseDirectory, DateTimeOffset now)
    {
        var name = now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        for (var attempt = 1; ; attempt++)
        {
            var root = Path.Combine(baseDirectory, "azcfg-demo", attempt == 1 ? name : $"{name}-{attempt}");
            Directory.CreateDirectory(root);
            try
            {
                using var marker = new FileStream(Path.Combine(root, ".azcfg-demo"), FileMode.CreateNew, FileAccess.Write);
                return new DemoLocation(root);
            }
            catch (IOException) when (File.Exists(Path.Combine(root, ".azcfg-demo")))
            {
            }
        }
    }
}
