using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;
using Microsoft.DotNet.Interactive;
using Microsoft.DotNet.Interactive.Connection;

namespace FirebirdSql.DotNet.Interactive.Firebird;

public class ConnectFirebirdCommand : ConnectKernelCommand
{
    public ConnectFirebirdCommand()
        : base("firebird", "Connects to a Firebird database")
    {
        Add(ConnectionStringArgument);
    }

    public Argument<string> ConnectionStringArgument { get; } =
        new("connectionString", "The connection string used to connect to the database");

    public override Task<IEnumerable<Kernel>> ConnectKernelsAsync(
        KernelInvocationContext context,
        InvocationContext commandLineContext)
    {
        var connectionString = commandLineContext.ParseResult.GetValueForArgument(ConnectionStringArgument);
        var localName = commandLineContext.ParseResult.GetValueForOption(KernelNameOption);
        var kernel = new FirebirdKernel($"sql-{localName}", connectionString);
        kernel.UseValueSharing();
        return Task.FromResult<IEnumerable<Kernel>>(new[] { kernel });
    }
}