using System.Net;
using System.Net.Http.Json;
using RestaurantCRM.Application.CashRegister;
using RestaurantCRM.Application.Clients;
using RestaurantCRM.Application.Inventory;
using RestaurantCRM.Application.Menu;
using RestaurantCRM.Application.Orders;
using RestaurantCRM.Application.Tables;
using RestaurantCRM.Domain.Enums;
using RestaurantCRM.IntegrationTests.Infrastructure;
using Shouldly;

namespace RestaurantCRM.IntegrationTests;

/// <summary>
/// The money path, end to end over HTTP: order → payment → stock deduction →
/// cash ledger → client balance. These flows commit atomically in one
/// SaveChangesAsync, so the assertions double as consistency checks.
/// </summary>
[Collection("Api")]
public class OrderLifecycleTests(ApiFixture fx)
{
    private sealed record Venue(TenantSession S, Guid TableId, Guid MenuItemId, Guid ProductId);

    [Fact]
    public async Task Creating_an_order_occupies_the_table_and_snapshots_prices()
    {
        var v = await SetupVenueAsync(price: 1500m);
        var order = await OpenOrderAsync(v, quantity: 2);

        order.Status.ShouldBe("Open");
        order.Total.ShouldBe(3000m);
        order.Items.Single().Status.ShouldBe("Pending");
        (await v.S.GetAsync<List<TableDto>>("/api/tables")).Single().Status.ShouldBe("Occupied");

        // Raising the menu price must not alter the already-open order (snapshot pattern).
        var item = (await v.S.GetAsync<List<MenuCategoryDto>>("/api/menu")).Single().Items.Single();
        await v.S.PutAsync<MenuItemDto>($"/api/menu/items/{v.MenuItemId}",
            new UpdateMenuItemRequest(item.CategoryId, item.Name, item.Description, 9999m, item.PhotoUrl, item.IsAvailable));

        (await v.S.GetAsync<OrderDto>($"/api/orders/{order.Id}")).Total.ShouldBe(3000m);
    }

    [Fact]
    public async Task Paying_cash_deducts_stock_records_income_and_frees_the_table()
    {
        var v = await SetupVenueAsync(price: 1500m, initialStock: 10m, perPortion: 2m);
        var order = await OpenOrderAsync(v, quantity: 2);

        var paid = await PayAsync(v, order.Id, "Cash");

        paid.Status.ShouldBe("Paid");
        paid.PaymentMethod.ShouldBe("Cash");

        // Recipe: 2 kg per portion × 2 portions = 4 kg gone, in the same transaction.
        (await v.S.GetAsync<ProductDto>($"/api/products/{v.ProductId}")).CurrentStock.ShouldBe(6m);
        var sale = (await v.S.GetAsync<List<StockMovementDto>>($"/api/products/{v.ProductId}/movements"))
            .First(m => m.Type == StockMovementType.Sale);
        sale.QuantityChange.ShouldBe(-4m);
        sale.QuantityAfter.ShouldBe(6m);

        var summary = await v.S.GetAsync<CashRegisterSummaryDto>($"/api/cash-register/summary?{TimeWindow()}");
        summary.IncomeCash.ShouldBe(3000m);
        summary.CashBalance.ShouldBe(3000m);

        (await v.S.GetAsync<List<TableDto>>("/api/tables")).Single().Status.ShouldBe("Free");
    }

