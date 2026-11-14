#if DEBUG
// ============================================================================================
// Rule 3 of #43 — no answer is read from an instant the ring no longer holds.
//
// The one failure a digest structurally cannot see: a record overwritten under a live reader changes
// an *answer* while the record stream stays identical, so two runs agree and both are wrong. G7
// (check-window) proves the ring's algebra and one live sampler; this proves the machine never asks
// for something the window has already dropped — on every log, on every run the harness makes.
//
// Compiled out of a release build (ADR 0008 §6), like Engine.Debug.cs: the partial methods it
// implements are declared unimplemented, so a release build carries neither the calls nor the text.
// ============================================================================================

namespace TheST.Machine;

public sealed partial class ValueLog
{
    partial void OnAsked(long instant)
    {
        if (!Verifying || Overwritten == 0 || _count == 0 || instant == long.MinValue)
        {
            return;
        }

        if (instant < _records[_head].Instant)
        {
            throw Fail($"a sample at {instant} is below the window (oldest held {_records[_head].Instant}, {Overwritten} overwritten)");
        }
    }

    partial void OnCursor(long seq)
    {
        // a cursor one behind the window is exact: nothing between it and the oldest record held
        if (!Verifying || Overwritten == 0 || _count == 0 || seq == long.MinValue)
        {
            return;
        }

        if (seq < _records[_head].Seq - 1)
        {
            throw Fail($"a cursor at seq {seq} is below the window (oldest held {_records[_head].Seq}, {Overwritten} overwritten) — the scan would silently skip what it lost");
        }
    }

    partial void OnAskedAt(long seq)
    {
        if (!Verifying || Overwritten == 0 || _count == 0 || seq < 0)
        {
            return;
        }

        if (seq < _records[_head].Seq)
        {
            throw Fail($"a record named by seq {seq} was overwritten (oldest held {_records[_head].Seq}, {Overwritten} overwritten)");
        }
    }

    InvalidOperationException Fail(string detail) =>
        Log(new InvalidOperationException($"ASSERTION 8 failed — {Channel.Name}: {detail} (#43 rule 3; design/scheduler.md §4)"));

    // A chip's coroutine is launched fire-and-forget (`_ = Run()`), so an exception thrown inside it is
    // captured in a ValueTask nobody observes and the machine simply stops doing work — a stall, not a
    // failure. Printing before throwing makes a window violation visible in the harness log.
    static InvalidOperationException Log(InvalidOperationException failure)
    {
        Console.Error.WriteLine(failure.Message);
        return failure;
    }
}
#endif
