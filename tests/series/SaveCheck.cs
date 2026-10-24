using System;
using System.IO;
using System.Reflection;
using System.Xml.Serialization;
using BecquerelMonitor;

public static class SaveCheck
{
    static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("FAIL usage: <dir>");
            return 1;
        }

        int failures = 0;
        string dir = args[0];

        // Check 1: save_writes_file
        try
        {
            ResultData rd = new ResultData();
            rd.EnergySpectrum = new EnergySpectrum(1.0, 1024);   // the default spectrum has no channel array
            rd.EnergySpectrum.EnergyCalibration = new PolynomialEnergyCalibration { PolynomialOrder = 1, Coefficients = new double[] { 0.0, 3.0 } };   // Clone refuses a spectrum without an energy scale
            rd.Series = new SeriesSettings { Enabled = true, Count = 3, DelaySeconds = 5, Template = "{prefix}_{n:000}" };
            rd.EnergySpectrum.Spectrum[10] = 77;
            rd.EnergySpectrum.MeasurementTime = 123.0;

            Type hostType = typeof(MeasurementSeries).Assembly.GetType("BecquerelMonitor.SeriesHost");
            object host = Activator.CreateInstance(hostType, new object[] { null, null, rd });
            MethodInfo saveMethod = hostType.GetMethod("SaveSpectrum");

            string path = Path.Combine(dir, "sp_001.xml");
            object[] invokeArgs = new object[] { path, null };
            bool result = (bool)saveMethod.Invoke(host, invokeArgs);
            string error = invokeArgs[1] as string;

            if (!result)
            {
                Console.WriteLine("FAIL save_writes_file: SaveSpectrum returned false: " + error);
                failures++;
            }
            else if (!File.Exists(path))
            {
                Console.WriteLine("FAIL save_writes_file: File does not exist");
                failures++;
            }
            else
            {
                long size = new FileInfo(path).Length;
                if (size <= 200)
                {
                    Console.WriteLine("FAIL save_writes_file: File size is " + size + " bytes (expected > 200)");
                    failures++;
                }
                else
                {
                    Console.WriteLine("PASS save_writes_file");
                }
            }
        }
        catch (Exception e)
        {
            Exception ex = e is TargetInvocationException ? e.InnerException : e;
            Console.WriteLine("FAIL save_writes_file: " + ex.GetType().Name + ": " + ex.Message);
            failures++;
        }

        // Check 2: series_not_in_series_file
        try
        {
            string path = Path.Combine(dir, "sp_001.xml");
            if (!File.Exists(path))
            {
                Console.WriteLine("FAIL series_not_in_series_file: File does not exist");
                failures++;
            }
            else
            {
                string text = File.ReadAllText(path);
                if (text.Contains("<Series"))
                {
                    Console.WriteLine("FAIL series_not_in_series_file: File contains <Series");
                    failures++;
                }
                else
                {
                    Console.WriteLine("PASS series_not_in_series_file");
                }
            }
        }
        catch (Exception e)
        {
            Exception ex = e is TargetInvocationException ? e.InnerException : e;
            Console.WriteLine("FAIL series_not_in_series_file: " + ex.GetType().Name + ": " + ex.Message);
            failures++;
        }

        // Check 3: series_file_roundtrip
        try
        {
            string path = Path.Combine(dir, "sp_001.xml");
            if (!File.Exists(path))
            {
                Console.WriteLine("FAIL series_file_roundtrip: File does not exist");
                failures++;
            }
            else
            {
                XmlSerializer serializer = new XmlSerializer(typeof(ResultDataFile));
                using (FileStream fs = File.OpenRead(path))
                {
                    ResultDataFile rdf = (ResultDataFile)serializer.Deserialize(fs);
                    if (rdf.ResultDataList.Count != 1)
                    {
                        Console.WriteLine("FAIL series_file_roundtrip: Expected 1 ResultData, got " + rdf.ResultDataList.Count);
                        failures++;
                    }
                    else
                    {
                        ResultData rd = rdf.ResultDataList[0];
                        bool ok = true;
                        string detail = "";

                        if (rd.EnergySpectrum.Spectrum[10] != 77)
                        {
                            ok = false;
                            detail += "Spectrum[10] is " + rd.EnergySpectrum.Spectrum[10] + " (expected 77); ";
                        }
                        if (rd.EnergySpectrum.MeasurementTime != 123.0)
                        {
                            ok = false;
                            detail += "MeasurementTime is " + rd.EnergySpectrum.MeasurementTime + " (expected 123.0); ";
                        }
                        if (rd.Series != null)
                        {
                            ok = false;
                            detail += "Series is not null; ";
                        }

                        if (ok)
                        {
                            Console.WriteLine("PASS series_file_roundtrip");
                        }
                        else
                        {
                            Console.WriteLine("FAIL series_file_roundtrip: " + detail.TrimEnd());
                            failures++;
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Exception ex = e is TargetInvocationException ? e.InnerException : e;
            Console.WriteLine("FAIL series_file_roundtrip: " + ex.GetType().Name + ": " + ex.Message);
            failures++;
        }

        // Check 4: document_keeps_series_settings
        try
        {
            ResultData rd = new ResultData();
            rd.Series = new SeriesSettings { Enabled = true, Count = 3, DelaySeconds = 5, Template = "{prefix}_{n:000}" };

            ResultDataFile rdf = new ResultDataFile();
            rdf.InitFormatVersion();
            rdf.ResultDataList.Add(rd);

            XmlSerializer serializer = new XmlSerializer(typeof(ResultDataFile));
            using (StringWriter sw = new StringWriter())
            {
                serializer.Serialize(sw, rdf);
                string xml = sw.ToString();

                using (StringReader sr = new StringReader(xml))
                {
                    ResultDataFile rdf2 = (ResultDataFile)serializer.Deserialize(sr);
                    if (rdf2.ResultDataList.Count != 1)
                    {
                        Console.WriteLine("FAIL document_keeps_series_settings: Expected 1 ResultData, got " + rdf2.ResultDataList.Count);
                        failures++;
                    }
                    else
                    {
                        ResultData rd2 = rdf2.ResultDataList[0];
                        bool ok = true;
                        string detail = "";

                        if (rd2.Series == null)
                        {
                            ok = false;
                            detail += "Series is null; ";
                        }
                        else
                        {
                            if (!rd2.Series.Enabled)
                            {
                                ok = false;
                                detail += "Series.Enabled is false; ";
                            }
                            if (rd2.Series.Count != 3)
                            {
                                ok = false;
                                detail += "Series.Count is " + rd2.Series.Count + " (expected 3); ";
                            }
                            if (rd2.Series.DelaySeconds != 5)
                            {
                                ok = false;
                                detail += "Series.DelaySeconds is " + rd2.Series.DelaySeconds + " (expected 5); ";
                            }
                            if (rd2.Series.Template != "{prefix}_{n:000}")
                            {
                                ok = false;
                                detail += "Series.Template is \"" + rd2.Series.Template + "\" (expected \"{prefix}_{n:000}\"); ";
                            }
                        }

                        if (ok)
                        {
                            Console.WriteLine("PASS document_keeps_series_settings");
                        }
                        else
                        {
                            Console.WriteLine("FAIL document_keeps_series_settings: " + detail.TrimEnd());
                            failures++;
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Exception ex = e is TargetInvocationException ? e.InnerException : e;
            Console.WriteLine("FAIL document_keeps_series_settings: " + ex.GetType().Name + ": " + ex.Message);
            failures++;
        }

        // Check 5: no_series_means_no_element
        try
        {
            ResultData rd = new ResultData();
            ResultDataFile rdf = new ResultDataFile();
            rdf.InitFormatVersion();
            rdf.ResultDataList.Add(rd);

            XmlSerializer serializer = new XmlSerializer(typeof(ResultDataFile));
            using (StringWriter sw = new StringWriter())
            {
                serializer.Serialize(sw, rdf);
                string xml = sw.ToString();

                if (xml.Contains("<Series"))
                {
                    Console.WriteLine("FAIL no_series_means_no_element: XML contains <Series");
                    failures++;
                }
                else
                {
                    Console.WriteLine("PASS no_series_means_no_element");
                }
            }
        }
        catch (Exception e)
        {
            Exception ex = e is TargetInvocationException ? e.InnerException : e;
            Console.WriteLine("FAIL no_series_means_no_element: " + ex.GetType().Name + ": " + ex.Message);
            failures++;
        }

        // Check 6: save_failure_reports_error
        try
        {
            string blockerPath = Path.Combine(dir, "blocker.txt");
            using (FileStream fs = File.Create(blockerPath))
            {
                fs.WriteByte(0);
            }

            ResultData rd = new ResultData();
            Type hostType = typeof(MeasurementSeries).Assembly.GetType("BecquerelMonitor.SeriesHost");
            object host = Activator.CreateInstance(hostType, new object[] { null, null, rd });
            MethodInfo saveMethod = hostType.GetMethod("SaveSpectrum");

            string badPath = Path.Combine(dir, "blocker.txt", "x.xml");
            object[] invokeArgs = new object[] { badPath, null };
            bool result = (bool)saveMethod.Invoke(host, invokeArgs);
            string error = invokeArgs[1] as string;

            if (result)
            {
                Console.WriteLine("FAIL save_failure_reports_error: SaveSpectrum returned true");
                failures++;
            }
            else if (string.IsNullOrEmpty(error))
            {
                Console.WriteLine("FAIL save_failure_reports_error: Error string is empty");
                failures++;
            }
            else
            {
                Console.WriteLine("PASS save_failure_reports_error");
            }
        }
        catch (Exception e)
        {
            Exception ex = e is TargetInvocationException ? e.InnerException : e;
            Console.WriteLine("FAIL save_failure_reports_error: " + ex.GetType().Name + ": " + ex.Message);
            failures++;
        }

        Console.WriteLine("SUMMARY fail=" + failures);
        return failures;
    }
}
