using Microsoft.EntityFrameworkCore;
using RestaurantCRM.Application.ActivityLog;
using RestaurantCRM.Application.Common.Interfaces;
using RestaurantCRM.Application.Menu;
using RestaurantCRM.Domain.Entities;
using RestaurantCRM.Domain.Enums;
using RestaurantCRM.Infrastructure.Persistence;

namespace RestaurantCRM.Infrastructure.Services;

public class MenuService(AppDbContext db, ITenantContext tenant, IActivityLogService activityLog) : IMenuService
{
    public async Task<List<MenuCategoryDto>> GetAllAsync(CancellationToken ct = default)
    {
        var categories = await db.MenuCategories
            .Include(c => c.Items)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(ct);

        // Compute canFulfill per item by checking each recipe ingredient against current stock.
        // Single extra query loads ALL recipes + product stocks for the whole catalog —
        // O(1) round-trips regardless of menu size. Menus are tiny (~100 items typical), so
        // in-memory join is cheaper than a per-item subquery.
        var allItemIds = categories.SelectMany(c => c.Items).Select(i => i.Id).ToList();
        var canFulfillByItemId = await ComputeCanFulfillAsync(allItemIds, ct);

        return categories.Select(c => ToDto(c, canFulfillByItemId)).ToList();
    }

    /// <summary>
    /// For each menu item: true if every ingredient has at least its per-portion quantity
    /// in stock. Items without a recipe default to true (we don't track them).
    /// </summary>
    private async Task<Dictionary<Guid, bool>> ComputeCanFulfillAsync(List<Guid> itemIds, CancellationToken ct)
    {
        if (itemIds.Count == 0) return [];

        var recipeRows = await db.MenuItemRecipes
            .Include(r => r.Product)
            .Where(r => itemIds.Contains(r.MenuItemId))
            .Select(r => new { r.MenuItemId, r.Quantity, ProductStock = r.Product.CurrentStock })
            .ToListAsync(ct);

        var byItem = recipeRows.GroupBy(r => r.MenuItemId);
        var trackedItems = new HashSet<Guid>(byItem.Select(g => g.Key));

        var result = new Dictionary<Guid, bool>();
        foreach (var id in itemIds)
            result[id] = !trackedItems.Contains(id); // default true for untracked items

        foreach (var group in byItem)
            result[group.Key] = group.All(r => r.ProductStock >= r.Quantity);

        return result;
    }

