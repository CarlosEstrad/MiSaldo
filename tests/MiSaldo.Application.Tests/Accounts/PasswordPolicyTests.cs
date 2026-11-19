using MiSaldo.Application.Accounts;
using MiSaldo.Application.Common;
using Xunit;

namespace MiSaldo.Application.Tests.Accounts;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("short")]
    [InlineData("123456789")] // 9 characters
    public void Password_below_ten_characters_is_rejected(string? password)
    {
        var result = PasswordPolicy.Validate(password);

        Assert.True(result.IsFailure);
        Assert.Equal(PasswordPolicy.TooShortCode, result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Theory]
    [InlineData("1234567890")] // exactly 10
    [InlineData("a much longer passphrase")]
    public void Password_of_ten_or_more_characters_is_accepted(string password)
    {
        Assert.True(PasswordPolicy.Validate(password).IsSuccess);
    }
}
