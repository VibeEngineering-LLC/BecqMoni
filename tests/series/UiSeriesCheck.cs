using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;
using BecquerelMonitor;

static class UiSeriesCheck
{
    // Control.Visible is false for children of a form that was never shown; the flag the series code sets is the control's own state bit (STATE_VISIBLE = 2)
    static bool VisibleFlag(Control c)
    {
        return (bool)typeof(Control).GetMethod("GetState", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(int) }, null).Invoke(c, new object[] { 2 });
    }

    static int Main(string[] args)
    {
        int fails = 0;
        fails += RunCheck("U1_close_declined_keeps_series", U1);
        fails += RunCheck("U2_two_spectra_one_question", U2);
        fails += RunCheck("U3_stop_path_stops_series", U3);
        fails += RunCheck("U4_stale_stop_answer", U4);
        fails += RunCheck("U5_attach_detach_refused_in_series", U5);
        fails += RunCheck("U6_tcp_board_refused", U6);
        fails += RunCheck("U7_tab_switch_panel_follows_document", U7);
        fails += UiStage2.Run();
        fails += UiStage3.Run();
        fails += UiStage4.Run();
        fails += UiStage5.Run();
        Console.WriteLine("SUMMARY fail=" + fails);
        return fails;
    }

    static int RunCheck(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine("PASS " + name);
            return 0;
        }
        catch (TargetInvocationException ex)
        {
            Exception inner = ex.InnerException ?? ex;
            Console.WriteLine("FAIL " + name + ": " + inner.GetType().Name + ": " + inner.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL " + name + ": " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    static void U1()
    {
        DocEnergySpectrum doc = new DocEnergySpectrum();
        ResultData rd = doc.ActiveResultData;
        SimpleHost host = new SimpleHost();
        MeasurementSeries series = MakeSeries(5, 0, host);
        rd.MeasurementController.Series = series;
        series.Begin(0);

        MainForm mf = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
        MethodInfo mi = typeof(MainForm).GetMethod("ConfirmCloseDuringSeries", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (mi == null) throw new Exception("ConfirmCloseDuringSeries not found");

        object res = mi.Invoke(mf, new object[] { doc, (Func<bool>)(() => false) });
        if ((bool)res) throw new Exception("Expected false when declined");
        if (!series.IsActive) throw new Exception("Series should still be active after decline");

        int askCount = 0;
        Func<bool> ask = () => { askCount++; return true; };
        object res2 = mi.Invoke(mf, new object[] { doc, ask });
        if (!(bool)res2) throw new Exception("Expected true when confirmed");
        if (askCount != 1) throw new Exception("Ask called " + askCount + " times, expected 1");
        if (!series.IsActive) throw new Exception("Series should still be active after confirm");
    }

    static void U2()
    {
        DocEnergySpectrum doc = new DocEnergySpectrum();
        ResultData rd1 = doc.ActiveResultData;
        doc.CreateResultData();
        ResultData rd2 = doc.ActiveResultData;

        SimpleHost host1 = new SimpleHost();
        SimpleHost host2 = new SimpleHost();
        MeasurementSeries s1 = MakeSeries(5, 0, host1);
        MeasurementSeries s2 = MakeSeries(5, 0, host2);
        rd1.MeasurementController.Series = s1;
        rd2.MeasurementController.Series = s2;
        s1.Begin(0);
        s2.Begin(0);

        MainForm mf = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
        MethodInfo mi = typeof(MainForm).GetMethod("ConfirmCloseDuringSeries", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (mi == null) throw new Exception("ConfirmCloseDuringSeries not found");

        int askCount = 0;
        Func<bool> ask = () => { askCount++; return false; };
        object res = mi.Invoke(mf, new object[] { doc, ask });
        if ((bool)res) throw new Exception("Expected false when declined");
        if (askCount != 1) throw new Exception("Ask called " + askCount + " times, expected 1");
        if (!s1.IsActive) throw new Exception("Series 1 should still be active");
        if (!s2.IsActive) throw new Exception("Series 2 should still be active");
    }

    static void U3()
    {
        DocEnergySpectrum doc = new DocEnergySpectrum();
        ResultData rd = doc.ActiveResultData;
        SimpleHost host = new SimpleHost();
        MeasurementSeries series = MakeSeries(5, 0, host);
        rd.MeasurementController.Series = series;
        series.Begin(0);

        MainForm mf = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
        MethodInfo mi = typeof(MainForm).GetMethod("StopSeriesOf", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (mi == null) throw new Exception("StopSeriesOf is missing");
        mi.Invoke(mf, new object[] { doc });
        if (series.State != SeriesState.Idle) throw new Exception("Series state is " + series.State + ", expected Idle");

        DocEnergySpectrum doc2 = new DocEnergySpectrum();
        ResultData rd2 = doc2.ActiveResultData;
        SimpleHost host2 = new SimpleHost();
        MeasurementSeries series2 = MakeSeries(3, 60, host2);
        rd2.MeasurementController.Series = series2;
        series2.Begin(0);
        series2.OnSpectrumComplete();

        MethodInfo mi2 = typeof(MainForm).GetMethod("StopRecordingOrTesting", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (mi2 == null) throw new Exception("StopRecordingOrTesting not found");
        mi2.Invoke(mf, new object[] { doc2, true });
        if (series2.State != SeriesState.Idle) throw new Exception("Series state is " + series2.State + ", expected Idle");
    }

    static void U4()
    {
        DocEnergySpectrum doc = new DocEnergySpectrum();
        ResultData rd = doc.ActiveResultData;
        SimpleHost host = new SimpleHost();
        MeasurementSeries series = MakeSeries(5, 0, host);
        rd.MeasurementController.Series = series;
        series.Begin(0);

        MainForm mf = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
        DCControlPanel panel = new DCControlPanel(mf);

        FieldInfo fi = typeof(DCControlPanel).GetField("SeriesDialog", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (fi == null) throw new Exception("SeriesDialog field not found");
        fi.SetValue(panel, (Func<string, MessageBoxButtons, DialogResult>)((text, buttons) =>
        {
            series.OnSpectrumComplete();
            return DialogResult.Yes;
        }));

        MethodInfo mi = typeof(DCControlPanel).GetMethod("SeriesBeforeStop", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (mi == null) throw new Exception("SeriesBeforeStop not found");
        object[] args = new object[] { doc, rd, true, false };
        mi.Invoke(panel, args);
        bool saveCurrent = (bool)args[3];

        MethodInfo mi2 = typeof(DCControlPanel).GetMethod("SeriesAfterStop", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (mi2 == null) throw new Exception("SeriesAfterStop not found");
        mi2.Invoke(panel, new object[] { rd, saveCurrent });

        if (host.Saves != 1) throw new Exception("Saves is " + host.Saves + ", expected 1");
        if (series.State != SeriesState.Idle) throw new Exception("Series state is " + series.State + ", expected Idle");
    }

    static void U5()
    {
        DocEnergySpectrum doc = new DocEnergySpectrum();
        ResultData rd = doc.ActiveResultData;
        SimpleHost host = new SimpleHost();
        MeasurementSeries series = MakeSeries(5, 0, host);
        rd.MeasurementController.Series = series;
        series.Begin(0);

        MainForm mf = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
        DCControlPanel panel = new DCControlPanel(mf);

        FieldInfo activeDocField = typeof(MainForm).GetField("activeDocument", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (activeDocField == null) throw new Exception("activeDocument field not found");
        activeDocField.SetValue(mf, doc);

        FieldInfo rtField = typeof(DCControlPanel).GetField("realTimeLimitTextBox", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (rtField == null) throw new Exception("realTimeLimitTextBox field not found");
        TextBox rtBox = (TextBox)rtField.GetValue(panel);
        if (rtBox != null) rtBox.Text = "600";

        int dialogCount = 0;
        FieldInfo fi = typeof(DCControlPanel).GetField("SeriesDialog", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (fi == null) throw new Exception("SeriesDialog field not found");
        fi.SetValue(panel, (Func<string, MessageBoxButtons, DialogResult>)((text, buttons) =>
        {
            dialogCount++;
            return DialogResult.OK;
        }));

        try
        {
            bool attachResult = panel.AttachToDevice();
            if (attachResult) throw new Exception("AttachToDevice should return false");
            if (dialogCount < 1) throw new Exception("Dialog not called during AttachToDevice");
        }
        catch (Exception ex)
        {
            if (ex is TargetInvocationException)
            {
                Exception inner = ex.InnerException ?? ex;
                throw new Exception("AttachToDevice exception: " + inner.Message);
            }
            throw new Exception("AttachToDevice exception: " + ex.Message);
        }

        dialogCount = 0;
        rd.ResultDataStatus.Recording = true;
        try
        {
            panel.DetachFromDevice();
            if (dialogCount < 1) throw new Exception("Dialog not called during DetachFromDevice");
            if (!rd.ResultDataStatus.Recording) throw new Exception("Recording should still be true");
        }
        catch (Exception ex)
        {
            if (ex is TargetInvocationException)
            {
                Exception inner = ex.InnerException ?? ex;
                throw new Exception("DetachFromDevice exception: " + inner.Message);
            }
            throw new Exception("DetachFromDevice exception: " + ex.Message);
        }
    }

    static void U6()
    {
        DocEnergySpectrum doc = new DocEnergySpectrum();
        ResultData rd = doc.ActiveResultData;
        rd.ResultDataStatus.ElapsedTime = TimeSpan.Zero;
        rd.DeviceConfig = new DeviceConfigInfo { DeviceType = AtomSpectraTcpDeviceConfig.DeviceTypeId };
        rd.Series = new SeriesSettings { Enabled = true, Count = 3, Folder = @"C:\s", Prefix = "sp" };

        MainForm mf = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
        DCControlPanel panel = new DCControlPanel(mf);

        FieldInfo rtField = typeof(DCControlPanel).GetField("realTimeLimitTextBox", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (rtField == null) throw new Exception("realTimeLimitTextBox field not found");
        TextBox rtBox = (TextBox)rtField.GetValue(panel);
        if (rtBox != null) rtBox.Text = "600";

        string capturedText = "";
        FieldInfo fi = typeof(DCControlPanel).GetField("SeriesDialog", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (fi == null) throw new Exception("SeriesDialog field not found");
        fi.SetValue(panel, (Func<string, MessageBoxButtons, DialogResult>)((text, buttons) =>
        {
            capturedText = text;
            return DialogResult.OK;
        }));

        MethodInfo mi = typeof(DCControlPanel).GetMethod("SeriesBegin", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (mi == null) throw new Exception("SeriesBegin not found");
        object res = mi.Invoke(panel, new object[] { doc, rd, rd.ResultDataStatus });
        if ((bool)res) throw new Exception("SeriesBegin should return false for TCP/REST");
        if (capturedText == null || !capturedText.Contains("TCP/REST")) throw new Exception("Captured text does not contain TCP/REST: " + capturedText);
        if (rd.MeasurementController.Series != null) throw new Exception("Series should be null");

        rd.DeviceConfig = new DeviceConfigInfo { DeviceType = "AtomSpectraVCP" };
        capturedText = "";
        object res2 = mi.Invoke(panel, new object[] { doc, rd, rd.ResultDataStatus });
        if (!(bool)res2) throw new Exception("SeriesBegin should return true for AtomSpectraVCP");
        if (capturedText != "") throw new Exception("Dialog text should be empty for AtomSpectraVCP");
        if (rd.MeasurementController.Series == null) throw new Exception("Series should not be null");
        rd.MeasurementController.Series.UserStop(false);
    }

    static void U7()
    {
        DocEnergySpectrum docA = new DocEnergySpectrum();
        ResultData rdA = docA.ActiveResultData;
        SimpleHost hostA = new SimpleHost();
        MeasurementSeries seriesA = MakeSeries(3, 60, hostA);
        rdA.MeasurementController.Series = seriesA;
        seriesA.Begin(0);
        seriesA.OnSpectrumComplete();

        DocEnergySpectrum docB = new DocEnergySpectrum();
        ResultData rdB = docB.ActiveResultData;

        MainForm mf = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
        DCControlPanel panel = new DCControlPanel(mf);

        FieldInfo lblField = typeof(DCControlPanel).GetField("seriesStatusLbl", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        FieldInfo btnField = typeof(DCControlPanel).GetField("seriesSkipBtn", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (lblField == null) throw new Exception("seriesStatusLbl field not found");
        if (btnField == null) throw new Exception("seriesSkipBtn field not found");

        MethodInfo mi = typeof(DCControlPanel).GetMethod("ShowSeriesStatus", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (mi == null) throw new Exception("ShowSeriesStatus not found");

        mi.Invoke(panel, new object[] { docA, rdA });
        Label lbl = (Label)lblField.GetValue(panel);
        Button btn = (Button)btnField.GetValue(panel);
        if (lbl == null || string.IsNullOrEmpty(lbl.Text)) throw new Exception("Label text should not be empty for docA");
        if (btn == null || !VisibleFlag(btn)) throw new Exception("Skip button should be visible for docA");

        mi.Invoke(panel, new object[] { docB, rdB });
        if (lbl == null || !string.IsNullOrEmpty(lbl.Text)) throw new Exception("Label text should be empty for docB");
        if (btn == null || VisibleFlag(btn)) throw new Exception("Skip button should not be visible for docB");
        if (seriesA.State != SeriesState.Delay) throw new Exception("Series A state is " + seriesA.State + ", expected Delay");

        mi.Invoke(panel, new object[] { docA, rdA });
        if (lbl == null || string.IsNullOrEmpty(lbl.Text)) throw new Exception("Label text should not be empty for docA again");
    }

    static MeasurementSeries MakeSeries(int count, int delay, SimpleHost host)
    {
        return new MeasurementSeries(new SeriesSettings
        {
            Enabled = true,
            Count = count,
            DelaySeconds = delay,
            ClearBetween = true,
            Folder = @"C:\s",
            Prefix = "sp"
        }, host, 600);
    }

    class SimpleHost : ISeriesHost
    {
        public int Saves { get; set; }
        public int Starts { get; set; }

        public DateTime Now { get { return DateTime.Now; } }
        public bool FileExists(string path) { return false; }
        public long FreeDiskBytes(string path) { return -1; }
        public bool SaveSpectrum(string path, out string error)
        {
            Saves++;
            error = "";
            return true;
        }
        public bool ClearSpectrum(out string error)
        {
            error = "";
            return true;
        }
        public bool StartAcquisition(int presetSeconds, out string error)
        {
            Starts++;
            error = "";
            return true;
        }
        public void StateChanged() { }
        public void SeriesFinished() { }
        public long MonotonicMs { get { return Environment.TickCount; } }
        public void SetKeepAwake(bool on) { }
    }
}
