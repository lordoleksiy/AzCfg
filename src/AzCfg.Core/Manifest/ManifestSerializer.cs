using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AzCfg.Core.Manifest;

/// <summary>
/// Reads and writes <c>azcfg.json</c> (ADR 0012, amendment 2) with BCL <c>System.Text.Json</c>: camelCase fields, comments
/// and trailing commas allowed on read. Reading is strict — an unknown field (e.g. <c>value</c>, <c>password</c>) is an
/// error naming that field — and runs <see cref="ManifestValidator"/>. Both directions throw <see cref="ManifestException"/>;
/// error messages never contain field values.
/// </summary>
public static class ManifestSerializer
{
    private const string SchemaVersionField = "schemaVersion";
    private const string ServiceField = "service";
    private const string ValuesField = "values";
    private const string SecretsField = "secrets";
    private const string VaultsField = "vaults";
    private const string TargetField = "target";
    private const string ProjectField = "project";

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonWriterOptions WriteOptions = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Parses and validates a manifest.</summary>
    /// <exception cref="ManifestException">The JSON is unreadable, does not match the schema, or has a newer version.</exception>
    public static ProjectManifest Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, ReadOptions);
        }
        catch (JsonException e)
        {
            var where = e.LineNumber is { } line ? $" (line {line + 1}, position {e.BytePositionInLine + 1})" : "";
            throw new ManifestException(
                [new ManifestError(ManifestErrorKind.Unreadable, null, $"The file is not valid JSON{where}.")], innerException: e);
        }

        using (document)
        {
            var manifest = Read(document.RootElement, out var errors);
            if (errors.Count == 0)
            {
                errors.AddRange(ManifestValidator.Validate(manifest!));
            }

            return errors.Count == 0 ? manifest! : throw new ManifestException(errors);
        }
    }

    /// <summary>Validates and writes a manifest as indented JSON (LF line endings); <c>vaults</c> is omitted when null.</summary>
    /// <exception cref="ManifestException">The manifest is invalid.</exception>
    public static string Serialize(ProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var errors = ManifestValidator.Validate(manifest);
        if (errors.Count > 0)
        {
            throw new ManifestException(errors);
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriteOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber(SchemaVersionField, manifest.SchemaVersion);
            writer.WriteString(ServiceField, manifest.Service);
            WriteTarget(writer, ValuesField, manifest.Values);
            WriteTarget(writer, SecretsField, manifest.Secrets);
            if (manifest.Vaults is { } vaults)
            {
                writer.WriteStartObject(VaultsField);
                foreach (var (environment, uri) in vaults)
                {
                    writer.WriteString(environment, uri.AbsoluteUri);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static void WriteTarget(Utf8JsonWriter writer, string name, ManifestTarget target)
    {
        writer.WriteStartObject(name);
        writer.WriteString(TargetField, target.Target);
        writer.WriteString(ProjectField, target.Project);
        writer.WriteEndObject();
    }

    private static ProjectManifest? Read(JsonElement root, out List<ManifestError> errors)
    {
        errors = [];
        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new(ManifestErrorKind.Schema, null, "The manifest must be a JSON object."));
            return null;
        }

        // A newer version may add fields: report only the version, not the fields this azcfg does not know yet.
        if (root.TryGetProperty(SchemaVersionField, out var versionElement)
            && versionElement.ValueKind == JsonValueKind.Number
            && versionElement.TryGetDecimal(out var newer)
            && newer == decimal.Truncate(newer)
            && newer > ManifestValidator.SupportedSchemaVersion)
        {
            errors.Add(ManifestValidator.UnsupportedVersion(newer));
            return null;
        }

        int? version = null;
        string? service = null;
        ManifestTarget? values = null, secrets = null;
        IReadOnlyDictionary<string, Uri>? vaults = null;

        foreach (var property in Properties(root, null, errors))
        {
            switch (property.Name)
            {
                case SchemaVersionField:
                    version = property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var v)
                        ? v
                        : Invalid<int?>(errors, SchemaVersionField, "an integer");
                    break;
                case ServiceField:
                    service = String(property.Value, ServiceField, errors);
                    break;
                case ValuesField:
                    values = Target(property.Value, ValuesField, errors);
                    break;
                case SecretsField:
                    secrets = Target(property.Value, SecretsField, errors);
                    break;
                case VaultsField:
                    vaults = Vaults(property.Value, errors);
                    break;
                default:
                    errors.Add(Unknown(property.Name));
                    break;
            }
        }

        Require(errors, root, SchemaVersionField);
        Require(errors, root, ServiceField);
        Require(errors, root, ValuesField);
        Require(errors, root, SecretsField);

        return errors.Count == 0 ? new ProjectManifest(version!.Value, service!, values!, secrets!, vaults) : null;
    }

    private static ManifestTarget? Target(JsonElement element, string name, List<ManifestError> errors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return Invalid<ManifestTarget>(errors, name, "an object");
        }

        string? target = null, project = null;
        foreach (var property in Properties(element, name, errors))
        {
            var field = $"{name}.{property.Name}";
            switch (property.Name)
            {
                case TargetField:
                    target = String(property.Value, field, errors);
                    break;
                case ProjectField:
                    project = String(property.Value, field, errors);
                    break;
                default:
                    errors.Add(Unknown(field));
                    break;
            }
        }

        Require(errors, element, TargetField, name);
        Require(errors, element, ProjectField, name);
        return target is null || project is null ? null : new ManifestTarget(target, project);
    }

    private static IReadOnlyDictionary<string, Uri>? Vaults(JsonElement element, List<ManifestError> errors)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return Invalid<IReadOnlyDictionary<string, Uri>>(errors, VaultsField, "an object of environment → Key Vault URI");
        }

        var vaults = new Dictionary<string, Uri>(StringComparer.Ordinal);
        foreach (var property in Properties(element, VaultsField, errors))
        {
            var field = $"{VaultsField}.{property.Name}";
            if (String(property.Value, field, errors) is not { } text)
            {
                continue;
            }

            if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
            {
                vaults[property.Name] = uri;
            }
            else
            {
                errors.Add(ManifestValidator.Schema(field, $"'{field}' must be an absolute https URI."));
            }
        }

        return vaults.AsReadOnly();
    }

    /// <summary>The object's properties, reporting duplicates (each is yielded once, the first occurrence).</summary>
    private static IEnumerable<JsonProperty> Properties(JsonElement element, string? parent, List<ManifestError> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (seen.Add(property.Name))
            {
                yield return property;
            }
            else
            {
                var field = parent is null ? property.Name : $"{parent}.{property.Name}";
                errors.Add(ManifestValidator.Schema(field, $"Duplicate field '{field}'."));
            }
        }
    }

    private static string? String(JsonElement element, string field, List<ManifestError> errors) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() : Invalid<string>(errors, field, "a string");

    private static void Require(List<ManifestError> errors, JsonElement element, string name, string? parent = null)
    {
        if (!element.TryGetProperty(name, out _))
        {
            var field = parent is null ? name : $"{parent}.{name}";
            errors.Add(ManifestValidator.Schema(field, $"Missing required field '{field}'."));
        }
    }

    private static T? Invalid<T>(List<ManifestError> errors, string field, string expected)
    {
        errors.Add(ManifestValidator.Schema(field, $"'{field}' must be {expected}."));
        return default;
    }

    private static ManifestError Unknown(string field) => ManifestValidator.Schema(
        field,
        $"Unknown field '{field}' (the manifest holds names and paths only, never values).");
}
