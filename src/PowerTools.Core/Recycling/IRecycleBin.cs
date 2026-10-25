using Microsoft.VisualBasic.FileIO;

namespace PowerTools.Core.Recycling;

public interface IRecycleBin
{
    void Delete(string folderPath);
}

/// <summary>Moves a folder to the Recycle Bin so the user can still restore it.</summary>
public sealed class ShellRecycleBin : IRecycleBin
{
    public void Delete(string folderPath)
        => FileSystem.DeleteDirectory(folderPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
}
