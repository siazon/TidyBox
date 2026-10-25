using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PowerTools.ElevatedHelper;

/// <summary>Keeps the quarantine folders out of reach of ordinary users, who can otherwise create folders under ProgramData first.</summary>
internal static class QuarantineFolder
{
    /// <summary>
    /// Makes sure <paramref name="path"/> is a real folder owned by a trusted account and open only to the trusted accounts.
    /// An existing folder that someone else owns is refused rather than taken over. Returns an error message, or null when it is safe.
    /// </summary>
    public static string? Secure(string path, QuarantineTrust trust)
    {
        try
        {
            var info = new DirectoryInfo(path);
            if (!info.Exists) info.Create(Rules(trust, trust.Owner));
            info.Refresh();

            // Checked after creating as well: someone may have created it between the two steps.
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return $"{path} is a link, not a folder";

            var owner = info.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            if (owner is null || !trust.FullControl.Contains(owner)) return $"{path} belongs to {owner?.Value ?? "an unknown account"}, so it is not trusted";

            info.SetAccessControl(Rules(trust, null));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return $"could not secure {path}: {ex.Message}";
        }
    }

    /// <summary>Creates a folder inside the quarantine and refuses it when it turns out to be a link planted in advance.</summary>
    public static string? CreateInside(string path)
    {
        var info = Directory.CreateDirectory(path);
        return info.Attributes.HasFlag(FileAttributes.ReparsePoint) ? $"{path} is a link, not a folder" : null;
    }

    private static DirectorySecurity Rules(QuarantineTrust trust, SecurityIdentifier? owner)
    {
        var security = new DirectorySecurity();
        if (owner is not null) security.SetOwner(owner);

        // No inheritance from the parent: ProgramData lets every user create things there.
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in trust.FullControl)
        {
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        }

        return security;
    }
}
