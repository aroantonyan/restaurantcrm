using FluentValidation;
using RestaurantCRM.Domain.Enums;

namespace RestaurantCRM.Application.Menu;

public class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500).When(x => x.Description is not null);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Station)
            .Must(s => Enum.TryParse<Station>(s, true, out _))
            .WithMessage("Station must be Kitchen or Bar.");
    }
}

public class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500).When(x => x.Description is not null);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Station)
            .Must(s => Enum.TryParse<Station>(s!, true, out _))
            .When(x => x.Station is not null)
            .WithMessage("Station must be Kitchen or Bar.");
    }
}

public class CreateMenuItemRequestValidator : AbstractValidator<CreateMenuItemRequest>
{
    public CreateMenuItemRequestValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
        RuleFor(x => x.Price).GreaterThan(0);
        RuleFor(x => x.PhotoUrl).MaximumLength(1000).When(x => x.PhotoUrl is not null);
    }
}

public class UpdateMenuItemRequestValidator : AbstractValidator<UpdateMenuItemRequest>
{
    public UpdateMenuItemRequestValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
        RuleFor(x => x.Price).GreaterThan(0);
        RuleFor(x => x.PhotoUrl).MaximumLength(1000).When(x => x.PhotoUrl is not null);
    }
}

public class SetRecipeRequestValidator : AbstractValidator<SetRecipeRequest>
{
    public SetRecipeRequestValidator()
    {
        RuleFor(x => x.Ingredients).NotNull();
        RuleForEach(x => x.Ingredients).ChildRules(ingredient =>
        {
            ingredient.RuleFor(i => i.ProductId).NotEmpty();
            ingredient.RuleFor(i => i.Quantity)
                .GreaterThan(0).WithMessage("Recipe quantity must be greater than zero.");
        });

        // Reject duplicate product IDs in the same payload — the unique index would also
        // catch this, but rejecting client-side gives a friendlier error message.
        RuleFor(x => x.Ingredients)
            .Must(list => list.Select(i => i.ProductId).Distinct().Count() == list.Count)
            .WithMessage("A product can appear in a recipe only once.");
    }
}
