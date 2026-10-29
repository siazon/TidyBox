using Microsoft.VisualBasic.FileIO;

namespace TidyBox.Core.Recycling;

/// <summary>Sends a single file to the Recycle Bin. Kept apart from <see cref="IRecycleBin"/>, which handles whole folders and emptying.</summary>
public interface IFileRecycler
{
    void Delete(string filePath);
}

/// <summary>
/// Moves a file to the Recycle Bin so the user can still restore it. Not unit-tested against the real Recycle Bin (that would
/// put files into the developer's or CI machine's bin); callers are tested against a fake <see cref="IFileRecycler"/> instead.
/// </summary>
public sealed class ShellFileRecycler : IFileRecycler
{
    public void Delete(string filePath)
        => FileSystem.DeleteFile(filePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
}
