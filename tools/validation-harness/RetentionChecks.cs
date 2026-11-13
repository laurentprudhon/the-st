using TheST.Machine;

namespace TheST.Harness;

/// <summary>
/// G7 (design/validation.md §3) — retention never truncates under a live reader.
///
/// <para>
/// The one failure a digest cannot catch: a log truncated under a live sampler changes an *answer*
/// while the record stream stays identical, so two runs agree and both are wrong. ADR 0004's
/// 2026-11-07 revision states the rule — a wire's log must retain back to the earliest instant a live
/// reader may still sample, and that watermark is the minimum over <b>subscribers and live sampling
/// windows</b>. This command tests both halves: the log's own truncation algebra, and a sampler reading
/// a hundred ticks into the past while a writer keeps appending and retention keeps trimming.
/// </para>
/// </summary>
internal static class RetentionChecks
{
    static int _failures;

    internal static int Run()
    {
        _failures = 0;
        Console.WriteLine("check-retention — G7: a log is truncated only where no reader can still ask (ADR 0004)");
        Console.WriteLine();

        LogAlgebra();
        LiveSampler();

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "retention PASS" : $"retention FAIL — {_failures} assertion(s)");
        return _failures == 0 ? 0 : 1;
    }

    /// <summary>The log on its own: what a trim keeps, what it drops, and what a sample then answers.</summary>
    static void LogAlgebra()
    {
        Console.WriteLine("the log's own algebra — 1 000 records, one per 10 ticks, value = its number");

        var channel = new Channel(ChannelKind.Wire, 0x3F0, "probe");
        var log = new ValueLog(channel);
        for (int index = 0; index < 1000; index++)
        {
            log.Append(new Record(index * 10L, index, channel, 1, 0xFFFF, (ushort)index));
        }

        static ushort Reference(long instant)
        {
            long index = instant / 10;                       // the last record at or before the instant
            return index < 0 ? (ushort)0 : (ushort)Math.Min(index, 999);
        }

        int discarded = log.TrimTo(5000);

        Check(discarded > 0, "the trim discarded records", $"{discarded} of 1 000");
        Check(log.Discarded == discarded, "the log accounts for what it discarded", $"Discarded = {log.Discarded}");
        Check(log.Count == 1000 - discarded, "Count is the records retained, not the records ever appended", $"Count = {log.Count}");
        Check(log[0].Instant < 5000 && log[1].Instant >= 5000,
            "one record at or before the watermark survives as the base", $"oldest retained instant = {log[0].Instant}");

        bool answers = true;
        foreach (long instant in new long[] { 5000, 5001, 5555, 6000, 7777, 9990, 100_000 })
        {
            ushort measured = log.ValueAt(instant);
            if (measured != Reference(instant))
            {
                answers = false;
                Console.WriteLine($"   wrong at {instant}: {measured}, the wire carried {Reference(instant)}");
            }
        }

        Check(answers, "every sample at or after the watermark answers what the wire carried (invariant 5)");

        // before the watermark the past is unreachable — never a *wrong* value (invariant 6's other half)
        Check(log.ValueAt(4000) == 0 && log.IndexAtOrBefore(4000) < 0,
            "a sample older than the retained history returns nothing, never a wrong value", "ValueAt(4000) = 0");

        int at = log.IndexAt(700);
        Check(at >= 0 && log[at].Seq == 700, "a sequence lookup still finds its record after a trim", $"IndexAt(700) = {at}");
        Check(log.IndexAfter(699) == at, "IndexAfter still finds the first record past a sequence number", $"IndexAfter(699) = {log.IndexAfter(699)}");
        Check(log.IndexAt(5) < 0, "a sequence number that was discarded is gone, not mis-resolved", $"IndexAt(5) = {log.IndexAt(5)}");

        Console.WriteLine();
    }

    /// <summary>
    /// The engine's side: the live sampling window. A writer appends every 50 ticks; a sampler reads
    /// the wire 1 000 ticks back — twenty records behind the watermark every subscription would allow —
    /// and the loop trims after every batch. Retention runs every sixteen batches, i.e. every ~800
    /// ticks here, so a trim lands inside the sampler's window constantly: without the window counted in
    /// the watermark, the record it asks for is gone and it reads the base record's value instead.
    /// </summary>
    static void LiveSampler()
    {
        Console.WriteLine("the live sampling window — a sampler reads 100 ticks back while retention trims");

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
        Check(log.Discarded > 0, "retention trimmed while the sampler was live", $"{log.Discarded} records discarded");

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

        Check(mismatches == 0, "every sample equals the value the writer had put there — no truncation under a live reader",
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
                ushort value = await ReadAt(instant, wire);   // the live sampling window
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
