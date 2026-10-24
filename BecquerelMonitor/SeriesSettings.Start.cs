using System;

namespace BecquerelMonitor
{
    /// <summary>
    /// Provides start timing configuration for a measurement series.
    /// </summary>
    public partial class SeriesSettings
    {
        /// <summary>
        /// Determines how the start time is calculated.
        /// 0 = start at once (default); 1 = at the wall-clock time StartAt; 2 = StartInSeconds seconds after the moment Start is pressed.
        /// </summary>
        public int StartKind { get; set; }

        /// <summary>
        /// The specific wall-clock time to start the series. Used when StartKind is 1.
        /// </summary>
        public DateTime StartAt { get; set; }

        /// <summary>
        /// The number of seconds to wait before starting. Used when StartKind is 2.
        /// </summary>
        public int StartInSeconds { get; set; }

        /// <summary>
        /// Calculates the number of seconds to wait from the given time until the series should start.
        /// </summary>
        /// <param name="now">The current time.</param>
        /// <returns>The number of seconds to wait. Negative if the start time is in the past.</returns>
        public long WaitSecondsFrom(DateTime now)
        {
            if (StartKind == 2)
            {
                return StartInSeconds;
            }

            if (StartKind == 1)
            {
                TimeSpan span = StartAt - now;
                return (long)Math.Ceiling(span.TotalSeconds);
            }

            return 0;
        }

        /// <summary>
        /// Calculates the expected end time of the series.
        /// </summary>
        /// <param name="now">The current time.</param>
        /// <param name="presetSeconds">The preset duration in seconds.</param>
        /// <returns>The expected end time, or null if the series is endless.</returns>
        public DateTime? ExpectedEnd(DateTime now, int presetSeconds)
        {
            long total = TotalSeconds(presetSeconds);
            if (total < 0)
            {
                return null;
            }

            DateTime start;
            if (StartKind == 1)
            {
                start = StartAt;
            }
            else if (StartKind == 2)
            {
                start = now.AddSeconds(StartInSeconds);
            }
            else
            {
                start = now;
            }

            return start.AddSeconds(total);
        }
    }
}
