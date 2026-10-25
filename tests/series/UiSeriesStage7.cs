using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;

public static class UiStage7
{
    public static int Run()
    {
        int total = 0;
        total += Check("Z27_boards_allowed", () => S7RunZ27());
        total += Check("Z28_rest_observe_only_refused", () => S7RunZ28());
        total += Check("Z29_start_refusal_is_reported", () => S7RunZ29());
        total += Check("Z30_pure_decisions", () => S7RunZ30());
        return total;
    }

    private static int Check(string name, Action a)
    {
        try
        {
            a();
            Console.WriteLine("PASS " + name);
            return 0;
        }
        catch (Exception ex)
        {
            Exception real = ex;
            while (real is TargetInvocationException tie)
            {
                real = tie.InnerException ?? real;
            }
            Console.WriteLine("FAIL " + name + ": " + real.GetType().Name + ": " + real.Message);
            return 1;
        }
    }

    private static void S7RunZ27()
    {
        S7WithCulture("ru-RU", () => S7Z27Core());
        S7WithCulture("en-US", () => S7Z27Core());
    }

    private static void S7Z27Core()
    {
        S7Z27One(AtomSpectraTransport.TcpBridge);
        S7Z27One(AtomSpectraTransport.Rest);
    }

    private static void S7Z27One(AtomSpectraTransport transport)
    {
        List<string> dialogs = new List<string>();
        DCControlPanel p = S7NewPanel(dialogs);
        DocEnergySpectrum doc = new DocEnergySpectrum();
        ResultData rd = S7Setup(doc, transport, false);
        bool ok = S7SeriesBegin(p, doc, rd);
        if (!ok) throw new Exception("SeriesBegin returned false");
        if (dialogs.Count != 0) throw new Exception("dialogs not empty");
        if (rd.MeasurementController.Series == null) throw new Exception("Series is null");
        rd.MeasurementController.Series.UserStop(false);
    }

    private static void S7RunZ28()
    {
        S7WithCulture("ru-RU", () => S7Z28Core());
        S7WithCulture("en-US", () => S7Z28Core());
    }

    private static void S7Z28Core()
    {
        // Rest observeOnly true -> refused
        {
            List<string> dialogs = new List<string>();
            DCControlPanel p = S7NewPanel(dialogs);
            DocEnergySpectrum doc = new DocEnergySpectrum();
            ResultData rd = S7Setup(doc, AtomSpectraTransport.Rest, true);
            bool ok = S7SeriesBegin(p, doc, rd);
            if (ok) throw new Exception("SeriesBegin should be false");
            if (dialogs.Count != 1) throw new Exception("dialogs count != 1");
            string expected = S7Txt("ObserveOnlyUnavailable");
            if (dialogs[0] != expected) throw new Exception("dialog text mismatch");
            if (rd.MeasurementController.Series != null) throw new Exception("Series should be null");

            S7InvokeVoid(p, "ShowSeriesStatus", new object[] { doc, rd });
            CheckBox cb = (CheckBox)S7GetField(p, "seriesCheck");
            Button btn = (Button)S7GetField(p, "seriesSetupBtn");
            Label lbl = (Label)S7GetField(p, "seriesStatusLbl");
            if (cb.Enabled) throw new Exception("seriesCheck should be disabled");
            if (btn.Enabled) throw new Exception("seriesSetupBtn should be disabled");
            string shortTxt = S7Txt("ObserveOnlyShort");
            if (lbl.Text != shortTxt) throw new Exception("status label text mismatch");
        }

        // TcpBridge observeOnly false -> control
        {
            List<string> dialogs = new List<string>();
            DCControlPanel p = S7NewPanel(dialogs);
            DocEnergySpectrum doc = new DocEnergySpectrum();
            ResultData rd = S7Setup(doc, AtomSpectraTransport.TcpBridge, false);
            bool ok = S7SeriesBegin(p, doc, rd);
            if (!ok) throw new Exception("SeriesBegin should be true");
            rd.MeasurementController.Series.UserStop(false);   // an active series would disable the controls by itself
            S7InvokeVoid(p, "ShowSeriesStatus", new object[] { doc, rd });
            CheckBox cb = (CheckBox)S7GetField(p, "seriesCheck");
            if (!cb.Enabled) throw new Exception("seriesCheck should be enabled");
            Label lbl = (Label)S7GetField(p, "seriesStatusLbl");
            if (lbl.Text != "") throw new Exception("status label should be empty");
            rd.MeasurementController.Series.UserStop(false);
        }
    }

