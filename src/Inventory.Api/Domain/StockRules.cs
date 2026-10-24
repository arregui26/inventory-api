namespace Inventory.Api.Domain;

public sealed class InventoryException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public static class StockRules
{
    public const decimal MaxQuantity = 999999999.999m;
    public const decimal MaxBalance = 999999999999999.999m;

    public static void Validate(string kind, decimal quantity)
    {
        if (quantity <= 0 || quantity > MaxQuantity || decimal.Round(quantity, 3) != quantity)
            throw new InventoryException(400, "invalid_quantity", "Quantity must be positive with at most three decimals.");
        if (kind is not ("IN" or "OUT"))
            throw new InventoryException(400, "invalid_kind", "Kind must be IN or OUT.");
    }

    public static decimal Apply(decimal balance, string kind, decimal quantity)
    {
        Validate(kind, quantity);
        if (kind == "OUT" && quantity > balance)
            throw new InventoryException(409, "insufficient_stock", "Not enough stock for this movement.");
        var result = kind == "IN" ? balance + quantity : balance - quantity;
        if (result > MaxBalance)
            throw new InventoryException(409, "balance_limit", "The stock limit would be exceeded.");
        return result;
    }
}
