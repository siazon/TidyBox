namespace TidyBox.Modules.ContextMenu.Model;

/// <summary>The places in the registry whose context menu entries this module manages.</summary>
public enum MenuLocation
{
    AllFiles,
    AllFileSystemObjects,
    Directory,
    DirectoryBackground,
    Folder,
    Drive,
}

public static class MenuLocationExtensions
{
    /// <summary>Key name under Software\Classes, e.g. <c>Directory\Background</c>.</summary>
    public static string RegistryKeyName(this MenuLocation location) => location switch
    {
        MenuLocation.AllFiles => "*",
        MenuLocation.AllFileSystemObjects => "AllFilesystemObjects",
        MenuLocation.Directory => "Directory",
        MenuLocation.DirectoryBackground => @"Directory\Background",
        MenuLocation.Folder => "Folder",
        MenuLocation.Drive => "Drive",
        _ => throw new ArgumentOutOfRangeException(nameof(location), location, null),
    };

    public static string DisplayName(this MenuLocation location) => location switch
    {
        MenuLocation.AllFiles => "所有文件",
        MenuLocation.AllFileSystemObjects => "所有文件和文件夹",
        MenuLocation.Directory => "文件夹",
        MenuLocation.DirectoryBackground => "文件夹空白处",
        MenuLocation.Folder => "文件夹和库",
        MenuLocation.Drive => "驱动器",
        _ => throw new ArgumentOutOfRangeException(nameof(location), location, null),
    };
}
