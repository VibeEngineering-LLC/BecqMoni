using System;

namespace BecquerelMonitor
{
    /// <summary>
    /// Device types a measurement series is offered for: a WHITE list (any other type, also one added later, has no series).
    /// AtomSpectraVCP covers AtomSpectra, Nano and GS (one device type in the program, no model field);
    /// AtomSpectraTCP is the ESP32 board (still refused by the panel until the next step).
    /// </summary>
    internal static class SeriesDevices
    {
        private static readonly string[] allowed = { "AtomSpectraVCP", AtomSpectraTcpDeviceConfig.DeviceTypeId };

        /// <summary>true: the series is offered for this type (ordinal comparison; null and empty are not allowed).</summary>
        public static bool IsAllowed(string deviceType)
        {
            return !string.IsNullOrEmpty(deviceType) && Array.IndexOf(allowed, deviceType) >= 0;
        }
    }
}
