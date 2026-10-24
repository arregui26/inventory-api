using System.ComponentModel.DataAnnotations;
using Inventory.Api.Contracts;
using Inventory.Api.Data;
using Inventory.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("v1/products")]
public sealed class ProductsController(InventoryDb db) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ProductResponse>> Create(CreateProductRequest request, CancellationToken ct)
    {
        var product = new Product { Sku = request.Sku.ToUpperInvariant(), Name = request.Name.Trim(), Unit = request.Unit };
        db.Products.Add(product);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new InventoryException(409, "sku_exists", "The SKU already exists.");
        }
        return CreatedAtAction(nameof(Get), new { id = product.Id }, ProductResponse.From(product));
    }

    [HttpGet]
    public async Task<IReadOnlyList<ProductResponse>> List(
        [FromQuery, Range(1, 100)] int limit = 20, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken ct = default)
    {
        var products = await db.Products.AsNoTracking().OrderBy(p => p.Sku).Skip(offset).Take(limit).ToListAsync(ct);
        return products.Select(ProductResponse.From).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductResponse>> Get(Guid id, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        return product is null ? NotFound() : Ok(ProductResponse.From(product));
    }

    [HttpGet("{id:guid}/movements")]
    public async Task<ActionResult<IReadOnlyList<MovementResponse>>> Movements(Guid id,
        [FromQuery, Range(1, 100)] int limit = 20, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken ct = default)
    {
        if (!await db.Products.AnyAsync(p => p.Id == id, ct)) return NotFound();
        var history = await db.Movements.AsNoTracking().Where(m => m.ProductId == id)
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).Skip(offset).Take(limit).ToListAsync(ct);
        return Ok(history.Select(MovementResponse.From).ToList());
    }
}
