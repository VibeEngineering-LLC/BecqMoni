using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using BecquerelMonitor;

public static class UiStage8
{
    private static int _failures;

    public static int Run()
    {
        _failures = 0;

        Check("Y1_field_is_shown_and_editable", () => RunCultureCheck("Y1_field_is_shown_and_editable", S8CheckY1));
        Check("Y2_label_says_real_time", () => RunCultureCheck("Y2_label_says_real_time", S8CheckY2));
        Check("Y3_changes_recalculate_total_and_end", () => RunCultureCheck("Y3_changes_recalculate_total_and_end", S8CheckY3));
        Check("Y4_ok_takes_the_value", () => RunCultureCheck("Y4_ok_takes_the_value", S8CheckY4));
        Check("Y5_minimum_is_one_second", () => RunCultureCheck("Y5_minimum_is_one_second", S8CheckY5));
        Check("Y6_upper_limit_is_the_panel_limit", S8CheckY6);
        Check("Y7_panel_takes_the_value_once", S8CheckY7);
        Check("Y8_cancel_changes_nothing", S8CheckY8);
        Check("Y9_fields_are_visible_and_on_top", () => RunCultureCheck("Y9_fields_are_visible_and_on_top", S8CheckY9));
        Check("Y10_time_base_is_kept_for_later", S8CheckY10);
        Check("Y11_time_is_fixed_while_a_series_runs", S8CheckY11);

        return _failures;
    }

    private static void Check(string name, Action a)
    {
        try
        {
            a();
            Console.WriteLine("PASS " + name);
        }
        catch (Exception ex)
        {
            Exception real = ex;
            while (real is TargetInvocationException tie)
            {
                real = tie.InnerException ?? real;
            }
            _failures++;
            Console.WriteLine("FAIL " + name + ": " + real.GetType().Name + ": " + real.Message);
        }
    }

