namespace PowerTools.Core.Elevation;

/// <summary>
/// Represents the response from an elevated operation performed via IPC.
/// </summary>
/// <param name="Success">Whether the operation completed successfully.</param>
/// <param name="ErrorMessage">An optional error message if the operation failed.</param>
/// <param name="ResultJson">The JSON-serialized result payload, if the operation succeeded.</param>
public record ElevationResponse(bool Success, string? ErrorMessage, string? ResultJson);
