using System;
using System.Collections.Generic;
using System.CommandLine.Parsing;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using FirebirdSql.Data.FirebirdClient;
using Microsoft.AspNetCore.Html;
using Microsoft.DotNet.Interactive;
using Microsoft.DotNet.Interactive.Commands;
using Microsoft.DotNet.Interactive.Events;
using Microsoft.DotNet.Interactive.Formatting.TabularData;
using Microsoft.DotNet.Interactive.ValueSharing;
using Enumerable = System.Linq.Enumerable;

namespace FirebirdSql.DotNet.Interactive.Firebird;

public class FirebirdKernel 
    : Kernel
    , IKernelCommandHandler<SubmitCode>
    , IKernelCommandHandler<RequestValue>
    , IKernelCommandHandler<RequestValueInfos>
{
    private readonly string connectionString;
    private IEnumerable<IEnumerable<IEnumerable<(string name, object value)>>> tables;
    private readonly Dictionary<string, object> resultSets = new(StringComparer.Ordinal);
    private ChooseFirebirdKernelDirective chooseKernelDirective;

    public FirebirdKernel(string name, string connectionString) : base(name)
    {
        KernelInfo.LanguageName = "Firebird";
        KernelInfo.Description ="""
                                This kernel is backed by a Firebird database.
                                It can execute SQL statements against the database, display the results as tables and share the results.
                                """;
                                
        this.connectionString = connectionString;
    }

    public override ChooseKernelDirective ChooseKernelDirective => chooseKernelDirective ??= new(this);
    private DbConnection OpenConnection()
    {
        return new FbConnection(connectionString);
    }

    async Task IKernelCommandHandler<SubmitCode>.HandleAsync(
        SubmitCode submitCode,
        KernelInvocationContext context)
    {
        await using var connection = OpenConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var dbCommand = connection.CreateCommand();

        dbCommand.CommandText = submitCode.Code;

        tables = Execute(dbCommand);
        var results = new List<TabularDataResource>();

        try
        {
            foreach (var table in tables)
            {
                var tabularDataResource = table.ToTabularDataResource();
                results.Add(tabularDataResource);

                var explorer = DataExplorer.CreateDefault(tabularDataResource);
                context.Display(explorer);
            }
        }
        finally
        {
            StoreQueryResults(results, submitCode.KernelChooserParseResult);
        }

    }

    private IEnumerable<IEnumerable<IEnumerable<(string name, object value)>>> Execute(IDbCommand command)
    {
        using var reader = command.ExecuteReader();

        do
        {
            var values = new object[reader.FieldCount];
            var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();

            ResolveColumnNameClashes(names);

            // holds the result of a single statement within the query
            var table = new List<(string, object)[]>();

            while (reader.Read())
            {
                reader.GetValues(values);
                var row = new (string, object)[values.Length];
                for (var i = 0; i < values.Length; i++)
                {
                    row[i] = (names[i], values[i]);
                }

                table.Add(row);
            }

            yield return table;
        } while (reader.NextResult());

        yield break;

        void ResolveColumnNameClashes(string[] names)
        {
            var nameCounts = new Dictionary<string, int>(capacity: names.Length);
            for (var i1 = 0; i1 < names.Length; i1++)
            {
                var columnName = names[i1];
                if (nameCounts.TryGetValue(columnName, out var count))
                {
                    nameCounts[columnName] = ++count;
                    names[i1] = columnName + $" ({count})";
                }
                else
                {
                    nameCounts[columnName] = 1;
                }
            }
        }
    }

    public static void AddFirebirdKernelConnectorTo(CompositeKernel kernel)
    {
        kernel.AddKernelConnector(new ConnectFirebirdCommand());

        KernelInvocationContext.Current?.Display(
            new HtmlString("""
                           <details><summary>Query Firebird databases.</summary>
                               <p>This extension adds support for connecting to Firebird databases using the <code>#!connect firebird</code> magic command. For more information, run a cell using the <code>#!sql</code> magic command.</p>
                               </details>
                           """),
            "text/html");
    }

    public static void AddFirebirdKernelConnectorToCurrentRoot()
    {
        if (KernelInvocationContext.Current is { } context &&
            context.HandlingKernel.RootKernel is CompositeKernel root)
        {
            FirebirdKernel.AddFirebirdKernelConnectorTo(root);
        }
    }

    public Task HandleAsync(RequestValue command, KernelInvocationContext context)
    {
        if (TryGetValue<object>(command.Name, out var value))
        {
            context.PublishValueProduced(command, value);
        }
        else
        {
            context.Fail(command, message: $"Value '{command.Name}' not found in kernel {Name}");
        }

        return Task.CompletedTask;
    }

    private void StoreQueryResultSet(string name, IReadOnlyCollection<TabularDataResource> queryResultSet)
    {
        resultSets[name] = queryResultSet;
    }

    private bool TryGetValue<T>(string name, out T value)
    {
        if (resultSets.TryGetValue(name, out var resultSet) &&
            resultSet is T resultSetT)
        {
            value = resultSetT;
            return true;
        }

        value = default;
        return false;
    }

    private void StoreQueryResults(IReadOnlyCollection<TabularDataResource> results, ParseResult commandKernelChooserParseResult)
    {
        var chooser = chooseKernelDirective;
        var name = commandKernelChooserParseResult?.GetValueForOption(chooser.NameOption);
        if (!string.IsNullOrWhiteSpace(name))
        {
            StoreQueryResultSet(name, results);
        }
    }

    public Task HandleAsync(RequestValueInfos command, KernelInvocationContext context)
    {
        var valueInfos = CreateKernelValueInfos(resultSets, command.MimeType).ToArray();

        context.Publish(new ValueInfosProduced(valueInfos, command));

        return Task.CompletedTask;

        static IEnumerable<KernelValueInfo> CreateKernelValueInfos(IReadOnlyDictionary<string, object> source, string mimeType)
        {
            return source.Keys.Select(key =>
            {
                var formattedValues = FormattedValue.CreateSingleFromObject(
                    source[key],
                    mimeType);

                return new KernelValueInfo(
                    key,
                    formattedValues,
                    type: typeof(IEnumerable<TabularDataResource>));
            });
        }
    }
}

public class SqlRow : Dictionary<string, object>
{
}