using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;
using BecquerelMonitor.Utils;

namespace BecquerelMonitor
{
    internal sealed class SeriesHost : ISeriesHost, IDisposable
    {
        private readonly MainForm _mainForm;
        private readonly DocEnergySpectrum _doc;
        private readonly ResultData _rd;
        private MeasurementSeries _series;
        private System.Windows.Forms.Timer _timer;
        private int _lostTicks;
        private bool _reportedError;

        public static MeasurementSeries Create(MainForm mainForm, DocEnergySpectrum doc, ResultData rd, int presetSeconds)
        {
            if (rd.MeasurementController.SeriesHost != null)
            {
                rd.MeasurementController.SeriesHost.Dispose();
                rd.MeasurementController.SeriesHost = null;
            }

            SeriesSettings settings = rd.Series.Clone();
            settings.Normalize();

            if (string.IsNullOrEmpty(settings.Folder))
            {
                string dir = Path.GetDirectoryName(doc.Filename);
                settings.Folder = string.IsNullOrEmpty(dir)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                    : dir;
            }

            if (string.IsNullOrEmpty(settings.Prefix))
            {
                string name = Path.GetFileNameWithoutExtension(doc.Filename);
                settings.Prefix = string.IsNullOrEmpty(name) ? "spectrum" : name;
            }

            var host = new SeriesHost(mainForm, doc, rd);
            var series = new MeasurementSeries(settings, host, presetSeconds);
            rd.MeasurementController.Series = series;
            rd.MeasurementController.SeriesHost = host;
            host.Attach(series);
            return series;
        }

        public SeriesHost(MainForm mainForm, DocEnergySpectrum doc, ResultData rd)
        {
            _mainForm = mainForm;
            _doc = doc;
            _rd = rd;
        }

        public void Attach(MeasurementSeries series)
        {
            _series = series;
            _lostTicks = 0;
            _reportedError = false;
            if (_timer == null)
            {
                _timer = new System.Windows.Forms.Timer { Interval = 500 };
                _timer.Tick += OnTimerTick;
            }
            _timer.Start();
        }

        public void Dispose()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= OnTimerTick;
                _timer.Dispose();
                _timer = null;
            }
            _series = null;
        }

        public DateTime Now
        {
            get { return DateTime.Now; }
        }

        public bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public long FreeDiskBytes(string folder)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(folder));
                if (root.StartsWith("\\\\"))
                {
                    return -1;
                }
                return new DriveInfo(root).AvailableFreeSpace;
            }
            catch
            {
                return -1;
            }
        }

        public bool SaveSpectrum(string path, out string error)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                ResultData copy = _rd.Clone();
                copy.Series = null;

                var file = new ResultDataFile();
                file.InitFormatVersion();
                file.ResultDataList.Add(copy);

                var serializer = new XmlSerializer(typeof(ResultDataFile));
                var settings = new XmlWriterSettings
                {
                    Encoding = new System.Text.UTF8Encoding(true),
                    Indent = true
                };

                AtomicFileWriter.Write(path, stream =>
                {
                    using (XmlWriter w = XmlWriter.Create(stream, settings))
                    {
                        serializer.Serialize(w, file);
                        w.Flush();
                    }
                });

                error = "";
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public bool ClearSpectrum(out string error)
        {
            try
            {
                _rd.MeasurementController.ClearMeasurementResult();
                _rd.ResultDataStatus.TotalTime = TimeSpan.Zero;
                _rd.ResultDataStatus.ElapsedTime = TimeSpan.Zero;
                _rd.EnergySpectrum.Initialize();
                _rd.PulseCollection.Pulses.Clear();
                _rd.CountRates.Clear();
                _doc.Dirty = true;
                if (_mainForm.ActiveDocument == _doc)
                {
                    _mainForm.UpdateSampleInfo();
                }
                error = "";
                return true;
            }
            catch (Exception ex)
            {
                error = SeriesText.ClearFailed + " " + ex.Message;
                return false;
            }
        }

        public bool StartAcquisition(int presetSeconds, out string error)
        {
            try
            {
                _rd.ResultDataStatus.PresetTime = presetSeconds;
                _doc.Dirty = true;
                _doc.UpdateSpectrum = true;
                _rd.Dirty = true;

                if (_doc.PulseDetector != null)
                {
                    DCPulseView v = _mainForm.DCPulseView;
                    _doc.PulseDetector.PulseView = v.PulseView;
                    _doc.PulseDetector.NGPulseView = v.NGPulseView;
                    _doc.PulseDetector.DoUpdatePulseView = _mainForm.DoUpdatePulseView;
                }

                bool ok = _rd.MeasurementController.StartRecording();
                if (!ok)
                {
                    _doc.UpdateSpectrum = false;
                    error = SeriesText.StartFailed;
                    return false;
                }

                if (_mainForm.ActiveDocument == _doc)
                {
                    _mainForm.UpdateSampleInfo();
                }

                error = "";
                return true;
            }
            catch (Exception ex)
            {
                _doc.UpdateSpectrum = false;
                error = SeriesText.StartFailed + " " + ex.Message;
                return false;
            }
        }

        public void StateChanged()
        {
            try
            {
                if (_series == null)
                {
                    return;
                }

                SeriesState state = _series.State;

                if (state == SeriesState.Done || state == SeriesState.PausedError || state == SeriesState.Idle)
                {
                    if (_timer != null)
                    {
                        _timer.Stop();
                    }
                }
                else if (_timer != null && !_timer.Enabled)
                {
                    _timer.Start();   // the series began after Attach (e.g. after a question): the countdown timer must run
                }

                if (_mainForm.ActiveDocument == _doc)
                {
                    _mainForm.ControlPanel.ShowDocumentStatus();
                    _doc.UpdateEnergySpectrum();
                }

                if (state == SeriesState.PausedError)
                {
                    if (!_reportedError && AppUi.HasWindows)
                    {
                        AppUi.Report(_series.LastError, SeriesText.Warning, MessageBoxIcon.Warning);
                        _reportedError = true;
                    }
                }
                else
                {
                    _reportedError = false;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("SeriesHost.StateChanged: " + ex);
            }
        }

        public void SeriesFinished()
        {
            try
            {
                _rd.MeasurementController.RunEndOfMeasurementActions();
            }
            catch (Exception ex)
            {
                Trace.WriteLine("SeriesHost.SeriesFinished: " + ex);
            }
            Trace.WriteLine("Series finished: " + _doc.Filename);
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            try
            {
                if (_series == null || !_series.IsActive)
                {
                    if (_timer != null)
                    {
                        _timer.Stop();
                    }
                    return;
                }

                _series.Tick();

                if (_series.State == SeriesState.Acquiring && !_rd.ResultDataStatus.Recording)
                {
                    _lostTicks++;
                    if (_lostTicks >= 3)
                    {
                        _series.OnDeviceLost(SeriesText.DeviceLost);
                        _lostTicks = 0;
                    }
                }
                else
                {
                    _lostTicks = 0;
                }

                if (_series.State == SeriesState.Delay && _mainForm.ActiveDocument == _doc)
                {
                    _mainForm.ControlPanel.ShowSeriesCountdown(_doc, _rd);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("SeriesHost.OnTimerTick: " + ex);
            }
        }
    }
}