    private static void S7RunZ29()
    {
        S7WithCulture("ru-RU", () => S7Z29Core());
        S7WithCulture("en-US", () => S7Z29Core());
    }

    private static void S7Z29Core()
    {
        // Part 1: refusal reported
        {
            List<string> dialogs = new List<string>();
            DCControlPanel p = S7NewPanel(dialogs);
            DocEnergySpectrum doc = new DocEnergySpectrum();
            ResultData rd = S7Setup(doc, AtomSpectraTransport.TcpBridge, false);
            bool ok = S7SeriesBegin(p, doc, rd);
            if (!ok) throw new Exception("SeriesBegin should be true");

            AtomSpectraTcpDeviceController ctrl = new AtomSpectraTcpDeviceController();
            S7SetDeviceController(rd.MeasurementController, ctrl);
            string refusal = S7Txt("BoardCounting");
            S7SetSeriesRefusal(ctrl, refusal);

            dialogs.Clear();
            S7InvokeVoid(p, "SeriesAfterStart", new object[] { rd, false });

            if (dialogs.Count != 1) throw new Exception("dialogs count != 1");
            if (dialogs[0] != refusal) throw new Exception("dialog text mismatch");
            if (rd.MeasurementController.Series.State != SeriesState.Idle) throw new Exception("state should be Idle");
        }

        // Part 2: no refusal
        {
            List<string> dialogs = new List<string>();
            DCControlPanel p = S7NewPanel(dialogs);
            DocEnergySpectrum doc = new DocEnergySpectrum();
            ResultData rd = S7Setup(doc, AtomSpectraTransport.TcpBridge, false);
            bool ok = S7SeriesBegin(p, doc, rd);
            if (!ok) throw new Exception("SeriesBegin should be true");

            AtomSpectraTcpDeviceController ctrl = new AtomSpectraTcpDeviceController();
            S7SetDeviceController(rd.MeasurementController, ctrl);
            S7SetSeriesRefusal(ctrl, null);

            dialogs.Clear();
            S7InvokeVoid(p, "SeriesAfterStart", new object[] { rd, false });

            if (dialogs.Count != 0) throw new Exception("dialogs should be empty");
            if (rd.MeasurementController.Series.State != SeriesState.Idle) throw new Exception("state should be Idle");
        }

        // Part 3: started true -> stays Acquiring
        {
            List<string> dialogs = new List<string>();
            DCControlPanel p = S7NewPanel(dialogs);
            DocEnergySpectrum doc = new DocEnergySpectrum();
            ResultData rd = S7Setup(doc, AtomSpectraTransport.TcpBridge, false);
            bool ok = S7SeriesBegin(p, doc, rd);
            if (!ok) throw new Exception("SeriesBegin should be true");

            S7InvokeVoid(p, "SeriesAfterStart", new object[] { rd, true });

            if (rd.MeasurementController.Series.State != SeriesState.Acquiring) throw new Exception("state should be Acquiring");
            rd.MeasurementController.Series.UserStop(false);
        }
    }

    private static void S7RunZ30()
    {
        S7WithCulture("ru-RU", () => S7Z30Core());
        S7WithCulture("en-US", () => S7Z30Core());
    }

