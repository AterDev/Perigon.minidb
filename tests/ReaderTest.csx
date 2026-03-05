using Perigon.MiniDb.Client.Services;
using Perigon.MiniDb.Client.Models;

var filePath = @"E:\codes\Perigon.minidb\sample.mds";
var tables = MiniDbFileReader.GetTableNames(filePath, out var err0);
Console.WriteLine($"Tables: {string.Join(", ", tables)} (error: {err0})");

foreach (var t in tables)
{
    var data = MiniDbFileReader.LoadTableData(filePath, t, out var err);
    Console.WriteLine($"Table '{t}': Fields=[{string.Join(", ", data.FieldNames)}], Records={data.Records.Count}, Error={err}");
    foreach (var r in data.Records.Take(2))
    {
        var vals = string.Join(" | ", data.FieldNames.Select(f => $"{f}={r[f]}"));
        Console.WriteLine($"  {vals}");
    }
}
