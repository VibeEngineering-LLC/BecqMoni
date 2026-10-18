// Decisions of the REST transport that need no network and no window, so mock tests drive them directly.
// The document mirrors the board, the board is the only source of truth.

using System;

namespace BecquerelMonitor
{
    public sealed class AtomSpectraRestBoardSample
    {
        public bool HasData;
        public long Time;
        public long Total;
        public long HistOk;
        public long AtMs;
        public int[] Bins;          // the channels of the snapshot (null for a sample without data)
        public AtomSpectraRestEpoch Epoch;   // F5 (plan K2): null when the answer carries no epoch field

        public static AtomSpectraRestBoardSample NoData(long atMs)
        {
            return new AtomSpectraRestBoardSample
            {
                HasData = false,
                Time = 0,
                Total = 0,
                HistOk = 0,
                AtMs = atMs
            };
        }
    }

    // F5 (plan K2): the board's epoch, three optional top-level fields of the v2 answer. A change of any of them (growth by
    // any amount or a drop = one event) is a new acquisition on the board: a new session, never a Trip.
    public sealed class AtomSpectraRestEpoch
    {
        public long? BootId, DevResets, SpecResets;   // null: the field is absent

        public bool Any { get { return BootId.HasValue || DevResets.HasValue || SpecResets.HasValue; } }

        // What changed since `before` (null: the same epoch); a field absent on either side is not compared
        public string ChangeFrom(AtomSpectraRestEpoch before)
        {
            if (before == null) return null;
            if (Differs(BootId, before.BootId)) return "boot_id: " + before.BootId + " -> " + BootId;
            if (Differs(DevResets, before.DevResets)) return "dev_resets: " + before.DevResets + " -> " + DevResets;
            if (Differs(SpecResets, before.SpecResets)) return "spec_resets: " + before.SpecResets + " -> " + SpecResets;
            return null;
        }

        private static bool Differs(long? now, long? before) { return now.HasValue && before.HasValue && now.Value != before.Value; }
    }

    public enum AtomSpectraRestStartChoice
    {
        Continue,
        Restart,
        Cancel
    }

    public enum AtomSpectraRestStartPlan
    {
        // Join = no command at all
        Join,
        // Resume = board stopped with data, -sta [N] without reset
        Resume,
        // StartNew = nothing to lose, -sta [N]
        StartNew,
        // Restart = -sto if counting, POST /api/reset, -sta [N]
        Restart,
        // Ask = show the dialog
        Ask,
        // Cancel
        Cancel,
        // RefuseHeadless = no window to ask and the board holds less than the document
        RefuseHeadless
    }

    public enum AtomSpectraRestUpdate
    {
        Apply,
        NoData,
        Decreased,
        Stale       // pass A N5: right after OUR reset the 2 s JSON cache still shows the acquisition before it
    }

    public static class AtomSpectraRestPolicy
    {
        // The board caches /api/spectrum.json for 2 s
        public const int MinProbeGapMs = 3000;

        public static bool IsCounting(AtomSpectraRestBoardSample first, AtomSpectraRestBoardSample second)
        {
            if (first == null || second == null)
            {
                return true;
            }

            if (first.HasData != second.HasData)
            {
                return true;
            }

            if (!first.HasData)
            {
                return false;
            }

            if (second.AtMs - first.AtMs < MinProbeGapMs)
            {
                return true;
            }

            return second.HistOk > first.HistOk || second.Time > first.Time;
        }

        public static bool BoardHasData(AtomSpectraRestBoardSample s)
        {
            return s != null && s.HasData && (s.Time > 0 || s.Total > 0);
        }

        public static bool BoardBehindDocument(AtomSpectraRestBoardSample board, long docSeconds, long docCounts)
        {
            if (docSeconds <= 0 && docCounts <= 0)
            {
                return false;
            }

            long t = BoardHasData(board) ? board.Time : 0;
            long n = BoardHasData(board) ? board.Total : 0;

            return t < docSeconds || n < docCounts;
        }

        public static AtomSpectraRestStartPlan PlanStart(bool observeOnly, bool hasWindows, bool counting, AtomSpectraRestBoardSample board, long docSeconds, long docCounts)
        {
            if (observeOnly)
            {
                return AtomSpectraRestStartPlan.Join;
            }

            bool behind = BoardBehindDocument(board, docSeconds, docCounts);
            bool ask = counting || BoardHasData(board) || docSeconds > 0 || docCounts > 0;

            if (!ask)
            {
                return AtomSpectraRestStartPlan.StartNew;
            }

            if (!hasWindows)
            {
                if (behind) return AtomSpectraRestStartPlan.RefuseHeadless;
                // pass A N7 (plan 9.1): a stopped board with data gets -sta without a reset, a counting one is joined
                return !counting && BoardHasData(board) ? AtomSpectraRestStartPlan.Resume : AtomSpectraRestStartPlan.Join;
            }

            return AtomSpectraRestStartPlan.Ask;
        }