    private static void S7Z30Core()
    {
        Type ctrlType = typeof(AtomSpectraTcpDeviceController);
        Type mcType = typeof(MeasurementController);
        Type sdType = typeof(MeasurementController).Assembly.GetType("BecquerelMonitor.SeriesDevices");

        // AskAllowed
        MethodInfo askAllowed = ctrlType.GetMethod("AskAllowed", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (askAllowed == null) throw new Exception("AskAllowed not found");
        if (!(bool)askAllowed.Invoke(null, new object[] { true, false })) throw new Exception("AskAllowed(true,false)");
        if ((bool)askAllowed.Invoke(null, new object[] { true, true })) throw new Exception("AskAllowed(true,true)");
        if ((bool)askAllowed.Invoke(null, new object[] { false, false })) throw new Exception("AskAllowed(false,false)");
        if ((bool)askAllowed.Invoke(null, new object[] { false, true })) throw new Exception("AskAllowed(false,true)");

        // ClearFailureThrows
        MethodInfo cft = mcType.GetMethod("ClearFailureThrows", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (cft == null) throw new Exception("ClearFailureThrows not found");
        if (!(bool)cft.Invoke(null, new object[] { false, false })) throw new Exception("CFT(false,false)");
        if (!(bool)cft.Invoke(null, new object[] { false, true })) throw new Exception("CFT(false,true)");
        if (!(bool)cft.Invoke(null, new object[] { true, true })) throw new Exception("CFT(true,true)");
        if ((bool)cft.Invoke(null, new object[] { true, false })) throw new Exception("CFT(true,false)");

        // SeriesStrict
        MethodInfo ss = ctrlType.GetMethod("SeriesStrict", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (ss == null) throw new Exception("SeriesStrict not found");

        List<string> dialogs = new List<string>();
        DCControlPanel p = S7NewPanel(dialogs);
        DocEnergySpectrum doc = new DocEnergySpectrum();
        ResultData rd = S7Setup(doc, AtomSpectraTransport.TcpBridge, false);
        if ((bool)ss.Invoke(null, new object[] { rd })) throw new Exception("SeriesStrict should be false before begin");

        bool ok = S7SeriesBegin(p, doc, rd);
        if (!ok) throw new Exception("SeriesBegin failed");
        if (!(bool)ss.Invoke(null, new object[] { rd })) throw new Exception("SeriesStrict should be true after begin");

        rd.MeasurementController.Series.UserStop(false);
        if ((bool)ss.Invoke(null, new object[] { rd })) throw new Exception("SeriesStrict should be false after stop");

        // IsObserveOnly
        MethodInfo io = sdType.GetMethod("IsObserveOnly", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (io == null) throw new Exception("IsObserveOnly not found");

        ResultData rdRestTrue = S7Setup(new DocEnergySpectrum(), AtomSpectraTransport.Rest, true);
        if (!(bool)io.Invoke(null, new object[] { rdRestTrue })) throw new Exception("IsObserveOnly Rest true");

        ResultData rdRestFalse = S7Setup(new DocEnergySpectrum(), AtomSpectraTransport.Rest, false);
        if ((bool)io.Invoke(null, new object[] { rdRestFalse })) throw new Exception("IsObserveOnly Rest false");

        ResultData rdTcpTrue = S7Setup(new DocEnergySpectrum(), AtomSpectraTransport.TcpBridge, true);
        if ((bool)io.Invoke(null, new object[] { rdTcpTrue })) throw new Exception("IsObserveOnly Tcp true");

        ResultData rdTcpFalse = S7Setup(new DocEnergySpectrum(), AtomSpectraTransport.TcpBridge, false);
        if ((bool)io.Invoke(null, new object[] { rdTcpFalse })) throw new Exception("IsObserveOnly Tcp false");
    }

    private static void S7WithCulture(string cultureName, Action action)
    {
        CultureInfo originalUi = Thread.CurrentThread.CurrentUICulture;
        CultureInfo originalCulture = Thread.CurrentThread.CurrentCulture;
        try
        {
            CultureInfo ci = new CultureInfo(cultureName);
            Thread.CurrentThread.CurrentUICulture = ci;
            Thread.CurrentThread.CurrentCulture = ci;
            action();
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = originalUi;
            Thread.CurrentThread.CurrentCulture = originalCulture;
        }
    }

    private static DCControlPanel S7NewPanel(List<string> dialogs)
    {
        MainForm mf = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
        DCControlPanel p = new DCControlPanel(mf);

        FieldInfo dialogField = typeof(DCControlPanel).GetField("SeriesDialog", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (dialogField == null) throw new Exception("SeriesDialog field not found");
        dialogField.SetValue(p, (Func<string, MessageBoxButtons, DialogResult>)((text, buttons) =>
        {
            dialogs.Add(text);
            return DialogResult.OK;
        }));

        FieldInfo limitField = typeof(DCControlPanel).GetField("realTimeLimitTextBox", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (limitField == null) throw new Exception("realTimeLimitTextBox field not found");
        ((TextBox)limitField.GetValue(p)).Text = "600";

        return p;
    }

    private static ResultData S7Setup(DocEnergySpectrum doc, AtomSpectraTransport transport, bool observeOnly)
    {
        ResultData rd = doc.ActiveResultData;
        rd.Series = new SeriesSettings
        {
            Enabled = true,
            Count = 5,
            Folder = @"C:\s",
            Template = "{prefix}_{n:000}",
            Prefix = "sp",
            NoOverwrite = true
        };
        rd.DeviceConfig = new DeviceConfigInfo
        {
            DeviceType = "AtomSpectraTCP",
            Guid = Guid.NewGuid().ToString(),
            InputDeviceConfig = new AtomSpectraTcpDeviceConfig
            {
                Transport = transport,
                RestObserveOnly = observeOnly
            }
        };
        rd.ResultDataStatus.ElapsedTime = TimeSpan.Zero;
        return rd;
    }

    private static bool S7SeriesBegin(DCControlPanel p, DocEnergySpectrum doc, ResultData rd)
    {
        MethodInfo mi = typeof(DCControlPanel).GetMethod("SeriesBegin", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (mi == null) throw new Exception("SeriesBegin method not found");
        object[] args = new object[] { doc, rd, rd.ResultDataStatus };
        return (bool)mi.Invoke(p, args);
    }

    private static string S7Txt(string name)
    {
        Type t = typeof(DCControlPanel).Assembly.GetType("BecquerelMonitor.SeriesText");
        if (t == null) throw new Exception("SeriesText type not found");
        PropertyInfo pi = t.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (pi == null) throw new Exception("SeriesText property " + name + " not found");
        return (string)pi.GetValue(null);
    }

    private static void S7InvokeVoid(object obj, string methodName, object[] args)
    {
        MethodInfo mi = obj.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (mi == null) throw new Exception("Method " + methodName + " not found");
        mi.Invoke(obj, args);
    }

    private static object S7GetField(object obj, string fieldName)
    {
        FieldInfo fi = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (fi == null) throw new Exception("Field " + fieldName + " not found");
        return fi.GetValue(obj);
    }

    private static void S7SetDeviceController(MeasurementController mc, AtomSpectraTcpDeviceController ctrl)
    {
        PropertyInfo pi = typeof(MeasurementController).GetProperty("DeviceController", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (pi == null) throw new Exception("DeviceController property not found");
        if (pi.CanWrite)
        {
            pi.SetValue(mc, ctrl);
        }
        else
        {
            MethodInfo setter = pi.GetSetMethod(true);
            if (setter == null) throw new Exception("DeviceController setter not found");
            setter.Invoke(mc, new object[] { ctrl });
        }
    }

    private static void S7SetSeriesRefusal(AtomSpectraTcpDeviceController ctrl, string value)
    {
        PropertyInfo pi = typeof(AtomSpectraTcpDeviceController).GetProperty("SeriesRefusal", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (pi == null) throw new Exception("SeriesRefusal property not found");
        MethodInfo setter = pi.GetSetMethod(true);
        if (setter == null) throw new Exception("SeriesRefusal setter not found");
        setter.Invoke(ctrl, new object[] { value });
    }
}
