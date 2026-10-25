using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using BecquerelMonitor;

static partial class BoardCheck
{
    static List<string> Asks = new List<string>();
    static List<string> Reports = new List<string>();
    static bool AskAnswer = true;

    static bool RunPr1(string name)
    {
        switch (name)
        {
            case "clear_mid": S_clear_mid(); return true;
            case "clear_alone": S_clear_alone(); return true;
            case "clear_no": S_clear_no(); return true;
            case "fw_connfail": S_fw_connfail(); return true;
            case "fw_old_ok": S_fw_old_ok(); return true;
            default: return false;
        }
    }

    static bool InstallProbe()
    {
        Type appUi = typeof(DCControlPanel).Assembly.GetType("BecquerelMonitor.AppUi");
        if (appUi == null)
        {
            Check(false, "AppUi missing");
            return false;
        }
        FieldInfo probeField = appUi.GetField("Probe", BindingFlags.Public | BindingFlags.Static);
        if (probeField == null || probeField.FieldType != typeof(Func<string, string, string, bool>))
        {
            Check(false, "AppUi.Probe missing");
            return false;
        }
        Func<string, string, string, bool> probe = (kind, text, caption) =>
        {
            lock (Asks)
            {
                if (kind == "ask") Asks.Add(text);
                else Reports.Add(text);
            }
            return kind == "ask" ? AskAnswer : true;
        };
        probeField.SetValue(null, probe);
        return true;
    }

    static bool ConfirmAndClear()
    {
        Type mcType = Rd.MeasurementController.GetType();
        MethodInfo mi = mcType.GetMethod("ConfirmAndClear", BindingFlags.Public | BindingFlags.Instance);
        if (mi == null)
        {
            Check(false, "ConfirmAndClear missing");
            return false;
        }
        try
        {
            object result = mi.Invoke(Rd.MeasurementController, null);
            return (bool)result;
        }
        catch (TargetInvocationException tie)
        {
            throw tie.InnerException;
        }
    }

    static string FirmwareHint(string version)
    {
        Type fwType = typeof(DCControlPanel).Assembly.GetType("BecquerelMonitor.AtomSpectraFirmware");
        if (fwType == null)
        {
            Check(false, "AtomSpectraFirmware missing");
            return null;
        }
        MethodInfo mi = fwType.GetMethod("WarningText", BindingFlags.Public | BindingFlags.Static);
        if (mi == null)
        {
            Check(false, "WarningText missing");
            return null;
        }
        try
        {
            return (string)mi.Invoke(null, new object[] { version });
        }
        catch (TargetInvocationException tie)
        {
            throw tie.InnerException;
        }
    }

    static bool StartedNoSeries()
    {
        Rd.Series.Enabled = false;
        return Begin();
    }

    static void S_clear_mid()
    {
        Asks.Clear();
        Reports.Clear();
        AskAnswer = true;
        if (!InstallProbe()) return;
        Init(3, 6, 4);
        Check(Begin(), "started");
        Check(Pump(() => Files().Length >= 1 && Series.State == SeriesState.Acquiring, 120000), "second spectrum is being acquired");
        int before = Asks.Count;
        bool cleared = ConfirmAndClear();
        Check(cleared, "manual clear accepted");
        Check(Asks.Count - before == 1, "manual Clear asks exactly one question");
        Wait(Long);
        Check(Series.State == SeriesState.Done, "series done after a manual Clear");
        Check(Files().Length == 3, "three files");
        Check(Asks.Count == 1, "no question between spectra: only the manual one");
    }

    static void S_clear_alone()
    {
        Asks.Clear();
        Reports.Clear();
        AskAnswer = true;
        if (!InstallProbe()) return;
        Init(3, 0, 4);
        Check(StartedNoSeries(), "started");
        PumpFor(3000);
        bool cleared = ConfirmAndClear();
        Check(cleared, "cleared");
        Check(Asks.Count == 1, "exactly one question");
        try { Rd.MeasurementController.StopRecording(); } catch { }
    }

    static void S_clear_no()
    {
        Asks.Clear();
        Reports.Clear();
        AskAnswer = true;
        if (!InstallProbe()) return;
        Init(3, 0, 4);
        Check(StartedNoSeries(), "started");
        PumpFor(3000);
        AskAnswer = false;
        bool cleared = ConfirmAndClear();
        Check(!cleared, "declined");
        Check(Asks.Count == 1, "exactly one question");
        Check(Count("-rst") == 0, "nothing reaches the board after No");
        try { Rd.MeasurementController.StopRecording(); } catch { }
    }

    static void S_fw_connfail()
    {
        Init(2, 0, 4);
        bool started = Begin();
        Check(!started, "start refused (the TCP port is closed)");
        string hint = FirmwareHint("1.2.38");
        Check(hint != null && Dialogs.Any(d => d != null && d.Contains(hint)), "the refusal shown by the series carries the firmware hint");
        Check(Series.State == SeriesState.Idle, "series is idle, nothing hangs");
    }

    static void S_fw_old_ok()
    {
        Init(2, 0, 4);
        Check(Begin(), "started on firmware 1.2.38");
        Wait(Long);
        Check(Series.State == SeriesState.Done, "done");
        Check(Files().Length == 2, "two files");
        string hint = FirmwareHint("1.2.38");
        Check(hint != null && !Dialogs.Any(d => d != null && d.Contains(hint)), "no firmware hint when the board answers");
    }
}
