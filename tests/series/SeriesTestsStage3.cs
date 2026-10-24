using System;
using System.Collections.Generic;
using System.Linq;
using BecquerelMonitor;

namespace SeriesTestRun
{
    public static partial class Program
    {
        public static (string name, Action body)[] Stage3Tests()
        {
            return new (string, Action)[]
            {
                ("Z1_partial_saved_after_device_loss", Z1_partial_saved_after_device_loss),
                ("Z2_stop_yes_write_error_not_ignored", Z2_stop_yes_write_error_not_ignored),
                ("Z3_disk_unknown_warns_and_continues", Z3_disk_unknown_warns_and_continues),
                ("Z4_template_without_n", Z4_template_without_n)
            };
        }

        static void Z1_partial_saved_after_device_loss()
        {
            // Part 1: partial save succeeds after device loss
            var pair = MakeSeries(3, 0, true);
            var series = pair.Series;
            var host = pair.Host;

            series.Begin(0);
            series.OnSpectrumComplete(); // spectrum 1 saved, now acquiring 2

            series.OnDeviceLost("lost");
            True(series.State == SeriesState.PausedError, "state after loss");
            True(series.PartialPending, "partial pending after loss");
            True(series.Files.Count == 1, "one file saved before loss");

            string err;
            bool ok = series.SavePartialAfterLoss(out err);
            True(ok, "partial save should succeed");
            True(series.Files.Count == 2, "two files after partial save");
            Eq(P("sp_002"), series.Files[1], "second file path");
            True(host.Calls.Any(c => c == "save:" + P("sp_002")), "host recorded save call");
            True(!series.PartialPending, "partial pending cleared");

            // Second call should do nothing
            bool ok2 = series.SavePartialAfterLoss(out err);
            True(!ok2, "second partial save should fail");
            True(series.Files.Count == 2, "no extra file");
            True(host.Calls.Count(c => c == "save:" + P("sp_002")) == 1, "still one save call");

            // Part 2: save error propagates
            var pair2 = MakeSeries(3, 0, true);
            var series2 = pair2.Series;
            var host2 = pair2.Host;

            series2.Begin(0);
            series2.OnDeviceLost("x");
            host2.SaveError = "disk full";

            bool ok3 = series2.SavePartialAfterLoss(out err);
            True(!ok3, "save should fail with disk error");
            Contains(err, "disk full", "error message");
            True(series2.Files.Count == 0, "no files saved");
            True(series2.State == SeriesState.PausedError, "state still PausedError");

            // Part 3: no partial pending when lost during delay
            var pair3 = MakeSeries(3, 10, true);
            var series3 = pair3.Series;

            series3.Begin(0);
            series3.OnSpectrumComplete(); // now in Delay
            series3.OnDeviceLost("x");
            True(series3.State == SeriesState.PausedError, "state after loss in delay");
            True(!series3.PartialPending, "no partial pending when lost in delay");
            bool ok4 = series3.SavePartialAfterLoss(out err);
            True(!ok4, "save should fail when no partial pending");

            // Part 4: partial pending cleared after restart
            var pair4 = MakeSeries(3, 0, true);
            var series4 = pair4.Series;

            series4.Begin(0);
            series4.OnDeviceLost("x");
            True(series4.PartialPending, "partial pending before restart");
            series4.Begin(0); // restart
            True(!series4.PartialPending, "partial pending cleared after restart");
        }

        static void Z2_stop_yes_write_error_not_ignored()
        {
            // With save error: stop reports error, no file
            var pair = MakeSeries(3, 0, true);
            var series = pair.Series;
            var host = pair.Host;

            series.Begin(0);
            host.SaveError = "disk full";
            series.UserStop(true);

            True(series.State == SeriesState.PausedError, "state after stop with error");
            Contains(series.LastError, "disk full", "last error");
            True(series.Files.Count == 0, "no files saved");

            // Without error: stop succeeds
            var pair2 = MakeSeries(3, 0, true);
            var series2 = pair2.Series;

            series2.Begin(0);
            series2.UserStop(true);

            True(series2.State == SeriesState.Idle, "state after clean stop");
            True(series2.Files.Count == 1, "one file saved");
        }

        static void Z3_disk_unknown_warns_and_continues()
        {
            // Endless series with unknown free space
            var pair = MakeSeries(0, 0, true);
            var series = pair.Series;
            var host = pair.Host;

            host.Free = -1;
            series.Begin(0);
            series.OnSpectrumComplete();

            True(series.State == SeriesState.Acquiring, "state after spectrum");
            True(host.Calls.Any(c => c.StartsWith("save:")), "save call exists");
            Eq(MeasurementSeries.WarnDiskUnknown, series.Warning, "warning is disk unknown");
            True(!string.IsNullOrEmpty(MeasurementSeries.WarnDiskUnknown), "WarnDiskUnknown non-empty");
            True(MeasurementSeries.WarnDiskUnknown != MeasurementSeries.WarnLowDisk, "warnings differ");

            // Sufficient free space: no warning
            host.Free = 5000000000;
            series.OnSpectrumComplete();
            True(string.IsNullOrEmpty(series.Warning), "no warning with enough space");

            // Low free space: low disk warning
            host.Free = 500000000;
            series.OnSpectrumComplete();
            Eq(MeasurementSeries.WarnLowDisk, series.Warning, "warning is low disk");

            // Fixed count series: free space not checked
            var pair2 = MakeSeries(3, 0, true);
            var series2 = pair2.Series;
            var host2 = pair2.Host;

            host2.Free = -1;
            series2.Begin(0);
            series2.OnSpectrumComplete();
            True(string.IsNullOrEmpty(series2.Warning), "no warning for fixed count series");
        }

        static void Z4_template_without_n()
        {
            Eq(0, SeriesSettings.TemplateProblem("{prefix}_{n:000}", 5, true), "n token fine");
            Eq(0, SeriesSettings.TemplateProblem("{prefix}_{n}", 0, false), "n token endless fine");
            Eq(0, SeriesSettings.TemplateProblem("{prefix}_{time}", 5, false), "time token fine");
            Eq(0, SeriesSettings.TemplateProblem("{PREFIX}_{N:00}", 5, false), "case insensitive fine");
            Eq(0, SeriesSettings.TemplateProblem("data", 1, false), "single spectrum fine");
            Eq(1, SeriesSettings.TemplateProblem("data", 5, true), "no token, overwrite, warning");
            Eq(1, SeriesSettings.TemplateProblem("data", 0, true), "no token, endless, warning");
            Eq(2, SeriesSettings.TemplateProblem("data", 5, false), "no token, no overwrite, refusal");
            Eq(2, SeriesSettings.TemplateProblem("{prefix}_{date}", 5, false), "date token, no overwrite, refusal");
            Eq(1, SeriesSettings.TemplateProblem("{prefix}_{date}", 2, true), "date token, overwrite, warning");
            Eq(2, SeriesSettings.TemplateProblem(null, 5, false), "null template, refusal");
            Eq(1, SeriesSettings.TemplateProblem("", 3, true), "empty template, warning");
        }
    }
}
