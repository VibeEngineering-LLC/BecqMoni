using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using BecquerelMonitor;

namespace SeriesTestRun
{
    public class FakeHost : ISeriesHost
    {
        public DateTime Clock = new DateTime(2026, 10, 24, 12, 0, 0);
        public HashSet<string> Existing = new HashSet<string>();
        public List<string> Calls = new List<string>();
        public List<string> States = new List<string>();
        public List<string> Overwrites = new List<string>();
        public long Free = -1;
        public string SaveError;
        public string ClearError;
        public string StartError;

        public DateTime Now => Clock;

        public void Advance(int seconds)
        {
            Clock = Clock.AddSeconds(seconds);
        }

        public bool FileExists(string path)
        {
            return Existing.Contains(path);
        }

        public long FreeDiskBytes(string folder)
        {
            return Free;
        }

        public bool SaveSpectrum(string path, out string error)
        {
            if (SaveError != null)
            {
                error = SaveError;
                return false;
            }
            if (Existing.Contains(path))
            {
                Overwrites.Add(path);
            }
            Existing.Add(path);
            Calls.Add("save:" + path);
            error = null;
            return true;
        }

        public bool ClearSpectrum(out string error)
        {
            if (ClearError != null)
            {
                error = ClearError;
                return false;
            }
            Calls.Add("clear");
            error = null;
            return true;
        }

        public bool StartAcquisition(int presetSeconds, out string error)
        {
            if (StartError != null)
            {
                error = StartError;
                return false;
            }
            Calls.Add("start:" + presetSeconds);
            error = null;
            return true;
        }

        public void StateChanged()
        {
            States.Add("state");
        }

        public void SeriesFinished()
        {
            Calls.Add("finished");
        }
    }

    public class SeriesPair
    {
        public FakeHost Host;
        public MeasurementSeries Series;
    }

    public static class Program
    {
        static string P(string name)
        {
            return @"C:\s\" + name + ".xml";
        }

        static SeriesPair MakeSeries(int count, int delay, bool clear, string prefix = "sp", bool noOverwrite = true, int preset = 600, FakeHost host = null)
        {
            if (host == null) host = new FakeHost();
            var settings = new SeriesSettings
            {
                Enabled = true,
                Count = count,
                DelaySeconds = delay,
                ClearBetween = clear,
                Folder = @"C:\s",
                Template = "{prefix}_{n:000}",
                Prefix = prefix,
                NoOverwrite = noOverwrite
            };
            var series = new MeasurementSeries(settings, host, preset);
            return new SeriesPair { Host = host, Series = series };
        }

        static void True(bool cond, string msg)
        {
            if (!cond) throw new Exception(msg);
        }

        static void Eq<T>(T expected, T actual, string what)
        {
            bool same;
            if (expected is System.Collections.Generic.IEnumerable<string> && actual is System.Collections.Generic.IEnumerable<string>)
            {
                same = ((System.Collections.Generic.IEnumerable<string>)expected).SequenceEqual((System.Collections.Generic.IEnumerable<string>)actual);
            }
            else
            {
                same = EqualityComparer<T>.Default.Equals(expected, actual);
            }
            if (!same)
            {
                throw new Exception(what + ": expected " + Show(expected) + " but got " + Show(actual));
            }
        }

        static string Show(object o)
        {
            var e = o as System.Collections.Generic.IEnumerable<string>;
            return e != null && !(o is string) ? "[" + string.Join(", ", e) + "]" : Convert.ToString(o);
        }

        static void Contains(string haystack, string needle, string what)
        {
            if (haystack == null || !haystack.Contains(needle))
            {
                throw new Exception(what + ": expected to contain '" + needle + "' but was '" + haystack + "'");
            }
        }

        static void T1_N1_single()
        {
            var pair = MakeSeries(1, 30, true);
            pair.Series.Begin(0);
            pair.Series.OnSpectrumComplete();

            Eq(SeriesState.Done, pair.Series.State, "State");
            Eq(1, pair.Host.Calls.Count(c => c.StartsWith("save:")), "save count");
            True(!pair.Host.Calls.Any(c => c == "clear"), "no clear");
            True(!pair.Host.Calls.Any(c => c.StartsWith("start:")), "no start");
            Eq(1, pair.Host.Calls.Count(c => c == "finished"), "finished count");
            Eq(1, pair.Series.Files.Count, "Files.Count");
        }

