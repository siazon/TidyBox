namespace PowerTools.Core.Elevation;

/// <summary>
/// Represents a request to perform an elevated operation via IPC.
/// </summary>
/// <param name="OperationType">The type of operation to perform (e.g., "uninstall.removeResidual").</param>
/// <param name="PayloadJson">The JSON-serialized payload containing operation-specific parameters.</param>
public record ElevationRequest(string OperationType, string PayloadJson);
