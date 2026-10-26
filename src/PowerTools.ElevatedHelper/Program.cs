using System;
using System.Threading;
using System.Security.Principal;
using System.Threading.Tasks;
using PowerTools.ElevatedHelper;

// No default pipe name: a fixed, guessable name could be squatted. The launching app picks a random one per session.
if (args.Length == 0)
{
    Console.Error.WriteLine("usage: PowerTools.ElevatedHelper <pipeName> [allowedClientSid]");
    return 2;
}

var pipeName = args[0];
var allowedClient = args.Length > 1 ? new SecurityIdentifier(args[1]) : null;

// Handlers for the operations the modules ask the helper to perform.
var handlers = new IElevatedOperationHandler[] { new RemoveResidualHandler(), new QuarantineInspectHandler(), new QuarantinePurgeHandler() };

var server = new ElevationServer(pipeName, handlers, TimeSpan.FromMinutes(5), allowedClient);
await server.RunAsync(CancellationToken.None);
return 0;
