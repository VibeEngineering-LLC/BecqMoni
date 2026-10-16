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
        public int[] Bins;          // the 8192 channels of the snapshot (null for a sample without data)

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

                if (s == null || !s.HasData)
                {
                    if (hasLast && (lastTime > 0 || lastTotal > 0))
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