    [Fact]
    public async Task Depleting_an_ingredient_auto_disables_dependent_menu_items()
    {
        var v = await SetupVenueAsync(initialStock: 10m, perPortion: 2m);
        var order = await OpenOrderAsync(v, quantity: 5); // consumes exactly all 10 kg

        await PayAsync(v, order.Id, "Cash");

        (await v.S.GetAsync<ProductDto>($"/api/products/{v.ProductId}")).CurrentStock.ShouldBe(0m);
        (await v.S.GetAsync<List<MenuCategoryDto>>("/api/menu"))
            .Single().Items.Single().IsAvailable.ShouldBeFalse();

        // The auto-86'd item can no longer be ordered.
        var response = await v.S.Client.PostAsJsonAsync("/api/orders",
            new CreateOrderRequest(v.TableId, [new AddOrderItemRequest(v.MenuItemId, 1)]), ApiFixture.Json);
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_order_can_only_be_paid_once()
    {
        var v = await SetupVenueAsync();
        var order = await OpenOrderAsync(v, quantity: 1);
        await PayAsync(v, order.Id, "Cash");

        var second = await v.S.Client.PatchAsJsonAsync($"/api/orders/{order.Id}/status",
            new UpdateOrderStatusRequest("Paid", "Cash"), ApiFixture.Json);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cancelling_leaves_stock_and_cash_untouched_but_frees_the_table()
    {
        var v = await SetupVenueAsync(initialStock: 10m);
        var order = await OpenOrderAsync(v, quantity: 2);

        var cancelled = await v.S.PatchAsync<OrderDto>($"/api/orders/{order.Id}/cancel", new { });

        cancelled.Status.ShouldBe("Cancelled");
        (await v.S.GetAsync<ProductDto>($"/api/products/{v.ProductId}")).CurrentStock.ShouldBe(10m);
        (await v.S.GetAsync<CashRegisterSummaryDto>($"/api/cash-register/summary?{TimeWindow()}")).Net.ShouldBe(0m);
        (await v.S.GetAsync<List<TableDto>>("/api/tables")).Single().Status.ShouldBe("Free");
    }

    [Fact]
    public async Task Vip_table_surcharge_is_added_to_the_bill_and_the_charge()
    {
        var v = await SetupVenueAsync(price: 1500m, vip: true, vipAmount: 500m);
        var order = await OpenOrderAsync(v, quantity: 2);

        var bill = await v.S.GetAsync<BillPreviewDto>($"/api/orders/{order.Id}/bill");
        bill.Subtotal.ShouldBe(3000m);
        bill.VipSurcharge.ShouldBe(500m);
        bill.SuggestedCharge.ShouldBe(3500m);

        await PayAsync(v, order.Id, "Cash");
        (await v.S.GetAsync<CashRegisterSummaryDto>($"/api/cash-register/summary?{TimeWindow()}"))
            .IncomeCash.ShouldBe(3500m);
    }

    [Fact]
    public async Task Deposit_balance_absorbs_the_bill_and_is_debited()
    {
        var v = await SetupVenueAsync(price: 1500m);
        var client = await v.S.PostAsync<ClientDto>("/api/clients",
            new CreateClientRequest("Karen Petrosyan", "+37491000001", null, null, null));
        await v.S.PostAsync<ClientDto>($"/api/clients/{client.Id}/deposit",
            new ClientDepositRequest(5000m, PaymentMethod.Cash, null));

        var order = await OpenOrderAsync(v, quantity: 2, client.Id); // bill = 3000
        var paid = await PayAsync(v, order.Id, "Card", useDeposit: true);

        // The balance covered the whole bill → settled method is Deposit, not Card.
        paid.PaymentMethod.ShouldBe("Deposit");
        (await v.S.GetAsync<ClientDto>($"/api/clients/{client.Id}")).DepositBalance.ShouldBe(2000m);

        // Nothing out-of-pocket: no order income; the drawer holds only the earlier cash top-up.
        var summary = await v.S.GetAsync<CashRegisterSummaryDto>($"/api/cash-register/summary?{TimeWindow()}");
        summary.IncomeCard.ShouldBe(0m);
        summary.CashBalance.ShouldBe(5000m);
    }

    [Fact]
    public async Task Cashback_is_earned_only_on_the_out_of_pocket_remainder()
    {
        var v = await SetupVenueAsync(price: 1500m);
        var client = await v.S.PostAsync<ClientDto>("/api/clients",
            new CreateClientRequest("Anna Loyal", "+37491000002", null, null, null, LoyaltyType.Cashback, 10m));
        await v.S.PostAsync<ClientDto>($"/api/clients/{client.Id}/deposit",
            new ClientDepositRequest(1000m, PaymentMethod.Card, null));

        var order = await OpenOrderAsync(v, quantity: 2, client.Id); // bill = 3000
        var paid = await PayAsync(v, order.Id, "Card", useDeposit: true, applyCashback: true);

        // 1000 store credit + 2000 by card; cashback = 10% of the 2000 remainder only.
        paid.PaymentMethod.ShouldBe("Card");
        (await v.S.GetAsync<ClientDto>($"/api/clients/{client.Id}")).DepositBalance.ShouldBe(200m);

        var transactions = await v.S.GetAsync<List<ClientTransactionDto>>($"/api/clients/{client.Id}/transactions");
        transactions.ShouldContain(t => t.Type == ClientTransactionType.OrderPayment && t.Amount == -1000m);
        transactions.ShouldContain(t => t.Type == ClientTransactionType.CashbackEarned && t.Amount == 200m);

        (await v.S.GetAsync<CashRegisterSummaryDto>($"/api/cash-register/summary?{TimeWindow()}"))
            .IncomeCard.ShouldBe(2000m);
    }

    // ---- scenario builders ----

    private async Task<Venue> SetupVenueAsync(
        decimal price = 1500m,
        decimal initialStock = 10m,
        decimal perPortion = 2m,
        bool vip = false,
        decimal vipAmount = 0m)
    {
        var s = await fx.RegisterTenantAsync();
        var table = await s.PostAsync<TableDto>("/api/tables", new CreateTableRequest(1, 4, vip, vipAmount));
        var category = await s.PostAsync<MenuCategoryDto>("/api/menu/categories", new CreateCategoryRequest("Grill"));
        var item = await s.PostAsync<MenuItemDto>("/api/menu/items",
            new CreateMenuItemRequest(category.Id, "Khorovats", null, price, null));
        var product = await s.PostAsync<ProductDto>("/api/products",
            new CreateProductRequest("Pork", "Meat", ProductUnit.Kg, initialStock, 1m, null));
        await s.PutAsync<RecipeDto>($"/api/menu/items/{item.Id}/recipe",
            new SetRecipeRequest([new SetRecipeIngredient(product.Id, perPortion)]));
        return new Venue(s, table.Id, item.Id, product.Id);
    }

    private static Task<OrderDto> OpenOrderAsync(Venue v, int quantity, Guid? clientId = null) =>
        v.S.PostAsync<OrderDto>("/api/orders",
            new CreateOrderRequest(v.TableId, [new AddOrderItemRequest(v.MenuItemId, quantity)], clientId));

    private static Task<OrderDto> PayAsync(
        Venue v, Guid orderId, string method, bool useDeposit = false, bool applyCashback = false) =>
        v.S.PatchAsync<OrderDto>($"/api/orders/{orderId}/status",
            new UpdateOrderStatusRequest("Paid", method, useDeposit, applyCashback));

    private static string TimeWindow() =>
        $"from={Uri.EscapeDataString(DateTime.UtcNow.AddDays(-1).ToString("O"))}" +
        $"&to={Uri.EscapeDataString(DateTime.UtcNow.AddDays(1).ToString("O"))}";
}
