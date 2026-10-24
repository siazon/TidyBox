namespace PowerTools.Core.Elevation;

public interface IElevationClient
{
    Task<ElevationResponse> ExecuteAsync(ElevationRequest request, CancellationToken ct = default);
}
