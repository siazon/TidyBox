namespace TidyBox.Modules.Uninstaller.Icons;

/// <param name="Index">Icon number inside the file; a negative number is a resource id, as in "file.exe,-101".</param>
public record IconLocation(string File, int Index);

/// <summary>Reads the DisplayIcon registry value: a file, optionally quoted, optionally followed by ",index".</summary>
public static class DisplayIconParser
{
    public static IconLocation? Parse(string? text)
    {
        text = Environment.ExpandEnvironmentVariables(text ?? "").Trim();
        if (text.Length == 0) return null;

        string file, rest;
        if (text[0] == '"')
        {
            var close = text.IndexOf('"', 1);
            if (close < 0) return null;
            file = text.Substring(1, close - 1);
            rest = text.Substring(close + 1).Trim();
        }
        else
        {
            var comma = text.LastIndexOf(',');
            if (comma >= 0 && int.TryParse(text.Substring(comma + 1).Trim(), out _))
            {
                file = text.Substring(0, comma);
                rest = text.Substring(comma);
            }
            else
            {
                file = text;
                rest = "";
            }
        }

        file = file.Trim();
        if (file.Length == 0) return null;

        var index = rest.StartsWith(',') && int.TryParse(rest.Substring(1).Trim(), out var parsed) ? parsed : 0;
        return new IconLocation(file, index);
    }
}