        static void T2_Delay0_immediate()
        {
            var pair = MakeSeries(3, 0, true);
            pair.Series.Begin(0);
            pair.Series.OnSpectrumComplete();

            Eq(SeriesState.Acquiring, pair.Series.State, "State");
            Eq(2, pair.Series.Index, "Index");
        }

        static void T3_NoClear_accumulating()
        {
            var pair = MakeSeries(3, 0, false, preset: 600);
            pair.Series.Begin(0);
            Eq(600, pair.Series.PresetFor(1), "PresetFor(1)");
            Eq(1200, pair.Series.PresetFor(2), "PresetFor(2)");

            pair.Series.OnSpectrumComplete();
            pair.Series.OnSpectrumComplete();

            True(!pair.Host.Calls.Any(c => c == "clear"), "no clear");
            var starts = pair.Host.Calls.Where(c => c.StartsWith("start:")).ToList();
            Eq(2, starts.Count, "start count");
            Eq("start:1200", starts[0], "start 1");
            Eq("start:1800", starts[1], "start 2");

            var pair2 = MakeSeries(3, 0, false, preset: 600);
            pair2.Series.Begin(300);
            Eq(900, pair2.Series.PresetFor(1), "PresetFor(1) base 300");
        }

        static void T4_StopInDelay_nothing_written()
        {
            var pair = MakeSeries(3, 60, true);
            pair.Series.Begin(0);
            pair.Series.OnSpectrumComplete();

            Eq(SeriesState.Delay, pair.Series.State, "State");
            True(!pair.Series.StopNeedsQuestion, "StopNeedsQuestion");
            pair.Series.UserStop(true);

            Eq(SeriesState.Idle, pair.Series.State, "State after stop");
            Eq(1, pair.Host.Calls.Count(c => c.StartsWith("save:")), "save count");
            Eq(1, pair.Series.Files.Count, "Files.Count");
        }

        static void T5_StopInAcquiring()
        {
            var pair = MakeSeries(3, 0, true);
            pair.Series.Begin(0);
            True(pair.Series.StopNeedsQuestion, "StopNeedsQuestion");
            pair.Series.UserStop(false);
            Eq(SeriesState.Idle, pair.Series.State, "State");
            True(!pair.Host.Calls.Any(c => c.StartsWith("save:")), "no save");

            var pair2 = MakeSeries(3, 0, true);
            pair2.Series.Begin(0);
            pair2.Series.OnSpectrumComplete();
            pair2.Series.UserStop(true);
            Eq(2, pair2.Series.Files.Count, "Files.Count");
            Eq(P("sp_002"), pair2.Series.Files[1], "Files[1]");
        }

        static void T6_DeviceLost_in_pause()
        {
            var pair = MakeSeries(3, 10, true);
            pair.Series.Begin(0);
            pair.Series.OnSpectrumComplete();
            pair.Host.StartError = "connection lost";
            pair.Host.Advance(10);
            pair.Series.Tick();

            Eq(SeriesState.PausedError, pair.Series.State, "State");
            Contains(pair.Series.LastError, "connection lost", "LastError");
            Eq(1, pair.Series.Files.Count, "Files.Count");
            Eq(1, pair.Series.Index, "Index");
            pair.Host.Advance(100);
            pair.Series.Tick();
            Eq(SeriesState.PausedError, pair.Series.State, "State after tick");

            var pair2 = MakeSeries(3, 10, true);
            pair2.Series.Begin(0);
            pair2.Series.OnDeviceLost("cable");
            Eq(SeriesState.PausedError, pair2.Series.State, "State");
            Contains(pair2.Series.LastError, "cable", "LastError");
            pair2.Series.OnSpectrumComplete();
            True(!pair2.Host.Calls.Any(c => c.StartsWith("save:")), "no save");
            Eq(SeriesState.PausedError, pair2.Series.State, "State unchanged");

            var pair3 = MakeSeries(3, 10, true);
            pair3.Series.Begin(0);
            pair3.Series.OnSpectrumComplete();
            pair3.Series.OnDeviceLost("gone");
            pair3.Host.Advance(20);
            pair3.Series.Tick();
            Eq(SeriesState.PausedError, pair3.Series.State, "State");
            True(!pair3.Host.Calls.Any(c => c.StartsWith("start:")), "no start");
        }

