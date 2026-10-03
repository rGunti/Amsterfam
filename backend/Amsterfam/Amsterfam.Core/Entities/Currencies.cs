namespace Amsterfam.Core.Entities;

/// <summary>
/// The currencies an event can use. All have two minor digits, matching how amounts are
/// stored. Keep in sync with <c>frontend/src/app/shared/money.ts</c>.
/// </summary>
public static class Currencies
{
    public const string Default = "EUR";

    public static readonly IReadOnlyList<string> Supported =
    [
        "EUR",
        "GBP",
        "CHF",
        "USD",
        "CAD",
        "AUD",
        "NZD",
        "SEK",
        "DKK",
        "NOK",
        "CZK",
        "PLN",
        "HUF",
        "RON",
        "BGN",
        "TRY",
    ];

    public static bool IsSupported(string? code) => code is not null && Supported.Contains(code);
}
