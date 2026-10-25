using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;
using BecquerelMonitor;

public static class UiStage5
{
    public static int Run()
    {
        int failures = 0;
        failures += Check("Z20_atomspectra_vcp_is_allowed", Z20);
        failures += Check("Z21_other_types_are_refused", Z21);
        failures += Check("Z22_tcp_and_rest_boards_are_allowed", Z22);
        failures += Check("Z23_panel_controls_follow_the_device", Z23);
        failures += Check("Z24_every_known_type_has_a_decision", Z24);
        return failures;
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
            Console.WriteLine("FAIL " + name + ": " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    private static void Z20()
    {
        DocEnergySpectrum doc = new DocEnergySpectrum();
        ResultData rd = Setup(doc, "AtomSpectraVCP");
        int dialogCalls = 0;
        DCControlPanel panel = NewPanel((text, buttons) =>
        {
            dialogCalls++;
            return DialogResult.OK;
        });

        bool ok = CallSeriesBegin(panel, doc, rd);
        if (!ok) throw new Exception("SeriesBegin returned false for AtomSpectraVCP");
        if (dialogCalls != 0) throw new Exception("Dialog was called " + dialogCalls + " times, expected 0");
        if (rd.MeasurementController.Series == null) throw new Exception("Series is null after begin");
        rd.MeasurementController.Series.UserStop(false);
    }

    private static void Z21()
    {
        string[] types = { "AudioInputDevice", "RadiaCode", "Obsidian", "AmplitudaUSB", "AmplitudaSerial", "SomeFutureDevice", "" };
        foreach (string t in types)
        {
            RunRefusalCase(t);
        }
        RunRefusalCase(null);
    }

    private static void RunRefusalCase(string deviceType)
    {
        DocEnergySpectrum doc = new DocEnergySpectrum();
        ResultData rd = Setup(doc, deviceType);
        string capturedText = null;
        MessageBoxButtons capturedButtons = (MessageBoxButtons)0;
        int dialogCalls = 0;

        DCControlPanel panel = NewPanel((text, buttons) =>
        {
            dialogCalls++;
            capturedText = text;
            capturedButtons = buttons;
            return DialogResult.OK;
        });

        bool ok = CallSeriesBegin(panel, doc, rd);
        if (ok) throw new Exception("SeriesBegin returned true for " + DescribeType(deviceType));
        if (rd.MeasurementController.Series != null) throw new Exception("Series is not null for " + DescribeType(deviceType));
        if (dialogCalls != 1) throw new Exception("Dialog called " + dialogCalls + " times for " + DescribeType(deviceType));
        if (capturedButtons != MessageBoxButtons.OK) throw new Exception("Buttons not OK for " + DescribeType(deviceType));
        if (string.IsNullOrEmpty(capturedText)) throw new Exception("Text is empty for " + DescribeType(deviceType));
        if (capturedText.Contains("TCP/REST")) throw new Exception("Text contains TCP/REST for " + DescribeType(deviceType));
    }

    private static void Z22()
    {
        RunTcpCase(AtomSpectraTransport.TcpBridge);
        RunTcpCase(AtomSpectraTransport.Rest);
    }

    private static void RunTcpCase(AtomSpectraTransport transport)
    {
        DocEnergySpectrum doc = new DocEnergySpectrum();
        ResultData rd = Setup(doc, "AtomSpectraTCP");
        rd.DeviceConfig.InputDeviceConfig = new AtomSpectraTcpDeviceConfig { Transport = transport };

        string capturedText = null;
        int dialogCalls = 0;
        DCControlPanel panel = NewPanel((text, buttons) =>
        {
            dialogCalls++;
            capturedText = text;
            return DialogResult.OK;
        });

        bool ok = CallSeriesBegin(panel, doc, rd);
        if (!ok) throw new Exception("SeriesBegin returned false for TCP " + transport + " (IMPL-6: boards are allowed)");
        if (rd.MeasurementController.Series == null) throw new Exception("Series is null for TCP " + transport);
        if (dialogCalls != 0) throw new Exception("Dialog called " + dialogCalls + " times for TCP " + transport + ": " + capturedText);
        rd.MeasurementController.Series.UserStop(false);
    }

    private static void Z23()
    {
        RunPanelControlCase("AtomSpectraVCP", true);
        RunPanelControlCase("RadiaCode", false);
        RunPanelControlCase("AmplitudaSerial", false);
        RunPanelControlCase(null, false);
    }

    private static void RunPanelControlCase(string deviceType, bool shouldEnable)
    {
        DocEnergySpectrum doc = new DocEnergySpectrum();
        ResultData rd = Setup(doc, deviceType);
        DCControlPanel panel = NewPanel((text, buttons) => DialogResult.OK);

        CallShowSeriesStatus(panel, doc, rd);

        FieldInfo checkField = GetField(panel, "seriesCheck");
        FieldInfo btnField = GetField(panel, "seriesSetupBtn");
        FieldInfo lblField = GetField(panel, "seriesStatusLbl");

        Control check = (Control)checkField.GetValue(panel);
        Control btn = (Control)btnField.GetValue(panel);
        Label lbl = (Label)lblField.GetValue(panel);

        if (check.Enabled != shouldEnable)
            throw new Exception("seriesCheck enabled=" + check.Enabled + " expected=" + shouldEnable + " for " + DescribeType(deviceType));
        if (btn.Enabled != shouldEnable)
            throw new Exception("seriesSetupBtn enabled=" + btn.Enabled + " expected=" + shouldEnable + " for " + DescribeType(deviceType));
        if (!shouldEnable && string.IsNullOrEmpty(lbl.Text))
            throw new Exception("seriesStatusLbl.Text is empty for " + DescribeType(deviceType));
    }

    private static void Z24()
    {
        try
        {
            DeviceType.InitializeDeviceTypes();
        }
        catch (Exception)
        {
            // Already initialized, continue
        }

        Dictionary<string, DeviceType> map = DeviceType.DeviceTypeMap;
        if (map == null) throw new Exception("DeviceTypeMap is null");

        HashSet<string> expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "AudioInputDevice", "AtomSpectraVCP", "RadiaCode", "Obsidian", "AmplitudaUSB", "AmplitudaSerial", "AtomSpectraTCP"
        };

        if (map.Count != expected.Count)
            throw new Exception("Map count " + map.Count + " != expected " + expected.Count);

        foreach (string key in expected)
        {
            if (!map.ContainsKey(key))
                throw new Exception("Missing key: " + key);
        }

        foreach (string key in map.Keys)
        {
            if (!expected.Contains(key))
                throw new Exception("Unexpected key: " + key);
        }

        Type seriesDevicesType = typeof(MainForm).Assembly.GetType("BecquerelMonitor.SeriesDevices");
        if (seriesDevicesType == null) throw new Exception("SeriesDevices type not found");

        MethodInfo isAllowed = seriesDevicesType.GetMethod("IsAllowed", BindingFlags.Public | BindingFlags.Static);
        if (isAllowed == null) throw new Exception("IsAllowed method not found");

        bool vcpAllowed = (bool)isAllowed.Invoke(null, new object[] { "AtomSpectraVCP" });
        bool tcpAllowed = (bool)isAllowed.Invoke(null, new object[] { "AtomSpectraTCP" });
        if (!vcpAllowed) throw new Exception("AtomSpectraVCP not allowed");
        if (!tcpAllowed) throw new Exception("AtomSpectraTCP not allowed");

        string[] notAllowed = { "AudioInputDevice", "RadiaCode", "Obsidian", "AmplitudaUSB", "AmplitudaSerial" };
        foreach (string t in notAllowed)
        {
            bool allowed = (bool)isAllowed.Invoke(null, new object[] { t });
            if (allowed) throw new Exception(t + " should not be allowed");
        }

        bool nullAllowed = (bool)isAllowed.Invoke(null, new object[] { null });
        if (nullAllowed) throw new Exception("null should not be allowed");

        bool emptyAllowed = (bool)isAllowed.Invoke(null, new object[] { "" });
        if (emptyAllowed) throw new Exception("empty should not be allowed");

        bool lowerAllowed = (bool)isAllowed.Invoke(null, new object[] { "atomspectravcp" });
        if (lowerAllowed) throw new Exception("lowercase should not be allowed");

        bool futureAllowed = (bool)isAllowed.Invoke(null, new object[] { "SomeFutureDevice" });
        if (futureAllowed) throw new Exception("SomeFutureDevice should not be allowed");
    }

    private static DCControlPanel NewPanel(Func<string, MessageBoxButtons, DialogResult> dialog)
    {
        MainForm mainForm = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
        DCControlPanel panel = new DCControlPanel(mainForm);

        FieldInfo dialogField = GetField(panel, "SeriesDialog");
        dialogField.SetValue(panel, dialog);

        FieldInfo rtField = GetField(panel, "realTimeLimitTextBox");
        TextBox rtBox = (TextBox)rtField.GetValue(panel);
        rtBox.Text = "600";

        return panel;
    }

    private static ResultData Setup(DocEnergySpectrum doc, string deviceType)
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
        rd.DeviceConfig = new DeviceConfigInfo { DeviceType = deviceType };
        rd.ResultDataStatus.ElapsedTime = TimeSpan.Zero;
        return rd;
    }

    private static FieldInfo GetField(object obj, string name)
    {
        FieldInfo f = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (f == null) throw new Exception(name + " field missing");
        return f;
    }

    private static bool CallSeriesBegin(DCControlPanel panel, DocEnergySpectrum doc, ResultData rd)
    {
        MethodInfo m = panel.GetType().GetMethod("SeriesBegin", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (m == null) throw new Exception("SeriesBegin method missing");
        return (bool)m.Invoke(panel, new object[] { doc, rd, rd.ResultDataStatus });
    }

    private static void CallShowSeriesStatus(DCControlPanel panel, DocEnergySpectrum doc, ResultData rd)
    {
        MethodInfo m = panel.GetType().GetMethod("ShowSeriesStatus", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (m == null) throw new Exception("ShowSeriesStatus method missing");
        m.Invoke(panel, new object[] { doc, rd });
    }

    private static string DescribeType(string t)
    {
        return t == null ? "<null>" : t;
    }
}