        static void T7_NameTaken_suffix()
        {
            var pair = MakeSeries(2, 0, true);
            pair.Host.Existing.Add(P("sp_001"));
            pair.Series.Begin(0);
            pair.Series.OnSpectrumComplete();

            Eq(P("sp_001_1"), pair.Series.Files[0], "Files[0]");
            True(pair.Host.Overwrites.Count == 0, "Overwrites empty");

            var pair2 = MakeSeries(2, 0, true, noOverwrite: false);
            pair2.Host.Existing.Add(P("sp_001"));
            pair2.Series.Begin(0);
            pair2.Series.OnSpectrumComplete();

            Eq(P("sp_001"), pair2.Series.Files[0], "Files[0]");
            True(pair2.Host.Overwrites.Contains(P("sp_001")), "Overwrites contains");
        }

        static void T8_Two_documents_independent()
        {
            var pairA = MakeSeries(3, 0, true);
            var pairB = MakeSeries(3, 0, true);
            pairA.Series.Begin(0);
            pairB.Series.Begin(0);
            pairA.Series.OnSpectrumComplete();
            pairA.Series.OnSpectrumComplete();

            Eq(3, pairA.Series.Index, "A.Index");
            Eq(1, pairB.Series.Index, "B.Index");
            Eq(SeriesState.Acquiring, pairB.Series.State, "B.State");
            True(pairB.Host.Calls.Count == 0, "B no calls");
        }

        static void T9_Endless_and_disk()
        {
            var pair = MakeSeries(0, 0, true);
            pair.Series.Begin(0);
            for (int i = 0; i < 1200; i++)
            {
                pair.Series.OnSpectrumComplete();
            }

            Eq(SeriesState.Acquiring, pair.Series.State, "State");
            Eq(1201, pair.Series.Index, "Index");
            Eq(1200, pair.Series.Files.Count, "Files.Count");
            True(pair.Series.Files.Contains(P("sp_1000")), "Files contains sp_1000");
            True(pair.Series.Files.Contains(P("sp_001")), "Files contains sp_001");

            var pair2 = MakeSeries(0, 0, true);
            pair2.Host.Free = 500000000;
            pair2.Series.Begin(0);
            pair2.Series.OnSpectrumComplete();
            Eq(SeriesState.PausedError, pair2.Series.State, "State");
            Contains(pair2.Series.LastError, "1 GB", "LastError");
            True(!pair2.Host.Calls.Any(c => c.StartsWith("save:")), "no save");

            var pair3 = MakeSeries(0, 0, true);
            pair3.Host.Free = -1;
            pair3.Series.Begin(0);
            pair3.Series.OnSpectrumComplete();
            True(pair3.Host.Calls.Any(c => c.StartsWith("save:")), "save called");

            var pair4 = MakeSeries(3, 0, true);
            pair4.Host.Free = 500000000;
            pair4.Series.Begin(0);
            pair4.Series.OnSpectrumComplete();
            True(pair4.Host.Calls.Any(c => c.StartsWith("save:")), "save called");
        }

        static void T10_SaveFailure_keeps_data()
        {
            var pair = MakeSeries(3, 0, true);
            pair.Host.SaveError = "disk full";
            pair.Series.Begin(0);
            pair.Series.OnSpectrumComplete();

            Eq(SeriesState.PausedError, pair.Series.State, "State");
            Contains(pair.Series.LastError, "disk full", "LastError");
            Eq(1, pair.Series.Index, "Index");
            Eq(0, pair.Series.Files.Count, "Files.Count");
            True(!pair.Host.Calls.Any(c => c.StartsWith("start:")), "no start");

            pair.Series.Begin(0);
            Eq(SeriesState.Acquiring, pair.Series.State, "State after restart");
            Eq(1, pair.Series.Index, "Index after restart");
            Eq("", pair.Series.LastError, "LastError after restart");
        }

