using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;

public static class UiStage3
{
    public static int Run()
    {
        int fails = 0;
        fails += Check("Z1_delete_declined_keeps_series", Z1);
        fails += Check("Z2_delete_confirmed_stops_series", Z2);
        fails += Check("Z3_menu_close_asks_about_series", Z3);
        fails += Check("Z4_tab_close_asks_about_series", Z4);
        fails += Check("Z5_close_document_by_name_asks", Z5);
        fails += Check("Z6_stop_cancel_keeps_series", Z6);
        fails += Check("Z7_after_stop_stops_series", Z7);
        fails += Check("Z8_rest_board_refused", Z8);
        fails += Check("Z9_device_lost_while_stop_question_open", Z9);
        fails += Check("Z10_form_refuses_template_without_n", Z10);
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

    private static void Z1()
    {
        Z3Host host = new Z3Host();
        DocEnergySpectrum doc = new DocEnergySpectrum();
        doc.CreateResultData();
        ResultData rd = doc.ActiveResultData;
        rd.MeasurementController.Series = MakeSeries(host, 5);
        rd.MeasurementController.Series.Begin(0);

        int askCount = 0;
        Func<string, MessageBoxButtons, DialogResult> ask = (text, buttons) =>
        {
            askCount++;
            return DialogResult.No;
        };

        bool seam;
        MainForm mf = NewMain(ask, out seam);

        using (new DialogWatcher())
        {
            Call(mf, "DeleteActiveSpectrum", new object[] { doc });
        }

        if (doc.ResultDataFile.ResultDataList.Count != 2)
            throw new Exception("list count is " + doc.ResultDataFile.ResultDataList.Count);
        if (!rd.MeasurementController.Series.IsActive)
            throw new Exception("series not active");
        if (host.Saves != 0)
            throw new Exception("saves is " + host.Saves);

        if (seam)
        {
            if (askCount != 1)
                throw new Exception("ask count is " + askCount);
        }
        else
        {
            if (doc.ResultDataFile.ResultDataList.Count != 2 && !rd.MeasurementController.Series.IsActive)
                throw new Exception("no UiAsk seam: the delete flow showed an unconfirmable dialog");
        }
    }

    private static void Z2()
    {
        Z3Host host = new Z3Host();
        DocEnergySpectrum doc = new DocEnergySpectrum();
        doc.CreateResultData();
        ResultData rd = doc.ActiveResultData;
        rd.MeasurementController.Series = MakeSeries(host, 5);
        rd.MeasurementController.Series.Begin(0);

        int seriesAsks = 0;
        Func<string, MessageBoxButtons, DialogResult> ask = (text, buttons) =>
        {
            if (buttons == MessageBoxButtons.OKCancel)
                return DialogResult.OK;
            seriesAsks++;
            return DialogResult.Yes;
        };

        bool seam;
        MainForm mf = NewMain(ask, out seam);

        try
        {
            using (new DialogWatcher())
            {
                Call(mf, "DeleteActiveSpectrum", new object[] { doc });
            }
        }
        catch
        {
            // ignore exceptions from teardown
        }

        if (rd.MeasurementController.Series.State != SeriesState.Idle)
            throw new Exception("state is " + rd.MeasurementController.Series.State);
        if (host.Saves != 0)
            throw new Exception("saves is " + host.Saves);
        if (!seam)
            throw new Exception("UiAsk missing");
        if (seriesAsks != 1)
            throw new Exception("seriesAsks is " + seriesAsks);
    }

    private static void Z3()
    {
        Z3Host host = new Z3Host();
        DocEnergySpectrum doc = new DocEnergySpectrum();
        ResultData rd = doc.ActiveResultData;
        rd.MeasurementController.Series = MakeSeries(host, 5);
        rd.MeasurementController.Series.Begin(0);

        int askCount = 0;
        Func<string, MessageBoxButtons, DialogResult> ask = (text, buttons) =>
        {
            askCount++;
            return DialogResult.No;
        };

        bool seam;
        MainForm mf = NewMain(ask, out seam);
        SetField(mf, "activeDocument", doc);

        object result = Call(mf, "CloseActiveDocument", null);
        if (!(bool)result)
            throw new Exception("CloseActiveDocument returned false");
        if (askCount != 1)
            throw new Exception("ask count is " + askCount);
        if (!rd.MeasurementController.Series.IsActive)
            throw new Exception("series not active");
        if (!seam)
            throw new Exception("UiAsk missing");
    }

    private static void Z4()
    {
        Z3Host host = new Z3Host();
        DocEnergySpectrum doc = new DocEnergySpectrum();
        ResultData rd = doc.ActiveResultData;
        rd.MeasurementController.Series = MakeSeries(host, 5);
        rd.MeasurementController.Series.Begin(0);

        int askCount = 0;
        Func<string, MessageBoxButtons, DialogResult> ask = (text, buttons) =>
        {
            askCount++;
            return DialogResult.No;
        };

        bool seam;
        MainForm mf = NewMain(ask, out seam);

        FormClosingEventArgs e = new FormClosingEventArgs(CloseReason.UserClosing, false);
        Call(mf, "DocEnergySpectrum_FormClosing", new object[] { doc, e });

        if (!e.Cancel)
            throw new Exception("e.Cancel is false");
        if (askCount != 1)
            throw new Exception("ask count is " + askCount);
        if (!rd.MeasurementController.Series.IsActive)
            throw new Exception("series not active");
    }

    private static void Z5()
    {
        Z3Host host = new Z3Host();
        DocEnergySpectrum doc = new DocEnergySpectrum();
        doc.Filename = "z5.xml";
        ResultData rd = doc.ActiveResultData;
        rd.MeasurementController.Series = MakeSeries(host, 5);
        rd.MeasurementController.Series.Begin(0);

        int askCount = 0;
        Func<string, MessageBoxButtons, DialogResult> ask = (text, buttons) =>
        {
            askCount++;
            return DialogResult.No;
        };

        bool seam;
        MainForm mf = NewMain(ask, out seam);

        DocumentManager dm = DocumentManager.GetInstance();
        dm.DocumentList.Add(doc);
        SetField(mf, "documentManager", dm);

        try
        {
            for (int i = 0; i < 2; i++)
            {
                bool force = (i == 0);
                using (new DialogWatcher())
                {
                    try
                    {
                        Call(mf, "CloseDocument", new object[] { "z5.xml", force });
                    }
                    catch
                    {
                        throw new Exception("CloseDocument threw");
                    }
                }

                if (!rd.MeasurementController.Series.IsActive)
                    throw new Exception("series not active after force=" + force);
                if (!dm.DocumentList.Contains(doc))
                    throw new Exception("doc removed from list");
            }

            if (askCount != 2)
                throw new Exception("ask count is " + askCount);
        }
        finally
        {
            dm.DocumentList.Remove(doc);
        }
    }

    private static void Z6()
    {
        Z3Host host = new Z3Host();
        DocEnergySpectrum doc = new DocEnergySpectrum();
        ResultData rd = doc.ActiveResultData;
        rd.MeasurementController.Series = MakeSeries(host, 5);
        rd.MeasurementController.Series.Begin(0);

        DCControlPanel panel = NewPanel((text, buttons) => DialogResult.Cancel);

        bool saveCurrent;
        object result = Call(panel, "SeriesBeforeStop", new object[] { doc, rd, true, false });
        int ret = (int)result;

        if (ret != 2)
            throw new Exception("ret is " + ret);
        if (rd.MeasurementController.Series.State != SeriesState.Acquiring)
            throw new Exception("state is " + rd.MeasurementController.Series.State);
        if (host.Saves != 0)
            throw new Exception("saves is " + host.Saves);
    }

    private static void Z7()
    {
        Z3Host host = new Z3Host();
        DocEnergySpectrum doc = new DocEnergySpectrum();
        ResultData rd = doc.ActiveResultData;
        rd.MeasurementController.Series = MakeSeries(host, 5);
        rd.MeasurementController.Series.Begin(0);

        DCControlPanel panel = NewPanel((text, buttons) => DialogResult.OK);

        try
        {
            Call(panel, "SeriesAfterStop", new object[] { rd, true });
        }
        catch
        {
            // ignore exceptions from status display
        }

        if (rd.MeasurementController.Series.State != SeriesState.Idle)
            throw new Exception("state is " + rd.MeasurementController.Series.State);
        if (host.Saves != 1)
            throw new Exception("saves is " + host.Saves);

        // Second part
        Z3Host host2 = new Z3Host();
        DocEnergySpectrum doc2 = new DocEnergySpectrum();
        ResultData rd2 = doc2.ActiveResultData;
        rd2.MeasurementController.Series = MakeSeries(host2, 5);
        rd2.MeasurementController.Series.Begin(0);

        DCControlPanel panel2 = NewPanel((text, buttons) => DialogResult.OK);

        try
        {
            Call(panel2, "SeriesAfterStop", new object[] { rd2, false });
        }
        catch
        {
            // ignore exceptions
        }

        if (rd2.MeasurementController.Series.State != SeriesState.Idle)
            throw new Exception("state is " + rd2.MeasurementController.Series.State);
        if (host2.Saves != 0)
            throw new Exception("saves is " + host2.Saves);
    }

    private static void Z8()
    {
        Z3Host host = new Z3Host();
        DocEnergySpectrum doc = new DocEnergySpectrum();
        ResultData rd = doc.ActiveResultData;

        rd.DeviceConfig = new DeviceConfigInfo
        {
            DeviceType = AtomSpectraTcpDeviceConfig.DeviceTypeId,
            InputDeviceConfig = new AtomSpectraTcpDeviceConfig
            {
                Transport = AtomSpectraTransport.Rest
            }
        };
        rd.Series = new SeriesSettings
        {
            Enabled = true,
            Count = 3,
            Folder = @"C:\s",
            Prefix = "sp"
        };
        rd.ResultDataStatus.ElapsedTime = TimeSpan.Zero;

        string capturedText = null;
        DCControlPanel panel = NewPanel((text, buttons) =>
        {
            capturedText = text;
            return DialogResult.OK;
        });

        object result = Call(panel, "SeriesBegin", new object[] { doc, rd, rd.ResultDataStatus });
        bool ret = (bool)result;

        if (ret)
            throw new Exception("SeriesBegin returned true");
        if (capturedText == null || !capturedText.Contains("TCP/REST"))
            throw new Exception("text does not contain TCP/REST: " + capturedText);
        if (rd.MeasurementController.Series != null)
            throw new Exception("series is not null");
    }

    private static void Z9()
    {
        // Part 1: First call triggers OnDeviceLost, returns Yes; second call returns OK
        Z3Host host = new Z3Host();
        DocEnergySpectrum doc = new DocEnergySpectrum();
        ResultData rd = doc.ActiveResultData;
        MeasurementSeries series = MakeSeries(host, 5);
        rd.MeasurementController.Series = series;
        series.Begin(0);

        int dialogCalls = 0;
        string lastText = null;
        DCControlPanel panel = NewPanel((text, buttons) =>
        {
            dialogCalls++;
            lastText = text;
            if (dialogCalls == 1)
            {
                series.OnDeviceLost("lost");
                return DialogResult.Yes;
            }
            return DialogResult.OK;
        });

        bool saveCurrent;
        Call(panel, "SeriesBeforeStop", new object[] { doc, rd, true, false });

        if (host.Saves != 1)
            throw new Exception("saves is " + host.Saves);
        if (dialogCalls != 2)
            throw new Exception("dialog calls is " + dialogCalls);
        if (series.State != SeriesState.Idle)
            throw new Exception("state is " + series.State);

        // Part 2: First answer is No
        Z3Host host2 = new Z3Host();
        DocEnergySpectrum doc2 = new DocEnergySpectrum();
        ResultData rd2 = doc2.ActiveResultData;
        MeasurementSeries series2 = MakeSeries(host2, 5);
        rd2.MeasurementController.Series = series2;
        series2.Begin(0);

        int dialogCalls2 = 0;
        DCControlPanel panel2 = NewPanel((text, buttons) =>
        {
            dialogCalls2++;
            if (dialogCalls2 == 1)
            {
                series2.OnDeviceLost("lost");
                return DialogResult.No;
            }
            return DialogResult.OK;
        });

        Call(panel2, "SeriesBeforeStop", new object[] { doc2, rd2, true, false });

        if (host2.Saves != 0)
            throw new Exception("saves is " + host2.Saves);
        if (dialogCalls2 != 1)
            throw new Exception("dialog calls is " + dialogCalls2);

        // Part 3: SaveError set, first answer Yes
        Z3Host host3 = new Z3Host();
        host3.SaveError = "disk full";
        DocEnergySpectrum doc3 = new DocEnergySpectrum();
        ResultData rd3 = doc3.ActiveResultData;
        MeasurementSeries series3 = MakeSeries(host3, 5);
        rd3.MeasurementController.Series = series3;
        series3.Begin(0);

        int dialogCalls3 = 0;
        string lastText3 = null;
        DCControlPanel panel3 = NewPanel((text, buttons) =>
        {
            dialogCalls3++;
            lastText3 = text;
            if (dialogCalls3 == 1)
            {
                series3.OnDeviceLost("lost");
                return DialogResult.Yes;
            }
            return DialogResult.OK;
        });

        Call(panel3, "SeriesBeforeStop", new object[] { doc3, rd3, true, false });

        if (host3.Saves != 0)
            throw new Exception("saves is " + host3.Saves);
        if (dialogCalls3 != 2)
            throw new Exception("dialog calls is " + dialogCalls3);
        if (lastText3 == null || !lastText3.Contains("disk full"))
            throw new Exception("text does not contain disk full: " + lastText3);
    }

    private static void Z10()
    {
        Type formType = typeof(MainForm).Assembly.GetType("BecquerelMonitor.SeriesSettingsForm");
        if (formType == null)
            throw new Exception("SeriesSettingsForm type not found");

        // Part 1: Template without {n}, NoOverwrite false
        SeriesSettings settings1 = new SeriesSettings
        {
            Enabled = true,
            Count = 5,
            Folder = @"C:\s",
            Template = "data",
            Prefix = "sp",
            NoOverwrite = false
        };

        object form1 = Activator.CreateInstance(formType, settings1, 600, @"C:\s", "doc");
        using (new DialogWatcher())
        {
            try
            {
                Call(form1, "BtnOk_Click", new object[] { null, EventArgs.Empty });
            }
            catch
            {
                // ignore exceptions from Close
            }
        }

        PropertyInfo resultProp = formType.GetProperty("Result");
        object result1 = resultProp.GetValue(form1);
        if (result1 != null)
            throw new Exception("Result is not null");

        // Part 2: NoOverwrite true, Template "data"
        SeriesSettings settings2 = new SeriesSettings
        {
            Enabled = true,
            Count = 5,
            Folder = @"C:\s",
            Template = "data",
            Prefix = "sp",
            NoOverwrite = true
        };

        object form2 = Activator.CreateInstance(formType, settings2, 600, @"C:\s", "doc");
        using (new DialogWatcher())
        {
            try
            {
                Call(form2, "BtnOk_Click", new object[] { null, EventArgs.Empty });
            }
            catch
            {
                // ignore exceptions from Close
            }
        }

        object result2 = resultProp.GetValue(form2);
        if (result2 == null)
            throw new Exception("Result is null");
        PropertyInfo templateProp = result2.GetType().GetProperty("Template");
        if (templateProp == null || (string)templateProp.GetValue(result2) != "data")
            throw new Exception("Template is not data");

        // Part 3: Template with {n}, NoOverwrite false
        SeriesSettings settings3 = new SeriesSettings
        {
            Enabled = true,
            Count = 5,
            Folder = @"C:\s",
            Template = "{prefix}_{n}",
            Prefix = "sp",
            NoOverwrite = false
        };

        object form3 = Activator.CreateInstance(formType, settings3, 600, @"C:\s", "doc");
        using (new DialogWatcher())
        {
            try
            {
                Call(form3, "BtnOk_Click", new object[] { null, EventArgs.Empty });
            }
            catch
            {
                // ignore exceptions from Close
            }
        }

        object result3 = resultProp.GetValue(form3);
        if (result3 == null)
            throw new Exception("Result is null");
    }

    private static MeasurementSeries MakeSeries(Z3Host h, int count)
    {
        return new MeasurementSeries(
            new SeriesSettings
            {
                Enabled = true,
                Count = count,
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
        MethodInfo mi = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (mi == null)
            throw new Exception(name + " is missing");
        try
        {
            return mi.Invoke(target, args);
        }
        catch (TargetInvocationException tie)
        {
            Exception inner = tie.InnerException;
            throw new Exception(inner.GetType().Name + ": " + inner.Message);
        }
    }

    private static void SetField(object o, string name, object value)
    {
        FieldInfo fi = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (fi == null)
            throw new Exception(name + " is missing");
        fi.SetValue(o, value);
    }

    private static bool TrySetField(object o, string name, object value)
    {
        FieldInfo fi = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (fi == null)
            return false;
        fi.SetValue(o, value);
        return true;
    }

    private static DCControlPanel NewPanel(Func<string, MessageBoxButtons, DialogResult> dialog)
    {
        MainForm mf = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
        DCControlPanel panel = new DCControlPanel(mf);
        SetField(panel, "SeriesDialog", dialog);
        TextBox tb = (TextBox)GetField(panel, "realTimeLimitTextBox");
        if (tb != null)
            tb.Text = "600";
        return panel;
    }

    private static object GetField(object o, string name)
    {
        FieldInfo fi = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (fi == null)
            return null;
        return fi.GetValue(o);
    }

    private static MainForm NewMain(Func<string, MessageBoxButtons, DialogResult> ask, out bool seam)
    {
        MainForm mf = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
        seam = TrySetField(mf, "UiAsk", ask);
        return mf;
    }

    private class Z3Host : ISeriesHost
    {
        public int Saves;
        public int Starts;
        public string SaveError;

        public DateTime Now
        {
            get { return DateTime.Now; }
        }

        public long MonotonicMs
        {
            get { return Environment.TickCount; }
        }

        public void SetKeepAwake(bool on)
        {
        }

        public bool SaveSpectrum(string path, out string error)
        {
            if (SaveError != null)
            {
                error = SaveError;
                return false;
            }
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

        public void StateChanged()
        {
        }

        public void SeriesFinished()
        {
        }

        public bool FileExists(string path)
        {
            return false;
        }

        public long FreeDiskBytes(string path)
        {
            return -1;
        }
    }

    private class DialogWatcher : IDisposable
    {
        private Thread _thread;
        private volatile bool _stop;
        public int Seen;

        public DialogWatcher()
        {
            _thread = new Thread(Work);
            _thread.IsBackground = true;
            _thread.Start();
        }

        private void Work()
        {
            while (!_stop)
            {
                IntPtr hwnd = FindWindow("#32770", null);
                if (hwnd != IntPtr.Zero)
                {
                    uint pid;
                    GetWindowThreadProcessId(hwnd, out pid);
                    if (pid == (uint)Process.GetCurrentProcess().Id)
                    {
                        IntPtr firstButton = IntPtr.Zero;
                        EnumChildWindows(hwnd, (child, lparam) =>
                        {
                            if (firstButton == IntPtr.Zero)
                            {
                                StringBuilder sb = new StringBuilder(256);
                                GetClassName(child, sb, 256);
                                if (sb.ToString() == "Button")
                                {
                                    firstButton = child;
                                    return false;
                                }
                            }
                            return true;
                        }, IntPtr.Zero);

                        if (firstButton != IntPtr.Zero)
                        {
                            PostMessage(firstButton, 0x00F5, IntPtr.Zero, IntPtr.Zero);
                            Seen++;
                        }
                    }
                }
                Thread.Sleep(100);
            }
        }

        public void Dispose()
        {
            _stop = true;
            if (_thread != null)
                _thread.Join(1000);
        }

        private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    }
}
