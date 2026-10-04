namespace Amsterfam.Core.Entities;

/// <summary>
/// The qualified handle that addresses a user, e.g. "klaus@discord": the login-provider handle
/// plus the source it came from, or just the handle while the source isn't known yet.
/// </summary>
public static class UserHandle
{
    public static string Format(string handle, string? authSource) =>
        authSource is null ? handle : $"{handle}@{authSource}";

    /// <summary>
    /// Splits on the last "@". Source slugs never contain one, but handles can (an internal
    /// account may use an email address as its username). Returns null as the source when
    /// there is no "@" to split on.
    /// </summary>
    public static (string Handle, string? AuthSource) Parse(string qualified)
    {
        var at = qualified.LastIndexOf('@');
        return at <= 0 || at == qualified.Length - 1
            ? (qualified, null)
            : (qualified[..at], qualified[(at + 1)..]);
    }
}
