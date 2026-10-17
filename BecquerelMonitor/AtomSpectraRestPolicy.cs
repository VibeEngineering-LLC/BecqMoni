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
            if (Differs(BootId, before.BootId)) return "boot_id " + before.BootId + " -> " + BootId;
            if (Differs(DevResets, before.DevResets)) return "dev_resets " + before.DevResets + " -> " + DevResets;
            if (Differs(SpecResets, before.SpecResets)) return "spec_resets " + before.SpecResets + " -> " + SpecResets;
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
    // 5 full polls in a row halve it, not below the configured period and not below 2 s. Skip/Full: true = the period changed.
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
            int next = Math.Max(Math.Max(configuredMs, FloorMs), EffectiveMs / 2);
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

        // preTime/preTotal: the board before our reset; a snapshot not below them is the cached old one (pass A N5).
        public void AcceptReset(long preTime, long preTotal)
        {
            Begin(0, 0, true);
            lock (sync)
            {
                staleTime = preTime;
                staleTotal = preTotal;
                afterReset = preTime > 0 || preTotal > 0;
            }
        }

        public AtomSpectraRestUpdate Check(AtomSpectraRestBoardSample s)
        {
            lock (sync)
            {
                if (Tripped)
                {
                    return AtomSpectraRestUpdate.Decreased;
                }

                if (afterReset && s != null && s.HasData && s.Time >= staleTime && s.Total >= staleTotal)
                {
                    return AtomSpectraRestUpdate.Stale;   // the cached snapshot from before our reset: ignored
                }
                afterReset = false;

                // F5 (plan K2): a changed epoch = a new acquisition on the board; the path of our own reset, no Trip
                string change = s != null && s.HasData && s.Epoch != null ? s.Epoch.ChangeFrom(lastEpoch) : null;
                if (change != null)
                {
                    AcceptReset(0, 0);
                    EpochChanges++;
                    LastEpochChange = change;
                }

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
