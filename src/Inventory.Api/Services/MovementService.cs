using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Inventory.Api.Contracts;
using Inventory.Api.Data;
using Inventory.Api.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Api.Services;

public sealed partial class MovementService(InventoryDb db)
{
    [GeneratedRegex("^[A-Za-z0-9_.:-]{1,128}$")]
    private static partial Regex KeyPattern();

    public async Task<(MovementResponse Movement, bool Replayed)> CreateAsync(
        CreateMovementRequest input, string? key, CancellationToken cancellationToken)
    {
        if (key is null || !KeyPattern().IsMatch(key))
            throw new InventoryException(400, "invalid_idempotency_key", "Provide an ASCII Idempotency-Key of 1 to 128 characters.");
        if (input.ProductId == Guid.Empty)
            throw new InventoryException(400, "invalid_product_id", "ProductId cannot be empty.");
        // Validate precision before acquiring locks. The real balance is read inside the transaction.
        StockRules.Validate(input.Kind, input.Quantity);
        var reference = input.Reference.Trim();
        var canonical = JsonSerializer.Serialize(new
        {
            input.ProductId,
            input.Kind,
            Quantity = input.Quantity.ToString("0.000", CultureInfo.InvariantCulture),
            Reference = reference
        });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        // Serialize equal keys without holding range locks in the movements index.
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@resource, " +
            "@LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=5000; SELECT @r;";
        command.Parameters.Add(new SqlParameter("@resource", "inventory-movement-" +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))));
        var lockResult = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (lockResult < 0)
            throw new InventoryException(409, "movement_busy", "A matching request is busy; retry with the same key.");

        var existing = await db.Movements.AsNoTracking().SingleOrDefaultAsync(m => m.IdempotencyKey == key, cancellationToken);
        if (existing is not null)
        {
            if (existing.PayloadHash != hash)
                throw new InventoryException(409, "idempotency_conflict", "This key belongs to another movement.");
            await transaction.CommitAsync(cancellationToken);
            return (MovementResponse.From(existing), true);
        }

        var product = await db.Products.FromSqlInterpolated(
            $"SELECT * FROM [Products] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {input.ProductId}")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InventoryException(404, "product_not_found", "Product not found.");
        var after = StockRules.Apply(product.Balance, input.Kind, input.Quantity);
        var movement = new StockMovement
        {
            ProductId = product.Id,
            Kind = input.Kind,
            Quantity = input.Quantity,
            BalanceBefore = product.Balance,
            BalanceAfter = after,
            Reference = reference,
            IdempotencyKey = key,
            PayloadHash = hash
        };
        product.Balance = after;
        db.Movements.Add(movement);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (MovementResponse.From(movement), false);
    }
}
