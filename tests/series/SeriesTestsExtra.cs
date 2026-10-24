using System;
using System.Collections.Generic;
using System.Linq;
using BecquerelMonitor;

namespace SeriesTestRun
{
    public static partial class Program
    {
        public static void T15_AcquisitionId()
        {
            var pair = MakeSeries(3, 0, false);
            var series = pair.Series;
            var host = pair.Host;

            series.Begin(0);
            int id1 = series.AcquisitionId;
            series.OnSpectrumComplete();
            True(series.AcquisitionId != id1, "AcquisitionId should change after OnSpectrumComplete with delay 0");

            var pair2 = MakeSeries(3, 60, false);
            var series2 = pair2.Series;
            var host2 = pair2.Host;

            series2.Begin(0);
            int id2 = series2.AcquisitionId;
            series2.OnSpectrumComplete();
            Eq(id2, series2.AcquisitionId, "AcquisitionId should not change during Delay");
            host2.Advance(60);
            series2.Tick();
            True(series2.AcquisitionId != id2, "AcquisitionId should change after Tick into Acquiring");

            int idBeforeStop = series2.AcquisitionId;
            series2.UserStop(false);
            Eq(idBeforeStop, series2.AcquisitionId, "AcquisitionId should not change on UserStop");
        }

        public static void T16_TotalSeconds()
        {
            Eq(3240, new SeriesSettings { Count = 5, DelaySeconds = 60 }.TotalSeconds(600), "TotalSeconds (5,60,600)");
            Eq(3600, new SeriesSettings { Count = 1, DelaySeconds = 120 }.TotalSeconds(3600), "TotalSeconds (1,120,3600)");
            Eq(540, new SeriesSettings { Count = 12, DelaySeconds = 0 }.TotalSeconds(45), "TotalSeconds (12,0,45)");
            Eq(35992801, new SeriesSettings { Count = 9999, DelaySeconds = 3599 }.TotalSeconds(1), "TotalSeconds (9999,3599,1)");
            Eq(48041, new SeriesSettings { Count = 7, DelaySeconds = 3661 }.TotalSeconds(3725), "TotalSeconds (7,3661,3725)");
            Eq(532799, new SeriesSettings { Count = 2, DelaySeconds = 359999 }.TotalSeconds(86400), "TotalSeconds (2,359999,86400)");
            Eq(-1, new SeriesSettings { Count = 0, DelaySeconds = 60 }.TotalSeconds(600), "TotalSeconds Count 0");

            Eq("00:54:00", SeriesSettings.FormatHms(3240), "FormatHms 3240");
            Eq("9998:00:01", SeriesSettings.FormatHms(35992801), "FormatHms 35992801");
            Eq("00:00:00", SeriesSettings.FormatHms(-5), "FormatHms -5");
            Eq("00:00:00", SeriesSettings.FormatHms(0), "FormatHms 0");
        }

        public static void T17_Normalize_ranges()
        {
            var s1 = new SeriesSettings { DelaySeconds = -5 };
            s1.Normalize();
            Eq(0, s1.DelaySeconds, "DelaySeconds -5 -> 0");

            var s2 = new SeriesSettings { DelaySeconds = 400000 };
            s2.Normalize();
            Eq(359999, s2.DelaySeconds, "DelaySeconds 400000 -> 359999");

            var s3 = new SeriesSettings { Count = -3 };
            s3.Normalize();
            Eq(0, s3.Count, "Count -3 -> 0");

            var s4 = new SeriesSettings { Count = 20000 };
            s4.Normalize();
            Eq(9999, s4.Count, "Count 20000 -> 9999");

            var s5 = new SeriesSettings { Template = "  " };
            s5.Normalize();
            Eq("{prefix}_{n:000}", s5.Template, "Template whitespace -> default");

            var s6 = new SeriesSettings { Folder = null };
            s6.Normalize();
            Eq("", s6.Folder, "Folder null -> empty");

            var s7 = new SeriesSettings { Prefix = null };
            s7.Normalize();
            Eq("", s7.Prefix, "Prefix null -> empty");
        }

        public static void T18_Countdown_ceiling()
        {
            var pair = MakeSeries(2, 60, false);
            var series = pair.Series;
            var host = pair.Host;

            series.Begin(0);
            series.OnSpectrumComplete();

            host.Clock = host.Clock.AddMilliseconds(59500);
            Eq(1, series.SecondsToResume, "SecondsToResume at 59.5s left");

            host.Clock = host.Clock.AddMilliseconds(400);
            Eq(1, series.SecondsToResume, "SecondsToResume at 0.1s left");

            host.Clock = host.Clock.AddMilliseconds(100);
            Eq(0, series.SecondsToResume, "SecondsToResume at 0s left");
            series.Tick();
            Eq(SeriesState.Acquiring, series.State, "State after Tick when delay expires");
        }