        static void T11_Order_clear_before_start()
        {
            var pair = MakeSeries(3, 0, true, preset: 600);
            pair.Series.Begin(0);
            pair.Series.OnSpectrumComplete();

            var expected = new List<string> { "save:" + P("sp_001"), "clear", "start:600" };
            Eq(expected, pair.Host.Calls, "Calls");

            pair.Series.OnSpectrumComplete();
            var nextThree = pair.Host.Calls.Skip(3).Take(3).ToList();
            var expectedNext = new List<string> { "save:" + P("sp_002"), "clear", "start:600" };
            Eq(expectedNext, nextThree, "Next three calls");
        }

        static void T12_Names_follow_index()
        {
            var pair = MakeSeries(3, 0, true);
            pair.Series.Begin(0);
            pair.Series.OnSpectrumComplete();
            pair.Series.OnSpectrumComplete();
            pair.Series.OnSpectrumComplete();

            Eq(SeriesState.Done, pair.Series.State, "State");
            var expectedFiles = new List<string> { P("sp_001"), P("sp_002"), P("sp_003") };
            Eq(expectedFiles, pair.Series.Files.ToList(), "Files");
            Eq(1, pair.Host.Calls.Count(c => c == "finished"), "finished count");
        }

        static void T13_Delay_countdown()
        {
            var pair = MakeSeries(2, 60, true);
            pair.Series.Begin(0);
            pair.Series.OnSpectrumComplete();

            Eq(SeriesState.Delay, pair.Series.State, "State");
            Eq(60, pair.Series.SecondsToResume, "SecondsToResume");
            pair.Host.Advance(59);
            pair.Series.Tick();
            Eq(SeriesState.Delay, pair.Series.State, "State after 59s");
            Eq(1, pair.Series.SecondsToResume, "SecondsToResume after 59s");
            pair.Host.Advance(1);
            pair.Series.Tick();
            Eq(SeriesState.Acquiring, pair.Series.State, "State after 60s");
            Eq(2, pair.Series.Index, "Index");

            var pair2 = MakeSeries(2, 60, true);
            pair2.Series.Begin(0);
            pair2.Series.OnSpectrumComplete();
            pair2.Series.SkipDelay();
            Eq(SeriesState.Acquiring, pair2.Series.State, "State after SkipDelay");
            Eq(2, pair2.Series.Index, "Index after SkipDelay");
        }

        static void T14_ClearFailure()
        {
            var pair = MakeSeries(3, 0, true);
            pair.Host.ClearError = "board refused reset";
            pair.Series.Begin(0);
            pair.Series.OnSpectrumComplete();

            Eq(SeriesState.PausedError, pair.Series.State, "State");
            Contains(pair.Series.LastError, "board refused reset", "LastError");
            True(!pair.Host.Calls.Any(c => c.StartsWith("start:")), "no start");
        }

        static int Main(string[] args)
        {
            var tests = new (string name, Action body)[]
            {
                ("T1_N1_single", T1_N1_single),
                ("T2_Delay0_immediate", T2_Delay0_immediate),
                ("T3_NoClear_accumulating", T3_NoClear_accumulating),
                ("T4_StopInDelay_nothing_written", T4_StopInDelay_nothing_written),
                ("T5_StopInAcquiring", T5_StopInAcquiring),
                ("T6_DeviceLost_in_pause", T6_DeviceLost_in_pause),
                ("T7_NameTaken_suffix", T7_NameTaken_suffix),
                ("T8_Two_documents_independent", T8_Two_documents_independent),
                ("T9_Endless_and_disk", T9_Endless_and_disk),
                ("T10_SaveFailure_keeps_data", T10_SaveFailure_keeps_data),
                ("T11_Order_clear_before_start", T11_Order_clear_before_start),
                ("T12_Names_follow_index", T12_Names_follow_index),
                ("T13_Delay_countdown", T13_Delay_countdown),
                ("T14_ClearFailure", T14_ClearFailure)
            };

            int pass = 0;
            int fail = 0;

            foreach (var test in tests)
            {
                if (args.Length == 1 && !test.name.StartsWith(args[0]))
                {
                    continue;
                }

                try
                {
                    test.body();
                    Console.WriteLine("PASS " + test.name);
                    pass++;
                }
                catch (Exception e)
                {
                    Console.WriteLine("FAIL " + test.name + ": " + e.Message);
                    fail++;
                }
            }

            Console.WriteLine("SUMMARY pass=" + pass + " fail=" + fail);
            return fail;
        }
    }
}
