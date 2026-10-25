using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;
using BecquerelMonitor;

public static class UiStage4
{
    public static int Run()
    {
        int fails = 0;
        fails += Check("Z13_start_refuses_bad_template", Z13);
        fails += Check("Z14_delete_cancel_keeps_series", Z14);
        fails += Check("Z15_close_by_name_asks_series_before_save", Z15);
        fails += Check("Z16_close_active_asks_series_before_save", Z16);
        fails += Check("Z18_start_refuses_long_folder", Z18);
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
            Console.WriteLine("FAIL " + name + ": " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    private static void Z13()
    {
        // Case A: bad template, noOverwrite false -> refusal (OK button)
        {
            var doc = new DocEnergySpectrum();
            doc.CreateResultData();
            var rd = doc.ActiveResultData;
            Setup(doc, "data", 5, false, @"C:\s");
            var calls = new List<string>();
            var buttons = new List<MessageBoxButtons>();
            var panel = NewPanel((text, btn) => { calls.Add(text); buttons.Add(btn); return DialogResult.Yes; });
            bool ok = (bool)Call(panel, "SeriesBegin", new object[] { doc, rd, rd.ResultDataStatus });
            if (ok) throw new Exception("Case A: expected false");
            if (rd.MeasurementController.Series != null) throw new Exception("Case A: series not null");
            if (calls.Count != 1) throw new Exception("Case A: dialog calls " + calls.Count);
            if (buttons[0] != MessageBoxButtons.OK) throw new Exception("Case A: expected OK button");
        }
        // Case B: bad template, noOverwrite true -> warning (YesNo), user says No
        {
            var doc = new DocEnergySpectrum();
            doc.CreateResultData();
            var rd = doc.ActiveResultData;
            Setup(doc, "data", 5, true, @"C:\s");
            var calls = new List<string>();
            var buttons = new List<MessageBoxButtons>();
            var panel = NewPanel((text, btn) => { calls.Add(text); buttons.Add(btn); return DialogResult.No; });
            bool ok = (bool)Call(panel, "SeriesBegin", new object[] { doc, rd, rd.ResultDataStatus });
            if (ok) throw new Exception("Case B: expected false");
            if (rd.MeasurementController.Series != null) throw new Exception("Case B: series not null");
            if (calls.Count != 1) throw new Exception("Case B: dialog calls " + calls.Count);
            if (buttons[0] != MessageBoxButtons.YesNo) throw new Exception("Case B: expected YesNo button");
        }
        // Case C: bad template, noOverwrite true -> warning (YesNo), user says Yes
        {
            var doc = new DocEnergySpectrum();
            doc.CreateResultData();
            var rd = doc.ActiveResultData;
            Setup(doc, "data", 5, true, @"C:\s");
            var calls = new List<string>();
            var buttons = new List<MessageBoxButtons>();
            var panel = NewPanel((text, btn) => { calls.Add(text); buttons.Add(btn); return DialogResult.Yes; });
            bool ok = (bool)Call(panel, "SeriesBegin", new object[] { doc, rd, rd.ResultDataStatus });
            if (!ok) throw new Exception("Case C: expected true");
            if (rd.MeasurementController.Series == null) throw new Exception("Case C: series null");
            if (calls.Count != 1) throw new Exception("Case C: dialog calls " + calls.Count);
            if (buttons[0] != MessageBoxButtons.YesNo) throw new Exception("Case C: expected YesNo button");
            rd.MeasurementController.Series.UserStop(false);
        }
        // Case D: good template, noOverwrite false -> no dialog
        {
            var doc = new DocEnergySpectrum();
            doc.CreateResultData();
            var rd = doc.ActiveResultData;
            Setup(doc, "{prefix}_{n:000}", 5, false, @"C:\s");
            var calls = new List<string>();
            var panel = NewPanel((text, btn) => { calls.Add(text); return DialogResult.Yes; });
            bool ok = (bool)Call(panel, "SeriesBegin", new object[] { doc, rd, rd.ResultDataStatus });
            if (!ok) throw new Exception("Case D: expected true");
            if (rd.MeasurementController.Series == null) throw new Exception("Case D: series null");
            if (calls.Count != 0) throw new Exception("Case D: dialog called " + calls.Count + " times");
            rd.MeasurementController.Series.UserStop(false);
        }
        // Case E: count 1, bad template, noOverwrite false -> no dialog
        {
            var doc = new DocEnergySpectrum();
            doc.CreateResultData();
            var rd = doc.ActiveResultData;
            Setup(doc, "data", 1, false, @"C:\s");
            var calls = new List<string>();
            var panel = NewPanel((text, btn) => { calls.Add(text); return DialogResult.Yes; });
            bool ok = (bool)Call(panel, "SeriesBegin", new object[] { doc, rd, rd.ResultDataStatus });
            if (!ok) throw new Exception("Case E: expected true");
            if (rd.MeasurementController.Series == null) throw new Exception("Case E: series null");
            if (calls.Count != 0) throw new Exception("Case E: dialog called " + calls.Count + " times");
            rd.MeasurementController.Series.UserStop(false);
        }
    }

    private static void Z14()
    {
        var host = new Z4Host();
        var doc = new DocEnergySpectrum();
        doc.CreateResultData();
        var rd = doc.ActiveResultData;
        rd.MeasurementController.Series = MakeSeries(host);
        rd.MeasurementController.Series.Begin(0);

        var log = new List<string>();
        var main = NewMain();
        SetField(main, "UiAsk", (Func<string, MessageBoxButtons, DialogResult>)((text, btn) =>
        {
            if (btn == MessageBoxButtons.YesNo) { log.Add("series"); return DialogResult.Yes; }
            if (btn == MessageBoxButtons.OKCancel) { log.Add("delete"); return DialogResult.Cancel; }
            return DialogResult.Cancel;
        }));

        Call(main, "DeleteActiveSpectrum", new object[] { doc });

        if (doc.ResultDataFile.ResultDataList.Count != 2) throw new Exception("list count " + doc.ResultDataFile.ResultDataList.Count);
        if (!rd.MeasurementController.Series.IsActive) throw new Exception("series not active");
        if (host.Saves != 0) throw new Exception("saves " + host.Saves);
        if (log.Count != 2 || log[0] != "series" || log[1] != "delete") throw new Exception("log: " + string.Join(",", log));
    }

    private static void Z15()
    {
        var host = new Z4Host();
        var doc = new DocEnergySpectrum();
        doc.CreateResultData();
        doc.Filename = "z15.xml";
        var rd = doc.ActiveResultData;
        rd.MeasurementController.Series = MakeSeries(host);
        rd.MeasurementController.Series.Begin(0);

        var dm = DocumentManager.GetInstance();
        dm.DocumentList.Add(doc);
        var main = NewMain();
        SetField(main, "documentManager", dm);

        try
        {
            var log = new List<string>();
            SetField(main, "UiAsk", (Func<string, MessageBoxButtons, DialogResult>)((text, btn) =>
            {
                if (btn == MessageBoxButtons.YesNo) { log.Add("series"); return DialogResult.Yes; }
                return DialogResult.Cancel;
            }));
            SetField(main, "SaveAsk", (Func<DocEnergySpectrum, bool>)((d) => { log.Add("save"); return false; }));

            Call(main, "CloseDocument", new object[] { "z15.xml", false });

            if (log.Count != 2 || log[0] != "series" || log[1] != "save") throw new Exception("log: " + string.Join(",", log));
            if (!rd.MeasurementController.Series.IsActive) throw new Exception("series not active");
            if (!dm.DocumentList.Contains(doc)) throw new Exception("doc not in list");

            log.Clear();
            SetField(main, "UiAsk", (Func<string, MessageBoxButtons, DialogResult>)((text, btn) =>
            {
                if (btn == MessageBoxButtons.YesNo) { log.Add("series"); return DialogResult.No; }
                return DialogResult.Cancel;
            }));

            Call(main, "CloseDocument", new object[] { "z15.xml", true });

            if (log.Count != 1 || log[0] != "series") throw new Exception("log: " + string.Join(",", log));
            if (!rd.MeasurementController.Series.IsActive) throw new Exception("series not active");
            if (!dm.DocumentList.Contains(doc)) throw new Exception("doc not in list");
        }
        finally
        {
            dm.DocumentList.Remove(doc);
        }
    }

    private static void Z16()
    {
        var host = new Z4Host();
        var doc = new DocEnergySpectrum();
        doc.CreateResultData();
        var rd = doc.ActiveResultData;
        rd.MeasurementController.Series = MakeSeries(host);
        rd.MeasurementController.Series.Begin(0);

        var main = NewMain();
        SetField(main, "activeDocument", doc);

        var log = new List<string>();
        SetField(main, "UiAsk", (Func<string, MessageBoxButtons, DialogResult>)((text, btn) =>
        {
            if (btn == MessageBoxButtons.YesNo) { log.Add("series"); return DialogResult.Yes; }
            return DialogResult.Cancel;
        }));
        SetField(main, "SaveAsk", (Func<DocEnergySpectrum, bool>)((d) => { log.Add("save"); return false; }));

        bool result = (bool)Call(main, "CloseActiveDocument", new object[] { });
        if (result) throw new Exception("expected false");
        if (log.Count != 2 || log[0] != "series" || log[1] != "save") throw new Exception("log: " + string.Join(",", log));
        if (!rd.MeasurementController.Series.IsActive) throw new Exception("series not active");

        log.Clear();
        SetField(main, "UiAsk", (Func<string, MessageBoxButtons, DialogResult>)((text, btn) =>
        {
            if (btn == MessageBoxButtons.YesNo) { log.Add("series"); return DialogResult.No; }
            return DialogResult.Cancel;
        }));

        result = (bool)Call(main, "CloseActiveDocument", new object[] { });
        if (result) throw new Exception("expected false");
        if (log.Count != 1 || log[0] != "series") throw new Exception("log: " + string.Join(",", log));
        if (!rd.MeasurementController.Series.IsActive) throw new Exception("series not active");
    }

    private static void Z18()
    {
        var doc = new DocEnergySpectrum();
        doc.CreateResultData();
        var rd = doc.ActiveResultData;
        Setup(doc, "{prefix}_{n:000}", 5, true, @"C:\" + new string('d', 240));
        var calls = new List<string>();
        var buttons = new List<MessageBoxButtons>();
        var panel = NewPanel((text, btn) => { calls.Add(text); buttons.Add(btn); return DialogResult.Yes; });
        bool ok = (bool)Call(panel, "SeriesBegin", new object[] { doc, rd, rd.ResultDataStatus });
        if (ok) throw new Exception("expected false");
        if (rd.MeasurementController.Series != null) throw new Exception("series not null");
        if (calls.Count != 1) throw new Exception("dialog calls " + calls.Count);
        if (buttons[0] != MessageBoxButtons.OK) throw new Exception("expected OK button");
    }

    private static ResultData Setup(DocEnergySpectrum doc, string template, int count, bool noOverwrite, string folder)
    {
        var rd = doc.ActiveResultData;
        rd.Series = new SeriesSettings
        {
            Enabled = true,
            Count = count,
            Folder = folder,
            Template = template,
            Prefix = "sp",
            NoOverwrite = noOverwrite
        };
        rd.DeviceConfig = new DeviceConfigInfo { DeviceType = "AudioInput" };
        rd.ResultDataStatus.ElapsedTime = TimeSpan.Zero;
        return rd;
    }

    private static MeasurementSeries MakeSeries(Z4Host h)
    {
        return new MeasurementSeries(
            new SeriesSettings
            {
                Enabled = true,
                Count = 5,
                DelaySeconds = 0,
                ClearBetween = true,
                Folder = @"C:\s",
                Prefix = "sp"
            },
            h,
            600);
    }

    private static object Call(object target, string name, object[] args)
    {
        var mi = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (mi == null) throw new Exception(name + " is missing");
        try
        {
            return mi.Invoke(target, args);
        }
        catch (TargetInvocationException tie)
        {
            throw new Exception(tie.InnerException.GetType().Name + ": " + tie.InnerException.Message);
        }
    }

    private static void SetField(object o, string name, object value)
    {
        var fi = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (fi == null) throw new Exception(name + " seam missing");
        fi.SetValue(o, value);
    }

    private static DCControlPanel NewPanel(Func<string, MessageBoxButtons, DialogResult> dialog)
    {
        var main = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
        var panel = new DCControlPanel(main);
        SetField(panel, "SeriesDialog", dialog);
        ((TextBox)typeof(DCControlPanel).GetField("realTimeLimitTextBox", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(panel)).Text = "600";
        return panel;
    }

    private static MainForm NewMain()
    {
        return (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
    }
}

public class Z4Host : ISeriesHost
{
    public int Saves { get; set; }

    public DateTime Now => DateTime.Now;

    public long MonotonicMs => Environment.TickCount;

    public bool FileExists(string path) => false;

    public long FreeDiskBytes(string path) => -1;

    public bool SaveSpectrum(string path, out string error)
    {
        Saves++;
        error = null;
        return true;
    }

    public bool ClearSpectrum(out string error)
    {
        error = null;
        return true;
    }

    public bool StartAcquisition(int presetSeconds, out string error)
    {
        error = null;
        return true;
    }

    public void StateChanged() { }

    public void SeriesFinished() { }

    public void SetKeepAwake(bool on) { }
}
