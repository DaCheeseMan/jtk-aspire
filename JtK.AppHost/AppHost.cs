using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.PostgreSql;

var builder = DistributedApplication.CreateBuilder(args);

// Keycloak admin password parameter
var keycloakPassword = builder.AddParameter("KeycloakPassword", secret: true, value: "admin");

// Keycloak resource
var keycloak = builder.AddKeycloak("keycloak", adminPassword: keycloakPassword)
    .WithEnvironment("KC_HTTP_ENABLED", "true")
    .WithEnvironment("KC_PROXY_HEADERS", "xforwarded")
    .WithEnvironment("KC_HOSTNAME_STRICT", "false")
    .WithEndpoint("http", e => e.IsExternal = true);

if (builder.ExecutionContext.IsRunMode)
{
    // Local dev: use Keycloak's built-in dev-file DB, import realm and persist with a volume
    keycloak.WithDataVolume()
            .WithRealmImport("../realms");
}
else
{
    // Azure publish: back Keycloak with Azure PostgreSQL Flexible Server
    builder.AddAzureContainerAppEnvironment("cae");

    var postgresUser = builder.AddParameter("PostgresUser", value: "jtk");
    var postgresPassword = builder.AddParameter("PostgresPassword", secret: true, value: "Password1!");

    var keycloakPostgres = builder.AddAzurePostgresFlexibleServer("keycloak-postgres")
        .WithPasswordAuthentication(postgresUser, postgresPassword)
        .ConfigureInfrastructure(infra =>
        {
            var pg = infra.GetProvisionableResources()
                          .OfType<PostgreSqlFlexibleServer>()
                          .Single();
            infra.Add(new ProvisioningOutput("hostname", typeof(string))
            {
                Value = pg.FullyQualifiedDomainName
            });
        });

    var keycloakDb = keycloakPostgres.AddDatabase("keycloakDB", "keycloak");

    var keycloakDbUrl = ReferenceExpression.Create(
        $"jdbc:postgresql://{keycloakPostgres.GetOutput("hostname")}/{keycloakDb.Resource.DatabaseName}"
    );

    keycloak
        .WithEnvironment("KC_DB", "postgres")
        .WithEnvironment("KC_DB_URL", keycloakDbUrl)
        .WithEnvironment("KC_DB_USERNAME", postgresUser)
        .WithEnvironment("KC_DB_PASSWORD", postgresPassword);
}

// App PostgreSQL database
var appDb = builder.AddPostgres("appdb")
    .AddDatabase("jtkdb");

// API server
var server = builder.AddProject<Projects.JtK_Server>("server")
    .WithReference(appDb)
    .WithReference(keycloak)
    .WaitFor(appDb)
    .WaitFor(keycloak)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

// Augment PATH so Aspire can find npm regardless of how Node was installed
// (Homebrew ARM, Homebrew Intel, nvm, volta, fnm)
var nvmDefaultBin = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".nvm", "versions", "node",
    Directory.EnumerateDirectories(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nvm", "versions", "node")
    ).Select(Path.GetFileName).OrderDescending().FirstOrDefault() ?? "",
    "bin");

var nodePaths = new[]
{
    nvmDefaultBin,
    "/opt/homebrew/bin",
    "/usr/local/bin",
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".volta/bin"),
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fnm/aliases/default/bin"),
};
var extraPath = string.Join(":", nodePaths.Where(Directory.Exists));
var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
var fullPath = string.IsNullOrEmpty(extraPath) ? currentPath : $"{extraPath}:{currentPath}";

// React frontend
var webfrontend = builder.AddViteApp("webfrontend", "../frontend")
    .WithReference(server)
    .WithReference(keycloak, "VITE_KEYCLOAK_URL")
    .WithEnvironment("PATH", fullPath)
    .WaitFor(server);

server.PublishWithContainerFiles(webfrontend, "wwwroot");

builder.Build().Run();
