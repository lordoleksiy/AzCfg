using Microsoft.AspNetCore.Components;

namespace AzCfg.Ui.Components.KeyTree;

/// <summary>A column of <c>KeyTreeGrid</c> after the key column. <see cref="Width"/> is a CSS grid track size ("1fr", "160px").</summary>
public sealed record KeyTreeColumn<TRow>(string Title, string Width, RenderFragment<KeyTreeRow<TRow>> Cell)
    where TRow : class;
