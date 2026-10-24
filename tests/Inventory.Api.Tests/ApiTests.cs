using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventory.Api.Contracts;

namespace Inventory.Api.Tests;

[Collection("SQL Server"), Trait("Category", "Integration")]
public sealed class ApiTests(SqlFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<ProductResponse> Product(HttpClient client, string sku = "DEMO-PEN")
    {
        var response = await client.PostAsJsonAsync("/v1/products", new { sku, name = "Demo pen", unit = "UNIT" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private static Task<HttpResponseMessage> Move(HttpClient client, Guid productId, string kind, decimal quantity, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/movements")
        {
            Content = JsonContent.Create(new { productId, kind, quantity, reference = "Fictional demo" })
        };
        request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task ProtectedEndpointsRequireKey()
    {
        using var client = fixture.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task CreatesProductAndRejectsDuplicateSku()
    {
        using var client = fixture.Client();
        var first = await Product(client, "demo-pen");
        Assert.Equal("DEMO-PEN", first.Sku);
        Assert.Equal(0, first.Balance);
        var duplicate = await client.PostAsJsonAsync("/v1/products", new { sku = "DEMO-PEN", name = "Other", unit = "UNIT" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task EntryExitAndImmutableHistory()
    {
        using var client = fixture.Client();
        var product = await Product(client);
        Assert.Equal(HttpStatusCode.Created, (await Move(client, product.Id, "IN", 10.125m, "entry")).StatusCode);
        var exit = await Move(client, product.Id, "OUT", 2m, "exit");
        var movement = (await exit.Content.ReadFromJsonAsync<MovementResponse>())!;
        Assert.Equal(10.125m, movement.BalanceBefore);
        Assert.Equal(8.125m, movement.BalanceAfter);
        var current = await client.GetFromJsonAsync<ProductResponse>($"/v1/products/{product.Id}");
        Assert.Equal(8.125m, current!.Balance);
        var history = await client.GetFromJsonAsync<MovementResponse[]>($"/v1/products/{product.Id}/movements");
        Assert.Equal(2, history!.Length);
    }

    [Fact]
    public async Task DuplicateMovementReplaysAndChangedPayloadConflicts()
    {
        using var client = fixture.Client();
        var product = await Product(client);
        var first = await Move(client, product.Id, "IN", 5, "same-key");
        var replay = await Move(client, product.Id, "IN", 5.000m, "same-key");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal((await first.Content.ReadFromJsonAsync<MovementResponse>())!.Id,
            (await replay.Content.ReadFromJsonAsync<MovementResponse>())!.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await Move(client, product.Id, "IN", 6, "same-key")).StatusCode);
    }

    [Fact]
    public async Task InsufficientStockRollsBackWithoutMovement()
    {
        using var client = fixture.Client();
        var product = await Product(client);
        Assert.Equal(HttpStatusCode.Conflict, (await Move(client, product.Id, "OUT", 1, "no-stock")).StatusCode);
        var history = await client.GetFromJsonAsync<MovementResponse[]>($"/v1/products/{product.Id}/movements");
        Assert.Empty(history!);
        Assert.Equal(0, (await client.GetFromJsonAsync<ProductResponse>($"/v1/products/{product.Id}"))!.Balance);
    }

    [Fact]
    public async Task ConcurrentExitsCannotOverdraw()
    {
        using var client = fixture.Client();
        var product = await Product(client);
        await Move(client, product.Id, "IN", 1, "seed");
        var results = await Task.WhenAll(Move(client, product.Id, "OUT", 1, "out-a"), Move(client, product.Id, "OUT", 1, "out-b"));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(0, (await client.GetFromJsonAsync<ProductResponse>($"/v1/products/{product.Id}"))!.Balance);
    }

    [Fact]
    public async Task ConcurrentSameKeyCreatesOneMovement()
    {
        using var client = fixture.Client();
        var product = await Product(client);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Move(client, product.Id, "IN", 1, "race")));
        Assert.All(results, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK }));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(1, (await client.GetFromJsonAsync<ProductResponse>($"/v1/products/{product.Id}"))!.Balance);
    }

    [Fact]
    public async Task DistinctConcurrentEntriesPreserveBalance()
    {
        using var client = fixture.Client();
        var product = await Product(client);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Move(client, product.Id, "IN", 1, $"entry-{i}")));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(8, (await client.GetFromJsonAsync<ProductResponse>($"/v1/products/{product.Id}"))!.Balance);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.0001")]
    public async Task InvalidQuantityRejected(string quantity)
    {
        using var client = fixture.Client();
        var product = await Product(client);
        var result = await Move(client, product.Id, "IN", decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), "invalid");
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task ClientCannotSetBalanceDirectly()
    {
        using var client = fixture.Client();
        var result = await client.PostAsJsonAsync("/v1/products", new { sku = "DEMO", name = "Demo", unit = "UNIT", balance = 100 });
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task MissingKeyAndUnknownProductRejected()
    {
        using var client = fixture.Client();
        var noKey = await client.PostAsJsonAsync("/v1/movements", new { productId = Guid.NewGuid(), kind = "IN", quantity = 1, reference = "Demo" });
        Assert.Equal(HttpStatusCode.BadRequest, noKey.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Move(client, Guid.NewGuid(), "IN", 1, "unknown")).StatusCode);
    }

    [Fact]
    public async Task KeysAreCaseSensitive()
    {
        using var client = fixture.Client();
        var product = await Product(client);
        Assert.Equal(HttpStatusCode.Created, (await Move(client, product.Id, "IN", 1, "CaseKey")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Move(client, product.Id, "IN", 1, "casekey")).StatusCode);
    }

    [Fact]
    public async Task PaginationAndReadiness()
    {
        using var client = fixture.Client();
        await Product(client, "DEMO-A");
        await Product(client, "DEMO-B");
        Assert.Single((await client.GetFromJsonAsync<ProductResponse[]>("/v1/products?limit=1&offset=1"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/v1/products?limit=1000")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        var spec = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        Assert.True(spec.GetProperty("paths").TryGetProperty("/v1/products", out _));
        var initializer = await client.GetStringAsync("/docs/index.js");
        Assert.Contains("../swagger/v1/swagger.json", initializer);
    }
}
