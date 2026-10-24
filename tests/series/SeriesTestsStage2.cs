using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using BecquerelMonitor;

namespace SeriesTestRun
{
    public static partial class Program
    {
        static void W1_Wait_seconds_resolution()
        {
            DateTime now = new DateTime(2026, 10, 24, 12, 0, 0);

            var s0 = new SeriesSettings();
            Eq(0, s0.WaitSecondsFrom(now), "default kind 0");

            var s2a = new SeriesSettings { StartKind = 2, StartInSeconds = 0 };
            Eq(0, s2a.WaitSecondsFrom(now), "kind 2 zero");

            var s2b = new SeriesSettings { StartKind = 2, StartInSeconds = 90 };
            Eq(90, s2b.WaitSecondsFrom(now), "kind 2 90");

            var s1a = new SeriesSettings { StartKind = 1, StartAt = now.AddHours(2) };
            Eq(7200, s1a.WaitSecondsFrom(now), "kind 1 +2h");

            var s1b = new SeriesSettings { StartKind = 1, StartAt = now };
            Eq(0, s1b.WaitSecondsFrom(now), "kind 1 equal");

            var s1c = new SeriesSettings { StartKind = 1, StartAt = now.AddSeconds(-30) };
            Eq(-30, s1c.WaitSecondsFrom(now), "kind 1 past");

            var s1d = new SeriesSettings { StartKind = 1, StartAt = now.AddMilliseconds(1500) };
            Eq(2, s1d.WaitSecondsFrom(now), "kind 1 ceiling");

            var host = new FakeHost();
            var series = MakeSeries(1, 0, false, host: host).Series;
            bool threw = false;
            try
            {
                series.BeginWaiting(0, 0);
            }
            catch (ArgumentOutOfRangeException)
            {
                threw = true;
            }
            True(threw, "BeginWaiting(0,0) throws");
            Eq(SeriesState.Idle, series.State, "state stays Idle");
        }

        static void W2_Waiting_waits_then_starts()
        {
            var host = new FakeHost();
            var series = MakeSeries(3, 0, true, preset: 600, host: host).Series;

            series.BeginWaiting(0, 60);
            Eq(SeriesState.Waiting, series.State, "state Waiting");
            True(series.IsActive, "IsActive");
            True(!series.StopNeedsQuestion, "StopNeedsQuestion false");
            Eq(60, series.SecondsToStart, "SecondsToStart 60");
            Eq(0, host.Calls.Count, "Calls empty");

            host.Advance(59);
            series.Tick();
            Eq(SeriesState.Waiting, series.State, "still Waiting after 59");
            Eq(1, series.SecondsToStart, "SecondsToStart 1");
            Eq(0, host.Calls.Count, "Calls still empty");

            host.Advance(1);
            series.Tick();
            Eq(SeriesState.Acquiring, series.State, "Acquiring after deadline");
            Eq(1, series.Index, "Index 1");
            Eq(new[] { "clear", "start:600" }, host.Calls.ToArray(), "Calls clear+start");
        }

        static void W3_System_clock_jump_ignored()
        {
            var host = new FakeHost();
            var series = MakeSeries(1, 0, false, host: host).Series;

            series.BeginWaiting(0, 60);
            host.JumpClock(3600);
            series.Tick();
            Eq(SeriesState.Waiting, series.State, "still Waiting after +1h wall");
            Eq(0, host.Calls.Count, "Calls empty after +1h wall");

            host.JumpClock(-7200);
            host.Advance(59);
            series.Tick();
            Eq(SeriesState.Waiting, series.State, "still Waiting after 59 mono");

            host.Advance(1);
            series.Tick();
            Eq(SeriesState.Acquiring, series.State, "Acquiring after 60 mono");

            var host2 = new FakeHost();
            var series2 = MakeSeries(1, 0, false, host: host2).Series;
            series2.BeginWaiting(0, 60);
            host2.JumpClock(-86400);
            host2.Advance(59);
            series2.Tick();
            Eq(SeriesState.Waiting, series2.State, "still Waiting after -1d wall");
            host2.Advance(1);
            series2.Tick();
            Eq(SeriesState.Acquiring, series2.State, "Acquiring after -1d wall + 60 mono");
        }

