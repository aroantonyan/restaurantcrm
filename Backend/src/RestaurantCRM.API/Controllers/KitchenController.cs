using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantCRM.API.Auth;
using RestaurantCRM.Application.Orders;
using RestaurantCRM.Domain.Enums;

namespace RestaurantCRM.API.Controllers;

/// <summary>
/// Read-only endpoints powering the Kitchen Display (KDS). Item-status changes
/// continue to go through PATCH /api/orders/{id}/items/{itemId}/status so the
/// existing realtime fan-out (orderChanged) drives live UI updates.
/// </summary>
[Authorize]
[Route("api/kitchen")]
public class KitchenController(IOrderService orderService) : BaseController
{
    [HttpGet("queue")]
    [RequirePermission(PermissionType.MoveOrderItems)]
    public async Task<IActionResult> Queue(CancellationToken ct)
    {
        var result = await orderService.GetKitchenQueueAsync(ct);
        return Ok(result);
    }

    public record BumpRequest(string Status, string? Station = null);

    // Bump every kitchen-side item on a ticket to Ready (or Served) in one shot.
    // Station narrows the bump to that station's items on multi-station tickets.
    [HttpPost("orders/{orderId:guid}/bump")]
    [RequirePermission(PermissionType.MoveOrderItems)]
    public async Task<IActionResult> Bump(Guid orderId, BumpRequest request, CancellationToken ct)
    {
        var result = await orderService.BumpOrderItemsAsync(orderId, request.Status, request.Station, ct);
        return Ok(result);
    }

    public record RecallRequest(List<Guid> ItemIds);

    // Undo a bump: flip the named Served items back to Ready so the ticket
    // reappears on the board. Ids scope the recall to exactly what was bumped.
    [HttpPost("orders/{orderId:guid}/recall")]
    [RequirePermission(PermissionType.MoveOrderItems)]
    public async Task<IActionResult> Recall(Guid orderId, RecallRequest request, CancellationToken ct)
    {
        var result = await orderService.RecallOrderItemsAsync(orderId, request.ItemIds ?? [], ct);
        return Ok(result);
    }
}
