using FluentValidation.TestHelper;
using RestaurantCRM.Application.Orders;

namespace RestaurantCRM.UnitTests.Validators;

public class CreateOrderRequestValidatorTests
{
    private readonly CreateOrderRequestValidator _validator = new();

    private static CreateOrderRequest Valid() =>
        new(Guid.NewGuid(), [new AddOrderItemRequest(Guid.NewGuid(), 2)]);

    [Fact]
    public void Valid_request_passes()
        => _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Order_without_items_is_rejected()
        => _validator.TestValidate(Valid() with { Items = [] })
            .ShouldHaveValidationErrorFor(x => x.Items);

    [Fact]
    public void Empty_table_id_is_rejected()
        => _validator.TestValidate(Valid() with { TableId = Guid.Empty })
            .ShouldHaveValidationErrorFor(x => x.TableId);

    [Fact]
    public void Invalid_nested_item_is_rejected()
        => _validator.TestValidate(Valid() with { Items = [new AddOrderItemRequest(Guid.NewGuid(), 0)] })
            .ShouldHaveValidationErrorFor("Items[0].Quantity");
}

public class AddOrderItemRequestValidatorTests
{
    private readonly AddOrderItemRequestValidator _validator = new();

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(99)]
    public void Quantity_within_1_to_99_passes(int quantity)
        => _validator.TestValidate(new AddOrderItemRequest(Guid.NewGuid(), quantity))
            .ShouldNotHaveValidationErrorFor(x => x.Quantity);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100)]
    public void Quantity_outside_1_to_99_fails(int quantity)
        => _validator.TestValidate(new AddOrderItemRequest(Guid.NewGuid(), quantity))
            .ShouldHaveValidationErrorFor(x => x.Quantity);

    [Fact]
    public void Notes_longer_than_500_chars_fail()
        => _validator.TestValidate(new AddOrderItemRequest(Guid.NewGuid(), 1, new string('x', 501)))
            .ShouldHaveValidationErrorFor(x => x.Notes);

    [Fact]
    public void Null_notes_are_allowed()
        => _validator.TestValidate(new AddOrderItemRequest(Guid.NewGuid(), 1, null))
            .ShouldNotHaveAnyValidationErrors();
}

public class UpdateOrderStatusRequestValidatorTests
{
    private readonly UpdateOrderStatusRequestValidator _validator = new();

    [Theory]
    [InlineData("Cash")]
    [InlineData("Card")]
    [InlineData("BankTransfer")]
    [InlineData("Deposit")]
    [InlineData("Other")]
    public void Paid_with_known_payment_method_passes(string method)
        => _validator.TestValidate(new UpdateOrderStatusRequest("Paid", method))
            .ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("Open")]
    [InlineData("Cancelled")]
    [InlineData("")]
    public void Only_Paid_is_accepted_as_target_status(string status)
        => _validator.TestValidate(new UpdateOrderStatusRequest(status, "Cash"))
            .ShouldHaveValidationErrorFor(x => x.Status);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bitcoin")]
    public void Unknown_or_missing_payment_method_fails(string? method)
        => _validator.TestValidate(new UpdateOrderStatusRequest("Paid", method))
            .ShouldHaveValidationErrorFor(x => x.PaymentMethod);
}

public class UpdateOrderItemStatusRequestValidatorTests
{
    private readonly UpdateOrderItemStatusRequestValidator _validator = new();

    [Theory]
    [InlineData("Pending")]
    [InlineData("Preparing")]
    [InlineData("Ready")]
    [InlineData("Served")]
    public void Known_kitchen_statuses_pass(string status)
        => _validator.TestValidate(new UpdateOrderItemStatusRequest(status))
            .ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("Delivered")]
    [InlineData("paid")]
    [InlineData("")]
    public void Unknown_statuses_fail(string status)
        => _validator.TestValidate(new UpdateOrderItemStatusRequest(status))
            .ShouldHaveValidationErrorFor(x => x.Status);
}
