namespace MiSaldo.Application.Common;

/// <summary>
/// Records print every property in their generated <c>ToString</c>, and
/// frameworks log that text (MVC logs action arguments at Trace). Types that
/// carry passwords, tokens or emails override <c>ToString</c> with this, so no
/// log level can reveal them (RNF-SEG-10, R14.4).
/// </summary>
public static class Redaction
{
    public static string Of(string typeName) => $"{typeName} {{ redacted }}";
}
