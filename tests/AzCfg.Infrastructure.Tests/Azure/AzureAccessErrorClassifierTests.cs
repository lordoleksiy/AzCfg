using AzCfg.Core.Health;
using AzCfg.Infrastructure.Azure;
using Azure;
using Azure.Identity;

namespace AzCfg.Infrastructure.Tests.Azure;

public sealed class AzureAccessErrorClassifierTests
{
    private readonly AzureAccessErrorClassifier _classifier = new();

    [Fact]
    public void Classify_AuthenticationFailed_AuthRequiredWithoutMessage()
    {
        var failure = _classifier.Classify(new AuthenticationFailedException("CANARY"));

        Assert.Equal(EnvironmentAccess.AuthRequired, failure.Access);
        Assert.Contains("az login", failure.Reason);
        Assert.DoesNotContain("CANARY", failure.Reason);
    }

    [Fact]
    public void Classify_CredentialUnavailable_AuthRequired() =>
        Assert.Equal(EnvironmentAccess.AuthRequired, _classifier.Classify(new CredentialUnavailableException("CANARY")).Access);

    [Fact]
    public void Classify_401_AuthRequired() =>
        Assert.Equal(EnvironmentAccess.AuthRequired, _classifier.Classify(new RequestFailedException(401, "CANARY")).Access);

    [Fact]
    public void Classify_403_ForbiddenNamingTheRole()
    {
        var failure = _classifier.Classify(new RequestFailedException(403, "CANARY"));

        Assert.Equal(EnvironmentAccess.Forbidden, failure.Access);
        Assert.Contains("403", failure.Reason);
        Assert.DoesNotContain("CANARY", failure.Reason);
    }

    [Fact]
    public void Classify_Network_Unreachable()
    {
        var failure = _classifier.Classify(new HttpRequestException("CANARY"));

        Assert.Equal(EnvironmentAccess.Unreachable, failure.Access);
        Assert.Contains("network", failure.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CANARY", failure.Reason);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(500)]
    public void Classify_OtherStatus_UnreachableWithTypeNameOnly(int status)
    {
        var failure = _classifier.Classify(new RequestFailedException(status, "CANARY"));

        Assert.Equal(new AccessFailure(EnvironmentAccess.Unreachable, nameof(RequestFailedException)), failure);
    }

    [Fact]
    public void Classify_Other_UnreachableWithTypeNameOnly() =>
        Assert.Equal(new AccessFailure(EnvironmentAccess.Unreachable, nameof(InvalidOperationException)), _classifier.Classify(new InvalidOperationException("CANARY")));
}