        public static AtomSpectraRestStartPlan PlanChoice(AtomSpectraRestStartChoice choice, bool counting, bool continueAllowed)
        {
            switch (choice)
            {
                case AtomSpectraRestStartChoice.Continue:
                    if (!continueAllowed)
                    {
                        return AtomSpectraRestStartPlan.Cancel;
                    }
                    return counting ? AtomSpectraRestStartPlan.Join : AtomSpectraRestStartPlan.Resume;
                case AtomSpectraRestStartChoice.Restart:
                    return AtomSpectraRestStartPlan.Restart;
                default:
                    return AtomSpectraRestStartPlan.Cancel;
            }
        }

        // The default preset 3600 s must not stop the board at the first tick (MeasurementController.cs:749 compares the board's time).
        public static int PresetForContinue(int documentPreset, long boardSeconds, int limitSeconds)
        {
            int add = limitSeconds > 0 ? limitSeconds : Math.Max(0, documentPreset);
            long wanted = Math.Max((long)documentPreset, boardSeconds + add);
            return wanted > int.MaxValue / 2 ? int.MaxValue / 2 : (int)wanted;
        }

        public static string StartCommand(int limitSeconds)
        {
            return limitSeconds > 0 ? "-sta " + limitSeconds : "-sta";
        }
    }

    // F5 (plan R5, B6): the effective poll period. A skipped poll doubles it (ceiling 30 s, or the configured period if longer);
    // 5 full polls in a row halve it, not below the configured period and not below 2 s (the operator 18.10: not more often than configured).
    // Skip/Full: true = the period changed.
    public sealed class AtomSpectraRestPeriod
    {
        public const int CeilingMs = 30000, FloorMs = 2000, FullToHalve = 5;
        private readonly int configuredMs;
        private int fullInRow;
        public int EffectiveMs { get; private set; }
        public AtomSpectraRestPeriod(int configuredMs) { this.configuredMs = configuredMs; EffectiveMs = configuredMs; }
        public bool Skip()
        {
            fullInRow = 0;
            return Set(Math.Min(Math.Max(CeilingMs, configuredMs), EffectiveMs * 2));
        }
        public bool Full()
        {
            if (++fullInRow < FullToHalve) return false;
            fullInRow = 0;
            int next = Math.Max(Math.Max(configuredMs, FloorMs), EffectiveMs / 2);   // decision of the operator 18.10: not more often than configured
            return Set(Math.Min(EffectiveMs, next));
        }
        private bool Set(int ms) { if (ms == EffectiveMs) return false; EffectiveMs = ms; return true; }
    }

    public sealed class AtomSpectraRestGuard
    {
        private readonly object sync = new object();
        private bool hasLast;
        private long lastTime;
        private long lastTotal;
        private long lastHistOk;
        private long lastGrowthMs = -1;
        private bool sawGrowth;
        private bool histFromSample;   // lastHistOk came from the board, not from the document baseline
        private int[] lastBins;        // pass A N1: channels of the last accepted snapshot
        private bool afterReset;       // pass A N5: our reset was accepted, the board may still serve the old snapshot
        private long staleTime, staleTotal;
        private AtomSpectraRestEpoch lastEpoch;   // F5 (plan K2): the epoch of the first answer after Start, then of the last one
        // The window of our own reset (letter "pravilo-okna"): opened BEFORE the reset request goes, judged by the TIME of the snapshots.
        // Any change of the epoch triple inside it belongs to our reset; the first snapshot does not close it. It closes
        // max(6 s, 2 x period) after the last change, or max(30 s, 2 x period + 10 s) after the opening when nothing changed.
        private bool ownReset;                    // the window is open
        private AtomSpectraRestEpoch epochBeforeOwnReset;   // the epoch the board had when WE reset it (Begin forgets lastEpoch)
        private long windowT0 = -1, windowShiftAt = -1;      // AtMs of the opening (-1: the first snapshot sets it) and of the last change
        private int windowChanges, changesSinceExpect;
        private bool openedByExpect, windowPrior;   // an ExpectOwnReset is pending (not yet confirmed / cancelled); a window was open before it
        public int PeriodMs = 1000;               // the period in force, set by the session before every Check
        public Action<string> Note;               // log lines of the guard (the session writes them to the log)

        public int EpochChanges { get; private set; }
        public string LastEpochChange { get; private set; }
        public bool Tripped { get; private set; }
        public string TripReason { get; private set; }

