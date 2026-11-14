using TheST.Machine;

namespace TheST.Harness;

/// <summary>
/// G7 (design/validation.md §3) — a ring never answers from outside its window (#43; this replaces
/// the retention check, because a ring deletes the cadence it used to tune).
///
/// <para>
/// The one failure a digest cannot catch: a record overwritten under a live reader changes an *answer*
/// while the record stream stays identical, so two runs agree and both are wrong. ADR 0004's rule is
/// unchanged — a record may be overwritten only once no reader can still ask for its instant, and a
/// reader asking older than the window must get "nothing", never a wrong value. This command tests both
/// halves: the ring's own algebra over 1 000 appends into 256 slots, and a sampler reading 1 000 ticks
/// into the past while a writer appends eight times as fast as the window can hold.
/// </para>
/// </summary>
internal static class WindowChecks
{
    static int _failures;

    internal static int Run()
    {
        _failures = 0;
        Console.WriteLine("check-window — G7: a ring answers inside its window, and answers nothing outside it (ADR 0004)");
        Console.WriteLine();

        WindowAlgebra();
        LiveSampler();

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "window PASS" : $"window FAIL — {_failures} assertion(s)");
        return _failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// The ring on its own: 1 000 records into <see cref="ValueLog.DefaultCapacity"/> slots, one per 10 ticks,
    /// whose value is its number <b>+1</b> — so a 0 answer can only mean "nothing here", never a value
    /// the wire legitimately carried.
    /// </summary>
    static void WindowAlgebra()
    {
        Console.WriteLine($"the ring's own algebra — 1 000 records into {ValueLog.DefaultCapacity} slots, one per 10 ticks");

        var channel = new Channel(ChannelKind.Wire, 0x3F0, "probe");
        var log = new ValueLog(channel);
        for (int index = 0; index < 1000; index++)
        {
            log.Append(new Record(index * 10L, index, channel, 1, 0xFFFF, (ushort)(index + 1)));
        }

        int held = ValueLog.DefaultCapacity;
        long oldest = 1000 - held;

        static ushort Reference(long instant)
        {
            long index = Math.Min(instant / 10, 999);         // the last record at or before the instant
            return index < 0 ? (ushort)0 : (ushort)(index + 1);
        }

        Check(log.Count == held, "the ring holds its capacity and no more", $"Count = {log.Count}");
        Check(log.Overwritten == 1000 - held, "it accounts for every record it overwrote", $"Overwritten = {log.Overwritten}");
        Check(log[0].Seq == oldest, "the oldest record held is the 745th", $"log[0].Seq = {log[0].Seq}");
        Check(log[log.Count - 1].Seq == 999, "the newest record held is the last one appended", $"newest = {log[log.Count - 1].Seq}");

        bool answers = true;
        foreach (long instant in new long[] { oldest * 10, oldest * 10 + 1, 8_888, 9_999, 100_000 })
        {
            ushort measured = log.ValueAt(instant);
            if (measured != Reference(instant))
            {
                answers = false;
                Console.WriteLine($"   wrong at {instant}: {measured}, the wire carried {Reference(instant)}");
            }
        }

        Check(answers, "every sample inside the window answers what the wire carried (invariant 5)");

        // outside the window the answer is "nothing" — never the oldest record's value, and never a
        // wrapped slot's, which is what a ring without a window test would return
        long below = (oldest - 1) * 10;
        Check(log.ValueAt(below) == 0 && log.IndexAtOrBefore(below) < 0,
            "a sample older than the window returns nothing, never a wrong value", $"ValueAt({below}) = 0");

        int at = log.IndexAt(900);
        Check(at >= 0 && log[at].Seq == 900, "a sequence lookup finds a record the ring still holds", $"IndexAt(900) = {at}");
        Check(log.IndexAfter(899) == at, "IndexAfter finds the first record past a sequence number", $"IndexAfter(899) = {log.IndexAfter(899)}");
        Check(log.IndexAt(700) < 0, "a sequence number the ring overwrote is gone, not mis-resolved", $"IndexAt(700) = {log.IndexAt(700)}");
        Check(log.IndexAfter(700) == 0,
            "IndexAfter below the window names the oldest held record, and only an assertion can tell that apart from a cursor that is genuinely there",
            $"IndexAfter(700) = {log.IndexAfter(700)}");

        Console.WriteLine();
    }

