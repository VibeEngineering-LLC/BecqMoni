using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using BecquerelMonitor;

static partial class BoardCheck
{
    static string Scenario;
    static string Transport;
    static string LogPath;
    static string CtlPath;
    static string OutDir;
    static int Port;
    static bool IsRest;

    static List<string> Failures = new List<string>();
    static PumpContext Ctx;

    static DocEnergySpectrum Doc;
    static ResultData Rd;
    static DCControlPanel Panel;
    static List<string> Dialogs = new List<string>();
    static Func<string, MessageBoxButtons, DialogResult> Answer;
    static int Preset;

    static MeasurementSeries Series => Rd.MeasurementController.Series;

    static int Main(string[] args)
    {
        if (args.Length != 7)
        {
            Console.WriteLine("FAIL args");
            Console.Out.Flush();
            return 2;
        }

        Scenario = args[0];
        Transport = args[1];
        Port = int.Parse(args[2]);
        LogPath = args[3];
        CtlPath = args[4];
        OutDir = args[5];
        string lang = args[6];

        IsRest = Transport.Equals("rest", StringComparison.OrdinalIgnoreCase);

        CultureInfo ci = lang.Equals("ru", StringComparison.OrdinalIgnoreCase)
            ? new CultureInfo("ru-RU")
            : new CultureInfo("en-US");
        Thread.CurrentThread.CurrentUICulture = ci;
        Thread.CurrentThread.CurrentCulture = ci;

        Ctx = new PumpContext();

        // Watchdog
        Thread wd = new Thread(() =>
        {
            Thread.Sleep(170000);
            Console.WriteLine("FAIL " + Scenario + ": hang (a window opened or the pump stalled)");
            Console.Out.Flush();
            Environment.Exit(3);
        });
        wd.IsBackground = true;
        wd.Start();

        try
        {
            DeviceType.InitializeDeviceTypes();
        }
        catch (Exception ex)
        {
            Check(false, "InitializeDeviceTypes: " + ex.Message);
        }

        // Install pump context into MainForm.originalContext
        try
        {
            Type mfType = typeof(MainForm);
            FieldInfo ctxField = mfType.GetField("originalContext", BindingFlags.Public | BindingFlags.Static);
            if (ctxField != null)
            {
                ctxField.SetValue(null, Ctx);
            }
        }
        catch (Exception ex)
        {
            Check(false, "Set originalContext: " + ex.Message);
        }

        // REST probe gap
        if (IsRest)
        {
            try
            {
                Assembly asm = typeof(DCControlPanel).Assembly;
                Type restType = asm.GetType("BecquerelMonitor.AtomSpectraRestSession");
                if (restType != null)
                {
                    FieldInfo gapField = restType.GetField("ProbeGapMs", BindingFlags.Public | BindingFlags.Static);
                    if (gapField != null)
                    {
                        gapField.SetValue(null, 3200);
                    }
                }
            }
            catch
            {
                // ignore if missing
            }
        }

        try
        {
            RunScenario(Scenario);
        }
        catch (Exception ex)
        {
            Check(false, "Exception: " + ex.GetType().Name + ": " + ex.Message);
        }
        try { Diag(); } catch (Exception) { }

        if (Failures.Count == 0)
        {
            Console.WriteLine("PASS " + Scenario);
            Console.Out.Flush();
            return 0;
        }
        else
        {
            Console.WriteLine("FAIL " + Scenario + ": " + string.Join("; ", Failures));
            Console.Out.Flush();
            return 1;
        }
    }

    static void Check(bool ok, string what)
    {
        if (!ok)
        {
            Failures.Add(what);
            Console.WriteLine("  check failed: " + what);
        }
    }

    static void Note(string s)
    {
        Console.WriteLine("  note: " + s);
    }