        public void Begin(long docSeconds, long docCounts, bool counting)
        {
            lock (sync)
            {
                hasLast = docSeconds > 0 || docCounts > 0;
                lastTime = docSeconds;
                lastTotal = docCounts;
                lastHistOk = 0;
                histFromSample = false;
                lastBins = null;
                afterReset = false;
                lastEpoch = null;
                ownReset = false;
                epochBeforeOwnReset = null;
                windowT0 = -1;
                windowShiftAt = -1;
                windowChanges = 0;
                changesSinceExpect = 0;
                openedByExpect = false;
                Tripped = false;
                TripReason = null;
                sawGrowth = counting;
                lastGrowthMs = -1;
            }
        }

        // After OUR reset the next smaller snapshot is the new acquisition.
        public void AcceptReset()
        {
            long t, n;
            lock (sync) { t = lastTime; n = lastTotal; }
            AcceptReset(t, n);
        }

        // P1-1: raised BEFORE the reset request is sent. Polling goes on while the request is in flight, and a snapshot with a new
        // epoch in that window is the answer to our reset, not a board reset from outside.
        public void ExpectOwnReset() { ExpectOwnReset(-1); }

        // nowMs: the clock of the snapshots (AtMs) at the moment of opening; -1 = the first snapshot sets it.
        // A repeated Clear before the first snapshot opens the window anew (from the new t0) and keeps its reference epoch.
        public void ExpectOwnReset(long nowMs)
        {
            lock (sync)
            {
                windowPrior = ownReset;
                openedByExpect = true;
                if (!ownReset) epochBeforeOwnReset = lastEpoch;
                ownReset = true;
                windowT0 = nowMs;
                windowShiftAt = -1;
                windowChanges = 0;
                changesSinceExpect = 0;
            }
        }

        // The request was accepted: the usual acceptance, unless the new acquisition has already been taken inside the window.
        public void ConfirmOwnReset()
        {
            bool done;
            lock (sync) { done = changesSinceExpect > 0; }
            if (!done) AcceptReset();
            lock (sync) { openedByExpect = false; }
        }

        // The request certainly never reached the board (NotSent) or the board said the reset was not done (sent=false):
        // the window is withdrawn at once. An unknown outcome (lost answer, exception) does NOT call this: the window lives to its term.
        public void CancelOwnReset()
        {
            lock (sync)
            {
                if (!openedByExpect || windowPrior || changesSinceExpect > 0) return;
                ownReset = false;
                epochBeforeOwnReset = null;
                windowT0 = -1;
                windowShiftAt = -1;
                openedByExpect = false;
            }
        }

        // preTime/preTotal: the board before our reset; a snapshot not below them is the cached old one (pass A N5).
        public void AcceptReset(long preTime, long preTotal)
        {
            AtomSpectraRestEpoch before;
            long t0, shift;
            bool wasOpen, byExpect;
            int changes, since;
            lock (sync)
            {
                wasOpen = ownReset; byExpect = openedByExpect; changes = windowChanges; since = changesSinceExpect;
                wasOpen = ownReset;
                t0 = wasOpen && byExpect ? windowT0 : -1; shift = wasOpen && byExpect ? windowShiftAt : -1;   // a bare reset (Start over) starts a window of its own
                before = lastEpoch ?? (wasOpen ? epochBeforeOwnReset : null);
                // a repeated reset before the first snapshot of the new set: the numbers of the board BEFORE the first reset stay
                if (wasOpen && !hasLast) { preTime = Math.Max(preTime, staleTime); preTotal = Math.Max(preTotal, staleTotal); }
            }
            Begin(0, 0, true);
            lock (sync)
            {
                staleTime = preTime;
                staleTotal = preTotal;
                afterReset = preTime > 0 || preTotal > 0;
                ownReset = true;               // the epoch of the board changes because of THIS reset, that is no board reset
                epochBeforeOwnReset = before;
                windowT0 = t0; windowShiftAt = shift; windowChanges = wasOpen && byExpect ? changes : 0; changesSinceExpect = wasOpen && byExpect ? since : 0;
                openedByExpect = wasOpen && byExpect;
            }
        }

        // The window ends max(6 s, 2 x period) after the last change; without a change max(30 s, 2 x period + 10 s) after the opening.
        private void ExpireWindow(long at)
        {
            if (!ownReset) return;
            if (windowT0 < 0) windowT0 = at;
            long term = windowShiftAt >= 0 ? windowShiftAt + Math.Max(6000L, 2L * PeriodMs) : windowT0 + Math.Max(30000L, 2L * PeriodMs + 10000L);
            if (at <= term) return;
            ownReset = false;
            epochBeforeOwnReset = null;
            windowT0 = windowShiftAt = -1;
            openedByExpect = false;
        }

