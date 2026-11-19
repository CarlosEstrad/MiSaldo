using MiSaldo.Application.Accounts;
using MiSaldo.Application.Common;
using Xunit;

namespace MiSaldo.Application.Tests.Accounts;

public class RegistrationValidatorTests
{
    private static RegisterUserCommand Valid() => new(
        Email: "ana@example.com",
        Password: "a long enough password",
        FullName: "Ana Pérez",
        BirthDate: new DateOnly(1990, 5, 1),
        Country: "CO",
        Gender: null,
        AcceptsPrivacyPolicy: true,
        AcceptsTerms: true);

    [Fact]
    public void A_complete_command_is_valid()
    {
        Assert.True(RegistrationValidator.Validate(Valid()).IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign.com")]
    [InlineData("@example.com")]
    [InlineData("ana@")]
    [InlineData("ana@@example.com")]
    [InlineData("ana@example")]
    [InlineData("ana@example.")]
    [InlineData("ana maria@example.com")]
    public void An_implausible_email_is_rejected(string email)
    {
        var result = RegistrationValidator.Validate(Valid() with { Email = email });
        AssertFails(result, RegistrationValidator.InvalidEmailCode);
    }

    [Fact]
    public void An_email_longer_than_320_characters_is_rejected()
    {
        var email = new string('a', 310) + "@example.com";
        AssertFails(RegistrationValidator.Validate(Valid() with { Email = email }), RegistrationValidator.InvalidEmailCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_blank_name_is_rejected(string name)
    {
        AssertFails(RegistrationValidator.Validate(Valid() with { FullName = name }), RegistrationValidator.InvalidNameCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("C")]
    [InlineData("COL")]
    [InlineData("C1")]
    public void A_country_that_is_not_two_letters_is_rejected(string country)
    {
        AssertFails(RegistrationValidator.Validate(Valid() with { Country = country }), RegistrationValidator.InvalidCountryCode);
    }

    [Fact]
    public void A_gender_longer_than_the_limit_is_rejected()
    {
        var command = Valid() with { Gender = new string('x', RegistrationValidator.MaxGenderLength + 1) };
        AssertFails(RegistrationValidator.Validate(command), RegistrationValidator.InvalidGenderCode);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void Both_consents_are_required(bool privacy, bool terms)
    {
        var command = Valid() with { AcceptsPrivacyPolicy = privacy, AcceptsTerms = terms };
        AssertFails(RegistrationValidator.Validate(command), RegistrationValidator.ConsentRequiredCode);
    }

    // RNF-SEG-09
    [Fact]
    public void A_short_password_is_rejected()
    {
        AssertFails(RegistrationValidator.Validate(Valid() with { Password = "short" }), PasswordPolicy.TooShortCode);
    }

    [Fact]
    public void Emails_are_normalized_to_trimmed_lower_case()
    {
        Assert.Equal("ana@example.com", RegistrationValidator.NormalizeEmail("  Ana@Example.COM "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("es")]
    [InlineData("en")]
    [InlineData(" EN ")]
    public void The_language_is_optional_and_case_insensitive(string? language)
    {
        Assert.True(RegistrationValidator.Validate(Valid() with { Language = language }).IsSuccess);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("")]
    [InlineData("español")]
    public void An_unsupported_language_is_rejected(string language)
    {
        AssertFails(RegistrationValidator.Validate(Valid() with { Language = language }), RegistrationValidator.InvalidLanguageCode);
    }

    [Theory]
    [InlineData("EN", "en")]
    [InlineData(" es ", "es")]
    [InlineData("pt", null)]
    [InlineData(null, null)]
    public void Languages_are_normalized_to_lower_case(string? language, string? expected)
    {
        Assert.Equal(expected, RegistrationValidator.NormalizeLanguage(language));
    }

    private static void AssertFails(Result result, string code)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }
}
