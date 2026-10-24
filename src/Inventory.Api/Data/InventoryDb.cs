using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

public sealed class InventoryDb(DbContextOptions<InventoryDb> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockMovement> Movements => Set<StockMovement>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var product = model.Entity<Product>();
        product.ToTable("Products", table => table.HasCheckConstraint("CK_Product_Balance", "[Balance] >= 0"));
        product.HasKey(p => p.Id);
        product.Property(p => p.Sku).HasMaxLength(40).IsRequired();
        product.HasIndex(p => p.Sku).IsUnique();
        product.Property(p => p.Name).HasMaxLength(120).IsRequired();
        product.Property(p => p.Unit).HasMaxLength(8).IsRequired();
        product.Property(p => p.Balance).HasPrecision(18, 3);
        product.Property(p => p.RowVersion).IsRowVersion();

        var movement = model.Entity<StockMovement>();
        movement.ToTable("Movements", table =>
        {
            table.HasCheckConstraint("CK_Movement_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_Movement_Kind", "[Kind] IN ('IN','OUT')");
            table.HasCheckConstraint("CK_Movement_Balances", "[BalanceBefore] >= 0 AND [BalanceAfter] >= 0");
            table.HasCheckConstraint("CK_Movement_Arithmetic", "[BalanceAfter] = CASE WHEN [Kind]='IN' THEN [BalanceBefore]+[Quantity] ELSE [BalanceBefore]-[Quantity] END");
        });
        movement.HasKey(m => m.Id);
        movement.HasOne<Product>().WithMany().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);
        movement.Property(m => m.Kind).HasMaxLength(3).IsRequired();
        movement.Property(m => m.Quantity).HasPrecision(12, 3);
        movement.Property(m => m.BalanceBefore).HasPrecision(18, 3);
        movement.Property(m => m.BalanceAfter).HasPrecision(18, 3);
        movement.Property(m => m.Reference).HasMaxLength(80).IsRequired();
        movement.Property(m => m.IdempotencyKey).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2").IsRequired();
        movement.HasIndex(m => m.IdempotencyKey).IsUnique();
        movement.Property(m => m.PayloadHash).HasMaxLength(64).IsRequired();
        movement.HasIndex(m => new { m.ProductId, m.CreatedAt });
    }
}
