using Inventory.Api.Contracts;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("v1/movements")]
public sealed class MovementsController(MovementService service, InventoryDb db) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<MovementResponse>> Create(CreateMovementRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct)
    {
        var (movement, replayed) = await service.CreateAsync(request, key, ct);
        Response.Headers["Idempotency-Replayed"] = replayed ? "true" : "false";
        return replayed ? Ok(movement) : CreatedAtAction(nameof(Get), new { id = movement.Id }, movement);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MovementResponse>> Get(Guid id, CancellationToken ct)
    {
        var movement = await db.Movements.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, ct);
        return movement is null ? NotFound() : Ok(MovementResponse.From(movement));
    }
}
