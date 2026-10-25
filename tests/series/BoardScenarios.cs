using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using BecquerelMonitor;
static partial class BoardCheck
{
    static void RunScenario(string name)
    {
        switch (name)
        {
            case "happy3": S_happy3(); break;
            case "userstop": S_userstop(); break;
            case "rst_fail": S_rst_fail(); break;
            case "sta_fail": S_sta_fail(); break;
            case "pause_link_lost": S_pause_link_lost(); break;
            case "pause_blip": S_pause_blip(); break;
            case "reset_outside": S_reset_outside(); break;
            case "stop_question_lost": S_stop_question_lost(); break;
            case "delayed_start": S_delayed_start(); break;
            case "counting_refuse": S_counting_refuse(); break;
            case "counting_mid": S_counting_mid(); break;
            case "foreign_data": S_foreign_data(); break;
            case "reuse_foreign": S_reuse_foreign(); break;
            case "observe": S_observe(); break;
            case "drop_acquiring": S_drop_acquiring(); break;
            case "sto_fail": S_sto_fail(); break;
            case "windowed_control": S_windowed_control(); break;
            case "windowed_series": S_windowed_series(); break;
            default: Check(false, "unknown scenario " + name); break;
        }
    }

    static bool Wait(int ms)
    {
        return Pump(Terminal, ms);
    }

    static int Long
    {
        get { return IsRest ? 170000 : 110000; }
    }

    static void S_happy3()
    {
        Init(3, 0, 4);
        Check(Begin(), "started");
        Wait(Long);
        Check(Series.State == SeriesState.Done, "state done");
        string[] files = Files();
        Check(files.Length == 3, "three files");
        Check(!Rd.ResultDataStatus.Recording, "not recording");
        long c1 = 0, c2 = 0, c3 = 0;
        double s1 = 0, s2 = 0, s3 = 0;
        ReadFile(files[0], out c1, out s1);
        ReadFile(files[1], out c2, out s2);
        ReadFile(files[2], out c3, out s3);
        Check(c1 > 0 && s1 > 3.5 && s1 < 9, "first file ok");
        Check(c2 > 0 && s2 > 3.5 && s2 < 9, "second file ok");
        Check(c3 > 0 && s3 > 3.5 && s3 < 9, "third file ok");
        Check(c2 < 1.8 * c1, "second not accumulated");
        Check(c3 < 1.8 * c1, "third not accumulated");
        string joined = string.Join("", Cmds().Where(c => c == "-sta" || c == "-rst"));
        Check(joined == "-sta-rst-sta-rst-sta", "command order");
    }

    static void S_userstop()
    {
        Init(5, 0, 40);
        Begin();
        Pump(() => Rd.ResultDataStatus.ElapsedTime.TotalSeconds >= 2, 40000);
        Answer = (text, buttons) => text == Txt("StopQuestion") ? DialogResult.Yes : DialogResult.OK;
        StopByUser();
        Check(Series.State == SeriesState.Idle, "idle");
        Check(!Rd.ResultDataStatus.Recording, "not recording");
        string[] files = Files();
        Check(files.Length == 1, "one file");
        long counts; double seconds;
        ReadFile(files[0], out counts, out seconds);
        Check(counts > 0, "counts");
        Check(seconds < 30, "seconds");
        Check(Count("-sto") >= 1, "stop sent");
        Check(Count("-rst") == 0, "no reset");
    }

    static void S_rst_fail()
    {
        Init(3, 0, 4);
        Begin();
        Wait(Long);
        Check(Series.State == SeriesState.PausedError, "paused error");
        string[] files = Files();
        Check(files.Length == 1, "one file");
        long counts; double seconds;
        ReadFile(files[0], out counts, out seconds);
        Check(counts > 0, "first kept");
        Check(!Rd.ResultDataStatus.Recording, "not recording");
        Check(!string.IsNullOrEmpty(Series.LastError), "error set");
        Note(Series.LastError);
        Check(Count("-sta") == 1, "one start");
    }

    static void S_sta_fail()
    {
        Init(3, 0, 4);
        Begin();
        Wait(Long);
        Check(Series.State == SeriesState.PausedError, "paused error");
        string[] files = Files();
        Check(files.Length == 1, "one file");
        long counts; double seconds;
        ReadFile(files[0], out counts, out seconds);
        Check(counts > 0, "first kept");
        Check(!Rd.ResultDataStatus.Recording, "not recording");
        Check(!string.IsNullOrEmpty(Series.LastError), "error set");
        Note(Series.LastError);
        Check(Count("-sta") == 2, "the second start was sent and not answered");
        Check(Count("-rst") == 1, "one reset");
    }

