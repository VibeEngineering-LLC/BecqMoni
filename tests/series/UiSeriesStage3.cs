using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;

public static class UiStage3
{
    public static int Run()
    {
        int fails = 0;
        fails += Check1();
        fails += Check2();
        fails += Check3();
        fails += Check4();
        fails += Check5();
        fails += Check6();
        fails += Check7();
        fails += Check8();
        fails += Check9();
        fails += Check10();
        fails += Check12();
        fails += Check11();
        return fails;
    }

    private static int Check1()
    {
        try
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

            MainForm mf = NewMain(ask);
            Call(mf, "DeleteActiveSpectrum", new object[] { doc });

            bool ok = doc.ResultDataFile.ResultDataList.Count == 2
                && rd.MeasurementController.Series.IsActive
                && host.Saves == 0
                && askCount == 1;

            if (ok)
            {
                Console.WriteLine("PASS Z1_delete_declined_keeps_series");
                return 0;
            }
            else
            {
                Console.WriteLine("FAIL Z1_delete_declined_keeps_series: count=" + doc.ResultDataFile.ResultDataList.Count + " active=" + rd.MeasurementController.Series.IsActive + " saves=" + host.Saves + " asks=" + askCount);
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL Z1_delete_declined_keeps_series: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    private static int Check12()
    {
        try
        {
            var host = new Z3Host();
            var settings = new SeriesSettings
            {
                Enabled = true,
                Count = 5,
                DelaySeconds = 60,
                ClearBetween = true,
                Folder = @"C:\s",
                Prefix = "sp"
            };
            var series = new MeasurementSeries(settings, host, 600);
            var doc = new DocEnergySpectrum();
            var rd = doc.ActiveResultData;
            rd.MeasurementController.Series = series;
            series.Begin(0);

            var panel = NewPanel((msg, buttons) =>
            {
                series.OnSpectrumComplete();
                return DialogResult.Yes;
            });

            var result = Call(panel, "SeriesBeforeStop", new object[] { doc, rd, true, false });
            int ret = (int)result;

            if (ret == 2 && host.Saves == 1 && series.State == SeriesState.Idle)
            {
                Console.WriteLine("PASS Z12_stale_answer_after_pause_started");
                return 0;
            }
            else
            {
                Console.WriteLine($"FAIL Z12_stale_answer_after_pause_started: ret={ret}, saves={host.Saves}, state={series.State}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL Z12_stale_answer_after_pause_started: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }
    private static int Check2()
    {
        try
        {
            Z3Host host = new Z3Host();
            DocEnergySpectrum doc = new DocEnergySpectrum();
            doc.CreateResultData();
            ResultData rd = doc.ActiveResultData;
            MeasurementSeries series = MakeSeries(host, 5);
            rd.MeasurementController.Series = series;
            series.Begin(0);

            int seriesAsks = 0;
            Func<string, MessageBoxButtons, DialogResult> ask = (text, buttons) =>
            {
                if (buttons == MessageBoxButtons.OKCancel)
                {
                    return DialogResult.OK;
                }
                seriesAsks++;
                return DialogResult.Yes;
            };

            MainForm mf = NewMain(ask);
            try
            {
                Call(mf, "DeleteActiveSpectrum", new object[] { doc });
            }
            catch
            {
                // Ignore exceptions from teardown
            }

            bool ok = series.State == SeriesState.Idle
                && host.Saves == 0
                && seriesAsks == 1;

            if (ok)
            {
                Console.WriteLine("PASS Z2_delete_confirmed_stops_series");
                return 0;
            }
            else
            {
                Console.WriteLine("FAIL Z2_delete_confirmed_stops_series: state=" + series.State + " saves=" + host.Saves + " asks=" + seriesAsks);
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL Z2_delete_confirmed_stops_series: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    private static int Check3()
    {
        try
        {
            Z3Host host = new Z3Host();
            DocEnergySpectrum doc = new DocEnergySpectrum();
            ResultData rd = doc.ActiveResultData;
            MeasurementSeries series = MakeSeries(host, 5);
            rd.MeasurementController.Series = series;
            series.Begin(0);

            int askCount = 0;
            Func<string, MessageBoxButtons, DialogResult> ask = (text, buttons) =>
            {
                askCount++;
                return DialogResult.No;
            };

            MainForm mf = NewMain(ask);
            SetField(mf, "activeDocument", doc);

            object result = Call(mf, "CloseActiveDocument", new object[] { });
            bool returnedFalse = (result is bool) && !(bool)result;

            bool ok = returnedFalse
                && askCount == 1
                && series.IsActive;

            if (ok)
            {
                Console.WriteLine("PASS Z3_menu_close_asks_about_series");
                return 0;
            }
            else
            {
                Console.WriteLine("FAIL Z3_menu_close_asks_about_series: ret=" + result + " asks=" + askCount + " active=" + series.IsActive);
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL Z3_menu_close_asks_about_series: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    private static int Check4()
    {
        try
        {
            Z3Host host = new Z3Host();
            DocEnergySpectrum doc = new DocEnergySpectrum();
            ResultData rd = doc.ActiveResultData;
            MeasurementSeries series = MakeSeries(host, 5);
            rd.MeasurementController.Series = series;
            series.Begin(0);

            int askCount = 0;
            Func<string, MessageBoxButtons, DialogResult> ask = (text, buttons) =>
            {
                askCount++;
                return DialogResult.No;
            };

            MainForm mf = NewMain(ask);
            SetField(mf, "activeDocument", doc);

            FormClosingEventArgs e = new FormClosingEventArgs(CloseReason.UserClosing, false);
            Call(mf, "DocEnergySpectrum_FormClosing", new object[] { doc, e });

            bool ok = e.Cancel
                && askCount == 1
                && series.IsActive;

            if (ok)
            {
                Console.WriteLine("PASS Z4_tab_close_asks_about_series");
                return 0;
            }
            else
            {
                Console.WriteLine("FAIL Z4_tab_close_asks_about_series: cancel=" + e.Cancel + " asks=" + askCount + " active=" + series.IsActive);
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL Z4_tab_close_asks_about_series: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    private static int Check5()
    {
        try
        {
            Z3Host host = new Z3Host();
            DocEnergySpectrum doc = new DocEnergySpectrum();
            doc.Filename = "z5.xml";
            ResultData rd = doc.ActiveResultData;
            MeasurementSeries series = MakeSeries(host, 5);
            rd.MeasurementController.Series = series;
            series.Begin(0);

            DocumentManager dm = DocumentManager.GetInstance();
            dm.DocumentList.Add(doc);

            int askCount = 0;
            Func<string, MessageBoxButtons, DialogResult> ask = (text, buttons) =>
            {
                askCount++;
                return DialogResult.No;
            };

            MainForm mf = NewMain(ask);
            SetField(mf, "documentManager", dm);

            bool ok = true;
            string detail = "";
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    bool force = (i == 0);
                    try
                    {
                        Call(mf, "CloseDocument", new object[] { "z5.xml", force });
                    }
                    catch (Exception ex)
                    {
                        ok = false;
                        detail = "exception: " + ex.GetType().Name + ": " + ex.Message;
                        break;
                    }

                    if (!series.IsActive)
                    {
                        ok = false;
                        detail = "series not active after call " + (i + 1);
                        break;
                    }
                    if (!dm.DocumentList.Contains(doc))
                    {
                        ok = false;
                        detail = "doc removed from list after call " + (i + 1);
                        break;
                    }
                    if (askCount != (i + 1))
                    {
                        ok = false;
                        detail = "ask count " + askCount + " expected " + (i + 1);
                        break;
                    }
                }
            }
            finally
            {
                dm.DocumentList.Remove(doc);
            }

            if (ok)
            {
                Console.WriteLine("PASS Z5_close_document_by_name_asks");
                return 0;
            }
            else
            {
                Console.WriteLine("FAIL Z5_close_document_by_name_asks: " + detail);
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL Z5_close_document_by_name_asks: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    private static int Check6()
    {
        try
        {
            Z3Host host = new Z3Host();
            DocEnergySpectrum doc = new DocEnergySpectrum();
            ResultData rd = doc.ActiveResultData;
            MeasurementSeries series = MakeSeries(host, 5);
            rd.MeasurementController.Series = series;
            series.Begin(0);

            DCControlPanel panel = NewPanel((text, buttons) => DialogResult.Cancel);

            bool saveCurrent;
            object result = Call(panel, "SeriesBeforeStop", new object[] { doc, rd, true, false });
            int ret = (result is int) ? (int)result : -1;

            bool ok = ret == 2
                && series.State == SeriesState.Acquiring
                && host.Saves == 0;

            if (ok)
            {
                Console.WriteLine("PASS Z6_stop_cancel_keeps_series");
                return 0;
            }
            else
            {
                Console.WriteLine("FAIL Z6_stop_cancel_keeps_series: ret=" + ret + " state=" + series.State + " saves=" + host.Saves);
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL Z6_stop_cancel_keeps_series: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    private static int Check7()
    {
        try
        {
            // Part 1: saveCurrent = true
            Z3Host host1 = new Z3Host();
            DocEnergySpectrum doc1 = new DocEnergySpectrum();
            ResultData rd1 = doc1.ActiveResultData;
            MeasurementSeries series1 = MakeSeries(host1, 5);
            rd1.MeasurementController.Series = series1;
            series1.Begin(0);

            DCControlPanel panel1 = NewPanel((text, buttons) => DialogResult.OK);
            try
            {
                Call(panel1, "SeriesAfterStop", new object[] { rd1, true });
            }
            catch
            {
                // Ignore exceptions from status display
            }

            bool ok1 = series1.State == SeriesState.Idle && host1.Saves == 1;

            // Part 2: saveCurrent = false
            Z3Host host2 = new Z3Host();
            DocEnergySpectrum doc2 = new DocEnergySpectrum();
            ResultData rd2 = doc2.ActiveResultData;
            MeasurementSeries series2 = MakeSeries(host2, 5);
            rd2.MeasurementController.Series = series2;
            series2.Begin(0);

            DCControlPanel panel2 = NewPanel((text, buttons) => DialogResult.OK);
            try
            {
                Call(panel2, "SeriesAfterStop", new object[] { rd2, false });
            }
            catch
            {
                // Ignore exceptions from status display
            }

            bool ok2 = series2.State == SeriesState.Idle && host2.Saves == 0;

            if (ok1 && ok2)
            {
                Console.WriteLine("PASS Z7_after_stop_stops_series");
                return 0;
            }
            else
            {
                Console.WriteLine("FAIL Z7_after_stop_stops_series: part1=" + ok1 + " part2=" + ok2);
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL Z7_after_stop_stops_series: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    private static int Check8()
    {
        try
        {
            Z3Host host = new Z3Host();
            DocEnergySpectrum doc = new DocEnergySpectrum();
            ResultData rd = doc.ActiveResultData;

            rd.DeviceConfig = new DeviceConfigInfo
            {
                DeviceType = AtomSpectraTcpDeviceConfig.DeviceTypeId,
                InputDeviceConfig = new AtomSpectraTcpDeviceConfig
                {
                    Transport = AtomSpectraTransport.Rest, RestObserveOnly = true
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

            string capturedText = "";
            DCControlPanel panel = NewPanel((text, buttons) =>
            {
                capturedText = text;
                return DialogResult.OK;
            });

            object result = Call(panel, "SeriesBegin", new object[] { doc, rd, rd.ResultDataStatus });
            bool returnedFalse = (result is bool) && !(bool)result;

            bool ok = returnedFalse
                && (capturedText.Contains("наблюдени") || capturedText.ToLowerInvariant().Contains("observe"))
                && rd.MeasurementController.Series == null;

            if (ok)
            {
                Console.WriteLine("PASS Z8_rest_observe_only_refused");
                return 0;
            }
            else
            {
                Console.WriteLine("FAIL Z8_rest_observe_only_refused: ret=" + result + " text=" + capturedText + " series=" + (rd.MeasurementController.Series == null ? "null" : "not null"));
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL Z8_rest_observe_only_refused: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    private static int Check9()
    {
        try
        {
            // Part 1: Save on device lost
            Z3Host host1 = new Z3Host();
            DocEnergySpectrum doc1 = new DocEnergySpectrum();
            ResultData rd1 = doc1.ActiveResultData;
            MeasurementSeries series1 = MakeSeries(host1, 5);
            rd1.MeasurementController.Series = series1;
            series1.Begin(0);

            int dialogCalls1 = 0;
            string lastText1 = "";
            DCControlPanel panel1 = NewPanel((text, buttons) =>
            {
                dialogCalls1++;
                lastText1 = text;
                if (dialogCalls1 == 1)
                {
                    series1.OnDeviceLost("lost");
                    return DialogResult.Yes;
                }
                return DialogResult.OK;
            });

            bool save1;
            Call(panel1, "SeriesBeforeStop", new object[] { doc1, rd1, true, false });

            bool ok1 = host1.Saves == 1
                && dialogCalls1 == 2
                && series1.State == SeriesState.Idle;

            // Part 2: Don't save on device lost
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

            bool save2;
            Call(panel2, "SeriesBeforeStop", new object[] { doc2, rd2, true, false });

            bool ok2 = host2.Saves == 0
                && dialogCalls2 == 1;

            // Part 3: Save error on device lost
            Z3Host host3 = new Z3Host();
            host3.SaveError = "disk full";
            DocEnergySpectrum doc3 = new DocEnergySpectrum();
            ResultData rd3 = doc3.ActiveResultData;
            MeasurementSeries series3 = MakeSeries(host3, 5);
            rd3.MeasurementController.Series = series3;
            series3.Begin(0);

            int dialogCalls3 = 0;
            string lastText3 = "";
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

            bool save3;
            Call(panel3, "SeriesBeforeStop", new object[] { doc3, rd3, true, false });

            bool ok3 = host3.Saves == 0
                && dialogCalls3 == 2
                && lastText3.Contains("disk full");

            if (ok1 && ok2 && ok3)
            {
                Console.WriteLine("PASS Z9_device_lost_while_stop_question_open");
                return 0;
            }
            else
            {
                Console.WriteLine("FAIL Z9_device_lost_while_stop_question_open: part1=" + ok1 + " part2=" + ok2 + " part3=" + ok3);
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL Z9_device_lost_while_stop_question_open: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    private static int Check10()
    {
        try
        {
            Type formType = typeof(MainForm).Assembly.GetType("BecquerelMonitor.SeriesSettingsForm");
            if (formType == null)
            {
                Console.WriteLine("FAIL Z10_form_refuses_template_without_n: SeriesSettingsForm type not found");
                return 1;
            }

            // Part 1: template "data", NoOverwrite false (refusal)
            SeriesSettings settings1 = new SeriesSettings
            {
                Enabled = true,
                Count = 5,
                Folder = @"C:\s",
                Template = "data",
                Prefix = "sp",
                NoOverwrite = false
            };

            object form1 = Activator.CreateInstance(formType, new object[] { settings1, 600, @"C:\s", "doc", true });

            // Check for WarnAsk seam
            FieldInfo warnAskField = formType.GetField("WarnAsk", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (warnAskField == null)
            {
                Console.WriteLine("FAIL Z10_form_refuses_template_without_n: WarnAsk seam missing");
                return 1;
            }

            string captured1 = CaptureError(() =>
            {
                try
                {
                    Call(form1, "BtnOk_Click", new object[] { null, EventArgs.Empty });
                }
                catch
                {
                    // Ignore exceptions from Close()
                }
            });

            object result1 = formType.GetProperty("Result").GetValue(form1);
            bool ok1 = result1 == null && !string.IsNullOrEmpty(captured1);

            // Part 2: template "data", NoOverwrite true (warning)
            SeriesSettings settings2 = new SeriesSettings
            {
                Enabled = true,
                Count = 5,
                Folder = @"C:\s",
                Template = "data",
                Prefix = "sp",
                NoOverwrite = true
            };

            object form2 = Activator.CreateInstance(formType, new object[] { settings2, 600, @"C:\s", "doc", true });

            int warnCalls2 = 0;
            Func<string, bool> warnAsk2 = (text) =>
            {
                warnCalls2++;
                return false;
            };
            warnAskField.SetValue(form2, warnAsk2);

            string captured2 = CaptureError(() =>
            {
                try
                {
                    Call(form2, "BtnOk_Click", new object[] { null, EventArgs.Empty });
                }
                catch
                {
                    // Ignore exceptions from Close()
                }
            });

            object result2 = formType.GetProperty("Result").GetValue(form2);
            bool ok2 = result2 == null && warnCalls2 == 1;

            // Part 2b: WarnAsk returns true
            object form2b = Activator.CreateInstance(formType, new object[] { settings2, 600, @"C:\s", "doc", true });

            int warnCalls2b = 0;
            Func<string, bool> warnAsk2b = (text) =>
            {
                warnCalls2b++;
                return true;
            };
            warnAskField.SetValue(form2b, warnAsk2b);

            string captured2b = CaptureError(() =>
            {
                try
                {
                    Call(form2b, "BtnOk_Click", new object[] { null, EventArgs.Empty });
                }
                catch
                {
                    // Ignore exceptions from Close()
                }
            });

            object result2b = formType.GetProperty("Result").GetValue(form2b);
            bool ok2b = result2b != null && warnCalls2b == 1;

            if (result2b != null)
            {
                object template2b = result2b.GetType().GetProperty("Template").GetValue(result2b);
                if (!template2b.Equals("data"))
                {
                    ok2b = false;
                }
            }

            // Part 3: template "{prefix}_{n}", NoOverwrite false
            SeriesSettings settings3 = new SeriesSettings
            {
                Enabled = true,
                Count = 5,
                Folder = @"C:\s",
                Template = "{prefix}_{n}",
                Prefix = "sp",
                NoOverwrite = false
            };

            object form3 = Activator.CreateInstance(formType, new object[] { settings3, 600, @"C:\s", "doc", true });

            int warnCalls3 = 0;
            Func<string, bool> warnAsk3 = (text) =>
            {
                warnCalls3++;
                return false;
            };
            warnAskField.SetValue(form3, warnAsk3);

            string captured3 = CaptureError(() =>
            {
                try
                {
                    Call(form3, "BtnOk_Click", new object[] { null, EventArgs.Empty });
                }
                catch
                {
                    // Ignore exceptions from Close()
                }
            });

            object result3 = formType.GetProperty("Result").GetValue(form3);
            bool ok3 = result3 != null && warnCalls3 == 0 && string.IsNullOrEmpty(captured3);

            if (ok1 && ok2 && ok2b && ok3)
            {
                Console.WriteLine("PASS Z10_form_refuses_template_without_n");
                return 0;
            }
            else
            {
                Console.WriteLine("FAIL Z10_form_refuses_template_without_n: part1=" + ok1 + " part2=" + ok2 + " part2b=" + ok2b + " part3=" + ok3);
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL Z10_form_refuses_template_without_n: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    private static int Check11()
    {
        try
        {
            bool okA = false;
            bool okB = false;
            bool okC = false;
            bool okD = false;

            // Sub-case (a): ConfirmCloseDuringSeries throws InvalidOperationException
            Z3Host hostA = new Z3Host();
            DocEnergySpectrum docA = new DocEnergySpectrum();
            docA.CreateResultData();
            ResultData rdA = docA.ActiveResultData;
            MeasurementSeries seriesA = MakeSeries(hostA, 5);
            rdA.MeasurementController.Series = seriesA;
            seriesA.Begin(0);

            MainForm mfA = NewMain(null);
            SetField(mfA, "activeDocument", docA);

            Thread threadA = new Thread(() =>
            {
                try
                {
                    CallRaw(mfA, "ConfirmCloseDuringSeries", new object[] { docA, null });
                    okA = false;
                }
                catch (InvalidOperationException)
                {
                    okA = true;
                }
                catch
                {
                    okA = false;
                }
            });
            threadA.IsBackground = true;
            threadA.Start();
            if (!threadA.Join(5000))
            {
                okA = false;
            }

            // Sub-case (b): SeriesBeforeStop throws InvalidOperationException
            Z3Host hostB = new Z3Host();
            DocEnergySpectrum docB = new DocEnergySpectrum();
            ResultData rdB = docB.ActiveResultData;
            MeasurementSeries seriesB = MakeSeries(hostB, 5);
            rdB.MeasurementController.Series = seriesB;
            seriesB.Begin(0);

            DCControlPanel panelB = NewPanel(null);

            Thread threadB = new Thread(() =>
            {
                try
                {
                    CallRaw(panelB, "SeriesBeforeStop", new object[] { docB, rdB, true, false });
                    okB = false;
                }
                catch (InvalidOperationException)
                {
                    okB = true;
                }
                catch
                {
                    okB = false;
                }
            });
            threadB.IsBackground = true;
            threadB.Start();
            if (!threadB.Join(5000))
            {
                okB = false;
            }

            // Sub-case (c): SeriesBegin returns false and captures "TCP/REST"
            Z3Host hostC = new Z3Host();
            DocEnergySpectrum docC = new DocEnergySpectrum();
            ResultData rdC = docC.ActiveResultData;

            rdC.DeviceConfig = new DeviceConfigInfo
            {
                DeviceType = AtomSpectraTcpDeviceConfig.DeviceTypeId,
                InputDeviceConfig = new AtomSpectraTcpDeviceConfig
                {
                    Transport = AtomSpectraTransport.Rest, RestObserveOnly = true
                }
            };

            rdC.Series = new SeriesSettings
            {
                Enabled = true,
                Count = 3,
                Folder = @"C:\s",
                Prefix = "sp"
            };

            rdC.ResultDataStatus.ElapsedTime = TimeSpan.Zero;

            DCControlPanel panelC = NewPanel(null);

            string capturedC = "";
            Thread threadC = new Thread(() =>
            {
                capturedC = CaptureError(() =>
                {
                    try
                    {
                        object result = Call(panelC, "SeriesBegin", new object[] { docC, rdC, rdC.ResultDataStatus });
                        if (result is bool && !(bool)result)
                        {
                            okC = true;
                        }
                    }
                    catch
                    {
                        okC = false;
                    }
                });
            });
            threadC.IsBackground = true;
            threadC.Start();
            if (!threadC.Join(5000))
            {
                okC = false;
            }
            else
            {
                if (!(capturedC.Contains("наблюдени") || capturedC.ToLowerInvariant().Contains("observe")))
                {
                    okC = false;
                }
            }

            // Sub-case (d): DeleteActiveSpectrum throws InvalidOperationException
            Z3Host hostD = new Z3Host();
            DocEnergySpectrum docD = new DocEnergySpectrum();
            docD.CreateResultData();
            ResultData rdD = docD.ActiveResultData;
            MeasurementSeries seriesD = MakeSeries(hostD, 5);
            rdD.MeasurementController.Series = seriesD;
            seriesD.Begin(0);

            MainForm mfD = NewMain(null);
            SetField(mfD, "activeDocument", docD);

            Thread threadD = new Thread(() =>
            {
                try
                {
                    CallRaw(mfD, "DeleteActiveSpectrum", new object[] { docD });
                    okD = false;
                }
                catch (InvalidOperationException)
                {
                    if (docD.ResultDataFile.ResultDataList.Count == 2 && seriesD.IsActive)
                    {
                        okD = true;
                    }
                }
                catch
                {
                    okD = false;
                }
            });
            threadD.IsBackground = true;
            threadD.Start();
            if (!threadD.Join(5000))
            {
                okD = false;
            }

            if (okA && okB && okC && okD)
            {
                Console.WriteLine("PASS Z11_headless_series_questions_open_no_window");
                return 0;
            }
            else
            {
                Console.WriteLine("FAIL Z11_headless_series_questions_open_no_window: a=" + okA + " b=" + okB + " c=" + okC + " d=" + okD);
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL Z11_headless_series_questions_open_no_window: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    private static object Call(object target, string name, object[] args)
    {
        MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (method == null)
        {
            throw new Exception(name + " is missing");
        }
        try
        {
            return method.Invoke(target, args);
        }
        catch (TargetInvocationException tie)
        {
            Exception inner = tie.InnerException;
            throw new Exception(inner.GetType().Name + ": " + inner.Message);
        }
    }

    private static object CallRaw(object target, string name, object[] args)
    {
        MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (method == null)
        {
            throw new Exception(name + " is missing");
        }
        try
        {
            return method.Invoke(target, args);
        }
        catch (TargetInvocationException tie)
        {
            throw tie.InnerException;
        }
    }

    private static void SetField(object o, string name, object value)
    {
        FieldInfo field = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (field == null)
        {
            throw new Exception(name + " seam missing");
        }
        field.SetValue(o, value);
    }

    private static bool TrySetField(object o, string name, object value)
    {
        FieldInfo field = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (field == null)
        {
            return false;
        }
        field.SetValue(o, value);
        return true;
    }

    private static DCControlPanel NewPanel(Func<string, MessageBoxButtons, DialogResult> dialog)
    {
        MainForm mf = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
        DCControlPanel panel = new DCControlPanel(mf);
        if (dialog != null)
        {
            SetField(panel, "SeriesDialog", dialog);
        }
        TextBox textBox = (TextBox)GetField(panel, "realTimeLimitTextBox");
        if (textBox != null)
        {
            textBox.Text = "600";
        }
        return panel;
    }

    private static object GetField(object o, string name)
    {
        FieldInfo field = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (field == null)
        {
            return null;
        }
        return field.GetValue(o);
    }

    private static MainForm NewMain(Func<string, MessageBoxButtons, DialogResult> ask)
    {
        MainForm mf = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
        if (ask != null)
        {
            SetField(mf, "UiAsk", ask);
        }
        return mf;
    }

    private static string CaptureError(Action a)
    {
        TextWriter oldError = Console.Error;
        StringWriter writer = new StringWriter();
        Console.SetError(writer);
        try
        {
            a();
        }
        finally
        {
            Console.SetError(oldError);
        }
        return writer.ToString();
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
}

public class Z3Host : ISeriesHost
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
        // Do nothing
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
        // Do nothing
    }

    public void SeriesFinished()
    {
        // Do nothing
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