    /// <summary>
    /// The engine's side, and the property G7 exists for: a writer appends every 50 ticks — 8 000
    /// records over the run, thirty-one windows — and a sampler reads the wire 1 000 ticks back, twenty
    /// records behind the newest. The ring overwrites constantly and every sample must still be exact.
    /// </summary>
    static void LiveSampler()
    {
        Console.WriteLine("the live reader — a sampler reads 1 000 ticks back while the ring overwrites under it");

        var wire = new Channel(ChannelKind.Wire, 0x3F1, "probe");
        var engine = new Engine();
        var appended = new List<(long Instant, ushort Value)>();

        var writer = new WriterChip(engine, 90, "writer", wire, appended);
        var sampler = new SamplerChip(engine, 91, "sampler", wire);
        engine.Add(writer);
        engine.Add(sampler);
        writer.Launch();
        sampler.Launch();

        for (int batch = 1; batch <= 400; batch++)
        {
            engine.Run(batch * 1000L);
        }

        ValueLog log = engine.Log(wire);

        Check(sampler.Samples > 100, "the sampler took samples", $"{sampler.Samples} samples");
        Check(log.Overwritten > 0, "the ring overwrote while the sampler was live", $"{log.Overwritten} records overwritten");

        int mismatches = 0;
        foreach ((long instant, ushort measured) in sampler.Observed)
        {
            ushort expected = 0;
            foreach ((long appendedAt, ushort value) in appended)
            {
                if (appendedAt > instant)
                {
                    break;
                }

                expected = value;
            }

            if (measured != expected)
            {
                if (mismatches < 5)
                {
                    Console.WriteLine($"   wrong at {instant}: {measured}, the writer had put {expected}");
                }

                mismatches++;
            }
        }

        Check(mismatches == 0, "every sample equals the value the writer had put there — no answer from outside the window",
            $"{sampler.Samples} samples, {mismatches} differences");

        Console.WriteLine();
    }

    /// <summary>The writer: one record every 50 ticks on the probe wire.</summary>
    sealed class WriterChip(Engine engine, int id, string name, Channel wire, List<(long Instant, ushort Value)> appended)
        : Chip(engine, id, name)
    {
        public void Launch() => _ = Run();

        async ValueTask Run()
        {
            ushort value = 0;
            while (true)
            {
                Advance(50);
                value++;
                Write(Clock, wire, 0xFFFF, value);
                appended.Add((Clock, value));
                await ReadAt(Clock + 50, wire);   // a dated read: the only suspension this chip needs
            }
        }
    }

    /// <summary>The sampler: one read 1 000 ticks into the past every 200 ticks.</summary>
    sealed class SamplerChip(Engine engine, int id, string name, Channel wire) : Chip(engine, id, name)
    {
        public List<(long Instant, ushort Value)> Observed { get; } = [];

        public int Samples => Observed.Count;

        public void Launch() => _ = Run();

        async ValueTask Run()
        {
            while (true)
            {
                long instant = Math.Max(0, Clock - 1000);
                ushort value = await ReadAt(instant, wire);   // the live reader
                Observed.Add((instant, value));
                await ReadAt(Clock + 200, wire);
            }
        }
    }

    static void Check(bool condition, string what, string? detail = null)
    {
        if (condition)
        {
            Console.WriteLine($"   ok    {what}{(detail is null ? string.Empty : $" — {detail}")}");
            return;
        }

        _failures++;
        Console.WriteLine($"   FAIL  {what}{(detail is null ? string.Empty : $" — {detail}")}");
    }
}
