using PowerTools.Modules.ContextMenu.Model;

namespace PowerTools.Modules.ContextMenu.Naming;

/// <summary>
/// Picks the best display name for an entry: a curated Chinese name first, then what the registration itself
/// says (MUIVerb / CLSID name), then the DLL's description, finally the raw key name. The curated list is only a
/// "show nicer if we know it" layer, so unknown entries still read sensibly on any system language.
/// </summary>
public sealed class MenuNameResolver : IMenuNameResolver
{
    private static readonly Dictionary<string, string> ByClsid = new(StringComparer.OrdinalIgnoreCase)
    {
        ["{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}"] = "共享",
        ["{09799AFB-AD67-11d1-ABCD-00C04FC30936}"] = "打开方式",
        ["{7BA4C740-9E81-11CF-99D3-00AA004AE837}"] = "发送到",
        ["{f3d06e7c-1e45-4a26-847e-f9fcdee59be0}"] = "复制文件地址",
        ["{D969A300-E7FF-11d0-A93B-00A0C90F2719}"] = "新建",
        ["{E61BF828-5E63-4287-BEF1-60B1A4FDE0E3}"] = "工作文件夹",
        ["{474C98EE-CF3D-41f5-80E3-4AAB0AB04301}"] = "脱机文件",
    };

    private static readonly Dictionary<string, string> ByKindAndKeyName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Handler|Sharing"] = "共享",
        ["Handler|Open With"] = "打开方式",
        ["Handler|SendTo"] = "发送到",
        ["Handler|CopyAsPathMenu"] = "复制文件地址",
        ["Handler|New"] = "新建",
        ["Handler|WorkFolders"] = "工作文件夹",
        ["Handler|Offline Files"] = "脱机文件",
        ["Verb|find"] = "搜索",
        ["Verb|cmd"] = "在此处打开命令窗口",
        ["Verb|Powershell"] = "在此处打开 PowerShell 窗口",
    };

    private readonly IIndirectStringLoader _indirect;
    private readonly Func<string, ModuleInfo?> _readModule;

    public MenuNameResolver(IIndirectStringLoader indirect, Func<string, ModuleInfo?> readModule)
    {
        _indirect = indirect;
        _readModule = readModule;
    }

    public static MenuNameResolver CreateDefault() => new(new IndirectStringLoader(), ModuleInfoReader.Read);

    public MenuNameInfo Resolve(MenuEntryKind kind, string keyName, string? clsid, string? modulePath, string? registeredName, string? muiVerb)
    {
        var module = ReadModule(modulePath);

        var name = FromTable(kind, keyName, clsid)
            ?? Expand(muiVerb)
            ?? Expand(registeredName)
            ?? module?.Description
            ?? keyName.Trim();

        return new MenuNameInfo(StripAccelerators(name), module?.Company);
    }

    private static string? FromTable(MenuEntryKind kind, string keyName, string? clsid)
    {
        if (!string.IsNullOrWhiteSpace(clsid) && ByClsid.TryGetValue(clsid.Trim(), out var byClsid)) return byClsid;
        return ByKindAndKeyName.TryGetValue($"{kind}|{keyName.Trim()}", out var byName) ? byName : null;
    }

    private string? Expand(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var loaded = _indirect.Load(raw.Trim());
        return string.IsNullOrWhiteSpace(loaded) ? null : loaded.Trim();
    }

    private ModuleInfo? ReadModule(string? modulePath)
    {
        if (string.IsNullOrWhiteSpace(modulePath)) return null;

        try
        {
            return _readModule(modulePath);
        }
        catch (Exception)
        {
            // A name is cosmetic; whatever goes wrong reading a DLL must never take the whole list down.
            return null;
        }
    }

    // "打开(&O)" -> "打开(O)"; a literal ampersand is written "&&".
    private static string StripAccelerators(string name) => name.Replace("&&", "\0").Replace("&", "").Replace("\0", "&");
}
