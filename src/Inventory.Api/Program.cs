using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Inventory.Api.Data;
using Inventory.Api.Domain;
using Inventory.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
var apiKey = builder.Configuration["API_KEY"];
if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length < 32)
    throw new InvalidOperationException("Configure an API_KEY of at least 32 characters.");
builder.Services.AddDbContext<InventoryDb>(options => options.UseSqlServer(
    builder.Configuration.GetConnectionString("Inventory")
        ?? throw new InvalidOperationException("Configure ConnectionStrings__Inventory.")));
builder.Services.AddScoped<MovementService>();
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Inventory API",
        Version = "v1",
        Description = "C# / SQL Server portfolio demo. Products, immutable movements and transactional stock."
    });
    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "X-API-Key"
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("ApiKey", document)] = []
    });
});
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Critical);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Update", LogLevel.Critical);
var app = builder.Build();

if (args.Contains("--migrate"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<InventoryDb>().Database.MigrateAsync();
    return;
}

app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (InventoryException error)
    {
        await Results.Problem(statusCode: error.Status, title: error.Code, detail: error.Message)
            .ExecuteAsync(context);
    }
    catch (DbUpdateConcurrencyException)
    {
        await Results.Problem(statusCode: 409, title: "concurrency_conflict",
            detail: "Stock changed concurrently; retry with the same idempotency key.").ExecuteAsync(context);
    }
    catch (Exception error)
    {
        app.Logger.LogError("Request failed: type={Type}, trace={Trace}", error.GetType().Name, context.TraceIdentifier);
        await Results.Problem(statusCode: 500, title: "internal_error", detail: "The request could not be completed.")
            .ExecuteAsync(context);
    }
});
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/v1") || context.Request.Path == "/health/ready")
    {
        var provided = context.Request.Headers["X-API-Key"];
        if (provided.Count != 1 || !CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided[0] ?? ""), Encoding.UTF8.GetBytes(apiKey)))
        {
            await Results.Problem(statusCode: 401, title: "unauthorized").ExecuteAsync(context);
            return;
        }
    }
    await next(context);
});
app.UseSwagger();
app.UseSwaggerUI(options => options.RoutePrefix = "docs");
app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (InventoryDb db, CancellationToken ct) =>
{
    try
    {
        if (!await db.Database.CanConnectAsync(ct) || (await db.Database.GetPendingMigrationsAsync(ct)).Any())
            return Results.Problem(statusCode: 503, title: "database_unavailable");
        return Results.Ok(new { status = "ready" });
    }
    catch { return Results.Problem(statusCode: 503, title: "database_unavailable"); }
});
app.MapControllers();
app.Run();

public partial class Program { }
