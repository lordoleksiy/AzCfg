using AzCfg.Core.Model;
using AzCfg.Ui.Features.Environments;

namespace AzCfg.Ui.Tests.Features.Environments;

public sealed class EnvironmentsDraftTests
{
    private static EnvironmentRow Row(string name, string endpoint = "https://dev.azconfig.io", string? label = null, bool isProtected = false) =>
        new() { Name = name, Endpoint = endpoint, Label = label, IsProtected = isProtected, IsNew = true };

    [Fact]
    public void From_Null_IsAnEmptyStorePerEnvironmentDraft()
    {
        var draft = EnvironmentsDraft.From(null);
        Assert.Empty(draft.Rows);
        Assert.Equal(EnvironmentMode.StorePerEnvironment, draft.Mode);
        Assert.False(draft.GitIgnoreLocalFiles);
    }

    [Fact]
    public void From_Profile_CopiesEnvironmentsAndOptionsAsExistingRows()
    {
        var profile = new UserProfile([new ConfigEnvironment("prod", new Uri("https://p.azconfig.io"), "prod", true)], EnvironmentMode.LabelPerEnvironment, "main", true);
        var draft = EnvironmentsDraft.From(profile);
        var row = Assert.Single(draft.Rows);
        Assert.Equal(("prod", "https://p.azconfig.io/", "prod", true, false), (row.Name, row.Endpoint, row.Label, row.IsProtected, row.IsNew));
        Assert.Equal((EnvironmentMode.LabelPerEnvironment, "main", true), (draft.Mode, draft.DefaultLabel, draft.GitIgnoreLocalFiles));
    }

    [Fact]
    public void ToProfile_TrimsAndSkipsExcludedRows()
    {
        var draft = EnvironmentsDraft.From(null);
        draft.DefaultLabel = "  ";
        draft.Rows.Add(Row(" dev ", " https://dev.azconfig.io ", " "));
        draft.Rows.Add(new EnvironmentRow { Name = "skipped", Endpoint = "https://s.azconfig.io", Included = false });
        var profile = draft.ToProfile();
        var env = Assert.Single(profile.Environments);
        Assert.Equal(("dev", new Uri("https://dev.azconfig.io"), null), (env.Name, env.StoreEndpoint, env.Label));
        Assert.Null(profile.DefaultLabel);
    }

    [Fact]
    public void Validate_ValidDraft_HasNoIssues()
    {
        var draft = EnvironmentsDraft.From(null);
        draft.Rows.Add(Row("dev"));
        draft.Rows.Add(Row("prod", "https://prod.azconfig.io"));
        Assert.Empty(draft.Validate());
    }

    [Theory]
    [InlineData("", "https://a.azconfig.io", "Name is required")]
    [InlineData("dev", "", "Endpoint must be an absolute https URL")]
    [InlineData("dev", "http://a.azconfig.io", "Endpoint must be an absolute https URL")]
    [InlineData("dev", "not a url", "Endpoint must be an absolute https URL")]
    public void Validate_InvalidRow_ReportsTheRowIssue(string name, string endpoint, string message)
    {
        var draft = EnvironmentsDraft.From(null);
        var row = Row(name, endpoint);
        draft.Rows.Add(row);
        var issue = Assert.Single(draft.Validate());
        Assert.Same(row, issue.Row);
        Assert.Equal(message, issue.Message);
    }

    [Fact]
    public void Validate_DuplicateNamesIgnoringCase_FlagsTheSecondRow()
    {
        var draft = EnvironmentsDraft.From(null);
        draft.Rows.Add(Row("dev"));
        var second = Row("DEV", "https://b.azconfig.io");
        draft.Rows.Add(second);
        var issue = Assert.Single(draft.Validate());
        Assert.Same(second, issue.Row);
        Assert.Equal("Duplicate name", issue.Message);
    }

    [Fact]
    public void Validate_ExcludedRowsAreIgnored()
    {
        var draft = EnvironmentsDraft.From(null);
        draft.Rows.Add(new EnvironmentRow { Name = "", Endpoint = "", Included = false });
        Assert.Empty(draft.Validate());
    }

    [Fact]
    public void Validate_LabelModeWithDifferentStores_ReportsOneStoreRule()
    {
        var draft = EnvironmentsDraft.From(null);
        draft.Mode = EnvironmentMode.LabelPerEnvironment;
        draft.Rows.Add(Row("dev"));
        draft.Rows.Add(Row("prod", "https://other.azconfig.io"));
        var issue = Assert.Single(draft.Validate());
        Assert.Null(issue.Row);
        Assert.Equal("Label mode uses one shared store: every environment needs the same endpoint", issue.Message);
    }

    [Fact]
    public void Validate_RequireOneWithoutRows_ReportsAnIssue()
    {
        var issue = Assert.Single(EnvironmentsDraft.From(null).Validate(requireOne: true));
        Assert.Equal("Add at least one environment", issue.Message);
    }

    [Fact]
    public void RemovedFrom_ListsOriginalNamesNoLongerIncluded()
    {
        var original = new UserProfile([
            new ConfigEnvironment("dev", new Uri("https://d.azconfig.io"), null, false),
            new ConfigEnvironment("prod", new Uri("https://p.azconfig.io"), null, true)]);
        var draft = EnvironmentsDraft.From(original);
        draft.Rows.RemoveAll(r => r.Name == "prod");
        Assert.Equal(["prod"], draft.RemovedFrom(original));
        Assert.Empty(EnvironmentsDraft.From(null).RemovedFrom(null));
    }

    [Fact]
    public void SuggestName_KnownLongNames_AreShortened()
    {
        Assert.Equal("dev", EnvironmentsDraft.SuggestName("Development"));
        Assert.Equal("stage", EnvironmentsDraft.SuggestName("STG"));
        Assert.Equal("prod", EnvironmentsDraft.SuggestName("production"));
        Assert.Equal("qa", EnvironmentsDraft.SuggestName(" qa "));
    }
}
