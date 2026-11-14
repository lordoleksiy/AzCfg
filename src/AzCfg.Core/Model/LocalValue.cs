using System.Text;

namespace AzCfg.Core.Model;

/// <summary>
/// A single key/value pair read from or written to a local target file. <see cref="Key"/> is the
/// configuration path without the scope prefix (see <see cref="ConfigEntry"/>). <see cref="Value"/> may be
/// a secret, so it is never included in <see cref="ToString"/>.
/// </summary>
public sealed record LocalValue(string Key, string Value)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(nameof(Key)).Append(" = ").Append(Key);
        builder.Append(", ").Append(nameof(Value)).Append(" = ***");
        return true;
    }
}
