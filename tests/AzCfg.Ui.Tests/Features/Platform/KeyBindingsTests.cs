using AzCfg.Ui.Features.Platform;
using AzCfg.Ui.Shared;
using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Tests.Features.Platform;

public sealed class KeyBindingsTests
{
    [Theory]
    [InlineData("Ctrl+K", CommandIds.Palette)]
    [InlineData("Ctrl+Shift+P", CommandIds.Palette)]
    [InlineData("Ctrl+P", CommandIds.QuickOpen)]
    [InlineData("Ctrl+B", CommandIds.ToggleSidebar)]
    [InlineData("Alt+I", CommandIds.ToggleInspector)]
    [InlineData("Alt+W", CommandIds.CloseTab)]
    [InlineData("Alt+PageDown", CommandIds.NextTab)]
    [InlineData("Alt+PageUp", CommandIds.PrevTab)]
    [InlineData("Alt+1", CommandIds.ModeBrowse)]
    [InlineData("Alt+2", CommandIds.ModeCompare)]
    [InlineData("Alt+3", CommandIds.ModeSync)]
    [InlineData("Alt+R", CommandIds.ProjectRefresh)]
    [InlineData("Ctrl+F", CommandIds.BrowseFind)]
    [InlineData("F2", CommandIds.BrowseEdit)]
    [InlineData("Delete", CommandIds.BrowseDelete)]
    [InlineData("Alt+N", CommandIds.BrowseAddKey)]
    [InlineData("Ctrl+S", CommandIds.DraftReview)]
    [InlineData("Ctrl+Enter", CommandIds.SyncApply)]
    [InlineData("Shift+Alt+F", CommandIds.EditorFormat)]
    public void Resolve_Section210Chord_ReturnsItsCommand(string chord, string commandId) =>
        Assert.Equal(commandId, KeyBindings.Resolve(KeyChord.Parse(chord)));

    [Fact]
    public void All_ContainsNoBrowserReservedChord() =>
        Assert.DoesNotContain(KeyBindings.All, b => KeyBindings.Reserved.Contains(b.Chord));

    [Fact]
    public void Reserved_ListsTheCombinationsOfG31()
    {
        foreach (var chord in new[] { "Ctrl+W", "Ctrl+T", "Ctrl+N", "Ctrl+Tab", "Ctrl+Shift+T", "Ctrl+Shift+N", "Ctrl+Shift+W", "F5", "Ctrl+R", "Ctrl+L", "Alt+F4" })
        {
            Assert.Contains(KeyChord.Parse(chord), KeyBindings.Reserved);
        }
    }

    [Fact]
    public void All_HasNoDuplicateChords() =>
        Assert.Equal(KeyBindings.All.Count, KeyBindings.All.Select(b => b.Chord).Distinct().Count());

    [Fact]
    public void ShortcutOf_CommandWithTwoChords_ReturnsTheFirst() =>
        Assert.Equal("Ctrl+K", KeyBindings.ShortcutOf(CommandIds.Palette));

    [Fact]
    public void ShortcutOf_UnboundCommand_IsNull() => Assert.Null(KeyBindings.ShortcutOf(CommandIds.EnvSetup));

    [Fact]
    public void Parse_LowerCaseLetter_IsNormalised() => Assert.Equal(KeyChord.Parse("Ctrl+K"), KeyChord.Parse("ctrl+k"));

    [Fact]
    public void ToString_RoundTripsThroughParse() =>
        Assert.All(KeyBindings.All, b => Assert.Equal(b.Chord, KeyChord.Parse(b.Chord.ToString())));

    [Fact]
    public void From_MetaKey_CountsAsCtrl()
    {
        var e = new FluentKeyCodeEventArgs { Value = "k", MetaKey = true };
        Assert.Equal(CommandIds.Palette, KeyBindings.Resolve(KeyChord.From(e)));
    }
}
