// ============================================================================================
// SuspensionBench — ticket #8's measurement artefact. See README.md for the commands.
//
//   dotnet run -c Release -- anchor|fastpath|suspension|write|dispatch|frame|sweep|determinism|pool|nesting|all
//
// Every measurement prints `MEASURE <name> <value> <unit> <n> <note>`; numbers go into
// research/suspension-cost.md, never the other way round.
// ============================================================================================

using System.Diagnostics;
using TheST.SuspensionBench;

string mode = args.Length > 0 ? args[0] : "all";
int Arg(int i, int fallback) => args.Length > i && int.TryParse(args[i], out var v) ? v : fallback;

Console.WriteLine($"# SuspensionBench mode={mode} runtime={Environment.Version} " +
                  $"cpus={Environment.ProcessorCount} gc={(System.Runtime.GCSettings.IsServerGC ? "server" : "workstation")} " +
                  $"tiered={System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported}");

switch (mode)
{
    case "anchor":
        Bench.Anchor();
        break;

    case "fastpath":
        Bench.FastPathPlain(Arg(1, 40_000_000));
        break;

    case "fastplain":
        Bench.FastPathPlain(Arg(1, 40_000_000));
        break;

    case "suspension":
        Bench.Suspension(Arg(1, 4_000_000), Arg(2, 1));
        break;

    case "write":
        Bench.AwaitedWrite(Arg(1, 2_000_000));
        break;

    case "dispatch":
        Bench.DispatchMicro(Arg(1, 2_000_000));
        break;

    case "frame":
        Bench.Frame(Arg(1, 1000), Arg(2, 60), Arg(3, 8), Arg(4, 8));
        break;

    case "sweep":
        // The share of synchronous reads at a stated crossing density. 10², 10³ and 10⁴ because the
        // ticket's own "10⁴ per frame" is stale (ADR 0002's DMA correction says 10²–10³) and #18 has
        // not measured it: sweeping says which figure came from where instead of picking one.
        foreach (int crossings in new[] { 100, 1000, 10_000 })
        {
            Bench.Frame(crossings, 20, 8, 8);            // warm-up: default tiering needs a frame of it
            for (int rep = 0; rep < 3; rep++) Bench.Frame(crossings, 60, 8, 8);
        }
        break;

    case "determinism":
    {
        // ADR 0008 §6: same input twice at T=1 must give a byte-identical digest stream.
        var a = Bench.Frame(1000, 40, 8, 8);
        var b = Bench.Frame(1000, 40, 8, 8);
        Console.WriteLine($"MEASURE\tdeterminism.serial\t{(a.Digest == b.Digest ? 1 : 0)}\tidentical\t2\t" +
                          $"digest_a={a.Digest:X16} digest_b={b.Digest:X16}");
        break;
    }

    case "pool":
        Bench.Pool(Arg(1, 1000), Arg(2, 20), Arg(3, 8), Arg(4, 3), collide: false);
        Bench.Pool(Arg(1, 1000), Arg(2, 20), Arg(3, 8), Arg(4, 3), collide: true);
        break;

    case "nesting":
        Nesting.Run();
        break;

    case "all":
        Bench.Anchor();
        Bench.FastPathPlain();
        Bench.Suspension();
        Bench.AwaitedWrite();
        Bench.DispatchMicro();
        foreach (int c in new[] { 100, 1000, 10_000 }) Bench.Frame(c, 60, 8, 8);
        Nesting.Run();
        break;

    default:
        Console.Error.WriteLine($"unknown mode '{mode}'");
        return 2;
}

return 0;
