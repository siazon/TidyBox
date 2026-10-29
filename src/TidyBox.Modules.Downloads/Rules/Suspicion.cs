namespace TidyBox.Modules.Downloads.Rules;

public static class Suspicion
{
    private static readonly string[] Executable = { ".exe", ".msi", ".bat", ".cmd", ".scr", ".js", ".vbs", ".ps1", ".com" };

    /// <summary>
    /// True for names like "invoice.pdf.exe": a document or image extension followed by an executable one, which is how a program
    /// passes for a document. Only the name is checked, not the file's content.
    /// </summary>
    public static bool HasDeceptiveDoubleExtension(string fileName)
    {
        if (!Executable.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase)) return false;

        var withoutLast = Path.GetFileNameWithoutExtension(fileName);
        return Categories.ForFile(withoutLast)?.Id is "documents" or "images";
    }
}
