using MergerNotes.Infrastructure.Import;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: MergerNotes.MergeSmoke <base.jwlibrary> <incoming.jwlibrary> <output.jwlibrary>");
    Environment.ExitCode = 2;
    return;
}

var merger = new JwlibraryBackupMerger();
var result = await merger.MergeAsync(args[0], args[1], args[2]);

Console.WriteLine($"Output: {result.OutputPath}");
Console.WriteLine($"Base notes: {result.Report.BaseNotes}");
Console.WriteLine($"Incoming notes: {result.Report.IncomingNotes}");
Console.WriteLine($"Merged notes: {result.Report.MergedNotes}");
Console.WriteLine($"Added notes: {result.Report.AddedNotes}");
Console.WriteLine($"Updated notes: {result.Report.UpdatedNotes}");
Console.WriteLine($"Added tag maps: {result.Report.AddedTagMaps}");
