// 2026-10-16 (operator): the board calibration goes to the Energy Calibration page after "Test connection (REST)" and "Refresh".
using BecquerelMonitor.Properties;
using System;
using System.Globalization;
using System.Linq;

namespace BecquerelMonitor
{
    public partial class AtomSpectraTcpDeviceForm
    {
        // The Energy Calibration page of DeviceConfigForm; the test harness puts its own here.
        internal Action<double[]> calibrationTarget = null;   // set by reflection in the test harness only

        // calib_set=false or a bad answer leaves the page as it is and says why in the output window.
        private void ApplyBoardCalibration(AtomSpectraTcpCalibrationResult r)
        {
            string text;

            if (r != null && r.Failure == AtomSpectraTcpCalibrationFailure.None && r.Coefficients != null)
            {
                Action<double[]> target = calibrationTarget ?? (deviceConfigForm == null ? null : new Action<double[]>(deviceConfigForm.ApplyAtomSpectraCalibration));
                if (target != null) target((double[])r.Coefficients.Clone());
                text = string.Format(CultureInfo.CurrentCulture, Resources.AtomSpectraCalibToPage, string.Join("; ", r.Coefficients.Select(c => c.ToString("R", CultureInfo.InvariantCulture))));
            }
            else if (r != null && r.Error == "calib_set is false")
            {
                text = Resources.AtomSpectraCalibNotSet;
            }
            else
            {
                text = string.Format(CultureInfo.CurrentCulture, Resources.AtomSpectraCalibNotRead, r == null ? "?" : (r.Error ?? r.Failure.ToString()));
            }

            AtomSpectraTcpLog.Info("Cal", text);
            textOutput.AppendText(text + Environment.NewLine);
        }
    }
}
