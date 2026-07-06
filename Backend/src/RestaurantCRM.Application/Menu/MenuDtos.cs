namespace RestaurantCRM.Application.Menu;

public record MenuCategoryDto(
    Guid Id,
    string Name,
    string? Description,
    int SortOrder,
    // KDS prep station this category routes to: "Kitchen" | "Bar".
    string Station,
    List<MenuItemDto> Items
);

public record MenuItemDto(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    string Name,
    string? Description,
    decimal Price,
    string? PhotoUrl,
    bool IsAvailable,
    // True when every recipe ingredient has enough stock for at least one portion.
    // Items without a recipe default to true (we don't track their consumption).
    bool CanFulfill = true
);

public record CreateCategoryRequest(string Name, string? Description = null, int SortOrder = 0, string Station = "Kitchen");

// Station is optional on update: null means "leave unchanged", so clients running
// an older bundle can't silently reset a category's routing.
public record UpdateCategoryRequest(string Name, string? Description, int SortOrder, string? Station = null);

public record CreateMenuItemRequest(
    Guid CategoryId,
    string Name,
    string? Description,
    decimal Price,
    string? PhotoUrl,
    bool IsAvailable = true
);

public record UpdateMenuItemRequest(
    Guid CategoryId,
    string Name,
    string? Description,
    decimal Price,
    string? PhotoUrl,
    bool IsAvailable
);

// ---- Recipe (BOM) ----

/// <summary>
/// One ingredient row of a menu item's recipe. ProductUnit is included for UI display only —
/// recipe quantities are always expressed in the product's own unit.
/// </summary>
public record RecipeIngredientDto(
    Guid ProductId,
    string ProductName,
    string ProductUnit,
    decimal Quantity);

public record RecipeDto(
    Guid MenuItemId,
    string MenuItemName,
    List<RecipeIngredientDto> Ingredients);

/// <summary>
/// Whole-recipe replace semantics — the request carries the full list of ingredients
/// and the server reconciles add/update/remove against the existing rows. Simpler for
/// clients than emitting per-row CRUD calls.
/// </summary>
public record SetRecipeRequest(List<SetRecipeIngredient> Ingredients);

public record SetRecipeIngredient(Guid ProductId, decimal Quantity);
