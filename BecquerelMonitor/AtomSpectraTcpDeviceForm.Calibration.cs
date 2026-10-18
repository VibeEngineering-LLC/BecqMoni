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
        internal Func<int> profileChannels = null;            // the channels of the profile edited (the test harness has no DeviceConfigForm)

        // calib_set=false or a bad answer leaves the page as it is and says why in the output window.
        private void ApplyBoardCalibration(AtomSpectraTcpCalibrationResult r)
        {
            string text;

            if (r != null && r.Failure == AtomSpectraTcpCalibrationFailure.None && r.Coefficients != null)
            {
                Action<double[]> target = calibrationTarget ?? (deviceConfigForm == null ? null : new Action<double[]>(deviceConfigForm.ApplyAtomSpectraCalibration));
                // F5 finding 6: the board polynomial is for 8192 channels; the page of a profile with fewer channels gets the same
                // rescale as the document at Start (no profile known: as it is; no whole fold: as it is, with a note in the log)
                double[] page = (double[])r.Coefficients.Clone();
                int n = profileChannels != null ? profileChannels() : (deviceConfigForm != null && deviceConfigForm.ActiveDeviceConfig != null ? deviceConfigForm.ActiveDeviceConfig.NumberOfChannels : 0);
                double[] folded = n > 0 ? AtomSpectraRestCalibration.ForChannels(page, 0, n) : page;
                if (folded != null) page = folded; else AtomSpectraTcpLog.Warn("Cal", "board calibration is for 8192 channels, the profile has " + n + ": given to the page as it is");
                if (target != null) target(page);
                text = string.Format(CultureInfo.CurrentCulture, Resources.AtomSpectraCalibToPage, string.Join("; ", page.Select(c => c.ToString("R", CultureInfo.InvariantCulture))));
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
