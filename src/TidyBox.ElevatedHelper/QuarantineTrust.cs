using System.Collections.Generic;
using System.Security.Principal;

namespace TidyBox.ElevatedHelper;

/// <summary>
/// Who may own and control the quarantine folders. The helper runs elevated and writes into them, so anyone else who can
/// change what is inside could swap a folder for a link and steer the helper to a place of their choosing.
/// </summary>
/// <param name="Owner">Owner given to folders the helper creates.</param>
/// <param name="FullControl">The only principals with access; an existing folder owned by anyone else is refused.</param>
public sealed record QuarantineTrust(SecurityIdentifier Owner, IReadOnlyList<SecurityIdentifier> FullControl)
{
    public static QuarantineTrust Machine { get; } = new(
        new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
        new[]
        {
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
        });
}
