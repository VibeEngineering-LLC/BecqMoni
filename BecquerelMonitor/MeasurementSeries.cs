using System;
using System.Collections.Generic;
using System.Globalization;

namespace BecquerelMonitor
{
    public enum SeriesState
    {
        Idle,
        Acquiring,
        Saving,
        Delay,
        Clearing,
        Done,
        PausedError,
        Waiting
    }

    public interface ISeriesHost
    {
        DateTime Now { get; }
        bool FileExists(string path);
        long FreeDiskBytes(string folder);
        bool SaveSpectrum(string path, out string error);
        bool ClearSpectrum(out string error);
        bool StartAcquisition(int presetSeconds, out string error);
        void StateChanged();
        void SeriesFinished();
        long MonotonicMs { get; }
        void SetKeepAwake(bool on);
    }

    public sealed partial class MeasurementSeries
    {
        private readonly ISeriesHost _host;
        private readonly int _presetSeconds;
        private readonly List<string> _files = new List<string>();
        private SeriesState _state = SeriesState.Idle;
        private int _index;
        private string _lastError = string.Empty;
        private DateTime _delayEnd;
        private int _baseSeconds;

        public MeasurementSeries(SeriesSettings settings, ISeriesHost host, int presetSeconds)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (presetSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(presetSeconds));

            _host = host;
            _presetSeconds = presetSeconds;
            Settings = settings.Clone();
            Settings.Normalize();
        }

        public SeriesState State
        {
            get { return _state; }
            private set
            {
                if (_state != value)
                {
                    SeriesState was = _state;
                    _state = value;
                    if (value == SeriesState.Waiting)
                    {
                        _host.SetKeepAwake(true);    // the computer must not sleep while the series waits for its start
                    }
                    else if (was == SeriesState.Waiting)
                    {
                        _host.SetKeepAwake(false);   // every way out of Waiting releases it (start, Stop, failure)
                    }
                    _host.StateChanged();
                }
            }
        }

        public int Index
        {
            get { return _index; }
            private set { _index = value; }
        }

        public string LastError
        {
            get { return _lastError; }
            private set { _lastError = value ?? string.Empty; }
        }

        public DateTime DelayEnd
        {
            get { return _delayEnd; }
            private set { _delayEnd = value; }
        }

        public IReadOnlyList<string> Files
        {
            get { return _files; }
        }

        public SeriesSettings Settings { get; }

        /// <summary>Number of the current acquisition run: grows with every NEW spectrum (Begin, successful start of k+1). A question asked about an older run is stale.</summary>
        public int AcquisitionId { get; private set; }

        /// <summary>Warning text for the status line ("" = none); a warning does not stop the series.</summary>
        public string Warning { get; private set; } = string.Empty;

        /// <summary>T: acquisition time of one spectrum, seconds (without the accumulation of a no-clear series).</summary>
        public int PresetSeconds
        {
            get { return _presetSeconds; }
        }

        public bool IsActive
        {
            get { return _state == SeriesState.Acquiring || _state == SeriesState.Saving || _state == SeriesState.Delay || _state == SeriesState.Clearing || _state == SeriesState.Waiting; }
        }

        public bool StopNeedsQuestion
        {
            get { return _state == SeriesState.Acquiring; }
        }

        public int BaseSeconds
        {
            get { return _baseSeconds; }
            private set { _baseSeconds = value; }
        }

        public int SecondsToResume
        {
            get
            {
                if (_state != SeriesState.Delay) return 0;
                TimeSpan remaining = _delayEnd - _host.Now;
                if (remaining.TotalSeconds <= 0) return 0;
                return (int)Math.Ceiling(remaining.TotalSeconds);
            }
        }

        public int PresetFor(int k)
        {
            if (Settings.ClearBetween) return _presetSeconds;
            long total = (long)_baseSeconds + (long)k * _presetSeconds;
            if (total > int.MaxValue) return int.MaxValue;
            if (total < 0) return 0;
            return (int)total;
        }