    static void S_pause_link_lost()
    {
        Init(3, 8, 4);
        Begin();
        Check(Pump(() => Series.State == SeriesState.Delay, 60000), "reached delay");
        Down(14000);
        Wait(Long);
        Check(Series.State == SeriesState.PausedError, "paused error");
        Check(Files().Length == 1, "one file");
        Check(Count("-sta") == 1, "one start");
        Check(!string.IsNullOrEmpty(Series.LastError), "error set");
        Check(!Rd.ResultDataStatus.Recording, "not recording");
    }

    static void S_pause_blip()
    {
        Init(3, 8, 4);
        Begin();
        Check(Pump(() => Series.State == SeriesState.Delay, 60000), "reached delay");
        Down(1500);
        Wait(Long);
        Check(Series.State == SeriesState.Done, "done");
        Check(Files().Length == 3, "three files");
    }

    static void S_reset_outside()
    {
        Init(3, 0, 40);
        Begin();
        Wait(Long);
        Check(Series.State == SeriesState.PausedError, "paused error");
        Check(Series.PartialPending, "partial pending");
        Check(Files().Length == 0, "no files yet");
        string err;
        bool ok = Series.SavePartialAfterLoss(out err);
        Check(ok, "saved partial");
        Note(err);
        string[] files = Files();
        Check(files.Length == 1, "one file");
        long counts; double seconds;
        ReadFile(files[0], out counts, out seconds);
        Check(counts > 0, "counts");
        Check(seconds >= 2, "seconds");
        Check(!Rd.ResultDataStatus.Recording, "not recording");
    }

    static void S_stop_question_lost()
    {
        Init(3, 0, 40);
        Begin();
        Check(Pump(() => Series.State == SeriesState.Acquiring && Rd.ResultDataStatus.ElapsedTime.TotalSeconds >= 1, 60000), "acquiring");
        Answer = (text, buttons) =>
        {
            if (text == Txt("StopQuestion"))
            {
                Pump(() => Series.PartialPending, 60000);
                return DialogResult.Yes;
            }
            return DialogResult.OK;
        };
        StopByUser();
        Check(Dialogs.Contains(Txt("StopQuestion")), "stop question shown");
        Check(Dialogs.Any(d => d.StartsWith(Txt("LostSaved"))), "lost saved shown");
        Check(Series.State == SeriesState.Idle, "idle");
        string[] files = Files();
        Check(files.Length == 1, "one file");
        long counts; double seconds;
        ReadFile(files[0], out counts, out seconds);
        Check(counts > 0, "counts");
    }

    static void S_delayed_start()
    {
        Init(2, 0, 4, 2, 6);
        int baseLines = LogLines() + Accepts() * 0; int baseAccepts = Accepts();   // the readiness probe of the driver is in the log already
        bool started = Begin();
        Check(!started, "not started yet");
        Check(Series.State == SeriesState.Waiting, "waiting");
        PumpFor(4000);
        Check(Accepts() == baseAccepts && Cmds().Count == 0 && (!IsRest || LogLines() == baseLines), "the board is not touched while the series waits");
        Check(Count("-sta") == 0, "no start");
        Wait(Long);
        Check(Series.State == SeriesState.Done, "done");
        Check(Files().Length == 2, "two files");
    }

    static void S_counting_refuse()
    {
        Init(3, 0, 4);
        bool started = Begin();
        Check(!started, "refused");
        Check(Series.State == SeriesState.Idle, "idle");
        Check(Dialogs.Contains(Txt("BoardCounting")), "counting dialog");
        Check(!Rd.ResultDataStatus.Recording, "not recording");
        Check(Cmds().Count == 0, "no commands");
    }

    static void S_counting_mid()
    {
        if (IsRest) { Note("n/a for rest"); return; }
        Init(3, 6, 4);
        Begin();
        Wait(Long);
        Check(Series.State == SeriesState.PausedError, "paused error");
        Check(Files().Length == 1, "one file");
        Check(Series.LastError.Contains(Txt("BoardCounting")), "counting error");
        Check(Count("-rst") == 0, "no reset");
        Check(Count("-sta") == 1, "one start");
    }

