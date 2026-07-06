namespace RestaurantCRM.Application.Orders;

public interface IOrderService
{
    Task<List<OrderDto>> GetAllAsync(CancellationToken ct = default);
    Task<OrderDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<OrderDto> CreateAsync(CreateOrderRequest request, Guid createdById, CancellationToken ct = default);
    Task<OrderDto> AddItemAsync(Guid orderId, AddOrderItemRequest request, CancellationToken ct = default);
    Task<OrderDto> RemoveItemAsync(Guid orderId, Guid itemId, CancellationToken ct = default);
    Task<OrderDto> UpdateStatusAsync(Guid id, UpdateOrderStatusRequest request, Guid actingUserId, CancellationToken ct = default);
    Task<OrderDto> CancelAsync(Guid id, Guid actingUserId, CancellationToken ct = default);
    Task<OrderDto> UpdateItemStatusAsync(Guid orderId, Guid itemId, UpdateOrderItemStatusRequest request, CancellationToken ct = default);
    Task<OrderDto> AssignClientAsync(Guid orderId, Guid? clientId, CancellationToken ct = default);
    Task<BillPreviewDto> GetBillPreviewAsync(Guid orderId, CancellationToken ct = default);
    Task<List<KitchenQueueItemDto>> GetKitchenQueueAsync(CancellationToken ct = default);
    Task<OrderDto> BumpOrderItemsAsync(Guid orderId, string targetStatus, string? station = null, CancellationToken ct = default);
    Task<OrderDto> RecallOrderItemsAsync(Guid orderId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default);
}
