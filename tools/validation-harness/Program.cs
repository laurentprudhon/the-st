// ============================================================================================
// The validation harness — the instrument of design/validation.md §2, in its first stage.
//
// Commands in this stage (the rest of the set is #35's):
//   check-clocks   G4  — the clock arithmetic is ADR 0001's. Pure arithmetic, no emulation.
//   check-stores       Ram/Rom round-trip: the pins, machine byte order, lane isolation.
//   run            G1/G2 — the synthetic trace, ended by a byte-identical run.digest.
//
// The exit code is the verdict. Frames are never compared (validation.md §2) and sound is not in the
// gate. Nothing here boots an image: reading TOS is #34's.
// ============================================================================================

using System.Globalization;

namespace TheST.Harness;

internal static class Program
{
    internal static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            return Usage();
        }

        switch (args[0])
        {
            case "check-clocks":
                return ClockChecks.Run();

            case "check-stores":
                return StoreChecks.Run();

            case "check-window":
                return WindowChecks.Run();

            case "run":
                return TraceRunCommand.Run(args[1..]);

            case "boot":
                return BootCommand.Run(args[1..]);

            case "cpu-tests":
                return CpuTestsCommand.Run(args[1..]);

            case "cpu-surface":
                return SurfaceCommand.Run(args[1..]);

            case "diff":
                return DiffCommand.Run(args[1..]);

            case "-h":
            case "--help":
            case "help":
                return Usage();

            default:
                Console.Error.WriteLine($"validation-harness: unknown command '{args[0]}'");
                return Usage();
        }
    }

    internal static int Usage()
    {
        Console.WriteLine(
            """
            validation-harness — the walking skeleton's instrument (design/validation.md §2)

            usage: validation-harness <command> [options]

              check-clocks                       G4: the clock arithmetic (ADR 0001)
              check-stores                       Ram / Rom: the pins, machine byte order, lane isolation
              check-window                       G7: a ring answers inside its window, and nothing outside
              boot [--frames N] [--inject] [--out DIR] [--image PATH] [--trace]
                                                 the machine's own trace: EmuTOS to the GEM desktop
              diff --a DIR --b DIR
                                                 compare two runs' digest streams: the first differing
                                                 batch, the instant it drained at, and each run's tail
                                                 (G1/G2's one-line diff)
              cpu-surface [--audit] [--families DIR]
                                                 #85: the chip's public surface against ADR 0015 §1;
                                                 --families grades Decode against a corpus's own names
                                                 — the default is the freeze (non-zero on a divergence),
                                                 --audit prints the same report and exits 0 while the
                                                 surface is still being converted
              cpu-tests [--corpus clk|mame] [--dir DIR] [--file NAME] [--limit N] [--show N]
                        [--quiet] [--out FILE] [--failures FILE] [--clocks FILE]
                        [--trace N | --reads FILE]
                                                 the 680x0 corpus against the core, one instruction at a
                                                 time. --corpus mame grades MAME's microcoded corpus (the
                                                 gate, scripts/fetch-m68000-tests.sh; pc = m_au, so the
                                                 opcode is at pc - 4), --corpus clk the denser CLK suite
                                                 (default dir: tests/680x0/v1, scripts/fetch-680x0-tests.sh).
                                                 --trace prints our cycle list beside record N's own
                                                 transactions, --reads writes every record's fetch
                                                 addresses beside the reference's (Debug only), --clocks
                                                 dumps one line per record for the per-family deltas
              run [--frames N] [--inject] [--out DIR]
                                                 the synthetic trace; writes run.digest, run.state and
                                                 run.report into DIR (default runs/<stamp>/)
                                                 --inject perturbs host scheduling only (G2)

            The exit code is the verdict.
            """);
        return 1;
    }

    internal static int Unknown(string option)
    {
        Console.Error.WriteLine($"validation-harness: unknown option '{option}'");
        return Usage();
    }

    internal static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