    private static void RunCultureCheck(string name, Action action)
    {
        CultureInfo originalUi = Thread.CurrentThread.CurrentUICulture;
        CultureInfo originalCulture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("ru-RU");
            Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
            try
            {
                action();
            }
            catch (Exception ex)
            {
                throw new Exception("ru-RU: " + ex.Message, ex);
            }

            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            try
            {
                action();
            }
            catch (Exception ex)
            {
                throw new Exception("en-US: " + ex.Message, ex);
            }
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = originalUi;
            Thread.CurrentThread.CurrentCulture = originalCulture;
        }
    }

    private static SeriesSettings S8MakeSettings()
    {
        return new SeriesSettings
        {
            Folder = Path.GetTempPath(),
            Template = "{prefix}_{n:000}",
            Prefix = "sp",
            NoOverwrite = true,
            Count = 4,
            DelaySeconds = 10,
            Enabled = true
        };
    }

    private static object S8NewForm(int preset, bool editable)
    {
        Type formType = typeof(DCControlPanel).Assembly.GetType("BecquerelMonitor.SeriesSettingsForm");
        if (formType == null)
        {
            throw new Exception("SeriesSettingsForm type not found");
        }

        SeriesSettings settings = S8MakeSettings();
        object[] args = new object[] { settings, preset, Path.GetTempPath(), "doc", editable };
        return Activator.CreateInstance(formType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, args, null);
    }

    private static Control S8GetField(object form, string fieldName)
    {
        FieldInfo fi = form.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (fi == null)
        {
            throw new Exception("Field " + fieldName + " not found");
        }
        return (Control)fi.GetValue(form);
    }

    private static void S8SetNumeric(NumericUpDown num, decimal value)
    {
        num.Value = value;
    }

    private static decimal S8GetNumeric(NumericUpDown num)
    {
        return num.Value;
    }

    private static void S8InvokeOk(object form)
    {
        MethodInfo mi = form.GetType().GetMethod("BtnOk_Click", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (mi == null)
        {
            throw new Exception("BtnOk_Click not found");
        }
        mi.Invoke(form, new object[] { null, EventArgs.Empty });
    }

    private static object S8GetResult(object form)
    {
        PropertyInfo pi = form.GetType().GetProperty("Result");
        if (pi == null)
        {
            throw new Exception("Result property not found");
        }
        return pi.GetValue(form);
    }

    private static int S8GetPresetSeconds(object form)
    {
        PropertyInfo pi = form.GetType().GetProperty("PresetSeconds");
        if (pi == null)
        {
            throw new Exception("PresetSeconds property not found");
        }
        return (int)pi.GetValue(form);
    }

    private static DialogResult S8GetDialogResult(object form)
    {
        PropertyInfo pi = form.GetType().GetProperty("DialogResult");
        if (pi == null)
        {
            throw new Exception("DialogResult property not found");
        }
        return (DialogResult)pi.GetValue(form);
    }

    private static Label S8FindTimeLabel(object form)
    {
        Type seriesTextType = typeof(DCControlPanel).Assembly.GetType("BecquerelMonitor.SeriesText");
        if (seriesTextType == null)
        {
            throw new Exception("SeriesText type not found");
        }

        PropertyInfo lblTimeProp = seriesTextType.GetProperty("LblTime", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (lblTimeProp == null)
        {
            throw new Exception("LblTime property not found");
        }

        string expectedText = (string)lblTimeProp.GetValue(null);

        Label found = null;
        S8FindLabelRecursive((Control)form, expectedText, ref found);
        return found;
    }

    private static void S8FindLabelRecursive(Control parent, string expectedText, ref Label found)
    {
        if (found != null)
        {
            return;
        }

        foreach (Control c in parent.Controls)
        {
            if (c is Label lbl && lbl.Text == expectedText)
            {
                found = lbl;
                return;
            }
            S8FindLabelRecursive(c, expectedText, ref found);
        }
    }

    private static Control S8Deepest(Control root, Point clientPoint)
    {
        Control current = root;
        Point pt = clientPoint;

        while (true)
        {
            Control child = current.GetChildAtPoint(pt, GetChildAtPointSkip.None);
            if (child == null)
            {
                break;
            }
            current = child;
            pt = current.PointToClient(root.PointToScreen(clientPoint));
        }

        return current;
    }

    private static void S8CheckY1()
    {
        object form = S8NewForm(3725, true);
        try
        {
            NumericUpDown hours = (NumericUpDown)S8GetField(form, "_numPresetHours");
            NumericUpDown minutes = (NumericUpDown)S8GetField(form, "_numPresetMinutes");
            NumericUpDown seconds = (NumericUpDown)S8GetField(form, "_numPresetSeconds");

            if (S8GetNumeric(hours) != 1) throw new Exception("Hours should be 1");
            if (S8GetNumeric(minutes) != 2) throw new Exception("Minutes should be 2");
            if (S8GetNumeric(seconds) != 5) throw new Exception("Seconds should be 5");

            if (!hours.Enabled) throw new Exception("Hours should be enabled");
            if (!minutes.Enabled) throw new Exception("Minutes should be enabled");
            if (!seconds.Enabled) throw new Exception("Seconds should be enabled");

            if (S8GetPresetSeconds(form) != 3725) throw new Exception("PresetSeconds should be 3725");
        }
        finally
        {
            S8DisposeForm(form);
        }

        object form2 = S8NewForm(3725, false);
        try
        {
            NumericUpDown hours = (NumericUpDown)S8GetField(form2, "_numPresetHours");
            NumericUpDown minutes = (NumericUpDown)S8GetField(form2, "_numPresetMinutes");
            NumericUpDown seconds = (NumericUpDown)S8GetField(form2, "_numPresetSeconds");

            if (S8GetNumeric(hours) != 1) throw new Exception("Hours should be 1");
            if (S8GetNumeric(minutes) != 2) throw new Exception("Minutes should be 2");
            if (S8GetNumeric(seconds) != 5) throw new Exception("Seconds should be 5");

            if (hours.Enabled) throw new Exception("Hours should be disabled");
            if (minutes.Enabled) throw new Exception("Minutes should be disabled");
            if (seconds.Enabled) throw new Exception("Seconds should be disabled");
        }
        finally
        {
            S8DisposeForm(form2);
        }
    }

    private static void S8CheckY2()
    {
        object form = S8NewForm(3725, true);
        try
        {
            Label lbl = S8FindTimeLabel(form);
            if (lbl == null) throw new Exception("Time label not found");

            string text = lbl.Text;
            string culture = Thread.CurrentThread.CurrentUICulture.Name;

            if (culture == "ru-RU")
            {
                if (!text.Contains("реальн")) throw new Exception("Label should contain 'реальн' under ru-RU");
            }
            else if (culture == "en-US")
            {
                if (!text.ToLower().Contains("real")) throw new Exception("Label should contain 'real' under en-US");
            }
        }
        finally
        {
            S8DisposeForm(form);
        }
    }

    private static void S8CheckY3()
    {
        object form = S8NewForm(60, true);
        try
        {
            SeriesSettings settings = S8MakeSettings();
            string expectedTotal = SeriesSettings.FormatHms(settings.TotalSeconds(60));

            Control lblTotal = S8GetField(form, "_lblTotal");
            Control lblEnd = S8GetField(form, "_lblEnd");

            if (lblTotal.Text != expectedTotal) throw new Exception("Total should be " + expectedTotal + " but was " + lblTotal.Text);

            string endBefore = lblEnd.Text;

            NumericUpDown minutes = (NumericUpDown)S8GetField(form, "_numPresetMinutes");
            S8SetNumeric(minutes, 2);

            int newPreset = 120;
            string newExpectedTotal = SeriesSettings.FormatHms(settings.TotalSeconds(newPreset));

            if (lblTotal.Text != newExpectedTotal) throw new Exception("Total should be " + newExpectedTotal + " but was " + lblTotal.Text);

            if (lblEnd.Text == endBefore) throw new Exception("End label should change");
        }
        finally
        {
            S8DisposeForm(form);
        }
    }

    private static void S8CheckY4()
    {
        object form = S8NewForm(60, true);
        try
        {
            NumericUpDown hours = (NumericUpDown)S8GetField(form, "_numPresetHours");
            NumericUpDown minutes = (NumericUpDown)S8GetField(form, "_numPresetMinutes");
            NumericUpDown seconds = (NumericUpDown)S8GetField(form, "_numPresetSeconds");

            S8SetNumeric(hours, 0);
            S8SetNumeric(minutes, 2);
            S8SetNumeric(seconds, 30);

            S8InvokeOk(form);

            object result = S8GetResult(form);
            if (result == null) throw new Exception("Result should not be null");

            if (S8GetPresetSeconds(form) != 150) throw new Exception("PresetSeconds should be 150");

            SeriesSettings s = (SeriesSettings)result;
            if (s.Count != 4) throw new Exception("Count should be 4");
        }
        finally
        {
            S8DisposeForm(form);
        }
    }

    private static void S8CheckY5()
    {
        object form = S8NewForm(60, true);
        try
        {
            NumericUpDown hours = (NumericUpDown)S8GetField(form, "_numPresetHours");
            NumericUpDown minutes = (NumericUpDown)S8GetField(form, "_numPresetMinutes");
            NumericUpDown seconds = (NumericUpDown)S8GetField(form, "_numPresetSeconds");

            S8SetNumeric(hours, 0);
            S8SetNumeric(minutes, 0);
            S8SetNumeric(seconds, 0);

            S8InvokeOk(form);

            if (S8GetResult(form) != null) throw new Exception("Result should be null when all zero");
            if (S8GetDialogResult(form) == DialogResult.OK) throw new Exception("DialogResult should not be OK");

            S8SetNumeric(seconds, 1);
            S8InvokeOk(form);

            if (S8GetResult(form) == null) throw new Exception("Result should not be null after setting 1 second");
            if (S8GetPresetSeconds(form) != 1) throw new Exception("PresetSeconds should be 1");
        }
        finally
        {
            S8DisposeForm(form);
        }
    }

    private static void S8CheckY6()
    {
        object form = S8NewForm(60, true);
        try
        {
            NumericUpDown hours = (NumericUpDown)S8GetField(form, "_numPresetHours");
            NumericUpDown minutes = (NumericUpDown)S8GetField(form, "_numPresetMinutes");
            NumericUpDown seconds = (NumericUpDown)S8GetField(form, "_numPresetSeconds");

            if (hours.Maximum < 596523) throw new Exception("Hours Maximum should be >= 596523");

            S8SetNumeric(hours, hours.Maximum);
            S8SetNumeric(minutes, 59);
            S8SetNumeric(seconds, 59);

            S8InvokeOk(form);

            if (S8GetPresetSeconds(form) != int.MaxValue) throw new Exception("PresetSeconds should be int.MaxValue");
        }
        finally
        {
            S8DisposeForm(form);
        }
    }

    private static void S8CheckY7()
    {
        MainForm mf = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
        DCControlPanel p = new DCControlPanel(mf);
        DocEnergySpectrum doc = new DocEnergySpectrum();
        mf.ActiveDocument = doc;
        ResultData rd = doc.ActiveResultData;
        rd.Series = S8MakeSettings();
        p.PresetTime = 600;
        rd.ResultDataStatus.PresetTime = 600;
        rd.PresetTime = 600;
        doc.Dirty = false;

        object form = S8NewForm(600, true);
        try
        {
            NumericUpDown hours = (NumericUpDown)S8GetField(form, "_numPresetHours");
            NumericUpDown minutes = (NumericUpDown)S8GetField(form, "_numPresetMinutes");
            NumericUpDown seconds = (NumericUpDown)S8GetField(form, "_numPresetSeconds");

            S8SetNumeric(hours, 0);
            S8SetNumeric(minutes, 5);
            S8SetNumeric(seconds, 0);

            S8SetNumeric(minutes, 7);

            S8InvokeOk(form);

            MethodInfo applyMethod = typeof(DCControlPanel).GetMethod("ApplySeriesForm", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (applyMethod == null) throw new Exception("ApplySeriesForm not found");

            applyMethod.Invoke(p, new object[] { doc, rd, form });

            if (p.PresetTime != 420) throw new Exception("Panel PresetTime should be 420");
            if (rd.ResultDataStatus.PresetTime != 420) throw new Exception("ResultDataStatus.PresetTime should be 420");
            if (rd.PresetTime != 420) throw new Exception("rd.PresetTime should be 420");
            if (!doc.Dirty) throw new Exception("doc.Dirty should be true");
            if (rd.Series.Count != 4) throw new Exception("rd.Series.Count should be 4");

            doc.Dirty = false;

            object form2 = S8NewForm(420, true);
            try
            {
                S8InvokeOk(form2);
                applyMethod.Invoke(p, new object[] { doc, rd, form2 });

                if (!doc.Dirty) throw new Exception("OK marks the document for the series settings even when the time is the same");
                if (p.PresetTime != 420) throw new Exception("Panel PresetTime should stay 420");
            }
            finally
            {
                S8DisposeForm(form2);
            }
        }
        finally
        {
            S8DisposeForm(form);
        }
    }

    private static void S8CheckY8()
    {
        MainForm mf = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
        DCControlPanel p = new DCControlPanel(mf);
        DocEnergySpectrum doc = new DocEnergySpectrum();
        mf.ActiveDocument = doc;
        ResultData rd = doc.ActiveResultData;
        rd.Series = S8MakeSettings();
        p.PresetTime = 600;
        rd.ResultDataStatus.PresetTime = 600;
        rd.PresetTime = 600;
        doc.Dirty = false;

        object form = S8NewForm(600, true);
        try
        {
            NumericUpDown minutes = (NumericUpDown)S8GetField(form, "_numPresetMinutes");
            S8SetNumeric(minutes, 7);

            if (S8GetResult(form) != null) throw new Exception("Result should be null without OK");

            MethodInfo applyMethod = typeof(DCControlPanel).GetMethod("ApplySeriesForm", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (applyMethod == null) throw new Exception("ApplySeriesForm not found");

            applyMethod.Invoke(p, new object[] { doc, rd, form });

            if (p.PresetTime != 600) throw new Exception("Panel PresetTime should stay 600");
            if (rd.ResultDataStatus.PresetTime != 600) throw new Exception("ResultDataStatus.PresetTime should stay 600");
            if (rd.PresetTime != 600) throw new Exception("rd.PresetTime should stay 600");
            if (doc.Dirty) throw new Exception("doc.Dirty should stay false");
        }
        finally
        {
            S8DisposeForm(form);
        }
    }

    private static void S8CheckY9()
    {
        object form = S8NewForm(3725, true);
        try
        {
            ((Form)form).Show();
            Application.DoEvents();

            NumericUpDown hours = (NumericUpDown)S8GetField(form, "_numPresetHours");
            NumericUpDown minutes = (NumericUpDown)S8GetField(form, "_numPresetMinutes");
            NumericUpDown seconds = (NumericUpDown)S8GetField(form, "_numPresetSeconds");
            Label lbl = S8FindTimeLabel(form);

            if (!hours.Visible) throw new Exception("Hours should be visible");
            if (!minutes.Visible) throw new Exception("Minutes should be visible");
            if (!seconds.Visible) throw new Exception("Seconds should be visible");
            if (!lbl.Visible) throw new Exception("Label should be visible");

            Form f = (Form)form;

            Rectangle hoursRect = new Rectangle(f.PointToClient(hours.Parent.PointToScreen(hours.Location)), Size.Empty);
            hoursRect.Size = hours.Size;
            if (!f.ClientRectangle.Contains(hoursRect)) throw new Exception("Hours rect outside client area");

            Rectangle minutesRect = new Rectangle(f.PointToClient(minutes.Parent.PointToScreen(minutes.Location)), Size.Empty);
            minutesRect.Size = minutes.Size;
            if (!f.ClientRectangle.Contains(minutesRect)) throw new Exception("Minutes rect outside client area");

            Rectangle secondsRect = new Rectangle(f.PointToClient(seconds.Parent.PointToScreen(seconds.Location)), Size.Empty);
            secondsRect.Size = seconds.Size;
            if (!f.ClientRectangle.Contains(secondsRect)) throw new Exception("Seconds rect outside client area");

            Rectangle lblRect = new Rectangle(f.PointToClient(lbl.Parent.PointToScreen(lbl.Location)), Size.Empty);
            lblRect.Size = lbl.Size;
            if (!f.ClientRectangle.Contains(lblRect)) throw new Exception("Label rect outside client area");

            Point hoursCenter = new Point(hoursRect.X + hoursRect.Width / 2, hoursRect.Y + hoursRect.Height / 2);
            Control deepestHours = S8Deepest(f, hoursCenter);
            if (deepestHours != hours && !hours.Contains(deepestHours)) throw new Exception("Deepest at hours center should be hours");

            Point minutesCenter = new Point(minutesRect.X + minutesRect.Width / 2, minutesRect.Y + minutesRect.Height / 2);
            Control deepestMinutes = S8Deepest(f, minutesCenter);
            if (deepestMinutes != minutes && !minutes.Contains(deepestMinutes)) throw new Exception("Deepest at minutes center should be minutes");

            Point secondsCenter = new Point(secondsRect.X + secondsRect.Width / 2, secondsRect.Y + secondsRect.Height / 2);
            Control deepestSeconds = S8Deepest(f, secondsCenter);
            if (deepestSeconds != seconds && !seconds.Contains(deepestSeconds)) throw new Exception("Deepest at seconds center should be seconds");

            if (hoursRect.IntersectsWith(minutesRect)) throw new Exception("Hours and minutes intersect");
            if (hoursRect.IntersectsWith(secondsRect)) throw new Exception("Hours and seconds intersect");
            if (minutesRect.IntersectsWith(secondsRect)) throw new Exception("Minutes and seconds intersect");

            string shotDir = Environment.GetEnvironmentVariable("BM_SHOT_DIR");
            if (!string.IsNullOrEmpty(shotDir) && Directory.Exists(shotDir))
            {
                try
                {
                    string culture = Thread.CurrentThread.CurrentUICulture.Name;
                    string path = Path.Combine(shotDir, "series_window_" + culture + ".png");
                    using (Bitmap bmp = new Bitmap(f.Width, f.Height))
                    {
                        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                catch
                {
                }
            }

            f.Close();
        }
        finally
        {
            S8DisposeForm(form);
        }
    }

    // the time of each spectrum is editable unless a series is running on the document
    private static void S8CheckY11()
    {
        MethodInfo m = typeof(DCControlPanel).GetMethod("SeriesPresetEditable", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (m == null) throw new Exception("SeriesPresetEditable is missing");
        DocEnergySpectrum doc = new DocEnergySpectrum();
        ResultData rd = doc.ActiveResultData;
        if (!(bool)m.Invoke(null, new object[] { rd })) throw new Exception("no series: editable expected");
        MainForm mf = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
        DCControlPanel p = new DCControlPanel(mf);
        rd.Series = new SeriesSettings { Enabled = true, Count = 3, Folder = Path.GetTempPath(), Prefix = "sp" };
        rd.DeviceConfig = new DeviceConfigInfo { DeviceType = "AtomSpectraVCP" };
        TextBox box = (TextBox)typeof(DCControlPanel).GetField("realTimeLimitTextBox", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(p);
        box.Text = "60";
        FieldInfo dlg = typeof(DCControlPanel).GetField("SeriesDialog", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        dlg.SetValue(p, (Func<string, MessageBoxButtons, DialogResult>)((t, b) => DialogResult.OK));
        MethodInfo begin = typeof(DCControlPanel).GetMethod("SeriesBegin", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (!(bool)begin.Invoke(p, new object[] { doc, rd, rd.ResultDataStatus })) throw new Exception("series did not begin");
        if ((bool)m.Invoke(null, new object[] { rd })) throw new Exception("active series: not editable expected");
        rd.MeasurementController.Series.UserStop(false);
        if (!(bool)m.Invoke(null, new object[] { rd })) throw new Exception("stopped series: editable expected");
    }

    private static int S8TB(SeriesSettings s) { PropertyInfo p = typeof(SeriesSettings).GetProperty("TimeBase"); if (p == null) throw new Exception("SeriesSettings.TimeBase is missing"); return (int)p.GetValue(s, null); }
    private static void S8SetTB(SeriesSettings s, int v) { PropertyInfo p = typeof(SeriesSettings).GetProperty("TimeBase"); if (p == null) throw new Exception("SeriesSettings.TimeBase is missing"); p.SetValue(s, v, null); }

    private static void S8CheckY10()
    {
        SeriesSettings s = new SeriesSettings();
        if (S8TB(s) != 0) throw new Exception("Default TimeBase should be 0");

        S8SetTB(s, 1);
        SeriesSettings clone = s.Clone();
        if (S8TB(clone) != 1) throw new Exception("Clone should keep TimeBase 1");

        XmlSerializer ser = new XmlSerializer(typeof(SeriesSettings));
        using (StringWriter sw = new StringWriter())
        {
            ser.Serialize(sw, s);
            string xml = sw.ToString();

            using (StringReader sr = new StringReader(xml))
            {
                SeriesSettings deserialized = (SeriesSettings)ser.Deserialize(sr);
                if (S8TB(deserialized) != 1) throw new Exception("XmlSerializer round trip should keep TimeBase 1");
            }
        }

        SeriesSettings defaultS = new SeriesSettings();
        using (StringWriter sw = new StringWriter())
        {
            ser.Serialize(sw, defaultS);
            string oldXml = sw.ToString().Replace("<TimeBase>0</TimeBase>", "");

            using (StringReader sr = new StringReader(oldXml))
            {
                SeriesSettings deserialized = (SeriesSettings)ser.Deserialize(sr);
                if (S8TB(deserialized) != 0) throw new Exception("Old XML without TimeBase should give 0");
            }
        }
    }

    private static void S8DisposeForm(object form)
    {
        if (form is Form f)
        {
            f.Close();
            f.Dispose();
        }
        else if (form is IDisposable d)
        {
            d.Dispose();
        }
    }
}
