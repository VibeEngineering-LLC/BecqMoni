using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;

public static class UiStage6
{
    public static int Run()
    {
        int failures = 0;
        string[] cultures = { "ru-RU", "en-US" };
        Size[] sizes = { new Size(437, 292), new Size(437, 220), new Size(600, 400) };

        foreach (string cultureName in cultures)
        {
            CultureInfo originalUi = Thread.CurrentThread.CurrentUICulture;
            CultureInfo originalCulture = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(cultureName);
                Thread.CurrentThread.CurrentCulture = new CultureInfo(cultureName);

                foreach (Size sz in sizes)
                {
                    DCControlPanel panel = null;
                    try
                    {
                        MainForm mainForm = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
                        panel = new DCControlPanel(mainForm);
                        panel.ClientSize = sz;

                        // Get private fields
                        FieldInfo seriesCheckField = panel.GetType().GetField("seriesCheck", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        FieldInfo seriesSetupBtnField = panel.GetType().GetField("seriesSetupBtn", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        FieldInfo seriesSkipBtnField = panel.GetType().GetField("seriesSkipBtn", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        FieldInfo seriesSummaryLblField = panel.GetType().GetField("seriesSummaryLbl", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        FieldInfo seriesStatusLblField = panel.GetType().GetField("seriesStatusLbl", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        FieldInfo groupBox1Field = panel.GetType().GetField("groupBox1", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        FieldInfo realTimeLimitTextBoxField = panel.GetType().GetField("realTimeLimitTextBox", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                        if (seriesCheckField == null || seriesSetupBtnField == null || seriesSkipBtnField == null ||
                            seriesSummaryLblField == null || seriesStatusLblField == null || groupBox1Field == null ||
                            realTimeLimitTextBoxField == null)
                        {
                            throw new Exception("Missing required fields in DCControlPanel");
                        }

                        Control seriesCheck = (Control)seriesCheckField.GetValue(panel);
                        Control seriesSetupBtn = (Control)seriesSetupBtnField.GetValue(panel);
                        Control seriesSkipBtn = (Control)seriesSkipBtnField.GetValue(panel);
                        Control seriesSummaryLbl = (Control)seriesSummaryLblField.GetValue(panel);
                        Control seriesStatusLbl = (Control)seriesStatusLblField.GetValue(panel);
                        Control groupBox1 = (Control)groupBox1Field.GetValue(panel);
                        Control realTimeLimitTextBox = (Control)realTimeLimitTextBoxField.GetValue(panel);

                        // Set skip button visible
                        seriesSkipBtn.Visible = true;

                        // Show panel and process events
                        panel.Show();
                        Application.DoEvents();

                        // Check AutoScrollPosition
                        if (panel.AutoScrollPosition != new Point(0, 0))
                        {
                            throw new Exception($"AutoScrollPosition is not (0,0) for culture {cultureName}, size {sz.Width}x{sz.Height}");
                        }

                        // Define the five strip controls
                        Control[] stripControls = { seriesCheck, seriesSetupBtn, seriesSkipBtn, seriesSummaryLbl, seriesStatusLbl };

                        // Check each strip control
                        foreach (Control ctrl in stripControls)
                        {
                            // (a) Visible and parent is panel
                            if (!ctrl.Visible)
                            {
                                throw new Exception($"Control {ctrl.GetType().Name} is not visible for culture {cultureName}, size {sz.Width}x{sz.Height}");
                            }
                            if (ctrl.Parent != panel)
                            {
                                throw new Exception($"Control {ctrl.GetType().Name} parent is not panel for culture {cultureName}, size {sz.Width}x{sz.Height}");
                            }

                            // (b) Lies entirely inside client rectangle
                            if (ctrl.Left < 0 || ctrl.Top < 0 || ctrl.Right > panel.ClientSize.Width || ctrl.Bottom > panel.ClientSize.Height)
                            {
                                throw new Exception($"Control {ctrl.GetType().Name} is outside client rectangle for culture {cultureName}, size {sz.Width}x{sz.Height}");
                            }

                            // (c) On top layer at its own point
                            Point centerPoint = new Point(ctrl.Left + ctrl.Width / 2, ctrl.Top + ctrl.Height / 2);
                            Control topControl = panel.GetChildAtPoint(centerPoint);
                            if (!ReferenceEquals(topControl, ctrl))
                            {
                                throw new Exception($"Control {ctrl.GetType().Name} is not on top layer at its center for culture {cultureName}, size {sz.Width}x{sz.Height}");
                            }

                            // (d) Does not intersect with other visible non-strip controls
                            foreach (Control other in panel.Controls)
                            {
                                if (ReferenceEquals(other, ctrl)) continue;
                                if (!other.Visible) continue;
                                if (Array.IndexOf(stripControls, other) != -1) continue;

                                if (ctrl.Bounds.IntersectsWith(other.Bounds))
                                {
                                    throw new Exception($"Control {ctrl.GetType().Name} intersects with {other.GetType().Name} for culture {cultureName}, size {sz.Width}x{sz.Height}");
                                }
                            }
                        }

                        // Check groupBox1.Top >= seriesStatusLbl.Bottom
                        if (groupBox1.Top < seriesStatusLbl.Bottom)
                        {
                            throw new Exception($"groupBox1.Top ({groupBox1.Top}) is less than seriesStatusLbl.Bottom ({seriesStatusLbl.Bottom}) for culture {cultureName}, size {sz.Width}x{sz.Height}");
                        }

                        // Check texts are not empty
                        if (string.IsNullOrEmpty(seriesCheck.Text))
                        {
                            throw new Exception($"seriesCheck.Text is empty for culture {cultureName}, size {sz.Width}x{sz.Height}");
                        }
                        if (string.IsNullOrEmpty(seriesSetupBtn.Text))
                        {
                            throw new Exception($"seriesSetupBtn.Text is empty for culture {cultureName}, size {sz.Width}x{sz.Height}");
                        }

                        // Screenshot for size (437, 292) if BM_SHOT_DIR is set
                        if (sz.Width == 437 && sz.Height == 292)
                        {
                            string shotDir = Environment.GetEnvironmentVariable("BM_SHOT_DIR");
                            if (!string.IsNullOrEmpty(shotDir) && Directory.Exists(shotDir))
                            {
                                try
                                {
                                    DocEnergySpectrum doc = new DocEnergySpectrum();
                                    ResultData rd = doc.ActiveResultData;
                                    rd.Series = new SeriesSettings
                                    {
                                        Enabled = true,
                                        Count = 5,
                                        Folder = @"C:\s",
                                        Template = "{prefix}_{n:000}",
                                        Prefix = "sp"
                                    };
                                    rd.DeviceConfig = new DeviceConfigInfo
                                    {
                                        DeviceType = "AtomSpectraVCP"
                                    };
                                    realTimeLimitTextBox.Text = "600";

                                    MethodInfo showSeriesStatusMethod = panel.GetType().GetMethod("ShowSeriesStatus", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                    if (showSeriesStatusMethod != null)
                                    {
                                        showSeriesStatusMethod.Invoke(panel, new object[] { doc, rd });
                                    }
                                }
                                catch
                                {
                                    // Swallow exceptions from ShowSeriesStatus
                                }

                                try
                                {
                                    using (Bitmap bmp = new Bitmap(panel.Width, panel.Height))
                                    {
                                        panel.DrawToBitmap(bmp, new Rectangle(0, 0, panel.Width, panel.Height));
                                        string filePath = Path.Combine(shotDir, $"series_panel_{cultureName}.png");
                                        bmp.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
                                    }
                                }
                                catch
                                {
                                    // Swallow screenshot exceptions
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        failures++;
                        Console.WriteLine($"FAIL Z25_series_strip_is_visible_and_on_top: {ex.GetType().Name}: {ex.Message}");
                    }
                    finally
                    {
                        if (panel != null)
                        {
                            try
                            {
                                panel.Dispose();
                            }
                            catch
                            {
                                // Ignore dispose exceptions
                            }
                        }
                    }
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentUICulture = originalUi;
                Thread.CurrentThread.CurrentCulture = originalCulture;
            }
        }

        if (failures == 0)
        {
            Console.WriteLine("PASS Z25_series_strip_is_visible_and_on_top");
            return 0;
        }
        return 1;
    }
}