    static void S_foreign_data()
    {
        Init(3, 0, 4);
        bool started = Begin();
        Check(!started, "refused");
        Check(Series.State == SeriesState.Idle, "idle");
        Check(Dialogs.Contains(Txt("BoardNotReady")), "not ready dialog");
        Check(Cmds().Count == 0, "no commands");
    }

    static void S_observe()
    {
        if (!IsRest) { Note("n/a for tcp"); return; }
        Init(3, 0, 4);
        ((AtomSpectraTcpDeviceConfig)Rd.DeviceConfig.InputDeviceConfig).RestObserveOnly = true;
        bool started = Begin();
        Check(!started, "refused");
        Check(Dialogs.Contains(Txt("ObserveOnlyUnavailable")), "observe dialog");
        Check(Series == null || Series.State == SeriesState.Idle, "idle");
        Check(Cmds().Count == 0, "no commands");
    }

    static void S_drop_acquiring()
    {
        if (IsRest) { Note("n/a for rest"); return; }
        Init(3, 0, 40);
        Begin();
        Pump(() => Rd.ResultDataStatus.ElapsedTime.TotalSeconds >= 1, 40000);
        Kill();
        PumpFor(6000);
        Check(Series.State == SeriesState.Acquiring, "still acquiring");
        Check(Files().Length == 0, "no files");
        try
        {
            Series.UserStop(false);
            Rd.MeasurementController.StopRecording();
        }
        catch { }
    }

    static void S_sto_fail()
    {
        if (IsRest) { Note("n/a for rest"); return; }
        Init(3, 0, 4);
        Begin();
        Wait(Long);
        Check(Series.State == SeriesState.Done, "done");
        Check(Files().Length == 3, "three files");
    }

    static void S_windowed_control()
    {
        if (IsRest) { Note("n/a for rest"); return; }
        SetWindowsForAsk(() => true);
        Init(3, 0, 4);
        Rd.Series.Enabled = false;
        Check(Begin(), "started");
        object dc = Rd.MeasurementController.DeviceController;
        object session = dc.GetType().GetField("session", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dc);
        Check(AskOf(session) != null, "without a series the Start question is armed (control)");
        try { Rd.MeasurementController.StopRecording(); } catch { }
    }

    static void S_windowed_series()
    {
        SetWindowsForAsk(() => true);
        Init(2, 0, 4);
        Check(Begin(), "started");
        object dc = Rd.MeasurementController.DeviceController;
        object session = dc.GetType().GetField("session", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dc);
        Check(AskOf(session) == null, "a series never asks questions");
        Wait(Long);
        Check(Series.State == SeriesState.Done, "done");
        Check(Files().Length == 2, "two files");
    }

    static void SetWindowsForAsk(Func<bool> value)
    {
        Type t = typeof(AtomSpectraTcpDeviceController);
        FieldInfo f = t.GetField("WindowsForAsk", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        f.SetValue(null, value);
    }

    static object AskOf(object session)
    {
        if (session == null) return null;
        PropertyInfo p = session.GetType().GetProperty("Ask");
        if (p != null) return p.GetValue(session);
        FieldInfo f = session.GetType().GetField("Ask");
        if (f != null) return f.GetValue(session);
        return null;
    }
}

static partial class BoardCheck
{
    // connections the stub has accepted so far (the driver's own readiness probe is one of them)
    static int Accepts()
    {
        try
        {
            using (var fs = new System.IO.FileStream(LogPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
            using (var sr = new System.IO.StreamReader(fs))
            {
                int n = 0;
                string line;
                while ((line = sr.ReadLine()) != null) if (line.Contains("\"event\": \"accept\"")) n++;
                return n;
            }
        }
        catch (Exception) { return 0; }
    }
}

static partial class BoardCheck
{
    // a second series on the same document: emptied by hand, the board still holds the first series' last spectrum
    static void S_reuse_foreign()
    {
        Init(1, 0, 4);
        Check(Begin(), "first series started");
        Wait(Long);
        Check(Series.State == SeriesState.Done && Files().Length == 1, "first series done");
        int starts = Count("-sta");
        Rd.EnergySpectrum.Initialize();
        Rd.ResultDataStatus.ElapsedTime = TimeSpan.Zero;
        Rd.ResultDataStatus.TotalTime = TimeSpan.Zero;
        Dialogs.Clear();
        bool started = Begin();
        Check(!started, "the second series does not resume the measurement of the board");
        Check(Dialogs.Contains(Txt("BoardNotReady")), "not ready dialog");
        Check(Count("-sta") == starts && Count("-rst") == 0, "nothing was sent to the board");
    }
}
