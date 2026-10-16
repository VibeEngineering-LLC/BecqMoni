// 2026-10-16 (operator): at Start the board calibration goes to the document and to the device configuration (REST: the first snapshot after Start).
using System;
using System.Globalization;
using System.Linq;

namespace BecquerelMonitor
{
    public partial class AtomSpectraTcpDeviceController
    {
        // Set when a REST Start succeeds; the first snapshot after it carries the board's "calib".
        private bool boardCalibrationPending;

        // TCP: -cal through the open session at Start (the bridge serves one client), then the same rules as REST.
        private void RefreshCalibrationFromDevice(ResultData r)
        {
            AtomSpectraTcpSession tcp = session as AtomSpectraTcpSession;
            if (tcp == null || r == null) return;
            string answer = tcp.Query("-cal", InfoTimeoutMs);
            AtomSpectraTcpCalibrationResult cal = answer == null ? null : AtomSpectraTcpCalibrationReader.Parse(answer);
            double[] slots = cal != null && cal.Failure == AtomSpectraTcpCalibrationFailure.None ? cal.Coefficients : null;
            AtomSpectraTcpLog.Info("Cal", "-cal at Start: " + (cal == null ? "no answer" : cal.Failure.ToString()) + "; "
                + ApplyBoardCalibration(r.EnergySpectrum, r.DeviceConfig, slots, c => DeviceConfigManager.GetInstance().SaveConfig(c)));
        }

        // slots: the snapshot's "calib" (null when calib_set is false, NaN for an empty slot). Returns what was done, for the log.
        public static string ApplyBoardCalibration(EnergySpectrum spectrum, DeviceConfigInfo config, double[] slots, Func<DeviceConfigInfo, bool> save)
        {
            AtomSpectraTcpCalibrationResult cal = AtomSpectraRestCalibration.FromSlots(slots);

            if (cal.Failure != AtomSpectraTcpCalibrationFailure.None)
            {
                return "board calibration not taken (" + (cal.Error ?? cal.Failure.ToString()) + "): the document and the device configuration keep their own";
            }

            var board = new PolynomialEnergyCalibration
            {
                PolynomialOrder = cal.Coefficients.Length - 1,
                Coefficients = (double[])cal.Coefficients.Clone()
            };

            if (config != null && !board.CheckCalibration(config.NumberOfChannels))
            {
                return "board calibration not taken: it fails CheckCalibration for " + config.NumberOfChannels + " channels";
            }

            string text = string.Join("; ", cal.Coefficients.Select(c => c.ToString("R", CultureInfo.InvariantCulture)));
            string done = "";

            if (spectrum != null && !SameCalibration(spectrum.EnergyCalibration, cal.Coefficients))
            {
                spectrum.EnergyCalibration = board.Clone();
                done += " document";
            }

            if (config != null && !SameCalibration(config.EnergyCalibration, cal.Coefficients))
            {
                config.EnergyCalibration = board.Clone();
                bool saved = save != null && save(config);
                done += saved ? " configuration (saved)" : " configuration (NOT saved)";
            }

            return done.Length == 0
                ? "board calibration " + text + " is already in the document and the configuration"
                : "board calibration " + text + " ->" + done;
        }

        private static bool SameCalibration(EnergyCalibration c, double[] coefficients)
        {
            var p = c as PolynomialEnergyCalibration;
            if (p == null || p.Coefficients == null) return false;

            var mine = p.Coefficients.ToList();
            while (mine.Count > 0 && mine[mine.Count - 1] == 0.0)
            {
                mine.RemoveAt(mine.Count - 1);
            }

            return mine.SequenceEqual(coefficients);
        }
    }
}