        static void W4_Stop_in_waiting()
        {
            var host = new FakeHost();
            var series = MakeSeries(1, 0, false, host: host).Series;

            series.BeginWaiting(0, 600);
            series.UserStop(true);
            Eq(SeriesState.Idle, series.State, "Idle after stop");
            Eq(0, host.Calls.Count, "Calls empty");
            True(!host.KeepAwake, "KeepAwake false");
            Eq(0, series.Files.Count, "Files empty");

            host.Advance(1000);
            series.Tick();
            Eq(SeriesState.Idle, series.State, "still Idle after tick");
            Eq(0, host.Calls.Count, "Calls still empty");
        }

        static void W5_KeepAwake_lifecycle()
        {
            var host = new FakeHost();
            var series = MakeSeries(1, 0, false, host: host).Series;
            series.BeginWaiting(0, 60);
            True(host.KeepAwake, "KeepAwake true");
            Eq(new[] { true }, host.KeepAwakeLog.ToArray(), "Log [true]");

            host.Advance(60);
            series.Tick();
            True(!host.KeepAwake, "KeepAwake false after tick");
            Eq(new[] { true, false }, host.KeepAwakeLog.ToArray(), "Log [true,false]");

            var host2 = new FakeHost();
            var series2 = MakeSeries(1, 0, false, host: host2).Series;
            series2.BeginWaiting(0, 60);
            series2.UserStop(false);
            Eq(new[] { true, false }, host2.KeepAwakeLog.ToArray(), "Log [true,false] after stop");

            var host3 = new FakeHost { StartError = "boom" };
            var series3 = MakeSeries(1, 0, false, host: host3).Series;
            series3.BeginWaiting(0, 10);
            host3.Advance(10);
            series3.Tick();
            Eq(SeriesState.PausedError, series3.State, "PausedError");
            Contains("boom", series3.LastError, "LastError contains boom");
            True(!host3.KeepAwake, "KeepAwake false");
            Eq(new[] { true, false }, host3.KeepAwakeLog.ToArray(), "Log [true,false]");

            var host4 = new FakeHost();
            var series4 = MakeSeries(1, 0, false, host: host4).Series;
            series4.Begin(0);
            Eq(0, host4.KeepAwakeLog.Count, "Log empty for Begin");
        }

        static void W6_Waiting_ignores_other_calls()
        {
            var host = new FakeHost();
            var series = MakeSeries(1, 0, false, host: host).Series;

            series.BeginWaiting(0, 60);
            series.OnSpectrumComplete();
            series.SkipDelay();
            series.OnDeviceLost("x");
            series.Begin(0);

            Eq(SeriesState.Waiting, series.State, "still Waiting");
            Eq(0, host.Calls.Count, "Calls empty");
            Eq(new[] { true }, host.KeepAwakeLog.ToArray(), "Log [true]");
        }

