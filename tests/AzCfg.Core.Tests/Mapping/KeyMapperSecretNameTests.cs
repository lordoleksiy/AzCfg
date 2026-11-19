using AzCfg.Core.Mapping;

namespace AzCfg.Core.Tests.Mapping;

public class KeyMapperSecretNameTests
{
    [Theory]
    [InlineData("https://vault.vault.azure.net/secrets/Db-Password", "Db-Password")]
    [InlineData("https://vault.vault.azure.net/secrets/Db-Password/", "Db-Password")]
    [InlineData("https://vault.vault.azure.net/secrets/Db-Password/0123abcd", "Db-Password")]
    [InlineData("https://other.vault.azure.net/secrets/crm/0123abcd/", "crm")]
    public void SecretNameOf_WithOrWithoutVersion_ReturnsName(string uri, string expected)
    {
        Assert.Equal(expected, KeyMapper.SecretNameOf(new Uri(uri)));
    }

    [Theory]
    [InlineData("https://vault.vault.azure.net/")]
    [InlineData("https://vault.vault.azure.net/secrets")]
    [InlineData("https://vault.vault.azure.net/secrets/")]
    [InlineData("https://vault.vault.azure.net/keys/Db-Password")]
    public void SecretNameOf_WithoutSecretsSegment_ThrowsArgumentException(string uri)
    {
        Assert.Throws<ArgumentException>(() => KeyMapper.SecretNameOf(new Uri(uri)));
    }

    [Fact]
    public void SecretNameOf_RelativeUri_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => KeyMapper.SecretNameOf(new Uri("secrets/Db-Password", UriKind.Relative)));
    }

    [Fact]
    public void SecretNameOf_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => KeyMapper.SecretNameOf(null!));
    }
}