        public static void T19_BuildName_chars()
        {
            DateTime now = new DateTime(2026, 10, 24, 12, 0, 0);

            Eq("Проба №1 образец_007", SeriesSettings.BuildName("{prefix}_{n:000}", "Проба №1 образец", 7, now), "Cyrillic and spaces");
            Eq("a_b_c_d_e_f_g_h_i_j_007", SeriesSettings.BuildName("{prefix}_{n:000}", "a/b\\c:d*e?f\"g<h>i|j", 7, now), "Invalid chars replaced");
            Eq("2026-10-24", SeriesSettings.BuildName("{date}", "", 1, now), "Date format");
            Eq("12-00-00", SeriesSettings.BuildName("{time}", "", 1, now), "Time format");
            Eq("1000", SeriesSettings.BuildName("{n}", "", 1000, now), "N format");
            Eq("05", SeriesSettings.BuildName("{N:00}", "", 5, now), "N:00 format");
            Eq("x", SeriesSettings.BuildName("{prefix}", "x.", 7, now), "Prefix with dot");
            Eq("spectrum_7", SeriesSettings.BuildName("", "", 7, now), "Empty template");
            Eq("{foo}_001", SeriesSettings.BuildName("{foo}_{n:000}", "", 1, now), "Unknown placeholder");
        }

        public static void T20_BuildPath_second_collision()
        {
            DateTime now = new DateTime(2026, 10, 24, 12, 0, 0);
            var existing = new HashSet<string> { P("sp_001"), P("sp_001_1") };

            Eq(P("sp_001_2"), SeriesSettings.BuildPath(@"C:\s", "{prefix}_{n:000}", "sp", 1, now, existing.Contains, true), "Second collision with noOverwrite");
            Eq(P("sp_001"), SeriesSettings.BuildPath(@"C:\s", "{prefix}_{n:000}", "sp", 1, now, existing.Contains, false), "No collision with overwrite allowed");

            bool threw = false;
            try
            {
                SeriesSettings.BuildPath("", "{prefix}_{n:000}", "sp", 1, now, existing.Contains, true);
            }
            catch (ArgumentException)
            {
                threw = true;
            }
            True(threw, "Empty folder should throw ArgumentException");
        }

        public static void T21_SkipDelay_outside_pause()
        {
            var pair = MakeSeries(3, 60, true);
            var series = pair.Series;
            var host = pair.Host;

            series.Begin(0);
            series.SkipDelay();
            Eq(SeriesState.Acquiring, series.State, "State remains Acquiring after SkipDelay");
            Eq(1, series.Index, "Index remains 1 after SkipDelay");
            Eq(0, host.Calls.Count, "No calls made after SkipDelay");

            var pair2 = MakeSeries(3, 60, true);
            var series2 = pair2.Series;
            series2.SkipDelay();
            Eq(SeriesState.Idle, series2.State, "State remains Idle after SkipDelay on fresh series");

            var pair3 = MakeSeries(1, 60, true);
            var series3 = pair3.Series;
            var host3 = pair3.Host;
            series3.Begin(0);
            series3.OnSpectrumComplete();
            Eq(SeriesState.Done, series3.State, "State is Done after single spectrum");
            series3.SkipDelay();
            Eq(SeriesState.Done, series3.State, "State remains Done after SkipDelay");
            True(!host3.Calls.Any(c => c.StartsWith("start:")), "No start calls after SkipDelay on Done series");
            True(!host3.Calls.Any(c => c == "clear"), "No clear calls after SkipDelay on Done series");
        }

        public static void T22_Disk_warning_not_stop()
        {
            var pair = MakeSeries(0, 0, false);
            var series = pair.Series;
            var host = pair.Host;

            host.Free = 500000000;
            series.Begin(0);
            series.OnSpectrumComplete();
            Eq(SeriesState.Acquiring, series.State, "State is Acquiring for endless series");
            Eq(1, series.Files.Count, "Files count is 1");
            Contains(series.Warning, "1 GB", "Warning contains 1 GB");

            host.Free = 5000000000;
            series.OnSpectrumComplete();
            Eq("", series.Warning, "Warning cleared when free space >= 1 GB");

            host.Free = -1;
            series.OnSpectrumComplete();
            Eq("", series.Warning, "Warning cleared when free space unknown");

            var pair2 = MakeSeries(3, 0, false);
            var series2 = pair2.Series;
            var host2 = pair2.Host;
            host2.Free = 500000000;
            series2.Begin(0);
            series2.OnSpectrumComplete();
            Eq("", series2.Warning, "No warning for finite series");
        }

        public static void T23_Content_accumulates()
        {
            var hostTrue = new ContentHost();
            var seriesTrue = new MeasurementSeries(new SeriesSettings { Enabled = true, Count = 3, DelaySeconds = 0, ClearBetween = true, Folder = @"C:\s", Prefix = "sp" }, hostTrue, 600);
            seriesTrue.Begin(0);
            seriesTrue.OnSpectrumComplete();
            seriesTrue.OnSpectrumComplete();
            seriesTrue.OnSpectrumComplete();
            Eq("1,1,1", string.Join(",", hostTrue.Saved), "Saved with ClearBetween true");

            var hostFalse = new ContentHost();
            var seriesFalse = new MeasurementSeries(new SeriesSettings { Enabled = true, Count = 3, DelaySeconds = 0, ClearBetween = false, Folder = @"C:\s", Prefix = "sp" }, hostFalse, 600);
            seriesFalse.Begin(0);
            seriesFalse.OnSpectrumComplete();
            seriesFalse.OnSpectrumComplete();
            seriesFalse.OnSpectrumComplete();
            Eq("1,2,3", string.Join(",", hostFalse.Saved), "Saved with ClearBetween false");
        }

