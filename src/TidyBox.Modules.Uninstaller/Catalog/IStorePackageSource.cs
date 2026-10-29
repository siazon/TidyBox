using System.Runtime.InteropServices;
using Windows.Management.Deployment;

namespace TidyBox.Modules.Uninstaller.Catalog;

public record StorePackage(
    string FullName,
    string DisplayName,
    string? Publisher,
    string? Version,
    DateTime? InstalledOn,
    string? InstallLocation,
    bool IsFramework,
    bool IsResource,
    bool IsSystem);

public interface IStorePackageSource
{
    IReadOnlyList<StorePackage> FindForCurrentUser();
}

/// <summary>Packages installed for the current user, straight from the system's PackageManager.</summary>
public sealed class PackageManagerSource : IStorePackageSource
{
    public IReadOnlyList<StorePackage> FindForCurrentUser()
    {
        var result = new List<StorePackage>();
        foreach (var package in new PackageManager().FindPackagesForUser(""))
        {
            try
            {
                var id = package.Id;
                var display = package.DisplayName;
                result.Add(new StorePackage(
                    FullName: id.FullName,
                    DisplayName: string.IsNullOrWhiteSpace(display) ? id.Name : display,
                    Publisher: NullIfEmpty(package.PublisherDisplayName),
                    Version: $"{id.Version.Major}.{id.Version.Minor}.{id.Version.Build}.{id.Version.Revision}",
                    InstalledOn: package.InstalledDate.LocalDateTime,
                    InstallLocation: SafeLocation(package),
                    IsFramework: package.IsFramework,
                    IsResource: package.IsResourcePackage,
                    IsSystem: package.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.System));
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or FileNotFoundException)
            {
                // A package that cannot be described (being serviced, access denied) is left out; the rest still list.
            }
        }

        return result;
    }

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static string? SafeLocation(Windows.ApplicationModel.Package package)
    {
        try
        {
            return package.InstalledLocation?.Path;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or FileNotFoundException)
        {
            return null;
        }
    }
}
