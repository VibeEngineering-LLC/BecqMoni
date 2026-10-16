using System;
using System.Collections.Generic;
using System.Globalization;

namespace BecquerelMonitor
{
    public static class AtomSpectraRestCalibration
    {
        public static AtomSpectraTcpCalibrationResult Read(AtomSpectraRestClient client, int deadlineMs)
        {
            AtomSpectraRestResponse r = client.Get("/api/device", deadlineMs);
            AtomSpectraTcpCalibrationResult result;

            if (!r.Ok)
            {
                AtomSpectraTcpLog.Warn("Cal", "REST calibration failed: " + r.Error);
                result = new AtomSpectraTcpCalibrationResult(
                    AtomSpectraTcpCalibrationFailure.Connect,
                    r.Error,
                    null,
                    r.Body);
            }
            else
            {
                result = ParseDevice(r.Body);
            }

            string coeffStr = "";
            if (result.Coefficients != null)
            {
                List<string> parts = new List<string>();
                foreach (double c in result.Coefficients)
                {
                    parts.Add(c.ToString("R", CultureInfo.InvariantCulture));
                }
                coeffStr = string.Join("; ", parts);
            }
            else
            {
                coeffStr = result.Error ?? "";
            }

            AtomSpectraTcpLog.Info("Cal", "REST calibration: " + result.Failure + " " + coeffStr);
            return result;
        }

        // 2026-10-16: the "calib" slots of a spectrum snapshot (null = calib_set false; NaN = an empty slot), the rules of ParseDevice
        public static AtomSpectraTcpCalibrationResult FromSlots(double[] slots)
        {
            if (slots == null)
                return new AtomSpectraTcpCalibrationResult(AtomSpectraTcpCalibrationFailure.EmptyCoefficients, "calib_set is false", null, null);
            List<double> coeffs = new List<double>();
            bool emptySeen = false;
            for (int i = 0; i < Math.Min(5, slots.Length); i++)
            {
                if (double.IsNaN(slots[i])) { emptySeen = true; continue; }
                if (emptySeen)
                    return new AtomSpectraTcpCalibrationResult(AtomSpectraTcpCalibrationFailure.GapInCoefficients, "Number after empty slot at index " + i, null, null);
                if (double.IsInfinity(slots[i]))
                    return new AtomSpectraTcpCalibrationResult(AtomSpectraTcpCalibrationFailure.InvalidNumber, "Infinity at index " + i, null, null);
                coeffs.Add(slots[i]);
            }
            while (coeffs.Count > 0 && coeffs[coeffs.Count - 1] == 0.0) coeffs.RemoveAt(coeffs.Count - 1);
            return coeffs.Count < 2
                ? new AtomSpectraTcpCalibrationResult(AtomSpectraTcpCalibrationFailure.EmptyCoefficients, "Fewer than 2 coefficients", null, null)
                : new AtomSpectraTcpCalibrationResult(AtomSpectraTcpCalibrationFailure.None, null, coeffs.ToArray(), null);
        }

        public static AtomSpectraTcpCalibrationResult ParseDevice(string json)
        {
            try
            {
                object root = AtomSpectraJson.Parse(json);
                if (!(root is Dictionary<string, object>))
                {
                    return new AtomSpectraTcpCalibrationResult(
                        AtomSpectraTcpCalibrationFailure.Malformed,
                        "Root is not an object",
                        null,
                        json);
                }

                Dictionary<string, object> obj = (Dictionary<string, object>)root;
                // /api/device carries "calibration", /api/spectrum.json carries "calib" (5 slots), same coefficients
                List<object> calibArray = AtomSpectraJson.GetArray(obj, "calibration") ?? AtomSpectraJson.GetArray(obj, "calib");
                if (calibArray == null)
                {
                    return new AtomSpectraTcpCalibrationResult(
                        AtomSpectraTcpCalibrationFailure.Malformed,
                        "Missing calibration array",
                        null,
                        json);
                }

                bool calibSet = AtomSpectraJson.GetBool(obj, "calib_set", true);
                if (!calibSet)
                {
                    return new AtomSpectraTcpCalibrationResult(
                        AtomSpectraTcpCalibrationFailure.EmptyCoefficients,
                        "calib_set is false",
                        null,
                        json);
                }

                List<double> coeffs = new List<double>();
                bool emptySeen = false;

                int maxSlots = Math.Min(5, calibArray.Count);
                for (int i = 0; i < maxSlots; i++)
                {
                    object slot = calibArray[i];

                    if (slot == null)
                    {
                        emptySeen = true;
                        continue;
                    }

                    if (!(slot is double))
                    {
                        return new AtomSpectraTcpCalibrationResult(
                            AtomSpectraTcpCalibrationFailure.Malformed,
                            "Slot " + i + " is not a number",
                            null,
                            json);
                    }

                    double val = (double)slot;

                    if (emptySeen)
                    {
                        return new AtomSpectraTcpCalibrationResult(
                            AtomSpectraTcpCalibrationFailure.GapInCoefficients,
                            "Number after empty slot at index " + i,
                            null,
                            json);
                    }

                    if (double.IsNaN(val) || double.IsInfinity(val))
                    {
                        return new AtomSpectraTcpCalibrationResult(
                            AtomSpectraTcpCalibrationFailure.InvalidNumber,
                            "NaN or Infinity at index " + i,
                            null,
                            json);
                    }

                    coeffs.Add(val);
                }

                while (coeffs.Count > 0 && coeffs[coeffs.Count - 1] == 0.0)
                {
                    coeffs.RemoveAt(coeffs.Count - 1);
                }

                if (coeffs.Count < 2)
                {
                    return new AtomSpectraTcpCalibrationResult(
                        AtomSpectraTcpCalibrationFailure.EmptyCoefficients,
                        "Fewer than 2 coefficients",
                        null,
                        json);
                }

                return new AtomSpectraTcpCalibrationResult(
                    AtomSpectraTcpCalibrationFailure.None,
                    null,
                    coeffs.ToArray(),
                    json);
            }
            catch (Exception ex)
            {
                return new AtomSpectraTcpCalibrationResult(
                    AtomSpectraTcpCalibrationFailure.Malformed,
                    ex.Message,
                    null,
                    json);
            }
        }
    }
}
