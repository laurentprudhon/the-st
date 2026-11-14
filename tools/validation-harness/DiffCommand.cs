// ============================================================================================
// `diff` — the one-line answer to a G1/G2 mismatch (design/validation.md §4).
//
// `cmp` on two digest files says "byte 12 345 678"; the machine's own question is *when* the two
// runs first disagreed, and what was being appended then. This command streams both digests (they
// are hundreds of megabytes), stops at the first differing word, and names its batch number and the
// instant that batch drained — the sparse index `run.batches` is what makes that a lookup rather
// than a second copy of the stream. Then it prints each run's last eight batches' records, which is
// what the run itself could still see.
//
// Exit code 0 = the streams are identical (G1/G2 pass), 1 = they differ.
// ============================================================================================

using System.Globalization;
using TheST.Machine;

namespace TheST.Harness;

internal static class DiffCommand
{
    internal static int Run(string[] args)
    {
        string? a = null;
        string? b = null;

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--a":
                    if (++index >= args.Length) return Program.Unknown(args[index - 1]);
                    a = args[index];
                    break;

                case "--b":
                    if (++index >= args.Length) return Program.Unknown(args[index - 1]);
                    b = args[index];
                    break;

                default:
                    return Program.Unknown(args[index]);
            }
        }

        if (a is null || b is null)
        {
            Console.Error.WriteLine("diff: needs --a DIR --b DIR");
            return Program.Usage();
        }

        string pathA = Path.Combine(a, "run.digest");
        string pathB = Path.Combine(b, "run.digest");
        if (!File.Exists(pathA) || !File.Exists(pathB))
        {
            Console.Error.WriteLine($"diff: no run.digest in {(File.Exists(pathA) ? b : a)}");
            return 1;
        }

        long differedAt = CompareWords(pathA, pathB, out long batches, out string? wordA, out string? wordB);

        // the identity-free fold as well (#39): a queue-contents change may move run.digest's words but
        // never this file's, so G1/G2 compare both and *say which one* moved.
        string streamA = Path.Combine(a, "run.stream");
        string streamB = Path.Combine(b, "run.stream");
        if (differedAt >= 0 && File.Exists(streamA) && File.Exists(streamB))
        {
            long streamDifferedAt = CompareWords(streamA, streamB, out _, out string? streamWordA, out string? streamWordB);
            if (streamDifferedAt < 0)
            {
                Console.WriteLine("  run.stream is identical: the records, their instants and their order did not move —");
                Console.WriteLine("  only their identity did (#39's two counters). Nothing to diagnose here.");
            }
            else
            {
                Console.WriteLine($"  run.stream ALSO DIFFERS, first at batch {Program.Number(streamDifferedAt)}: {streamWordA} vs {streamWordB} —");
                Console.WriteLine("  a record, an instant or an ordering moved. That is the bug; read the records below.");
            }
        }

        if (differedAt < 0)
        {
            Console.WriteLine($"diff: identical — {Program.Number(batches)} batches, {Path.GetFileName(a)} = {Path.GetFileName(b)}");
            return 0;
        }

        Console.WriteLine($"diff: FIRST DIFFERENCE at batch {Program.Number(differedAt)} of {Program.Number(batches)}");
        Console.WriteLine($"  {a}: {(wordA is null ? "absent (the run ended)" : wordA)}");
        Console.WriteLine($"  {b}: {(wordB is null ? "absent (the run ended)" : wordB)}");
        Console.WriteLine($"  instant: {Describe(a, differedAt)} vs {Describe(b, differedAt)}");

        PrintTail(a, "a");
        PrintTail(b, "b");
        return 1;
    }

    /// <summary>Compares two word-per-line streams; -1 when identical, else the first differing batch.</summary>
    static long CompareWords(string pathA, string pathB, out long batches, out string? wordA, out string? wordB)
    {
        long differedAt = -1;
        batches = 0;
        wordA = null;
        wordB = null;

        using var first = new StreamReader(pathA);
        using var second = new StreamReader(pathB);
        while (true)
        {
            string? lineA = first.ReadLine();
            string? lineB = second.ReadLine();

            if (lineA is null && lineB is null)
            {
                break;
            }

            batches++;
            if (differedAt < 0 && (lineA is null || lineB is null || !string.Equals(lineA, lineB, StringComparison.Ordinal)))
            {
                differedAt = batches - 1;
                wordA = lineA;
                wordB = lineB;
            }

            // keep reading to the end: the batch count is part of the answer (a truncated run and a
            // differing run are different failures)
        }

        return differedAt;
    }

    /// <summary>The instant that batch drained at, from the sparse index — or a bracket when the batch sits between two entries.</summary>
    static string Describe(string directory, long batch)
    {
        string path = Path.Combine(directory, "run.batches");
        if (!File.Exists(path))
        {
            return "unknown (no run.batches: this run predates the index)";
        }

        (long Batch, long Instant)? before = null;
        (long Batch, long Instant)? after = null;

        foreach (string line in File.ReadLines(path))
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2
                || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long index)
                || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long instant))
            {
                continue;
            }

            if (index <= batch)
            {
                before = (index, instant);
                continue;
            }

            after = (index, instant);
            break;   // the index is in batch order
        }

        if (before is { } from && after is { } to)
        {
            return $"[{from.Instant} .. {to.Instant}) ticks (batches {Program.Number(from.Batch)}..{Program.Number(to.Batch)})";
        }

        return before is { } only ? $"{only.Instant} ticks (last indexed batch {Program.Number(only.Batch)})" : "unknown";
    }

    static void PrintTail(string directory, string label)
    {
        string path = Path.Combine(directory, "run.tail");
        if (!File.Exists(path))
        {
            return;
        }

        Console.WriteLine($"  run {label}'s last batches (the stream it could still see at its end):");
        foreach (string line in File.ReadLines(path))
        {
            Console.WriteLine($"    {line}");
        }
    }
}
