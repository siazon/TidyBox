using System.Threading.Tasks;
using TidyBox.Core.Elevation;

namespace TidyBox.ElevatedHelper;

public interface IElevatedOperationHandler
{
    string OperationType { get; }

    Task<ElevationResponse> HandleAsync(string payloadJson);
}
