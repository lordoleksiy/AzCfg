using AzCfg.Core.Model;
using AzCfg.Core.Security;

namespace AzCfg.Core.Tests.Security;

public class HmacSecretFingerprinterTests
{
    private static byte[] Key(byte seed = 1) => Enumerable.Repeat(seed, 32).ToArray();

    [Fact]
    public void Fingerprint_SameKeyAndValue_IsDeterministic()
    {
        var fingerprinter = new HmacSecretFingerprinter(Key());

        var a = fingerprinter.Fingerprint(new SecretString("value"));
        var b = fingerprinter.Fingerprint(new SecretString("value"));

        Assert.Equal(a, b);
    }

    [Fact]
    public void Fingerprint_DifferentValues_Differ()
    {
        var fingerprinter = new HmacSecretFingerprinter(Key());

        var a = fingerprinter.Fingerprint(new SecretString("value1"));
        var b = fingerprinter.Fingerprint(new SecretString("value2"));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Fingerprint_DifferentKeys_Differ()
    {
        var a = new HmacSecretFingerprinter(Key(1)).Fingerprint(new SecretString("value"));
        var b = new HmacSecretFingerprinter(Key(2)).Fingerprint(new SecretString("value"));

        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    public void Constructor_KeyShorterThan32Bytes_Throws(int length)
    {
        var key = new byte[length];

        Assert.Throws<ArgumentException>(() => new HmacSecretFingerprinter(key));
    }

    [Fact]
    public void Constructor_NullKey_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new HmacSecretFingerprinter(null!));
    }

    [Fact]
    public void Fingerprint_OutputNeverContainsInputText()
    {
        var fingerprinter = new HmacSecretFingerprinter(Key());
        const string secretText = "super-secret-value";

        var fingerprint = fingerprinter.Fingerprint(new SecretString(secretText));

        Assert.DoesNotContain(secretText, fingerprint, StringComparison.OrdinalIgnoreCase);
    }
}
