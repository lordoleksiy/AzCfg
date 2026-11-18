using AzCfg.Core.Mapping;
using AzCfg.Core.Model;
using AzCfg.Infrastructure.InMemory;

namespace AzCfg.Ui.Demo;

/// <summary>
/// The in-memory Azure of the demo: stores <c>appcs-shop-&lt;env&gt;</c> and vaults <c>kv-shop-&lt;env&gt;</c> for <c>dev</c>, <c>stage</c>
/// and <c>prod</c>; per environment 34 plain keys and 6 secrets of service <c>orders</c>, 10 <c>global:</c> keys and 8 keys of service
/// <c>billing</c>. <c>stage</c> carries a duplicate-label key; <c>prod</c> differs from <c>stage</c> in exactly 5 plain keys.
/// Secret values are fake and exist only in the in-memory vault.
/// </summary>
public static class DemoAzure
{
    public const string Service = "orders";

    private static readonly (string Key, string Value)[] OrdersKeys =
    [
        ("Orders:PageSize", "25"), ("Orders:MaxItemsPerOrder", "100"), ("Orders:DefaultCurrency", "EUR"), ("Orders:ReservationMinutes", "15"),
        ("Orders:AllowBackorders", "false"), ("Cache:Provider", "Redis"), ("Cache:Ttl", "300"), ("Cache:Prefix", "orders"), ("Cache:Enabled", "true"),
        ("Features:Payments:Enabled", "true"), ("Features:Wishlist:Enabled", "false"), ("Features:NewCheckout:Enabled", "false"),
        ("Features:Recommendations:Enabled", "true"), ("Payments:Provider", "Stripe"), ("Payments:Timeout", "30"), ("Payments:Retries", "3"),
        ("Payments:Webhook:Path", "/hooks/payments"), ("Messaging:Provider", "ServiceBus"), ("Messaging:Queues:Orders", "orders-created"),
        ("Messaging:Queues:Shipping", "shipping-requests"), ("Messaging:Retry:Max", "5"), ("Auth:Authority", "https://login.shop.example"),
        ("Auth:Audience", "orders-api"), ("Auth:Jwt:Issuer", "https://orders.shop.example"), ("Auth:Jwt:ExpiryMinutes", "60"),
        ("Smtp:Host", "smtp.shop.example"), ("Smtp:Port", "587"), ("Smtp:From", "orders@shop.example"), ("Smtp:UseTls", "true"),
        ("Http:Client:Timeout", "20"), ("Http:Client:Retries", "2"), ("Http:Cors:Origins", "https://shop.example"), ("Telemetry:SamplingRatio", "0.1"),
        ("Timeout", "30"),
    ];

    private static readonly string[] SecretKeys =
    [
        "ConnectionStrings:Orders", "Messaging:ServiceBus:ConnectionString", "Payments:Stripe:ApiKey", "Auth:Jwt:SigningKey", "Smtp:Password", "Redis:Password",
    ];

    private static readonly (string Key, string Value)[] GlobalKeys =
    [
        ("Logging:LogLevel:Default", "Information"), ("Logging:LogLevel:Microsoft.AspNetCore", "Warning"), ("Serilog:MinimumLevel", "Information"),
        ("Company:Name", "Shop"), ("Company:SupportEmail", "support@shop.example"), ("Localization:DefaultCulture", "en-US"), ("Health:Path", "/healthz"),
        ("Tracing:Enabled", "true"), ("Tracing:Endpoint", "https://otel.shop.example"), ("Platform:Region", "westeurope"),
    ];

    private static readonly (string Key, string Value)[] BillingKeys =
    [
        ("Invoices:Currency", "EUR"), ("Invoices:DueDays", "14"), ("Invoices:Prefix", "INV"), ("Worker:Interval", "60"), ("Worker:BatchSize", "100"),
        ("Worker:Enabled", "true"), ("Tax:Provider", "Internal"), ("Tax:DefaultRate", "0.21"),
    ];

    private static readonly Dictionary<string, Dictionary<string, string>> Overrides = new()
    {
        ["dev"] = new()
        {
            ["Orders:PageSize"] = "10",
            ["Cache:Provider"] = "Memory",
            ["Features:NewCheckout:Enabled"] = "true",
            ["Features:Wishlist:Enabled"] = "true",
            ["Payments:Provider"] = "Fake",
            ["Telemetry:SamplingRatio"] = "1.0",
            ["Smtp:Host"] = "localhost",
            ["Smtp:Port"] = "1025",
            ["Http:Cors:Origins"] = "http://localhost:5173",
        },
        ["stage"] = [],
        ["prod"] = new()
        {
            ["Orders:PageSize"] = "50",
            ["Cache:Ttl"] = "900",
            ["Telemetry:SamplingRatio"] = "0.05",
            ["Payments:Retries"] = "5",
            ["Messaging:Retry:Max"] = "10",
        },
    };

    public static IReadOnlyList<ConfigEnvironment> Environments { get; } =
    [
        new("dev", StoreUri("dev"), null, false),
        new("stage", StoreUri("stage"), null, false),
        new("prod", StoreUri("prod"), null, true),
    ];

    public static Uri StoreUri(string environment) => new($"https://appcs-shop-{environment}.azconfig.io");

    public static Uri VaultUri(string environment) => new($"https://kv-shop-{environment}.vault.azure.net");

    /// <summary>Adds the three stores and vaults with their keys and secrets to <paramref name="world"/>.</summary>
    public static void Populate(InMemoryWorld world)
    {
        foreach (var env in Environments.Select(e => e.Name))
        {
            var (store, vault) = (StoreUri(env).ToString(), VaultUri(env).ToString());
            world.Discovery.AddStore($"appcs-shop-{env}", store, new Dictionary<string, string> { ["environment"] = env });
            world.Discovery.AddVault($"kv-shop-{env}", vault);
            foreach (var (key, value) in OrdersKeys)
            {
                world.AppConfig.Set(store, $"orders:{key}", Overrides[env].GetValueOrDefault(key, value));
            }

            foreach (var key in SecretKeys)
            {
                var name = KeyMapper.ToKeyVaultSecretName($"orders:{key}");
                world.AppConfig.SetSecretReference(store, $"orders:{key}", world.Secrets.Add(vault, name, $"demo-{env}-{name}"));
            }

            foreach (var (key, value) in GlobalKeys)
            {
                world.AppConfig.Set(store, $"global:{key}", env == "dev" && key == "Logging:LogLevel:Default" ? "Debug" : value);
            }

            foreach (var (key, value) in BillingKeys)
            {
                world.AppConfig.Set(store, $"billing:{key}", value);
            }
        }

        world.AppConfig.Set(StoreUri("stage").ToString(), "orders:Timeout", "45", "Staging");
    }
}
