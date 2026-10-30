using MiSaldo.Domain.Common;
using Xunit;

namespace MiSaldo.Domain.Tests.Common;

public class CurrencyTests
{
    [Fact]
    public void Cop_has_zero_decimal_places()
    {
        Assert.Equal("COP", Currency.Cop.Code);
        Assert.Equal(0, Currency.Cop.DecimalPlaces);
    }

    [Fact]
    public void Usd_has_two_decimal_places()
    {
        Assert.Equal("USD", Currency.Usd.Code);
        Assert.Equal(2, Currency.Usd.DecimalPlaces);
    }

    [Fact]
    public void Code_is_normalized_to_uppercase()
    {
        var currency = new Currency("eur", 2);
        Assert.Equal("EUR", currency.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_code_is_rejected(string? code)
    {
        Assert.Throws<ArgumentException>(() => new Currency(code!, 2));
    }

    [Theory]
    [InlineData("US")]
    [InlineData("USDD")]
    public void Non_three_letter_code_is_rejected(string code)
    {
        Assert.Throws<ArgumentException>(() => new Currency(code, 2));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void Out_of_range_decimal_places_is_rejected(int decimals)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Currency("COP", decimals));
    }
}
