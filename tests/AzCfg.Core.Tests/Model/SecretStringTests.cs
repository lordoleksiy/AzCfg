using System.Text.Json;
using AzCfg.Core.Model;

namespace AzCfg.Core.Tests.Model;

public class SecretStringTests
{
    private const string SecretText = "super-secret-value";

    [Fact]
    public void ToString_ReturnsRedactedPlaceholder()
    {
        var secret = new SecretString(SecretText);

        Assert.Equal("***", secret.ToString());
    }

    [Fact]
    public void Reveal_ReturnsOriginalValue()
    {
        var secret = new SecretString(SecretText);

        Assert.Equal(SecretText, secret.Reveal());
    }

    [Fact]
    public void Equals_SameValue_AreEqual()
    {
        var a = new SecretString("value");
        var b = new SecretString("value");

        Assert.True(a.Equals(b));
        Assert.True(((object)a).Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentValue_AreNotEqual()
    {
        var a = new SecretString("value1");
        var b = new SecretString("value2");

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var a = new SecretString("value");

        Assert.False(a.Equals(null));
    }

    [Fact]
    public void Serialization_OfRecordContainingSecretString_IsRedacted()
    {
        var holder = new SecretHolder(new SecretString(SecretText));

        var json = JsonSerializer.Serialize(holder);

        Assert.DoesNotContain(SecretText, json);
        Assert.Contains("***", json);
    }

    [Fact]
    public void Deserialization_IsNotSupported()
    {
        Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize<SecretHolder>("""{"Secret":"x"}"""));
    }

    private sealed record SecretHolder(SecretString Secret);
}