        public static void T24_Reserved_names()
        {
            DateTime now = new DateTime(2026, 10, 24, 12, 0, 0);

            Eq("_CON", SeriesSettings.BuildName("{prefix}", "CON", 1, now), "CON -> _CON");
            Eq("_nul", SeriesSettings.BuildName("{prefix}", "nul", 1, now), "nul -> _nul");
            Eq("_COM1", SeriesSettings.BuildName("{prefix}", "COM1", 1, now), "COM1 -> _COM1");
            Eq("_LPT9", SeriesSettings.BuildName("{prefix}", "LPT9", 1, now), "LPT9 -> _LPT9");
            Eq("_CON.v", SeriesSettings.BuildName("{prefix}", "CON.v", 1, now), "CON.v -> _CON.v");
            Eq("CONSOLE", SeriesSettings.BuildName("{prefix}", "CONSOLE", 1, now), "CONSOLE unchanged");
            Eq("COM10", SeriesSettings.BuildName("{prefix}", "COM10", 1, now), "COM10 unchanged");
            Eq("COM", SeriesSettings.BuildName("{prefix}", "COM", 1, now), "COM unchanged");
            Eq("console_1", SeriesSettings.BuildName("{prefix}", "console_1", 1, now), "console_1 unchanged");

            Eq(@"C:\s\_CON.xml", SeriesSettings.BuildPath(@"C:\s", "{prefix}", "CON", 1, now, p => false, true), "BuildPath CON");
        }

        public static void T25_Clear_precedes_every_start()
        {
            var pair = MakeSeries(4, 0, true);
            var series = pair.Series;
            var host = pair.Host;

            series.Begin(0);
            series.OnSpectrumComplete();
            series.OnSpectrumComplete();
            series.OnSpectrumComplete();

            var starts = host.Calls.Where(c => c.StartsWith("start:")).ToList();
            Eq(3, starts.Count, "Exactly 3 start calls");
            Eq(3, host.Calls.Count(c => c == "clear"), "Exactly 3 clear calls");

            for (int i = 0; i < starts.Count; i++)
            {
                int startIdx = host.Calls.IndexOf(starts[i]);
                True(startIdx > 0, "Start call has preceding entry");
                Eq("clear", host.Calls[startIdx - 1], "Clear precedes start");
            }

            var pair2 = MakeSeries(4, 0, false);
            var series2 = pair2.Series;
            var host2 = pair2.Host;

            series2.Begin(0);
            series2.OnSpectrumComplete();
            series2.OnSpectrumComplete();
            series2.OnSpectrumComplete();

            Eq(0, host2.Calls.Count(c => c == "clear"), "Zero clear calls with clear false");
            Eq(3, host2.Calls.Count(c => c.StartsWith("start:")), "3 start calls with clear false");
        }

        public static (string name, Action body)[] ExtraTests()
        {
            return new (string, Action)[]
            {
                ("T15_AcquisitionId", T15_AcquisitionId),
                ("T16_TotalSeconds", T16_TotalSeconds),
                ("T17_Normalize_ranges", T17_Normalize_ranges),
                ("T18_Countdown_ceiling", T18_Countdown_ceiling),
                ("T19_BuildName_chars", T19_BuildName_chars),
                ("T20_BuildPath_second_collision", T20_BuildPath_second_collision),
                ("T21_SkipDelay_outside_pause", T21_SkipDelay_outside_pause),
                ("T22_Disk_warning_not_stop", T22_Disk_warning_not_stop),
                ("T23_Content_accumulates", T23_Content_accumulates),
                ("T24_Reserved_names", T24_Reserved_names),
                ("T25_Clear_precedes_every_start", T25_Clear_precedes_every_start)
            };
        }

        public class ContentHost : ISeriesHost
        {
            public int Acc = 1;
            public List<int> Saved = new List<int>();
            public DateTime Now { get { return new DateTime(2026, 10, 24, 12, 0, 0); } }

            public bool FileExists(string path) => false;
            public long FreeDiskBytes(string folder) { return -1; }

            public bool SaveSpectrum(string path, out string error)
            {
                Saved.Add(Acc);
                error = "";
                return true;
            }

            public bool ClearSpectrum(out string error)
            {
                Acc = 0;
                error = "";
                return true;
            }

            public bool StartAcquisition(int presetSeconds, out string error)
            {
                Acc += 1;
                error = "";
                return true;
            }

            public void StateChanged() { }
            public void SeriesFinished() { }
        }
    }
}
