using PowerTools.Modules.ContextMenu.Model;

namespace PowerTools.Modules.ContextMenu.Naming;

public record MenuNameInfo(string DisplayName, string? Publisher);

public interface IMenuNameResolver
{
    /// <param name="registeredName">The default value of the CLSID key (handlers) or of the verb key (verbs).</param>
    /// <param name="muiVerb">The verb's MUIVerb value, possibly an indirect string like <c>@shell32.dll,-8506</c>.</param>
    MenuNameInfo Resolve(MenuEntryKind kind, string keyName, string? clsid, string? modulePath, string? registeredName, string? muiVerb);
}
