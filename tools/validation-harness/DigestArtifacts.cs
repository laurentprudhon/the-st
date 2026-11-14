#if DEBUG
// ============================================================================================
// The digest artefacts — run.digest and the two sidecars that make a mismatch readable.
//
// design/validation.md §4 asks `run.report` to carry the digest stream's length and, when G1 or G2
// fails, "the first appearing-difference instant … with the full record stream for the last N
// batches — the one-line diff ADR 0008 promises". A digest is one word per batch, so the word alone
// names a *batch number*, never an instant, and a fold cannot be undone to recover its records.
//
//   run.digest   the contract: one hex word per batch, in batch order (this is compared, never read)
//   run.stream   the same batches, folded without the record's seq (#39): a change that only moves
//                how many queue entries a frame has must leave this file byte-identical, so the
//                witness ADR 0008 §7 condition 6 asks for is a `cmp` rather than a reading
//   run.batches  a sparse index, one line every 1 024 batches: `<batch> <instant>` — it turns a
//                differing line into an instant in a binary search, and costs ~20 000 lines for a
//                whole boot rather than a second copy of a 364 MB digest
//   run.tail     the last eight batches' records, whole — the shape of the machine at the end of the
//                run, and what `diff` prints beside a mismatch
//
// Debug only: a Release build carries no digest at all (ADR 0008 §6, and `validate.sh` proves it).
// ============================================================================================

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TheST.Machine;

namespace TheST.Harness;

internal static class DigestArtifacts
{
    /// <summary>Writes the three files. Returns the digest's word count.</summary>
    internal static int Write(string directory, Engine engine)
    {
        Directory.CreateDirectory(directory);

        WriteWords(directory, "run.digest", engine.DigestWords);
        WriteWords(directory, "run.stream", engine.StreamWords);

        var index = new StringBuilder(engine.BatchIndex.Count * 32);
        foreach ((long batch, long instant) in engine.BatchIndex)
        {
            index.Append(batch.ToString(CultureInfo.InvariantCulture)).Append(' ')
                 .Append(instant.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        File.WriteAllText(Path.Combine(directory, "run.batches"), index.ToString());

        var tail = new StringBuilder();
        foreach ((long instant, Record[] records) in engine.Tail)
        {
            tail.Append("batch ").Append(instant.ToString(CultureInfo.InvariantCulture))
                .Append(' ').Append(records.Length.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (Record record in records)
            {
                tail.Append("  ").Append(record.Seq.ToString(CultureInfo.InvariantCulture))
                    .Append(" i=").Append(record.Instant.ToString(CultureInfo.InvariantCulture))
                    .Append(" ch=").Append((record.Channel.Kind == ChannelKind.Wire ? "wire" : "int"))
                    .Append(':').Append(record.Channel.Id.ToString(CultureInfo.InvariantCulture))
                    .Append(" drv=").Append(record.Driver.ToString(CultureInfo.InvariantCulture))
                    .Append(" mask=").Append(record.Mask.ToString("x4", CultureInfo.InvariantCulture))
                    .Append(" value=").Append(record.Value.ToString("x4", CultureInfo.InvariantCulture))
                    .Append('\n');
            }
        }

        File.WriteAllText(Path.Combine(directory, "run.tail"), tail.ToString());
        return engine.DigestWords.Count;
    }

    /// <summary>The sha256 of an artefact, lowercase hex — what a commit message quotes.</summary>
    internal static string Sha256(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    static void WriteWords(string directory, string name, IReadOnlyList<ulong> words)
    {
        var text = new StringBuilder(words.Count * 17);
        foreach (ulong word in words)
        {
            text.Append(word.ToString("x16", CultureInfo.InvariantCulture)).Append('\n');
        }

        File.WriteAllText(Path.Combine(directory, name), text.ToString());
    }
}
#endif