        static void W7_Settings_persistence()
        {
            var s = new SeriesSettings { Enabled = true, Count = 3, StartKind = 2, StartInSeconds = 7200 };
            var sw = new StringWriter();
            new XmlSerializer(typeof(SeriesSettings)).Serialize(sw, s);
            var back = (SeriesSettings)new XmlSerializer(typeof(SeriesSettings)).Deserialize(new StringReader(sw.ToString()));

            Eq(2, back.StartKind, "StartKind 2");
            Eq(7200, back.StartInSeconds, "StartInSeconds 7200");

            DateTime t1 = new DateTime(2026, 10, 24, 12, 0, 0);
            Eq(7200, back.WaitSecondsFrom(t1), "WaitSecondsFrom t1");
            Eq(7200, back.WaitSecondsFrom(t1.AddDays(1)), "WaitSecondsFrom t1+1d");

            var sAbs = new SeriesSettings { StartKind = 1, StartAt = new DateTime(2026, 10, 25, 2, 30, 0) };
            var sw2 = new StringWriter();
            new XmlSerializer(typeof(SeriesSettings)).Serialize(sw2, sAbs);
            var back2 = (SeriesSettings)new XmlSerializer(typeof(SeriesSettings)).Deserialize(new StringReader(sw2.ToString()));
            Eq(new DateTime(2026, 10, 25, 2, 30, 0), back2.StartAt, "StartAt round-trip");

            var clone = s.Clone();
            Eq(2, clone.StartKind, "Clone StartKind");
            Eq(7200, clone.StartInSeconds, "Clone StartInSeconds");

            var n1 = new SeriesSettings { StartInSeconds = -5 };
            n1.Normalize();
            Eq(0, n1.StartInSeconds, "Normalize -5 -> 0");

            var n2 = new SeriesSettings { StartInSeconds = 400000 };
            n2.Normalize();
            Eq(359999, n2.StartInSeconds, "Normalize 400000 -> 359999");

            var n3 = new SeriesSettings { StartKind = 7 };
            n3.Normalize();
            Eq(0, n3.StartKind, "Normalize kind 7 -> 0");

            var n4 = new SeriesSettings { StartKind = -1 };
            n4.Normalize();
            Eq(0, n4.StartKind, "Normalize kind -1 -> 0");
        }

        static void W8_Expected_end_from_start()
        {
            var s = new SeriesSettings { Count = 3, DelaySeconds = 60 };
            DateTime now = new DateTime(2026, 10, 24, 12, 0, 0);

            s.StartKind = 0;
            Eq(now.AddSeconds(1920), s.ExpectedEnd(now, 600), "kind 0 end");

            s.StartKind = 1;
            s.StartAt = now.AddHours(2);
            Eq(s.StartAt.AddSeconds(1920), s.ExpectedEnd(now, 600), "kind 1 end");
            True(s.ExpectedEnd(now, 600) != now.AddSeconds(1920), "kind 1 not now-based");

            s.StartKind = 2;
            s.StartInSeconds = 90;
            Eq(now.AddSeconds(90 + 1920), s.ExpectedEnd(now, 600), "kind 2 end");

            s.Count = 0;
            s.StartKind = 0;
            True(s.ExpectedEnd(now, 600) == null, "count 0 kind 0 null");
            s.StartKind = 1;
            True(s.ExpectedEnd(now, 600) == null, "count 0 kind 1 null");
            s.StartKind = 2;
            True(s.ExpectedEnd(now, 600) == null, "count 0 kind 2 null");
        }

        static void W9_Past_target_is_negative_wait_not_zero()
        {
            DateTime now = new DateTime(2026, 10, 24, 12, 0, 0);
            var s = new SeriesSettings { StartKind = 1, StartAt = now.AddHours(-1) };
            True(s.WaitSecondsFrom(now) < 0, "past is negative");

            var s2 = new SeriesSettings { StartKind = 1, StartAt = now };
            Eq(0, s2.WaitSecondsFrom(now), "equal is zero");
        }

        public static (string name, Action body)[] Stage2Tests()
        {
            return new[]
            {
                ("W1_Wait_seconds_resolution", (Action)W1_Wait_seconds_resolution),
                ("W2_Waiting_waits_then_starts", (Action)W2_Waiting_waits_then_starts),
                ("W3_System_clock_jump_ignored", (Action)W3_System_clock_jump_ignored),
                ("W4_Stop_in_waiting", (Action)W4_Stop_in_waiting),
                ("W5_KeepAwake_lifecycle", (Action)W5_KeepAwake_lifecycle),
                ("W6_Waiting_ignores_other_calls", (Action)W6_Waiting_ignores_other_calls),
                ("W7_Settings_persistence", (Action)W7_Settings_persistence),
                ("W8_Expected_end_from_start", (Action)W8_Expected_end_from_start),
                ("W9_Past_target_is_negative_wait_not_zero", (Action)W9_Past_target_is_negative_wait_not_zero),
            };
        }
    }
}
