using System.Net;
using RestaurantCRM.Application.Inventory;
using RestaurantCRM.Application.Menu;
using RestaurantCRM.Application.Tables;
using RestaurantCRM.Domain.Enums;
using RestaurantCRM.IntegrationTests.Infrastructure;
using Shouldly;

namespace RestaurantCRM.IntegrationTests;

/// <summary>
/// The core promise of the multi-tenant design: restaurant B can never observe
/// restaurant A's data, even with a valid token, because the EF global query
/// filter scopes every read to the caller's restaurantId claim.
/// </summary>
[Collection("Api")]
public class TenantIsolationTests(ApiFixture fx)
{
    [Fact]
    public async Task Tenants_never_see_each_others_lists()
    {
        var a = await fx.RegisterTenantAsync();
        var b = await fx.RegisterTenantAsync();

        await a.PostAsync<TableDto>("/api/tables", new CreateTableRequest(1));
        var category = await a.PostAsync<MenuCategoryDto>("/api/menu/categories", new CreateCategoryRequest("Grill"));
        await a.PostAsync<MenuItemDto>("/api/menu/items",
            new CreateMenuItemRequest(category.Id, "Khorovats", null, 3500m, null));

        (await a.GetAsync<List<TableDto>>("/api/tables")).Count.ShouldBe(1);
        (await b.GetAsync<List<TableDto>>("/api/tables")).ShouldBeEmpty();
        (await b.GetAsync<List<MenuCategoryDto>>("/api/menu")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Cross_tenant_lookup_by_id_returns_404()
    {
        var a = await fx.RegisterTenantAsync();
        var b = await fx.RegisterTenantAsync();
        var product = await a.PostAsync<ProductDto>("/api/products",
            new CreateProductRequest("Pork", "Meat", ProductUnit.Kg, 10m, 1m, null));

        // 404 rather than 403: an outsider can't even confirm the resource exists.
        var response = await b.Client.GetAsync($"/api/products/{product.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Cross_tenant_mutation_by_id_returns_404()
    {
        var a = await fx.RegisterTenantAsync();
        var b = await fx.RegisterTenantAsync();
        var table = await a.PostAsync<TableDto>("/api/tables", new CreateTableRequest(2));

        var response = await b.Client.DeleteAsync($"/api/tables/{table.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await a.GetAsync<List<TableDto>>("/api/tables")).Count.ShouldBe(1);
    }
}
