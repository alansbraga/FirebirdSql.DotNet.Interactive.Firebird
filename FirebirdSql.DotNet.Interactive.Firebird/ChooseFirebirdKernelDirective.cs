using System.CommandLine;
using Microsoft.DotNet.Interactive;

namespace FirebirdSql.DotNet.Interactive.Firebird;

public class ChooseFirebirdKernelDirective : ChooseKernelDirective
{
    public ChooseFirebirdKernelDirective(Kernel kernel, string description = null) : base(kernel, description)
    {
        Add(NameOption);
    }
    
    public Option<string> NameOption { get; } = new(
        "--name",
        description: "Specify the value name to store the results.",
        getDefaultValue: () => "lastResults");

}