    static void Init(int count, int delaySeconds, int presetSeconds, int startMode = 0, int startInSeconds = 0)
    {
        Preset = presetSeconds;
        Doc = new DocEnergySpectrum();
        Rd = Doc.ActiveResultData;
        Rd.EnergySpectrum = new EnergySpectrum(1.0, 8192);
        Rd.EnergySpectrum.EnergyCalibration = new PolynomialEnergyCalibration();   // a saved spectrum needs a calibration

        AtomSpectraTcpDeviceConfig cfg;
        if (IsRest)
        {
            cfg = new AtomSpectraTcpDeviceConfig
            {
                Transport = AtomSpectraTransport.Rest,
                RestUrl = "http://127.0.0.1:" + Port,
                RestPollSeconds = 1
            };
        }
        else
        {
            cfg = new AtomSpectraTcpDeviceConfig
            {
                Transport = AtomSpectraTransport.TcpBridge,
                Host = "127.0.0.1",
                TcpPort = Port
            };
        }

        Rd.DeviceConfig = new DeviceConfigInfo
        {
            Guid = Guid.NewGuid().ToString(),
            Name = "series-board",
            DeviceType = AtomSpectraTcpDeviceConfig.DeviceTypeId,
            NumberOfChannels = 8192,
            InputDeviceConfig = cfg
        };
        DeviceConfigManager.GetInstance().DeviceConfigMap[Rd.DeviceConfig.Guid] = Rd.DeviceConfig;   // the controller looks the configuration up by its GUID

        // Clean old files
        if (Directory.Exists(OutDir))
        {
            foreach (string f in Directory.GetFiles(OutDir, "b_*.xml"))
            {
                try { File.Delete(f); } catch { }
            }
        }
        else
        {
            Directory.CreateDirectory(OutDir);
        }

        Rd.Series = new SeriesSettings
        {
            Enabled = true,
            Count = count,
            DelaySeconds = delaySeconds,
            ClearBetween = true,
            Folder = OutDir,
            Template = "{prefix}_{n:000}",
            Prefix = "b",
            NoOverwrite = true,
            StartKind = startMode,
            StartInSeconds = startInSeconds
        };

        Rd.ResultDataStatus.ElapsedTime = TimeSpan.Zero;

        MainForm mf = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
        Panel = new DCControlPanel(mf);

        // Set SeriesDialog field
        FieldInfo dlgField = typeof(DCControlPanel).GetField("SeriesDialog", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (dlgField != null)
        {
            dlgField.SetValue(Panel, (Func<string, MessageBoxButtons, DialogResult>)((text, buttons) =>
            {
                Dialogs.Add(text);
                if (Answer != null)
                    return Answer(text, buttons);
                return DialogResult.OK;
            }));
        }

        // Set realTimeLimitTextBox
        FieldInfo tbField = typeof(DCControlPanel).GetField("realTimeLimitTextBox", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (tbField != null)
        {
            TextBox tb = (TextBox)tbField.GetValue(Panel);
            if (tb != null)
            {
                tb.Text = presetSeconds.ToString();
            }
        }
    }

    static bool Begin()
    {
        if (IsRest)
        {
            // the stub's timeline starts here (its external events are relative to this moment)
            try { ((HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + Port + "/__sync")).GetResponse().Close(); } catch (Exception) { }
        }
        Rd.ResultDataStatus.PresetTime = Preset;

        try
        {
            MethodInfo mi = typeof(DCControlPanel).GetMethod("SeriesBegin", BindingFlags.Instance | BindingFlags.NonPublic, null,
                new Type[] { typeof(DocEnergySpectrum), typeof(ResultData), typeof(ResultDataStatus) }, null);
            if (mi == null)
            {
                Check(false, "SeriesBegin method not found");
                return false;
            }
            object result = mi.Invoke(Panel, new object[] { Doc, Rd, Rd.ResultDataStatus });
            if (!(bool)result)
            {
                return false;
            }
        }
        catch (TargetInvocationException tie)
        {
            throw tie.InnerException;
        }
        catch (Exception ex)
        {
            Check(false, "SeriesBegin invoke: " + ex.Message);
            return false;
        }

        bool started = Rd.MeasurementController.StartRecording();

        try
        {
            MethodInfo mi = typeof(DCControlPanel).GetMethod("SeriesAfterStart", BindingFlags.Instance | BindingFlags.NonPublic, null,
                new Type[] { typeof(ResultData), typeof(bool) }, null);
            if (mi != null)
            {
                mi.Invoke(Panel, new object[] { Rd, started });
            }
        }
        catch (TargetInvocationException tie)
        {
            throw tie.InnerException;
        }
        catch (Exception ex)
        {
            Check(false, "SeriesAfterStart invoke: " + ex.Message);
        }

        return started;
    }

    static void StopByUser()
    {
        try
        {
            MethodInfo mi = typeof(DCControlPanel).GetMethod("SeriesBeforeStop", BindingFlags.Instance | BindingFlags.NonPublic, null,
                new Type[] { typeof(DocEnergySpectrum), typeof(ResultData), typeof(bool), typeof(bool).MakeByRefType() }, null);
            if (mi == null)
            {
                Check(false, "SeriesBeforeStop method not found");
                return;
            }

            object[] args = new object[] { Doc, Rd, true, false };
            object result = mi.Invoke(Panel, args);
            bool saveCurrent = (bool)args[3];
            int ret = (int)result;

            if (ret == 2)
            {
                return;
            }

            if (!Rd.ResultDataStatus.Recording)
            {
                MethodInfo miAfter = typeof(DCControlPanel).GetMethod("SeriesAfterStop", BindingFlags.Instance | BindingFlags.NonPublic, null,
                    new Type[] { typeof(ResultData), typeof(bool) }, null);
                if (miAfter != null)
                {
                    miAfter.Invoke(Panel, new object[] { Rd, saveCurrent });
                }
                return;
            }
            else
            {
                Rd.MeasurementController.StopRecording(true);
                MethodInfo miAfter = typeof(DCControlPanel).GetMethod("SeriesAfterStop", BindingFlags.Instance | BindingFlags.NonPublic, null,
                    new Type[] { typeof(ResultData), typeof(bool) }, null);
                if (miAfter != null)
                {
                    miAfter.Invoke(Panel, new object[] { Rd, saveCurrent });
                }
            }
        }
        catch (TargetInvocationException tie)
        {
            throw tie.InnerException;
        }
        catch (Exception ex)
        {
            Check(false, "StopByUser: " + ex.Message);
        }
    }

    static bool Pump(Func<bool> until, int timeoutMs)
    {
        long start = Environment.TickCount;
        long lastTimer = 0;
        long lastHostTick = 0;

        while (Environment.TickCount - start < timeoutMs)
        {
            Ctx.Drain();

            long now = Environment.TickCount;
            if (now - lastTimer >= 100)
            {
                lastTimer = now;
                try
                {
                    Rd.MeasurementController.OnTimer(null, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    Note("OnTimer: " + ex.Message);
                }
            }

            if (now - lastHostTick >= 500)
            {
                lastHostTick = now;
                try
                {
                    FieldInfo hostField = typeof(MeasurementController).GetField("SeriesHost", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (hostField != null)
                    {
                        object host = hostField.GetValue(Rd.MeasurementController);
                        if (host != null)
                        {
                            MethodInfo mi = host.GetType().GetMethod("OnTimerTick", BindingFlags.Instance | BindingFlags.NonPublic, null,
                                new Type[] { typeof(object), typeof(EventArgs) }, null);
                            if (mi != null)
                            {
                                mi.Invoke(host, new object[] { null, EventArgs.Empty });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Note("HostTick: " + ex.Message);
                }
            }

            if (until())
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return false;
    }

    static bool PumpFor(int ms)
    {
        return Pump(() => false, ms);
    }

    static bool Terminal()
    {
        return Series != null && (Series.State == SeriesState.Done || Series.State == SeriesState.PausedError || Series.State == SeriesState.Idle);
    }

    static void Down(int ms)
    {
        if (IsRest)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + Port + "/__down?ms=" + ms);
                req.Timeout = 3000;
                req.Method = "GET";
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    // ignore body
                }
            }
            catch
            {
                // ignore
            }
        }
        else
        {
            try
            {
                File.AppendAllText(CtlPath, "down " + ms + "\n", Encoding.ASCII);
            }
            catch
            {
                // ignore
            }
        }
    }

    static void Kill()
    {
        if (!IsRest)
        {
            try
            {
                File.AppendAllText(CtlPath, "kill\n", Encoding.ASCII);
            }
            catch
            {
                // ignore
            }
        }
    }

    static string[] Files()
    {
        if (!Directory.Exists(OutDir))
        {
            return new string[0];
        }
        return Directory.GetFiles(OutDir, "b_*.xml").OrderBy(f => f, StringComparer.Ordinal).ToArray();
    }

    static void ReadFile(string path, out long counts, out double seconds)
    {
        counts = 0;
        seconds = 0;
        try
        {
            XmlSerializer ser = new XmlSerializer(typeof(ResultDataFile));
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                ResultDataFile rdf = (ResultDataFile)ser.Deserialize(fs);
                if (rdf != null && rdf.ResultDataList != null && rdf.ResultDataList.Count > 0)
                {
                    ResultData rd0 = rdf.ResultDataList[0];
                    if (rd0.EnergySpectrum != null)
                    {
                        counts = rd0.EnergySpectrum.ValidPulseCount;
                    }
                    if (rd0.ResultDataStatus != null)
                    {
                        seconds = rd0.EnergySpectrum.MeasurementTime;
                    }
                }
            }
        }
        catch
        {
            // ignore
        }
    }

    static List<string> Cmds()
    {
        List<string> cmds = new List<string>();
        if (!File.Exists(LogPath))
        {
            return cmds;
        }

        try
        {
            using (FileStream fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader sr = new StreamReader(fs))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    if (IsRest)
                    {
                        if (line.Contains("\"path\": \"/api/command\""))
                        {
                            // Extract body
                            int bodyIdx = line.IndexOf("\"body\": \"", StringComparison.Ordinal);
                            if (bodyIdx >= 0)
                            {
                                int start = bodyIdx + 9;
                                int end = line.IndexOf('"', start);
                                if (end > start)
                                {
                                    string body = line.Substring(start, end - start);
                                    string[] words = body.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                    if (words.Length > 0)
                                    {
                                        cmds.Add(words[0]);
                                    }
                                }
                            }
                        }
                        else if (line.Contains("\"path\": \"/api/reset\""))
                        {
                            cmds.Add("-rst");
                        }
                    }
                    else
                    {
                        if (line.Contains("\"cmd\": \""))
                        {
                            int idx = line.IndexOf("\"cmd\": \"", StringComparison.Ordinal);
                            int start = idx + 8;
                            int end = line.IndexOf('"', start);
                            if (end > start)
                            {
                                cmds.Add(line.Substring(start, end - start));
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // ignore
        }

        return cmds;
    }

    static int Count(string cmd)
    {
        return Cmds().Count(c => c == cmd);
    }

    static int LogLines()
    {
        if (!File.Exists(LogPath))
        {
            return 0;
        }

        int count = 0;
        try
        {
            using (FileStream fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader sr = new StreamReader(fs))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        count++;
                    }
                }
            }
        }
        catch
        {
            // ignore
        }

        return count;
    }

    static string Txt(string propertyName)
    {
        try
        {
            Assembly asm = typeof(DCControlPanel).Assembly;
            Type stType = asm.GetType("BecquerelMonitor.SeriesText");
            if (stType != null)
            {
                PropertyInfo pi = stType.GetProperty(propertyName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (pi != null)
                {
                    return pi.GetValue(null) as string;
                }
            }
        }
        catch
        {
            // ignore
        }
        return null;
    }

    sealed class PumpContext : SynchronizationContext
    {
        ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();

        public override void Post(SendOrPostCallback d, object state)
        {
            queue.Enqueue(() => d(state));
        }

        public override void Send(SendOrPostCallback d, object state)
        {
            d(state);
        }

        public void Drain()
        {
            Action a;
            while (queue.TryDequeue(out a))
            {
                try
                {
                    a();
                }
                catch (Exception ex)
                {
                    Note("Drain: " + ex.Message);
                }
            }
        }
    }
}

static partial class BoardCheck
{
    // the end state of the run, printed as notes for the reader of a failure
    static void Diag()
    {
        if (Failures.Count == 0 || Rd == null) return;
        MeasurementSeries s = Series;
        Note("series " + (s == null ? "none" : s.State + " index " + s.Index + " error [" + s.LastError + "]") + "; recording " + Rd.ResultDataStatus.Recording
            + "; elapsed " + Rd.ResultDataStatus.ElapsedTime.TotalSeconds + " s; files " + Files().Length + "; commands " + string.Join(" ", Cmds()));
        foreach (string d in Dialogs) Note("dialog: " + d);
    }
}
