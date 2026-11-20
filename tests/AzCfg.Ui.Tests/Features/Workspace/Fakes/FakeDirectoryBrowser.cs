using AzCfg.Ui.Application.Workspace;

namespace AzCfg.Ui.Tests.Features.Workspace.Fakes;

internal sealed class FakeDirectoryBrowser : IDirectoryBrowser
{
    public Dictionary<string, DirectoryListing> Listings { get; } = new(StringComparer.Ordinal);

    public DirectoryListing Roots { get; set; } = new(null, null, [new DirectoryItem("src", "C:/src", true), new DirectoryItem("docs", "C:/docs", false)]);

    public List<string?> Listed { get; } = [];

    public IReadOnlyList<string> Completions { get; set; } = [];

    public Task<DirectoryListing> ListAsync(string? path, CancellationToken ct)
    {
        Listed.Add(path);
        return Task.FromResult(path is null ? Roots : Listings.GetValueOrDefault(path, new DirectoryListing(path, null, [])));
    }

    public IReadOnlyList<string> Complete(string partialPath, int max = 20) => Completions;
}
