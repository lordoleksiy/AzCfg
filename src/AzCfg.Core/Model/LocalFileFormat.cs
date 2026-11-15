namespace AzCfg.Core.Model;

/// <summary>Serialization shape of a local target file (decision 1).</summary>
public enum LocalFileFormat
{
    /// <summary>Nested JSON objects, one segment of the key path per level (no scope prefix).</summary>
    NestedJson,

    /// <summary>A flat string dictionary; keys keep their full path (e.g. <c>A:B</c>) as-is, no nesting.</summary>
    FlatDictionary,
}
