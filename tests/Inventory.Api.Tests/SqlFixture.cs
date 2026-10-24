using Inventory.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.Tests;

[CollectionDefinition("SQL Server", DisableParallelization = true)]
public sealed class SqlCollection : ICollectionFixture<SqlFixture> { }

public sealed class SqlFixture : IAsyncLifetime
{
    public const string Key = "test-inventory-not-a-real-secret-1234567890";
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("TEST_SQLSERVER_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set TEST_SQLSERVER_CONNECTION_STRING to a disposable database ending in Tests.");
        var parsed = new SqlConnectionStringBuilder(connection);
        if (!parsed.InitialCatalog.EndsWith("Tests", StringComparison.Ordinal))
            throw new InvalidOperationException("The test database name must end in Tests.");
        Environment.SetEnvironmentVariable("API_KEY", Key);
        Environment.SetEnvironmentVariable("ConnectionStrings__Inventory", connection);
        Factory = new WebApplicationFactory<Program>();
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<InventoryDb>().Database.MigrateAsync();
    }

    public HttpClient Client()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", Key);
        return client;
    }

    public async Task ResetAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDb>();
        await db.Movements.ExecuteDeleteAsync();
        await db.Products.ExecuteDeleteAsync();
    }

    public async Task DisposeAsync() => await Factory.DisposeAsync();
}
