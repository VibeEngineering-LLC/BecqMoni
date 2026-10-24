using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;
using BecquerelMonitor;

public static class UiStage2
{
    public static int Run()
    {
        int fails = 0;
        fails += Check("V1_stop_in_waiting_no_question", () =>
        {
            var doc = new DocEnergySpectrum();
            var rd = doc.ActiveResultData;
            var host = new WaitHost();
            var series = MakeSeries(host, 5, 0);
            rd.MeasurementController.Series = series;
            BeginWaiting(series, 0, 3600);

            int dialogCalls = 0;
            var panel = NewPanel((msg, buttons) => { dialogCalls++; return DialogResult.Yes; });

            bool saveCurrent;
            int ret = (int)Invoke(panel, "SeriesBeforeStop", new object[] { doc, rd, true, false }, out saveCurrent);
            if (ret != 2) throw new Exception("Expected 2, got " + ret);
            if (dialogCalls != 0) throw new Exception("Dialog called " + dialogCalls + " times");
            if (series.State != SeriesState.Idle) throw new Exception("State is " + series.State);
            if (host.KeepAwake) throw new Exception("KeepAwake is true");
            if (host.KeepAwakeLog.Count != 2 || host.KeepAwakeLog[0] != true || host.KeepAwakeLog[1] != false)
                throw new Exception("KeepAwakeLog mismatch");
        });

        fails += Check("V2_close_in_waiting", () =>
        {
            var doc = new DocEnergySpectrum();
            var rd = doc.ActiveResultData;
            var host = new WaitHost();
            var series = MakeSeries(host, 5, 0);
            rd.MeasurementController.Series = series;
            BeginWaiting(series, 0, 3600);

            int askCalls = 0;
            Func<bool> ask = () => { askCalls++; return true; };
            var mf = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
            bool closed = (bool)Invoke(mf, "ConfirmCloseDuringSeries", new object[] { doc, ask });
            if (!closed) throw new Exception("ConfirmCloseDuringSeries returned false");
            if (askCalls != 1) throw new Exception("Ask called " + askCalls + " times");
            if (series.State != Waiting()) throw new Exception("State is " + series.State);
            if (!host.KeepAwake) throw new Exception("KeepAwake is false");

            Invoke(mf, "StopSeriesOf", new object[] { doc });
            if (series.State != SeriesState.Idle) throw new Exception("State is " + series.State);
            if (host.KeepAwake) throw new Exception("KeepAwake is true");
        });

        fails += Check("V3_past_target_asks", () =>
        {
            // Part 1: No
            var doc1 = new DocEnergySpectrum();
            var rd1 = doc1.ActiveResultData;
            rd1.Series = new SeriesSettings { Enabled = true, Count = 3, Folder = @"C:\s", Prefix = "sp" };
            SetProp(rd1.Series, "StartKind", 1);
            SetProp(rd1.Series, "StartAt", DateTime.Now.AddHours(-1));
            rd1.DeviceConfig = new DeviceConfigInfo { DeviceType = "AudioInput" };
            rd1.ResultDataStatus.ElapsedTime = TimeSpan.Zero;

            int dialogCalls1 = 0;
            var panel1 = NewPanel((msg, buttons) => { dialogCalls1++; return DialogResult.No; });

            bool ret1 = (bool)Invoke(panel1, "SeriesBegin", new object[] { doc1, rd1, rd1.ResultDataStatus });
            if (ret1) throw new Exception("Expected false");
            if (dialogCalls1 != 1) throw new Exception("Dialog called " + dialogCalls1 + " times");
            if (rd1.MeasurementController.Series != null) throw new Exception("Series is not null");

            // Part 2: Yes
            var doc2 = new DocEnergySpectrum();
            var rd2 = doc2.ActiveResultData;
            rd2.Series = new SeriesSettings { Enabled = true, Count = 3, Folder = @"C:\s", Prefix = "sp" };
            SetProp(rd2.Series, "StartKind", 1);
            SetProp(rd2.Series, "StartAt", DateTime.Now.AddHours(-1));
            rd2.DeviceConfig = new DeviceConfigInfo { DeviceType = "AudioInput" };
            rd2.ResultDataStatus.ElapsedTime = TimeSpan.Zero;

            int dialogCalls2 = 0;
            var panel2 = NewPanel((msg, buttons) => { dialogCalls2++; return DialogResult.Yes; });

            bool ret2 = (bool)Invoke(panel2, "SeriesBegin", new object[] { doc2, rd2, rd2.ResultDataStatus });
            if (!ret2) throw new Exception("Expected true");
            if (dialogCalls2 != 1) throw new Exception("Dialog called " + dialogCalls2 + " times");
            if (rd2.MeasurementController.Series == null) throw new Exception("Series is null");
            if (rd2.MeasurementController.Series.State != SeriesState.Acquiring) throw new Exception("State is " + rd2.MeasurementController.Series.State);
            rd2.MeasurementController.Series.UserStop(false);
        });

        fails += Check("V4_future_target_waits", () =>
        {
            var doc = new DocEnergySpectrum();
            var rd = doc.ActiveResultData;
            rd.Series = new SeriesSettings { Enabled = true, Count = 3, Folder = @"C:\s", Prefix = "sp" };
            SetProp(rd.Series, "StartKind", 2);
            SetProp(rd.Series, "StartInSeconds", 3600);
            rd.DeviceConfig = new DeviceConfigInfo { DeviceType = "AudioInput" };
            rd.ResultDataStatus.ElapsedTime = TimeSpan.Zero;

            int dialogCalls = 0;
            var panel = NewPanel((msg, buttons) => { dialogCalls++; return DialogResult.OK; });

            bool ret = (bool)Invoke(panel, "SeriesBegin", new object[] { doc, rd, rd.ResultDataStatus });
            if (ret) throw new Exception("Expected false");
            if (dialogCalls != 0) throw new Exception("Dialog called " + dialogCalls + " times");
            if (rd.MeasurementController.Series == null) throw new Exception("Series is null");
            if (rd.MeasurementController.Series.State != Waiting()) throw new Exception("State is " + rd.MeasurementController.Series.State);
            if (!rd.MeasurementController.Series.IsActive) throw new Exception("IsActive is false");

            Invoke(panel, "ShowSeriesStatus", new object[] { doc, rd });
            var lblField = typeof(DCControlPanel).GetField("seriesStatusLbl", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (lblField == null) throw new Exception("seriesStatusLbl field missing");
            var lbl = (Label)lblField.GetValue(panel);
            if (string.IsNullOrEmpty(lbl.Text)) throw new Exception("Label text is empty");

            bool save;
            int ret2 = (int)Invoke(panel, "SeriesBeforeStop", new object[] { doc, rd, true, false }, out save);
            if (ret2 != 2) throw new Exception("Expected 2, got " + ret2);
            if (rd.MeasurementController.Series.State != SeriesState.Idle) throw new Exception("State is " + rd.MeasurementController.Series.State);
        });

        fails += Check("V5_tcp_board_still_refused_with_wait", () =>
        {
            var doc = new DocEnergySpectrum();
            var rd = doc.ActiveResultData;
            rd.DeviceConfig = new DeviceConfigInfo { DeviceType = AtomSpectraTcpDeviceConfig.DeviceTypeId };
            rd.Series = new SeriesSettings { Enabled = true, Count = 3, Folder = @"C:\s", Prefix = "sp" };
            SetProp(rd.Series, "StartKind", 2);
            SetProp(rd.Series, "StartInSeconds", 3600);

            string capturedText = "";
            var panel = NewPanel((msg, buttons) => { capturedText = msg; return DialogResult.OK; });

            bool ret = (bool)Invoke(panel, "SeriesBegin", new object[] { doc, rd, rd.ResultDataStatus });
            if (ret) throw new Exception("Expected false");
            if (capturedText == null || !capturedText.Contains("TCP/REST")) throw new Exception("Text does not contain TCP/REST: " + capturedText);
            if (rd.MeasurementController.Series != null) throw new Exception("Series is not null");
        });

        return fails;
    }

    private static int Check(string name, Action action)
    {
        try
        {
            action();
            Console.WriteLine("PASS " + name);
            return 0;
        }
        catch (Exception ex)
        {
            Exception inner = ex;
            while (inner is TargetInvocationException)
            {
                inner = ((TargetInvocationException)inner).InnerException;
            }
            Console.WriteLine("FAIL " + name + ": " + inner.GetType().Name + ": " + inner.Message);
            return 1;
        }
    }

    private static object Invoke(object target, string methodName, object[] args, out bool saveCurrent)
    {
        saveCurrent = false;
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (method == null) throw new Exception(methodName + " is missing");
        try
        {
            return method.Invoke(target, args);
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException;
        }
    }

    private static object Invoke(object target, string methodName, object[] args)
    {
        bool dummy;
        return Invoke(target, methodName, args, out dummy);
    }

    private static void SetProp(object o, string name, object value)
    {
        var prop = o.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (prop == null) throw new Exception(name + " is missing");
        prop.SetValue(o, value);
    }

    private static void BeginWaiting(MeasurementSeries s, int baseSeconds, int waitSeconds)
    {
        var method = s.GetType().GetMethod("BeginWaiting", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null) throw new Exception("BeginWaiting is missing");
        try
        {
            method.Invoke(s, new object[] { baseSeconds, waitSeconds });
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException;
        }
    }

    private static SeriesState Waiting()
    {
        try
        {
            return (SeriesState)Enum.Parse(typeof(SeriesState), "Waiting");
        }
        catch
        {
            throw new Exception("SeriesState.Waiting is missing");
        }
    }

    private static MeasurementSeries MakeSeries(WaitHost h, int count, int delay)
    {
        return new MeasurementSeries(new SeriesSettings { Enabled = true, Count = count, DelaySeconds = delay, ClearBetween = true, Folder = @"C:\s", Prefix = "sp" }, h, 600);
    }

    private static DCControlPanel NewPanel(Func<string, MessageBoxButtons, DialogResult> dialog)
    {
        var mainForm = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
        var panel = new DCControlPanel(mainForm);
        var dialogField = typeof(DCControlPanel).GetField("SeriesDialog", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (dialogField != null)
        {
            dialogField.SetValue(panel, dialog);
        }
        var textBoxField = typeof(DCControlPanel).GetField("realTimeLimitTextBox", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (textBoxField != null)
        {
            var textBox = (TextBox)textBoxField.GetValue(panel);
            if (textBox != null)
            {
                textBox.Text = "600";
            }
        }
        return panel;
    }

    public class WaitHost : ISeriesHost
    {
        public int Saves;
        public int Starts;
        public int Clears;
        public bool KeepAwake;
        public List<bool> KeepAwakeLog = new List<bool>();
        public long Mono;

        public DateTime Now { get { return DateTime.Now; } }
        public long MonotonicMs { get { return Mono; } }

        public void SetKeepAwake(bool on)
        {
            KeepAwake = on;
            KeepAwakeLog.Add(on);
        }

        public bool FileExists(string path) { return false; }
        public long FreeDiskBytes(string path) { return -1; }
        public bool SaveSpectrum(string path, out string error) { Saves++; error = ""; return true; }
        public bool ClearSpectrum(out string error) { Clears++; error = ""; return true; }
        public bool StartAcquisition(int presetSeconds, out string error) { Starts++; error = ""; return true; }
        public void StateChanged() { }
        public void SeriesFinished() { }
    }
}
