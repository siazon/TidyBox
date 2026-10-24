using System.Threading.Tasks;
using PowerTools.Core.Elevation;

namespace PowerTools.ElevatedHelper;

public interface IElevatedOperationHandler
{
    string OperationType { get; }

    Task<ElevationResponse> HandleAsync(string payloadJson);
}
