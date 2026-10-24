using System.ComponentModel.DataAnnotations;
using Inventory.Api.Data;

namespace Inventory.Api.Contracts;

public sealed class CreateProductRequest
{
    [Required, RegularExpression("^[A-Za-z0-9_-]{1,40}$")]
    public string Sku { get; init; } = "";
    [Required, StringLength(120, MinimumLength = 1)]
    public string Name { get; init; } = "";
    [Required, RegularExpression("^[A-Z]{1,8}$")]
    public string Unit { get; init; } = "UNIT";
}

public sealed class CreateMovementRequest
{
    public Guid ProductId { get; init; }
    [Required, RegularExpression("^(IN|OUT)$")]
    public string Kind { get; init; } = "";
    [Range(typeof(decimal), "0.001", "999999999.999")]
    public decimal Quantity { get; init; }
    [Required, StringLength(80, MinimumLength = 1)]
    public string Reference { get; init; } = "";
}

public sealed record ProductResponse(Guid Id, string Sku, string Name, string Unit, decimal Balance, DateTimeOffset CreatedAt)
{
    public static ProductResponse From(Product product) => new(product.Id, product.Sku, product.Name, product.Unit, product.Balance, product.CreatedAt);
}

public sealed record MovementResponse(Guid Id, Guid ProductId, string Kind, decimal Quantity,
    decimal BalanceBefore, decimal BalanceAfter, string Reference, DateTimeOffset CreatedAt)
{
    public static MovementResponse From(StockMovement movement) => new(movement.Id, movement.ProductId,
        movement.Kind, movement.Quantity, movement.BalanceBefore, movement.BalanceAfter,
        movement.Reference, movement.CreatedAt);
}