        public void Begin(int baseSeconds)
        {
            if (IsActive) return;
            if (_state != SeriesState.Idle && _state != SeriesState.Done && _state != SeriesState.PausedError) return;

            _files.Clear();
            LastError = string.Empty;
            BaseSeconds = baseSeconds;
            Index = 1;
            Warning = string.Empty;
            AcquisitionId++;
            State = SeriesState.Acquiring;
        }

        public void OnSpectrumComplete()
        {
            if (_state != SeriesState.Acquiring) return;

            State = SeriesState.Saving;

            if (Settings.Count == 0)
            {
                long free = _host.FreeDiskBytes(Settings.Folder);
                if (free >= 0 && free < 1073741824L)
                {
                    // PLAN-becqmoni-series.md section 5 item 9: a WARNING, the series goes on (a real failure to write stops it below)
                    Warning = "Less than 1 GB free on the disk of the series folder";
                }
                else
                {
                    Warning = string.Empty;
                }
            }

            string path;
            try
            {
                path = SeriesSettings.BuildPath(Settings.Folder, Settings.Template, Settings.Prefix, Index, _host.Now, _host.FileExists, Settings.NoOverwrite);
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
                return;
            }

            string err;
            if (!_host.SaveSpectrum(path, out err))
            {
                Fail(err);
                return;
            }

            _files.Add(path);

            if ((Settings.Count > 0 && Index >= Settings.Count) || Index == int.MaxValue)
            {
                State = SeriesState.Done;
                _host.SeriesFinished();
                return;
            }

            if (Settings.DelaySeconds > 0)
            {
                State = SeriesState.Delay;
                DelayEnd = _host.Now.AddSeconds(Settings.DelaySeconds);
            }
            else
            {
                Proceed();
            }
        }

        public void Tick()
        {
            TickWaiting();
            if (_state == SeriesState.Delay && _host.Now >= _delayEnd)
            {
                Proceed();
            }
        }

        public void SkipDelay()
        {
            if (_state == SeriesState.Delay)
            {
                Proceed();
            }
        }

        public void UserStop(bool saveCurrent)
        {
            if (_state == SeriesState.Acquiring)
            {
                if (saveCurrent)
                {
                    string path;
                    try
                    {
                        path = SeriesSettings.BuildPath(Settings.Folder, Settings.Template, Settings.Prefix, Index, _host.Now, _host.FileExists, Settings.NoOverwrite);
                    }
                    catch (Exception ex)
                    {
                        Fail(ex.Message);
                        return;
                    }

                    string err;
                    if (!_host.SaveSpectrum(path, out err))
                    {
                        Fail(err);
                        return;
                    }

                    _files.Add(path);
                }
                State = SeriesState.Idle;
            }
            else if (_state == SeriesState.Waiting)
            {
                State = SeriesState.Idle;
            }
            else if (_state == SeriesState.Delay || _state == SeriesState.Clearing || _state == SeriesState.Saving)
            {
                State = SeriesState.Idle;
            }
            else if (_state == SeriesState.PausedError)
            {
                State = SeriesState.Idle;
            }
        }

        public void OnDeviceLost(string reason)
        {
            if (_state == SeriesState.Acquiring || _state == SeriesState.Delay || _state == SeriesState.Clearing)
            {
                Fail(reason);
            }
        }

        private void Proceed()
        {
            State = SeriesState.Clearing;

            if (Settings.ClearBetween)
            {
                string clearErr;
                if (!_host.ClearSpectrum(out clearErr))
                {
                    Fail(clearErr);
                    return;
                }
            }

            string err;
            if (!_host.StartAcquisition(PresetFor(Index + 1), out err))
            {
                Fail(err);
                return;
            }

            Index++;
            AcquisitionId++;
            State = SeriesState.Acquiring;
        }

        private void Fail(string message)
        {
            LastError = message;
            State = SeriesState.PausedError;
        }
    }
}
