namespace AzCfg.Ui.Features.Browse;

/// <summary>What <see cref="InspectorRequest"/> asks the inspector to open.</summary>
public enum InspectorEdit
{
    /// <summary>The input for a new Azure secret value of the key.</summary>
    AzureSecret,
}

/// <summary>A one-shot request of the Browse view to the inspector (a new instance each time, handled by reference).</summary>
public sealed record InspectorRequest(string Key, InspectorEdit Edit);
