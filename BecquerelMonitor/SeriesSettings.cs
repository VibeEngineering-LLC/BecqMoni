using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    /// <summary>
    /// Settings for a measurement series (N spectra in a row, each saved to its own file)
    /// plus pure helpers for file names and totals.
    /// </summary>
    [Serializable]
    public partial class SeriesSettings
    {
        /// <summary>Gets or sets a value indicating whether the series is enabled.</summary>
        public bool Enabled { get; set; }

        /// <summary>Gets or sets the number of spectra. 0 means endless; valid range is 0..9999.</summary>
        public int Count { get; set; } = 5;

        /// <summary>Gets or sets the pause between spectra in seconds. Must be >= 0.</summary>
        public int DelaySeconds { get; set; }

        /// <summary>Gets or sets a value indicating whether to clear the spectrum before each next one.</summary>
        public bool ClearBetween { get; set; } = true;

        /// <summary>Gets or sets the folder. Empty string means the folder of the current document.</summary>
        public string Folder { get; set; } = "";

        /// <summary>Gets or sets the file name template.</summary>
        public string Template { get; set; } = "{prefix}_{n:000}";

        /// <summary>Gets or sets the prefix. Empty string means the caller substitutes the document name.</summary>
        public string Prefix { get; set; } = "";

        /// <summary>Gets or sets a value indicating whether an existing file is never overwritten.</summary>
        public bool NoOverwrite { get; set; } = true;

        /// <summary>Creates a new instance of the <see cref="SeriesSettings"/> class.</summary>
        public SeriesSettings()
        {
        }

        /// <summary>The time base of the acquisition time of each spectrum: 0 = real (wall-clock) time, the only one now. Kept in the document so that another base can be added later without breaking old documents (absent = 0).</summary>
        public int TimeBase { get; set; }

        /// <summary>Creates a memberwise copy of this instance.</summary>
        /// <returns>A new instance with the same property values.</returns>
        public SeriesSettings Clone()
        {
            return new SeriesSettings
            {
                Enabled = this.Enabled,
                Count = this.Count,
                DelaySeconds = this.DelaySeconds,
                ClearBetween = this.ClearBetween,
                Folder = this.Folder,
                Template = this.Template,
                Prefix = this.Prefix,
                NoOverwrite = this.NoOverwrite,
                StartKind = this.StartKind,
                StartAt = this.StartAt,
                StartInSeconds = this.StartInSeconds,
                TimeBase = this.TimeBase
            };
        }

        /// <summary>Normalizes the property values to valid ranges and defaults.</summary>
        public void Normalize()
        {
            if (this.Count < 0)
            {
                this.Count = 0;
            }
            else if (this.Count > 9999)
            {
                this.Count = 9999;
            }

            if (this.DelaySeconds < 0)
            {
                this.DelaySeconds = 0;
            }
            else if (this.DelaySeconds > 359999)
            {
                this.DelaySeconds = 359999;
            }

            if (this.StartKind < 0 || this.StartKind > 2)
            {
                this.StartKind = 0;
            }
            this.StartInSeconds = Math.Max(0, Math.Min(359999, this.StartInSeconds));

            if (string.IsNullOrWhiteSpace(this.Template))
            {
                this.Template = "{prefix}_{n:000}";
            }

            if (this.Folder == null)
            {
                this.Folder = "";
            }

            if (this.Prefix == null)
            {
                this.Prefix = "";
            }
        }

        /// <summary>Calculates the total duration in seconds for the series.</summary>
        /// <param name="presetSeconds">The duration of a single spectrum in seconds.</param>
        /// <returns>The total duration in seconds, or -1 if the series is endless.</returns>
        public long TotalSeconds(int presetSeconds)
        {
            if (this.Count == 0)
            {
                return -1;
            }

            long n = this.Count;
            long t = presetSeconds;
            long d = this.DelaySeconds;

            return (n * t) + ((n - 1) * d);
        }

        /// <summary>Formats a duration in seconds as "hh:mm:ss".</summary>
        /// <param name="seconds">The duration in seconds.</param>
        /// <returns>The formatted duration string.</returns>
        public static string FormatHms(long seconds)
        {
            if (seconds < 0)
            {
                return "00:00:00";
            }

            long h = seconds / 3600;
            long m = (seconds % 3600) / 60;
            long s = seconds % 60;

            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", h, m, s);
        }

        /// <summary>Builds a file name (without extension) from a template.</summary>
        /// <param name="template">The template string with tokens.</param>
        /// <param name="prefix">The prefix to substitute for {prefix}.</param>
        /// <param name="n">The spectrum number.</param>
        /// <param name="now">The current date and time.</param>
        /// <returns>The sanitized file name.</returns>
        public static string BuildName(string template, string prefix, int n, DateTime now)
        {
            if (template == null)
            {
                template = "";
            }

            if (prefix == null)
            {
                prefix = "";
            }

            string result = Regex.Replace(template, @"\{([^}]*)\}", new MatchEvaluator(ReplaceToken));

            char[] invalidChars = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder(result.Length);
            foreach (char c in result)
            {
                if (c == '/' || c == '\\' || Array.IndexOf(invalidChars, c) >= 0)
                {
                    sb.Append('_');
                }
                else
                {
                    sb.Append(c);
                }
            }

            string name = sb.ToString().TrimEnd('.', ' ');

            name = ShortenName(name, MaxNameLength, n);
            if (string.IsNullOrEmpty(name))
            {
                name = "spectrum_" + n;
            }

            // Windows reserved device names (CON, PRN, AUX, NUL, COM1..9, LPT1..9) cannot be file names, with any extension
            int dot = name.IndexOf('.');
            if (IsReservedDeviceName((dot >= 0 ? name.Substring(0, dot) : name).TrimEnd(' ')))
            {
                name = "_" + name;
            }

            return name;

            string ReplaceToken(Match match)
            {
                string token = match.Groups[1].Value;

                if (string.Equals(token, "prefix", StringComparison.OrdinalIgnoreCase))
                {
                    return prefix;
                }

                if (string.Equals(token, "n", StringComparison.OrdinalIgnoreCase))
                {
                    return n.ToString(CultureInfo.InvariantCulture);
                }

                if (token.StartsWith("n:", StringComparison.OrdinalIgnoreCase))
                {
                    string widthPart = token.Substring(2);
                    int width = 0;
                    foreach (char c in widthPart)
                    {
                        if (c == '0')
                        {
                            width++;
                        }
                        else
                        {
                            break;
                        }
                    }

                    return n.ToString("D" + width, CultureInfo.InvariantCulture);
                }

                if (string.Equals(token, "date", StringComparison.OrdinalIgnoreCase))
                {
                    return now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }

                if (string.Equals(token, "time", StringComparison.OrdinalIgnoreCase))
                {
                    return now.ToString("HH-mm-ss", CultureInfo.InvariantCulture);
                }

                return match.Value;
            }
        }

        /// <summary>
        /// Does the name template tell the spectra of the series apart? 0 = yes (or one spectrum only); 1 = no, but a taken name
        /// gets a suffix _1, _2 (warning); 2 = no and files are overwritten: every spectrum would replace the previous one (refusal).
        /// Only {n}, {n:00} and {time} differ between spectra; {prefix} and {date} do not.
        /// </summary>
        public static int TemplateProblem(string template, int count, bool noOverwrite)
        {
            if (count == 1)
            {
                return 0;
            }
            bool tells = template != null && Regex.IsMatch(template, @"\{(n|n:[^}]*|time)\}", RegexOptions.IgnoreCase);
            return tells ? 0 : (noOverwrite ? 1 : 2);
        }

        /// <summary>Longest file name (without ".xml") a series writes; a longer one is shortened and ends with "_n".</summary>
        public const int MaxNameLength = 200;

        /// <summary>Longest series folder accepted; with it a file name of at least 37 characters still fits into the path limit.</summary>
        public const int MaxFolderLength = 200;

        private const int MaxPathLength = 250;

        /// <summary>true: the folder path is too long for the series (the path of a file would pass the Windows limit).</summary>
        public static bool FolderTooLong(string folder)
        {
            return folder != null && folder.Length > MaxFolderLength;
        }

        private static string ShortenName(string name, int max, int n)
        {
            if (name.Length <= max)
            {
                return name;
            }
            string tail = "_" + n.ToString(CultureInfo.InvariantCulture);   // the number survives: names of different spectra stay different
            return name.Substring(0, Math.Max(0, max - tail.Length)) + tail;
        }
        private static bool IsReservedDeviceName(string stem)
        {
            string s = stem.ToUpperInvariant();
            if (s == "CON" || s == "PRN" || s == "AUX" || s == "NUL")
            {
                return true;
            }
            return s.Length == 4 && (s.StartsWith("COM") || s.StartsWith("LPT")) && s[3] >= '1' && s[3] <= '9';
        }

        /// <summary>Builds the full file path for a spectrum, handling no-overwrite logic.</summary>
        /// <param name="folder">The folder path.</param>
        /// <param name="template">The file name template.</param>
        /// <param name="prefix">The prefix.</param>
        /// <param name="n">The spectrum number.</param>
        /// <param name="now">The current date and time.</param>
        /// <param name="exists">A function that checks if a file exists.</param>
        /// <param name="noOverwrite">Whether to avoid overwriting existing files.</param>
        /// <returns>The full file path.</returns>
        /// <exception cref="ArgumentException">If folder is null or empty.</exception>
        /// <exception cref="InvalidOperationException">If no free file name could be found after 100000 attempts.</exception>
        public static string BuildPath(string folder, string template, string prefix, int n, DateTime now, Func<string, bool> exists, bool noOverwrite)
        {
            if (string.IsNullOrEmpty(folder))
            {
                throw new ArgumentException("Folder must not be null or empty.", "folder");
            }

            string name = BuildName(template, prefix, n, now);
            // the whole path must stay under the Windows limit: ".xml" and a "_NNNNNN" suffix are reserved
            int room = MaxPathLength - 8 - (folder.Length + 1) - 4;
            if (room < 16)
            {
                throw new InvalidOperationException("The series folder path is too long for a file name: " + folder.Length + " characters.");
            }
            name = ShortenName(name, room, n);
            string plainPath = Path.Combine(folder, name + ".xml");

            if (!noOverwrite || exists == null || !exists(plainPath))
            {
                return plainPath;
            }

            for (int i = 1; i <= 100000; i++)
            {
                string candidate = Path.Combine(folder, name + "_" + i + ".xml");
                if (!exists(candidate))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException("Could not find a free file name after 100000 attempts.");
        }
    }
}
