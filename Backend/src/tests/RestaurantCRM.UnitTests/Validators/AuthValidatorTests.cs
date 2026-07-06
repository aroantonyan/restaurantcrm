using FluentValidation.TestHelper;
using RestaurantCRM.Application.Auth;

namespace RestaurantCRM.UnitTests.Validators;

public class RegisterRequestValidatorTests
{
    private readonly RegisterRequestValidator _validator = new();

    private static RegisterRequest Valid() =>
        new("Aro", "Antonyan", "Samvel", "aro@test.am", "secret123", "Tsirani");

    [Fact]
    public void Valid_request_passes()
        => _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("missing@tld@double.am")]
    public void Malformed_email_fails(string email)
        => _validator.TestValidate(Valid() with { Email = email })
            .ShouldHaveValidationErrorFor(x => x.Email);

    [Fact]
    public void Password_shorter_than_6_chars_fails()
        => _validator.TestValidate(Valid() with { Password = "12345" })
            .ShouldHaveValidationErrorFor(x => x.Password);

    [Fact]
    public void Names_are_capped_at_entity_column_lengths()
    {
        // Validator max lengths must mirror the EF column config (100 / 200),
        // otherwise the DB would throw instead of returning a clean 400.
        var result = _validator.TestValidate(Valid() with
        {
            FirstName = new string('a', 101),
            RestaurantName = new string('r', 201),
        });

        result.ShouldHaveValidationErrorFor(x => x.FirstName);
        result.ShouldHaveValidationErrorFor(x => x.RestaurantName);
    }

    [Fact]
    public void Every_field_is_required()
    {
        var result = _validator.TestValidate(new RegisterRequest("", "", "", "", "", ""));

        result.ShouldHaveValidationErrorFor(x => x.FirstName);
        result.ShouldHaveValidationErrorFor(x => x.LastName);
        result.ShouldHaveValidationErrorFor(x => x.FatherName);
        result.ShouldHaveValidationErrorFor(x => x.Email);
        result.ShouldHaveValidationErrorFor(x => x.Password);
        result.ShouldHaveValidationErrorFor(x => x.RestaurantName);
    }
}

public class LoginRequestValidatorTests
{
    private readonly LoginRequestValidator _validator = new();

    [Fact]
    public void Valid_credentials_shape_passes()
        => _validator.TestValidate(new LoginRequest("aro@test.am", "secret123"))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Malformed_email_fails()
        => _validator.TestValidate(new LoginRequest("nope", "secret123"))
            .ShouldHaveValidationErrorFor(x => x.Email);

    [Fact]
    public void Empty_password_fails()
        => _validator.TestValidate(new LoginRequest("aro@test.am", ""))
            .ShouldHaveValidationErrorFor(x => x.Password);
}

public class ChangePasswordRequestValidatorTests
{
    private readonly ChangePasswordRequestValidator _validator = new();

    [Fact]
    public void Valid_request_passes()
        => _validator.TestValidate(new ChangePasswordRequest("old-secret", "new-secret"))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void New_password_shorter_than_6_chars_fails()
        => _validator.TestValidate(new ChangePasswordRequest("old-secret", "12345"))
            .ShouldHaveValidationErrorFor(x => x.NewPassword);
}
