using Amsterfam.Core.Entities;

namespace Amsterfam.Api.Services;

/// <summary>
/// A new user's handle is already taken by someone else from the same sign-in source, so
/// their account can't be created. Accepted risk for now (#78): Authentik keeps usernames
/// unique, so this needs a rename racing a first sign-in. Until it actually happens it
/// surfaces as a 500; then decide how to handle it (a friendly error, or suffixing the
/// handle). An existing user hitting the same clash on resync keeps their old handle instead.
/// </summary>
public class HandleTakenException(string handle, string? authSource, Exception? inner = null)
    : Exception($"Handle {UserHandle.Format(handle, authSource)} is already taken.", inner)
{
    public string ProfileHandle { get; } = UserHandle.Format(handle, authSource);
}
