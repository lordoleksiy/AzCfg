using System.Text.Json;
using AzCfg.Infrastructure.Azure;
using Azure;
using Azure.Identity;

namespace AzCfg.Infrastructure.Tests.Azure;

public sealed class AzureErrorsTests
{
    public static TheoryData<Exception, AzureErrorKind> Kinds => new()
    {
        { new AuthenticationFailedException("x"), AzureErrorKind.Auth },
        { new CredentialUnavailableException("x"), AzureErrorKind.Auth },
        { new RequestFailedException(401, "x"), AzureErrorKind.Auth },
        { new RequestFailedException(403, "x"), AzureErrorKind.Forbidden },
        { new RequestFailedException(404, "x"), AzureErrorKind.NotFound },
        { new RequestFailedException(412, "x"), AzureErrorKind.PreconditionFailed },
        { new RequestFailedException(0, "x"), AzureErrorKind.Network },
        { new HttpRequestException("x"), AzureErrorKind.Network },
        { new TaskCanceledException("timeout"), AzureErrorKind.Network },
        { new ArgumentException("x"), AzureErrorKind.Validation },
        { new JsonException("x"), AzureErrorKind.Validation },
        { new InvalidOperationException("x"), AzureErrorKind.Validation },
        { new RequestFailedException(500, "x"), AzureErrorKind.Other },
        { new IOException("x"), AzureErrorKind.Other },
    };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Classify_KnownException_ReturnsKind(Exception ex, AzureErrorKind kind) =>
        Assert.Equal(kind, AzureErrors.Classify(ex));

    [Fact]
    public void Classify_TaskCanceledByToken_ReturnsOther()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Equal(AzureErrorKind.Other, AzureErrors.Classify(new TaskCanceledException("x", null, cts.Token)));
    }

    public static TheoryData<Exception, string> Texts => new()
    {
        { new AuthenticationFailedException("x"), "Azure authentication failed. Sign in with 'az login' (or configure DefaultAzureCredential) and retry." },
        { new RequestFailedException(401, "x"), "Azure rejected the credentials (401). Sign in again with 'az login'." },
        { new RequestFailedException(403, "x"), "Access denied (403). Required roles: 'App Configuration Data Owner' on the store; 'Key Vault Secrets User' (read) or 'Key Vault Secrets Officer' (write) on the vault." },
        { new InvalidOperationException("plain"), "plain" },
    };

    [Theory]
    [MemberData(nameof(Texts))]
    public void Describe_ExistingCliCases_ReturnsTodaysText(Exception ex, string expected) =>
        Assert.Equal(expected, AzureErrors.Describe(ex));

    [Fact]
    public void Describe_NotFound_NamesStatusAndKeepsMessage()
    {
        var text = AzureErrors.Describe(new RequestFailedException(404, "missing"));
        Assert.Contains("404", text);
        Assert.Contains("missing", text);
    }

    [Fact]
    public void Describe_PreconditionFailed_AsksToReload() =>
        Assert.Contains("412", AzureErrors.Describe(new RequestFailedException(412, "x")));

    [Theory]
    [MemberData(nameof(NetworkErrors))]
    public void Describe_NetworkError_MentionsConnection(Exception ex) =>
        Assert.Contains("connection", AzureErrors.Describe(ex));

    public static TheoryData<Exception> NetworkErrors =>
    [
        new HttpRequestException("x"),
        new RequestFailedException(0, "x"),
        new TaskCanceledException("x"),
    ];
}
