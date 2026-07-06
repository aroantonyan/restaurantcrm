using Microsoft.EntityFrameworkCore;
using RestaurantCRM.Application.ActivityLog;
using RestaurantCRM.Application.CashRegister;
using RestaurantCRM.Application.Clients;
using RestaurantCRM.Application.Common.Interfaces;
using RestaurantCRM.Application.Inventory;
using RestaurantCRM.Application.Orders;
using RestaurantCRM.Domain.Entities;
using RestaurantCRM.Domain.Enums;
using RestaurantCRM.Infrastructure.Persistence;

namespace RestaurantCRM.Infrastructure.Services;

public class OrderService(
    AppDbContext db,
    ITenantContext tenant,
    IRealtimeNotifier notifier,
    IInventoryService inventory,
    ICashRegisterService cashRegister,
    IClientService clients,
    IActivityLogService activityLog) : IOrderService
{
    public async Task<List<OrderDto>> GetAllAsync(CancellationToken ct = default)
    {
        // Always include every Open order (long-running tables) but cap closed orders
        // to the last 7 days. Without this, the list grows unbounded over time and
        // every page load pulls the entire history.
        var cutoff = DateTime.UtcNow.AddDays(-7);
        // Projection straight into the DTO so EF generates a tight SELECT and skips
        // change-tracking on the read path. The shape mirrors ToDto() — kept inline
        // because ToDto() is a regular method EF can't translate to SQL.
        return await db.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.Open || o.CreatedAt >= cutoff)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new OrderDto(
                o.Id,
                o.TableId,
                o.Table.Number,
                o.Status.ToString(),
                o.CreatedBy.FirstName + " " + o.CreatedBy.LastName,
                o.CreatedAt,
                o.Items.Select(i => new OrderItemDto(
                    i.Id, i.MenuItemId, i.MenuItemName, i.Price, i.Quantity, i.Status.ToString(), i.Notes)).ToList(),
                o.Items.Sum(i => i.Price * i.Quantity),
                o.PaymentMethod != null ? o.PaymentMethod.ToString() : null,
                o.ClientId,
                o.Client != null ? o.Client.FullName : null))
            .ToListAsync(ct);
    }

    public async Task<OrderDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var order = await db.Orders
            .Include(o => o.Table)
            .Include(o => o.CreatedBy)
            .Include(o => o.Items)
            .Include(o => o.Client)
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new KeyNotFoundException("Order not found.");
        return ToDto(order);
    }

    public async Task<OrderDto> CreateAsync(CreateOrderRequest request, Guid createdById, CancellationToken ct = default)
    {
        var table = await db.Tables.FirstOrDefaultAsync(t => t.Id == request.TableId, ct)
            ?? throw new KeyNotFoundException("Table not found.");

        var menuItemIds = request.Items.Select(i => i.MenuItemId).ToList();
        // Read-only lookup; the MenuItem rows themselves are never mutated here —
        // only their Name/Price/Id are snapshotted into the new OrderItem.
        var menuItems = await db.MenuItems
            .AsNoTracking()
            .Where(m => menuItemIds.Contains(m.Id))
            .ToListAsync(ct);

        var missing = menuItemIds.Except(menuItems.Select(m => m.Id)).ToList();
        if (missing.Count > 0) throw new KeyNotFoundException("One or more menu items not found.");

        var unavailable = menuItems.Where(m => !m.IsAvailable).Select(m => m.Name).ToList();
        if (unavailable.Count > 0)
            throw new InvalidOperationException($"Items not available: {string.Join(", ", unavailable)}");

        // Optional client assigned at creation time (before the order hits the kitchen),
        // so loyalty/deposit billing is ready when the bill is closed.
        if (request.ClientId is not null)
        {
            var clientExists = await db.Clients
                .AsNoTracking()
                .AnyAsync(c => c.Id == request.ClientId.Value && !c.IsArchived, ct);
            if (!clientExists) throw new KeyNotFoundException("Client not found.");
        }

        var order = new Order
        {
            RestaurantId = tenant.RestaurantId,
            TableId = table.Id,
            CreatedById = createdById,
            ClientId = request.ClientId,
            Items = request.Items.Select(i =>
            {
                var menuItem = menuItems.First(m => m.Id == i.MenuItemId);
                return new OrderItem
                {
                    RestaurantId = tenant.RestaurantId,
                    MenuItemId = menuItem.Id,
                    MenuItemName = menuItem.Name,
                    Price = menuItem.Price,
                    Quantity = i.Quantity,
                    Notes = i.Notes,
                };
            }).ToList(),
        };

        table.Status = TableStatus.Occupied;
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        await activityLog.LogAsync(createdById, ActivityCategory.Order, "Created", nameof(Order), order.Id,
            $"Order opened on table #{table.Number} with {order.Items.Count} item(s)", ct);

        // Push: a new order exists, and the table flipped to Occupied.
        await notifier.OrderChanged(order.Id, ct);
        await notifier.TableChanged(table.Id, ct);

        return await GetByIdAsync(order.Id, ct);
    }

    public async Task<OrderDto> AddItemAsync(Guid orderId, AddOrderItemRequest request, CancellationToken ct = default)
    {
        var order = await db.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new KeyNotFoundException("Order not found.");

        if (order.Status != OrderStatus.Open)
            throw new InvalidOperationException("Cannot add items to a closed order.");

        var menuItem = await db.MenuItems.FirstOrDefaultAsync(m => m.Id == request.MenuItemId, ct)
            ?? throw new KeyNotFoundException("Menu item not found.");

        if (!menuItem.IsAvailable)
            throw new InvalidOperationException($"'{menuItem.Name}' is not available.");

        // Merge into an existing pending line for the same item + same notes, so the
        // kitchen sees one "Bozbash ×3" instead of three separate "×1" tickets. We
        // only merge Pending lines (not yet started); anything already being prepared
        // stays its own line. Notes must match — a "no onions" line is distinct.
        var existing = order.Items.FirstOrDefault(i =>
            i.MenuItemId == menuItem.Id
            && i.Status == OrderItemStatus.Pending
            && (i.Notes ?? "") == (request.Notes ?? ""));

        if (existing is not null)
        {
            existing.Quantity += request.Quantity;
        }
        else
        {
            db.OrderItems.Add(new OrderItem
            {
                RestaurantId = tenant.RestaurantId,
                OrderId = order.Id,
                MenuItemId = menuItem.Id,
                MenuItemName = menuItem.Name,
                Price = menuItem.Price,
                Quantity = request.Quantity,
                Notes = request.Notes,
            });
        }
        await db.SaveChangesAsync(ct);

        await notifier.OrderChanged(orderId, ct);

        return await GetByIdAsync(orderId, ct);
    }

    public async Task<OrderDto> RemoveItemAsync(Guid orderId, Guid itemId, CancellationToken ct = default)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new KeyNotFoundException("Order not found.");

        if (order.Status != OrderStatus.Open)
            throw new InvalidOperationException("Cannot remove items from a closed order.");

        var item = await db.OrderItems.FirstOrDefaultAsync(i => i.Id == itemId && i.OrderId == orderId, ct)
            ?? throw new KeyNotFoundException("Order item not found.");

        db.OrderItems.Remove(item);
        await db.SaveChangesAsync(ct);

        await notifier.OrderChanged(orderId, ct);

        return await GetByIdAsync(orderId, ct);
    }

    public async Task<OrderDto> UpdateStatusAsync(Guid id, UpdateOrderStatusRequest request, Guid actingUserId, CancellationToken ct = default)
    {
        // The validator already enforces "Paid" + a valid PaymentMethod.
        // Parse the method string once here so TransitionStatusAsync stays typed.
        if (!Enum.TryParse<PaymentMethod>(request.PaymentMethod, out var method))
            throw new ArgumentException($"Invalid payment method: {request.PaymentMethod}");

        return await TransitionStatusAsync(id, OrderStatus.Paid, actingUserId, method, request.UseDeposit, request.ApplyCashback, ct);
    }

    public async Task<OrderDto> CancelAsync(Guid id, Guid actingUserId, CancellationToken ct = default)
    {
        // Cancelled orders do not deduct stock or record a payment — but the canceller
        // is captured via actingUserId so the audit log can attribute the void.
        return await TransitionStatusAsync(id, OrderStatus.Cancelled, actingUserId, null, false, false, ct);
    }

    private async Task<OrderDto> TransitionStatusAsync(Guid id, OrderStatus target, Guid actingUserId, PaymentMethod? method, bool useDeposit, bool applyCashback, CancellationToken ct)
    {
        var order = await db.Orders
            .Include(o => o.Table)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new KeyNotFoundException("Order not found.");

        if (order.Status != OrderStatus.Open)
            throw new InvalidOperationException("Only Open orders can change status.");

        order.Status = target;

        // Auto-deduct inventory + record cash payment + (optionally) charge client deposit
        // and credit cashback. All share the same SaveChangesAsync as the status change,
        // so they commit atomically — a failure in any one rolls back the whole transition.
        if (target == OrderStatus.Paid)
        {
            if (method is null)
                throw new InvalidOperationException("Payment method is required when paying an order.");

            // Applying store-credit (balance) or earning cashback both require a client.
            if ((useDeposit || method == PaymentMethod.Deposit || applyCashback) && order.ClientId is null)
                throw new InvalidOperationException("A client must be assigned to the order to use balance or cashback.");

            if (order.Items.Count > 0)
                await StageRecipeDeductionsAsync(order, actingUserId, ct);

            // Snapshot total at payment time. A VIP table adds a flat surcharge that
            // the customer pays on top of the items (it flows into the cash drawer /
            // deposit settlement just like the item subtotal).
            var vipSurcharge = order.Table.IsVip ? order.Table.VipAmount : 0m;
            var total = order.Items.Sum(i => i.Price * i.Quantity) + vipSurcharge;

            // ── Split the bill into store-credit (deposit) + out-of-pocket remainder ──
            // `method == Deposit` is the legacy "charge the whole bill to the client's
            // account" path (balance may go negative = on credit / в долг). Otherwise
            // `useDeposit` applies only the available positive balance, capped at the bill.
            decimal depositApplied;
            if (method == PaymentMethod.Deposit)
            {
                depositApplied = total;
            }
            else if (useDeposit && order.ClientId is not null)
            {
                var balance = await db.Clients
                    .Where(c => c.Id == order.ClientId.Value)
                    .Select(c => c.DepositBalance)
                    .FirstAsync(ct);
                depositApplied = Math.Min(Math.Max(balance, 0m), total);
            }
            else
            {
                depositApplied = 0m;
            }

            var remainder = total - depositApplied;

            // Record the method actually settled: Deposit when the balance covered the
            // whole bill, otherwise the chosen out-of-pocket method (cash/card/…).
            order.PaymentMethod = depositApplied > 0m && remainder == 0m ? PaymentMethod.Deposit : method.Value;

            // 1. Out-of-pocket portion hits the cash register ledger (only Cash moves the drawer).
            if (remainder > 0m)
                await cashRegister.StageOrderPaymentAsync(order.Id, remainder, order.PaymentMethod.Value, actingUserId, ct);

            // 2. Debit whatever store-credit was applied from the client's account.
            if (depositApplied > 0m)
                await clients.StageOrderPaymentAsync(order.ClientId!.Value, order.Id, depositApplied, actingUserId, ct);

            // 3. Cashback — opt-in, earned only on the out-of-pocket remainder. Never on
            //    store-credit, which would let a balance regenerate itself perpetually.
            if (applyCashback && order.ClientId is not null && remainder > 0m)
                await clients.StageCashbackAsync(order.ClientId.Value, order.Id, remainder, actingUserId, ct);
        }

        // If no other order keeps the table busy, release it.
        var hasOtherOpenOrders = await db.Orders
            .AnyAsync(o => o.TableId == order.TableId && o.Id != id && o.Status == OrderStatus.Open, ct);

        var tableFreed = false;
        if (!hasOtherOpenOrders)
        {
            order.Table.Status = TableStatus.Free;
            tableFreed = true;
        }

        await db.SaveChangesAsync(ct);

        // Audit log AFTER the main op committed — best-effort, never blocks.
        // Cancellation is the security-critical case: cash voids and refunds happen here.
        var shortId = order.Id.ToString()[..8];
        if (target == OrderStatus.Paid)
        {
            var orderTotal = order.Items.Sum(i => i.Price * i.Quantity)
                + (order.Table.IsVip ? order.Table.VipAmount : 0m);
            await activityLog.LogAsync(actingUserId, ActivityCategory.Order, "Paid", nameof(Order), order.Id,
                $"Order #{shortId} paid via {method} — total {orderTotal:N2}", ct);
        }
        else if (target == OrderStatus.Cancelled)
        {
            // Pass tenant's current user — CancelAsync passes Guid.Empty since
            // the controller path doesn't thread it. Falls back to "System".
            await activityLog.LogAsync(actingUserId, ActivityCategory.Order, "Cancelled", nameof(Order), order.Id,
                $"Order #{shortId} cancelled", ct);
        }

        await notifier.OrderChanged(id, ct);
        if (tableFreed) await notifier.TableChanged(order.TableId, ct);

        return await GetByIdAsync(id, ct);
    }

    /// <summary>
    /// Looks up each menu item's recipe and stages Sale movements deducting
    /// `recipeQty × itemQty` from each ingredient. Menu items without a recipe
    /// are silently skipped — restaurants commonly sell things that aren't tracked
    /// (e.g., bottled drinks). One realtime notification per affected product so
    /// the warehouse view refreshes live.
    /// </summary>
    private async Task StageRecipeDeductionsAsync(Order order, Guid actingUserId, CancellationToken ct)
    {
        var menuItemIds = order.Items.Select(i => i.MenuItemId).Distinct().ToList();

        var recipeRows = await db.MenuItemRecipes
            .Where(r => menuItemIds.Contains(r.MenuItemId))
            .ToListAsync(ct);
        if (recipeRows.Count == 0) return;

        var recipesByMenuItem = recipeRows
            .GroupBy(r => r.MenuItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var deductions = new List<SaleDeduction>();
        foreach (var item in order.Items)
        {
            if (!recipesByMenuItem.TryGetValue(item.MenuItemId, out var ingredients)) continue;
            foreach (var ing in ingredients)
                deductions.Add(new SaleDeduction(ing.ProductId, ing.Quantity * item.Quantity));
        }

        if (deductions.Count == 0) return;

        var depletedProductIds = await inventory.StageSaleDeductionsAsync(
            deductions,
            actingUserId,
            $"Order #{order.Id.ToString()[..8]}",
            ct);

        // Auto-86: any menu item that uses a depleted product (and is still marked
        // available) gets flipped to unavailable in the same transaction. Re-enabling
        // is intentionally manual — the kitchen confirms once the new delivery is
        // verified and prepped. Matches Toast / Square Restaurants behavior.
        var autoDisabledItemIds = new List<Guid>();
        if (depletedProductIds.Count > 0)
        {
            var menuItemsToDisable = await db.MenuItems
                .Where(i => i.IsAvailable && db.MenuItemRecipes
                    .Any(r => r.MenuItemId == i.Id && depletedProductIds.Contains(r.ProductId)))
                .ToListAsync(ct);

            foreach (var item in menuItemsToDisable)
            {
                item.IsAvailable = false;
                autoDisabledItemIds.Add(item.Id);
            }
        }

        // Realtime fan-out so warehouse + menu pages refresh. These pings fire
        // BEFORE the outer SaveChangesAsync commits — safe because the events
        // carry only ids and clients refetch via REST. If the outer transaction
        // rolls back, the refetch returns the pre-deduction state and the UI
        // self-heals; over-firing a notification is never worse than no-op.
        foreach (var productId in deductions.Select(d => d.ProductId).Distinct())
            await notifier.ProductChanged(productId, ct);

        foreach (var itemId in autoDisabledItemIds)
            await notifier.MenuItemChanged(itemId, ct);
    }

    public async Task<OrderDto> UpdateItemStatusAsync(Guid orderId, Guid itemId, UpdateOrderItemStatusRequest request, CancellationToken ct = default)
    {
        var item = await db.OrderItems.FirstOrDefaultAsync(i => i.Id == itemId && i.OrderId == orderId, ct)
            ?? throw new KeyNotFoundException("Order item not found.");

        if (!Enum.TryParse<OrderItemStatus>(request.Status, out var newStatus))
            throw new ArgumentException($"Invalid item status: {request.Status}");

        item.Status = newStatus;
        await db.SaveChangesAsync(ct);

        await notifier.OrderChanged(orderId, ct);

        return await GetByIdAsync(orderId, ct);
    }

    public async Task<OrderDto> AssignClientAsync(Guid orderId, Guid? clientId, CancellationToken ct = default)
    {
        var order = await db.Orders
            .Include(o => o.Items)
            .Include(o => o.Table)
            .Include(o => o.CreatedBy)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new KeyNotFoundException("Order not found.");

        // Only open orders can have their client changed. A paid order is a closed
        // accounting record — re-attribution would orphan its existing payment row.
        if (order.Status != OrderStatus.Open)
            throw new InvalidOperationException("Only open orders can be re-assigned.");

        if (clientId is not null)
        {
            var clientExists = await db.Clients.AnyAsync(c => c.Id == clientId.Value && !c.IsArchived, ct);
            if (!clientExists) throw new KeyNotFoundException("Client not found.");
        }

        order.ClientId = clientId;
        await db.SaveChangesAsync(ct);

        await notifier.OrderChanged(orderId, ct);
        return await GetByIdAsync(orderId, ct);
    }

    public async Task<BillPreviewDto> GetBillPreviewAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await db.Orders
            .Include(o => o.Items)
            .Include(o => o.Client)
            .Include(o => o.Table)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new KeyNotFoundException("Order not found.");

        var subtotal = order.Items.Sum(i => i.Price * i.Quantity);
        // VIP tables carry a flat surcharge on top of the items; it's part of the
        // amount owed, so all the deposit/cashback math runs against the bill total.
        var vipSurcharge = order.Table.IsVip ? order.Table.VipAmount : 0m;
        var billTotal = subtotal + vipSurcharge;
        var client = order.Client;

        // No client → plain bill: charge the bill total, nothing else to compute.
        if (client is null)
            return new BillPreviewDto(
                subtotal, vipSurcharge, null, null, 0m,
                LoyaltyType.None.ToString(), 0m,
                CashbackToEarn: 0m,
                SuggestedCharge: billTotal,
                DepositCovers: 0m,
                DepositRemainder: billTotal,
                BalanceAfterDeposit: 0m);

        // Cashback is earned on any non-deposit payment (matches the payment flow).
        var cashback = client.LoyaltyType == LoyaltyType.Cashback && client.LoyaltyRate > 0m
            ? Math.Round(billTotal * (client.LoyaltyRate / 100m), 2)
            : 0m;

        // Deposit path: the balance absorbs as much as it can; the rest is owed.
        // A negative remainder never happens (covers is capped at the bill total), but a
        // remainder > 0 with insufficient balance means the customer goes on credit.
        var depositCovers = Math.Min(Math.Max(client.DepositBalance, 0m), billTotal);
        var depositRemainder = billTotal - depositCovers;
        var balanceAfterDeposit = client.DepositBalance - billTotal;

        return new BillPreviewDto(
            subtotal,
            vipSurcharge,
            client.Id,
            client.FullName,
            client.DepositBalance,
            client.LoyaltyType.ToString(),
            client.LoyaltyRate,
            CashbackToEarn: cashback,
            SuggestedCharge: billTotal,
            DepositCovers: depositCovers,
            DepositRemainder: depositRemainder,
            BalanceAfterDeposit: balanceAfterDeposit);
    }

    /// <summary>
    /// Kitchen-display queue: every item across every Open order in the tenant
    /// whose status is on the kitchen side of the pipeline (Pending / Preparing
    /// / Ready). Served items drop off — the waiter has taken them out. Oldest
    /// first, because that is the canonical KDS FIFO. The elapsed-time badge
    /// (color: green / yellow / red as time grows) is rendered client-side from
    /// CreatedAt so we don't poll the API every second.
    /// </summary>
    public async Task<List<KitchenQueueItemDto>> GetKitchenQueueAsync(CancellationToken ct = default)
    {
        return await db.OrderItems
            .AsNoTracking()
            .Where(i => i.Order.Status == OrderStatus.Open
                     && (i.Status == OrderItemStatus.Pending
                      || i.Status == OrderItemStatus.Preparing
                      || i.Status == OrderItemStatus.Ready))
            .OrderBy(i => i.CreatedAt)
            .Select(i => new KitchenQueueItemDto(
                i.Id,
                i.OrderId,
                i.MenuItemName,
                i.Quantity,
                i.Notes,
                i.Status.ToString(),
                i.MenuItemRef.Category.Station.ToString(),
                i.Order.Table.Number,
                i.Order.TableId,
                i.Order.CreatedBy.FirstName,
                i.CreatedAt))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Advances every kitchen-side item on one order to <paramref name="targetStatus"/>
    /// in a single transaction — the "bump the whole ticket" KDS action. Doing it
    /// server-side beats N client PATCHes: one DB round-trip, one realtime event,
    /// and the move is atomic (all items flip together or none do).
    ///   • target Ready  → moves items still Pending/Preparing.
    ///   • target Served → moves anything not already Served (clears the ticket).
    /// When <paramref name="station"/> is given, only items routed to that station
    /// move — a bartender clearing drinks must not mark the kitchen's food Ready.
    /// Idempotent: items already at/beyond the target are left untouched.
    /// </summary>
    public async Task<OrderDto> BumpOrderItemsAsync(Guid orderId, string targetStatus, string? station = null, CancellationToken ct = default)
    {
        if (!Enum.TryParse<OrderItemStatus>(targetStatus, out var target)
            || (target != OrderItemStatus.Ready && target != OrderItemStatus.Served))
            throw new ArgumentException("Bump target must be Ready or Served.");

        Station? stationFilter = null;
        if (station is not null)
        {
            if (!Enum.TryParse<Station>(station, ignoreCase: true, out var parsed))
                throw new ArgumentException("Station must be Kitchen or Bar.");
            stationFilter = parsed;
        }

        var order = await db.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new KeyNotFoundException("Order not found.");

        if (order.Status != OrderStatus.Open)
            throw new InvalidOperationException("Only open orders can be bumped.");

        IEnumerable<OrderItem> candidates = order.Items;
        if (stationFilter is not null)
        {
            var stationByItemId = await db.OrderItems.AsNoTracking()
                .Where(i => i.OrderId == orderId)
                .Select(i => new { i.Id, i.MenuItemRef.Category.Station })
                .ToDictionaryAsync(x => x.Id, x => x.Station, ct);
            candidates = candidates.Where(i =>
                stationByItemId.TryGetValue(i.Id, out var s) && s == stationFilter);
        }

        var movable = candidates.Where(i => target == OrderItemStatus.Ready
            ? i.Status is OrderItemStatus.Pending or OrderItemStatus.Preparing
            : i.Status != OrderItemStatus.Served).ToList();

        // Idempotent no-op — nothing left to move (e.g. a double-tap or a stale view).
        if (movable.Count == 0) return await GetByIdAsync(orderId, ct);

        foreach (var i in movable) i.Status = target;
        await db.SaveChangesAsync(ct);

        await notifier.OrderChanged(orderId, ct);
        return await GetByIdAsync(orderId, ct);
    }

    /// <summary>
    /// Un-bumps a just-cleared ticket: flips the given Served items back to Ready
    /// so they reappear on the kitchen display — the KDS "recall" every mature
    /// system pairs with bump, because fat-fingered bumps are routine in a kitchen.
    /// The caller names the exact item ids it bumped, so a recall can never
    /// resurrect items that were legitimately served earlier (e.g. drinks taken
    /// out an hour ago). Items no longer Served are skipped, making it idempotent.
    /// </summary>
    public async Task<OrderDto> RecallOrderItemsAsync(Guid orderId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default)
    {
        var order = await db.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new KeyNotFoundException("Order not found.");

        if (order.Status != OrderStatus.Open)
            throw new InvalidOperationException("Only open orders can be recalled.");

        var ids = new HashSet<Guid>(itemIds);
        var restorable = order.Items
            .Where(i => i.Status == OrderItemStatus.Served && ids.Contains(i.Id))
            .ToList();

        if (restorable.Count == 0) return await GetByIdAsync(orderId, ct);

        foreach (var i in restorable) i.Status = OrderItemStatus.Ready;
        await db.SaveChangesAsync(ct);

        await notifier.OrderChanged(orderId, ct);
        return await GetByIdAsync(orderId, ct);
    }

    private static OrderDto ToDto(Order o) => new(
        o.Id,
        o.TableId,
        o.Table.Number,
        o.Status.ToString(),
        $"{o.CreatedBy.FirstName} {o.CreatedBy.LastName}",
        o.CreatedAt,
        o.Items.Select(i => new OrderItemDto(
            i.Id, i.MenuItemId, i.MenuItemName, i.Price, i.Quantity, i.Status.ToString(), i.Notes
        )).ToList(),
        o.Items.Sum(i => i.Price * i.Quantity),
        o.PaymentMethod?.ToString(),
        o.ClientId,
        o.Client?.FullName
    );
}
