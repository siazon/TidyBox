using System;
using System.Threading;
using System.Security.Principal;
using System.Threading.Tasks;
using PowerTools.ElevatedHelper;

var pipeName = args.Length > 0 ? args[0] : "PowerTools.ElevatedHelper";
var allowedClient = args.Length > 1 ? new SecurityIdentifier(args[1]) : null;

// No real elevated operation handlers exist yet; later module plans register
// their IElevatedOperationHandler implementations here.
var handlers = Array.Empty<IElevatedOperationHandler>();

var server = new ElevationServer(pipeName, handlers, TimeSpan.FromMinutes(5), allowedClient);
await server.RunAsync(CancellationToken.None);
