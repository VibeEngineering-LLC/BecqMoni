using System;
using System.Collections.Generic;

namespace BecquerelMonitor
{
    /// <summary>
    /// Represents a measurement series with waiting capabilities.
    /// </summary>
    public sealed partial class MeasurementSeries
    {
        private long _waitEndMono;

        /// <summary>
        /// Gets the wall-clock start target time. This is for display only.
        /// </summary>
        public DateTime StartAt { get; private set; }

        /// <summary>
        /// Gets the number of seconds remaining until the series starts.
        /// Returns 0 if not in the Waiting state or if the deadline has passed.
        /// </summary>
        public int SecondsToStart
        {
            get
            {
                if (_state != SeriesState.Waiting)
                {
                    return 0;
                }

                long left = _waitEndMono - _host.MonotonicMs;
                if (left <= 0)
                {
                    return 0;
                }

                return (int)((left + 999) / 1000);
            }
        }

        /// <summary>
        /// Begins the waiting period for the series.
        /// </summary>
        /// <param name="baseSeconds">The base seconds for the series.</param>
        /// <param name="waitSeconds">The number of seconds to wait.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if waitSeconds is less than or equal to 0.</exception>
        public void BeginWaiting(int baseSeconds, int waitSeconds)
        {
            if (waitSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException("waitSeconds");
            }

            if (IsActive)
            {
                return;
            }

            if (_state != SeriesState.Idle && _state != SeriesState.Done && _state != SeriesState.PausedError)
            {
                return;
            }

            _files.Clear();
            LastError = "";
            Warning = "";
            BaseSeconds = baseSeconds;
            Index = 1;
            _waitEndMono = _host.MonotonicMs + waitSeconds * 1000L;
            StartAt = _host.Now.AddSeconds(waitSeconds);
            State = SeriesState.Waiting;
        }

        /// <summary>
        /// Handles the waiting state tick. Called from the main Tick method.
        /// </summary>
        private void TickWaiting()
        {
            if (_state == SeriesState.Waiting && _host.MonotonicMs >= _waitEndMono)
            {
                StartFirst();
            }
        }

        /// <summary>
        /// Starts the first acquisition after the waiting period.
        /// </summary>
        private void StartFirst()
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
            if (!_host.StartAcquisition(PresetFor(1), out err))
            {
                Fail(err);
                return;
            }

            Index = 1;
            AcquisitionId++;
            State = SeriesState.Acquiring;
        }
    }
}
