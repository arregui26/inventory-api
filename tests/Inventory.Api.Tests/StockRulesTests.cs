using Inventory.Api.Domain;

namespace Inventory.Api.Tests;

public sealed class StockRulesTests
{
    [Theory]
    [InlineData("0", "IN", "2.125", "2.125")]
    [InlineData("5", "OUT", "2.125", "2.875")]
    [InlineData("1", "OUT", "1", "0")]
    [Trait("Category", "Unit")]
    public void AppliesExactDecimalMovement(string before, string kind, string quantity, string after)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        Assert.Equal(decimal.Parse(after, culture), StockRules.Apply(decimal.Parse(before, culture), kind, decimal.Parse(quantity, culture)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.0001")]
    [InlineData("1000000000")]
    [Trait("Category", "Unit")]
    public void RejectsInvalidQuantity(string quantity)
    {
        Assert.Equal("invalid_quantity", Assert.Throws<InventoryException>(() =>
            StockRules.Apply(10, "IN", decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture))).Code);
    }

    [Fact, Trait("Category", "Unit")]
    public void RejectsInsufficientStock() => Assert.Equal("insufficient_stock",
        Assert.Throws<InventoryException>(() => StockRules.Apply(1, "OUT", 2)).Code);

    [Fact, Trait("Category", "Unit")]
    public void RejectsUnknownKind() => Assert.Equal("invalid_kind",
        Assert.Throws<InventoryException>(() => StockRules.Apply(1, "TRANSFER", 1)).Code);

    [Fact, Trait("Category", "Unit")]
    public void RejectsOverflowingBalance() => Assert.Equal("balance_limit",
        Assert.Throws<InventoryException>(() => StockRules.Apply(StockRules.MaxBalance, "IN", 1)).Code);
}
