namespace AzCfg.Ui.Demo;

/// <summary>
/// Writes the sample solution <c>Shop</c>: <c>Shop.slnx</c> with <c>src/Orders.Api</c> (Web SDK, user secrets,
/// <c>appsettings.json</c>), <c>src/Billing.Worker</c> (Worker SDK) and <c>src/Shop.Contracts</c> (library), plus the loose test
/// project <c>tests/Orders.Tests</c> that is not part of the solution.
/// </summary>
public static class SampleWorkspace
{
    public const string OrdersApiUserSecretsId = "9d3b6f0e-4c1a-4f58-9a52-5d1c7e2b8a31";

    public static string OrdersApiCsproj(string workspace) => Path.Combine(workspace, "src", "Orders.Api", "Orders.Api.csproj");

    public static void Write(string workspace)
    {
        File.WriteAllText(
            Path.Combine(Directory.CreateDirectory(workspace).FullName, "Shop.slnx"),
            """
            <Solution>
              <Project Path="src/Orders.Api/Orders.Api.csproj" />
              <Project Path="src/Billing.Worker/Billing.Worker.csproj" />
              <Project Path="src/Shop.Contracts/Shop.Contracts.csproj" />
              <Project Path="src/Orders.Functions/Orders.Functions.csproj" />
            </Solution>

            """);
        WriteProject(workspace, "src/Orders.Api", "Orders.Api", "Microsoft.NET.Sdk.Web", $"<UserSecretsId>{OrdersApiUserSecretsId}</UserSecretsId>", """
            <ProjectReference Include="..\Shop.Contracts\Shop.Contracts.csproj" />
            """, withAppSettings: true);
        WriteProject(workspace, "src/Billing.Worker", "Billing.Worker", "Microsoft.NET.Sdk.Worker", "", "", withAppSettings: true);
        WriteProject(workspace, "src/Shop.Contracts", "Shop.Contracts", "Microsoft.NET.Sdk", "", "", withAppSettings: false);
        WriteProject(workspace, "src/Orders.Functions", "Orders.Functions", "Microsoft.NET.Sdk", "<AzureFunctionsVersion>v4</AzureFunctionsVersion>", "", withAppSettings: false);
        WriteFunctionsSettings(workspace, "src/Orders.Functions");
        WriteProject(workspace, "tests/Orders.Tests", "Orders.Tests", "Microsoft.NET.Sdk", "<IsTestProject>true</IsTestProject>", """
            <ProjectReference Include="..\..\src\Orders.Api\Orders.Api.csproj" />
            """, withAppSettings: false);
    }

    private static void WriteFunctionsSettings(string workspace, string folder)
    {
        var directory = Path.Combine(workspace, folder);
        File.WriteAllText(
            Path.Combine(directory, "host.json"),
            """
            {
              "version": "2.0"
            }

            """);
        File.WriteAllText(
            Path.Combine(directory, "local.settings.json"),
            """
            {
              "IsEncrypted": false,
              "Values": {
                "AzureWebJobsStorage": "UseDevelopmentStorage=true",
                "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
                "Orders:ProcessingDelaySeconds": "5"
              },
              "Host": {
                "CORS": "*"
              }
            }

            """);
    }

    private static void WriteProject(string workspace, string folder, string name, string sdk, string properties, string items, bool withAppSettings)
    {
        var directory = Directory.CreateDirectory(Path.Combine(workspace, folder)).FullName;
        File.WriteAllText(
            Path.Combine(directory, $"{name}.csproj"),
            $"""
            <Project Sdk="{sdk}">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                {properties}
              </PropertyGroup>
              <ItemGroup>
                {items}
              </ItemGroup>
            </Project>

            """);
        if (withAppSettings)
        {
            File.WriteAllText(
                Path.Combine(directory, "appsettings.json"),
                """
                {
                  "Logging": { "LogLevel": { "Default": "Information" } },
                  "AllowedHosts": "*"
                }

                """);
        }
    }
}