        // A change of the triple inside the window: a new acquisition of our own reset. The first one is its answer; each further one
        // (a real reset from outside, or a second step of ours) is swallowed and written to the log once.
        private void AbsorbChange(string change, long at)
        {
            afterReset = false;
            windowChanges++;
            changesSinceExpect++;
            windowShiftAt = at;
            hasLast = false; lastBins = null; histFromSample = false; lastHistOk = 0;
            if (windowChanges >= 2 && Note != null) Note("external reset swallowed by own-reset window: " + change);
        }

        public AtomSpectraRestUpdate Check(AtomSpectraRestBoardSample s)
        {
            lock (sync)
            {
                if (Tripped)
                {
                    return AtomSpectraRestUpdate.Decreased;
                }

                // F5 (plan K2, decision of the operator 17.10): a changed epoch is a reset of the board, polling stops and the document
                // is never overwritten (the same path as a drop of the counters), unless the window of OUR reset is open: then the
                // change belongs to it (checked BEFORE Stale: a new set that has caught up with the old numbers is not the cached one).
                if (s != null) ExpireWindow(s.AtMs);
                AtomSpectraRestEpoch refEpoch = lastEpoch ?? (ownReset ? epochBeforeOwnReset : null);
                string change = s != null && s.HasData && s.Epoch != null ? s.Epoch.ChangeFrom(refEpoch) : null;
                if (change != null && !ownReset)
                {
                    EpochChanges++;
                    LastEpochChange = change;
                    return Trip("board reset: " + change);
                }
                if (change != null) AbsorbChange(change, s.AtMs);

                // no reference epoch (Start over with probes that carry none): the first snapshot with an epoch is the reference
                if (afterReset && s != null && s.HasData && s.Time >= staleTime && s.Total >= staleTotal && !(s.Epoch != null && refEpoch == null))
                {
                    return AtomSpectraRestUpdate.Stale;   // the cached snapshot from before our reset: ignored
                }
                afterReset = false;

                if (s == null || !s.HasData)
                {
                    // F5 (plan K6): a board that reports its epoch answers 404 after a reset until the first sweep;
                    // the epoch of the next snapshot tells a new acquisition apart, so the 404 itself is no Trip
                    if (hasLast && (lastTime > 0 || lastTotal > 0) && lastEpoch == null)
                    {
                        return Trip("the board has no spectrum any more (404 after time " + lastTime + " s, total " + lastTotal + ")");
                    }
                    return AtomSpectraRestUpdate.NoData;
                }

                if (hasLast && (s.Time < lastTime || s.Total < lastTotal || (histFromSample && s.HistOk < lastHistOk)))
                {
                    return Trip("board counters went down: time " + lastTime + " -> " + s.Time + " s, total " + lastTotal + " -> " + s.Total + ", hist_ok " + lastHistOk + " -> " + s.HistOk);
                }

                int down = FirstDecrease(lastBins, s.Bins);
                if (down >= 0)
                {
                    return Trip("spectrum channel " + down + " went down (" + lastBins[down] + " -> " + s.Bins[down] + "): another acquisition replaced ours");
                }

                bool grew = !hasLast || s.Time > lastTime || (histFromSample && s.HistOk > lastHistOk);
                if (grew)
                {
                    if (hasLast)
                    {
                        sawGrowth = true;
                    }
                    lastGrowthMs = s.AtMs;
                }
                else if (lastGrowthMs < 0)
                {
                    lastGrowthMs = s.AtMs;
                }

                hasLast = true;
                lastTime = s.Time;
                lastTotal = s.Total;
                lastHistOk = s.HistOk;
                lastBins = s.Bins;
                if (s.Epoch != null) lastEpoch = s.Epoch;
                histFromSample = true;
                return AtomSpectraRestUpdate.Apply;
            }
        }

        // First channel that went down (-1: none): a continuing acquisition never loses counts in a channel (pass A N1).
        private static int FirstDecrease(int[] before, int[] now)
        {
            if (before == null || now == null || before.Length != now.Length) return -1;
            for (int i = 0; i < now.Length; i++)
            {
                if (now[i] < before[i]) return i;
            }
            return -1;
        }

        public bool Stalled(long nowMs, long stallMs)
        {
            lock (sync)
            {
                return sawGrowth && lastGrowthMs >= 0 && nowMs - lastGrowthMs >= stallMs;
            }
        }

        private AtomSpectraRestUpdate Trip(string reason)
        {
            Tripped = true;
            TripReason = reason;
            return AtomSpectraRestUpdate.Decreased;
        }
    }
}
