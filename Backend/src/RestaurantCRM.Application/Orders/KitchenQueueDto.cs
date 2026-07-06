namespace RestaurantCRM.Application.Orders;

/// <summary>
/// A single line on the kitchen display: one order-item from an Open order whose
/// status is still on the kitchen side of the pipeline (Pending / Preparing /
/// Ready). Served items drop off because the waiter has taken them out.
///
/// `CreatedAt` is shipped raw — the client renders the elapsed-time badge and
/// updates it every second locally so we don't poll the API once per second.
/// </summary>
public record KitchenQueueItemDto(
    Guid Id,
    Guid OrderId,
    string MenuItemName,
    int Quantity,
    string? Notes,
    string Status,
    // Prep station this item routes to ("Kitchen" | "Bar"), resolved live through
    // the menu category so re-routing a category re-routes queued items too.
    string Station,
    int TableNumber,
    Guid TableId,
    // First name of the waiter who opened the order — shown on the ticket so the
    // kitchen knows who to call when the food is up (standard KDS affordance).
    string ServerName,
    DateTime CreatedAt
);