    public async Task<MenuCategoryDto> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct = default)
    {
        var category = new MenuCategory
        {
            RestaurantId = tenant.RestaurantId,
            Name = request.Name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            SortOrder = request.SortOrder,
            Station = Enum.Parse<Station>(request.Station, ignoreCase: true),
        };
        db.MenuCategories.Add(category);
        await db.SaveChangesAsync(ct);
        category.Items = [];
        return ToDto(category);
    }

    public async Task<MenuCategoryDto> UpdateCategoryAsync(Guid id, UpdateCategoryRequest request, CancellationToken ct = default)
    {
        var category = await db.MenuCategories.Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException("Category not found.");

        category.Name = request.Name;
        category.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        category.SortOrder = request.SortOrder;
        if (request.Station is not null)
            category.Station = Enum.Parse<Station>(request.Station, ignoreCase: true);
        await db.SaveChangesAsync(ct);
        return ToDto(category);
    }

    public async Task DeleteCategoryAsync(Guid id, CancellationToken ct = default)
    {
        var category = await db.MenuCategories.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException("Category not found.");
        db.MenuCategories.Remove(category);
        await db.SaveChangesAsync(ct);
    }

    public async Task<MenuItemDto> CreateItemAsync(CreateMenuItemRequest request, CancellationToken ct = default)
    {
        var category = await db.MenuCategories.FirstOrDefaultAsync(c => c.Id == request.CategoryId, ct)
            ?? throw new KeyNotFoundException("Category not found.");

        var item = new MenuItem
        {
            // Always source from the tenant context — never derive from a lookup,
            // because IgnoreQueryFilters() could one day expose another tenant's row.
            RestaurantId = tenant.RestaurantId,
            CategoryId = category.Id,
            Name = request.Name,
            Description = request.Description,
            Price = request.Price,
            PhotoUrl = request.PhotoUrl,
            IsAvailable = request.IsAvailable,
        };
        db.MenuItems.Add(item);
        await db.SaveChangesAsync(ct);
        item.Category = category;

        await activityLog.LogAsync(tenant.UserId, ActivityCategory.Menu, "ItemCreated", nameof(MenuItem), item.Id,
            $"Menu item '{item.Name}' created at {item.Price:N2}", ct);

        return ToItemDto(item);
    }

    public async Task<MenuItemDto> UpdateItemAsync(Guid id, UpdateMenuItemRequest request, CancellationToken ct = default)
    {
        var item = await db.MenuItems.Include(i => i.Category)
            .FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new KeyNotFoundException("Menu item not found.");

        if (item.CategoryId != request.CategoryId)
        {
            var newCategory = await db.MenuCategories.FirstOrDefaultAsync(c => c.Id == request.CategoryId, ct)
                ?? throw new KeyNotFoundException("Category not found.");
            item.CategoryId = newCategory.Id;
            item.Category = newCategory;
        }

        var priceChanged = item.Price != request.Price;
        var oldPrice = item.Price;

        item.Name = request.Name;
        item.Description = request.Description;
        item.Price = request.Price;
        item.PhotoUrl = request.PhotoUrl;
        item.IsAvailable = request.IsAvailable;
        await db.SaveChangesAsync(ct);

        var desc = priceChanged
            ? $"Menu item '{item.Name}' updated — price {oldPrice:N2} → {item.Price:N2}"
            : $"Menu item '{item.Name}' updated";
        await activityLog.LogAsync(tenant.UserId, ActivityCategory.Menu, "ItemUpdated", nameof(MenuItem), item.Id, desc, ct);

        return ToItemDto(item);
    }

    public async Task<MenuItemDto> ToggleAvailabilityAsync(Guid id, CancellationToken ct = default)
    {
        var item = await db.MenuItems.Include(i => i.Category)
            .FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new KeyNotFoundException("Menu item not found.");

        item.IsAvailable = !item.IsAvailable;
        await db.SaveChangesAsync(ct);
        return ToItemDto(item);
    }

    public async Task DeleteItemAsync(Guid id, CancellationToken ct = default)
    {
        // Recipe rows reference this menu item with Restrict — clear them first
        // so a v1 catalog cleanup doesn't require a separate "remove ingredients" round-trip.
        var item = await db.MenuItems.FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new KeyNotFoundException("Menu item not found.");

        var recipeRows = await db.MenuItemRecipes.Where(r => r.MenuItemId == id).ToListAsync(ct);
        if (recipeRows.Count > 0) db.MenuItemRecipes.RemoveRange(recipeRows);

        var nameSnapshot = item.Name;
        db.MenuItems.Remove(item);
        await db.SaveChangesAsync(ct);

        await activityLog.LogAsync(tenant.UserId, ActivityCategory.Menu, "ItemDeleted", nameof(MenuItem), id,
            $"Menu item '{nameSnapshot}' deleted", ct);
    }

    // ---- Recipe (BOM) ----

    public async Task<RecipeDto> GetRecipeAsync(Guid menuItemId, CancellationToken ct = default)
    {
        var item = await db.MenuItems.FirstOrDefaultAsync(i => i.Id == menuItemId, ct)
            ?? throw new KeyNotFoundException("Menu item not found.");

        var ingredients = await db.MenuItemRecipes
            .Include(r => r.Product)
            .Where(r => r.MenuItemId == menuItemId)
            .OrderBy(r => r.Product.Name)
            .Select(r => new RecipeIngredientDto(
                r.ProductId,
                r.Product.Name,
                r.Product.Unit.ToString(),
                r.Quantity))
            .ToListAsync(ct);

        return new RecipeDto(item.Id, item.Name, ingredients);
    }

    public async Task<RecipeDto> SetRecipeAsync(Guid menuItemId, SetRecipeRequest request, CancellationToken ct = default)
    {
        var item = await db.MenuItems.FirstOrDefaultAsync(i => i.Id == menuItemId, ct)
            ?? throw new KeyNotFoundException("Menu item not found.");

        // Verify every product exists and isn't archived — fail fast with a clear message
        // instead of letting EF throw a generic FK violation later.
        var productIds = request.Ingredients.Select(i => i.ProductId).ToList();
        var foundProducts = await db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        foreach (var ing in request.Ingredients)
        {
            if (!foundProducts.TryGetValue(ing.ProductId, out var p))
                throw new KeyNotFoundException($"Product {ing.ProductId} not found.");
            if (p.IsArchived)
                throw new InvalidOperationException($"Product '{p.Name}' is archived and cannot be used in a recipe.");
        }

        // Reconcile: delete-all + add-all is fine here because recipes are small (typically
        // <20 rows) and rare-to-edit. The whole operation runs in one SaveChangesAsync.
        var existing = await db.MenuItemRecipes
            .Where(r => r.MenuItemId == menuItemId)
            .ToListAsync(ct);
        db.MenuItemRecipes.RemoveRange(existing);

        foreach (var ing in request.Ingredients)
        {
            db.MenuItemRecipes.Add(new MenuItemRecipe
            {
                RestaurantId = tenant.RestaurantId,
                MenuItemId = menuItemId,
                ProductId = ing.ProductId,
                Quantity = ing.Quantity,
            });
        }

        await db.SaveChangesAsync(ct);

        return await GetRecipeAsync(menuItemId, ct);
    }

    // ---- helpers ----

    private static MenuCategoryDto ToDto(MenuCategory c, IReadOnlyDictionary<Guid, bool>? canFulfillByItemId = null) =>
        new(c.Id, c.Name, c.Description, c.SortOrder, c.Station.ToString(), c.Items.Select(i => ToItemDto(i, canFulfillByItemId)).ToList());

    private static MenuItemDto ToItemDto(MenuItem i, IReadOnlyDictionary<Guid, bool>? canFulfillByItemId = null)
    {
        var canFulfill = canFulfillByItemId is null || !canFulfillByItemId.TryGetValue(i.Id, out var cf) || cf;
        return new(i.Id, i.CategoryId, i.Category.Name, i.Name, i.Description, i.Price, i.PhotoUrl, i.IsAvailable, canFulfill);
    }
}
