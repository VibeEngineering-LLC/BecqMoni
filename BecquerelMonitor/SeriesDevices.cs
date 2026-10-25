using System;

namespace BecquerelMonitor
{
    /// <summary>
    /// Device types a measurement series is offered for: a WHITE list (any other type, also one added later, has no series).
    /// AtomSpectraVCP covers AtomSpectra, Nano and GS (one device type in the program, no model field);
    /// AtomSpectraTCP is the ESP32 board (TCP and REST).
    /// </summary>
    internal static class SeriesDevices
    {
        private static readonly string[] allowed = { "AtomSpectraVCP", AtomSpectraTcpDeviceConfig.DeviceTypeId };

        /// <summary>true: the series is offered for this type (ordinal comparison; null and empty are not allowed).</summary>
        public static bool IsAllowed(string deviceType)
        {
            return !string.IsNullOrEmpty(deviceType) && Array.IndexOf(allowed, deviceType) >= 0;
        }

        /// <summary>
        /// REST board in the observe-only mode: the program never controls it, so no series.
        /// </summary>
        public static bool IsObserveOnly(ResultData rd)
        {
            AtomSpectraTcpDeviceConfig c = rd != null ? AtomSpectraTcpDeviceController.ConfigOf(rd) : null;
            return c != null && c.Transport == AtomSpectraTransport.Rest && c.RestObserveOnly;
        }

        /// <summary>
        /// Returns refusal text if the series is not offered, otherwise null.
        /// </summary>
        public static string RefusalText(ResultData rd)
        {
            string type = rd != null && rd.DeviceConfig != null ? rd.DeviceConfig.DeviceType : null;
            if (!IsAllowed(type))
            {
                return SeriesText.DeviceNotSupported;
            }
            if (IsObserveOnly(rd))
            {
                return SeriesText.ObserveOnlyUnavailable;
            }
            return null;
        }

        /// <summary>
        /// Returns short refusal text if the series is not offered, otherwise null.
        /// </summary>
        public static string ShortText(ResultData rd)
        {
            string type = rd != null && rd.DeviceConfig != null ? rd.DeviceConfig.DeviceType : null;
            if (!IsAllowed(type))
            {
                return SeriesText.DeviceNotSupportedShort;
            }
            if (IsObserveOnly(rd))
            {
                return SeriesText.ObserveOnlyShort;
            }
            return null;
        }

        /// <summary>
        /// The reason the board refused the last Start made while a series was active; "" = none.
        /// </summary>
        public static string BoardRefusal(ResultData rd)
        {
            AtomSpectraTcpDeviceController c = rd != null && rd.MeasurementController != null
                ? rd.MeasurementController.DeviceController as AtomSpectraTcpDeviceController
                : null;
            return c != null && c.SeriesRefusal != null ? c.SeriesRefusal : "";
        }
    }
}
