namespace PowerTools.Modules.Uninstaller.Residue;

public enum ResidueKind { Folder, RegistryKey }

/// <summary>High: the app's own install location. Medium: only the name matches.</summary>
public enum ResidueConfidence { High, Medium }

/// <param name="Path">A folder path, or a registry path like HKCU\Software\X.</param>
/// <param name="Removable">Registry keys are shown but never removable: a packaged app's deletes are virtualized and never reach the real registry.</param>
public record ResidueItem(ResidueKind Kind, string Path, ResidueConfidence Confidence, string Reason, bool NeedsElevation, bool Removable);
