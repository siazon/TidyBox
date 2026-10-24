using System;
using System.Threading;
using System.Threading.Tasks;
using PowerTools.ElevatedHelper;

var pipeName = args.Length > 0 ? args[0] : "PowerTools.ElevatedHelper";

// No real elevated operation handlers exist yet; later module plans register
// their IElevatedOperationHandler implementations here.
var handlers = Array.Empty<IElevatedOperationHandler>();

var server = new ElevationServer(pipeName, handlers, TimeSpan.FromMinutes(5));
await server.RunAsync(CancellationToken.None);
