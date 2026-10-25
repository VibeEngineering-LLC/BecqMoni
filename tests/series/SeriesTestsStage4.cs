using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using BecquerelMonitor;

namespace SeriesTestRun
{
    public static partial class Program
    {
        public static (string name, Action body)[] Stage4Tests()
        {
            return new (string name, Action body)[]
            {
                ("Z17_long_names_are_bounded", Z17_long_names_are_bounded)
            };
        }

        private static void Z17_long_names_are_bounded()
        {
            DateTime now = new DateTime(2026, 10, 25, 12, 0, 0);

            // 1. A template of 300 characters
            string longT = new string('x', 300) + "{n}";
            string name7 = SeriesSettings.BuildName(longT, "sp", 7, now);
            True(name7.Length <= SeriesSettings.MaxNameLength, "name7 length");
            True(name7.EndsWith("7"), "name7 ends with 7");

            string name8 = SeriesSettings.BuildName(longT, "sp", 8, now);
            True(name7 != name8, "name7 != name8");

            string name12345 = SeriesSettings.BuildName(longT, "sp", 12345, now);
            string name12346 = SeriesSettings.BuildName(longT, "sp", 12346, now);
            True(name12345 != name12346, "name12345 != name12346");

            // 2. A Cyrillic template with dots and spaces
            string cyr = string.Concat(Enumerable.Repeat("Спектр.образец №", 30));
            string cyrName = SeriesSettings.BuildName(cyr + "{n:000}", "п", 5, now);
            True(cyrName.Length <= SeriesSettings.MaxNameLength, "cyrName length");
            True(cyrName.EndsWith("5"), "cyrName ends with 5");

            char[] invalidChars = Path.GetInvalidFileNameChars();
            True(!cyrName.Any(c => invalidChars.Contains(c)), "cyrName has no invalid chars");
            True(!cyrName.EndsWith(".") && !cyrName.EndsWith(" "), "cyrName does not end with . or space");

            // 3. A short template stays unchanged
            Eq("sp_005", SeriesSettings.BuildName("{prefix}_{n:000}", "sp", 5, now), "short template 1");
            Eq("Спектр 3", SeriesSettings.BuildName("Спектр {n}", "", 3, now), "short template 2");

            // 4. Full paths
            string folder = @"C:\" + new string('d', 150);
            string path1 = SeriesSettings.BuildPath(folder, longT, "sp", 12345, now, null, true);
            True(path1.Length <= 250, "path1 length");

            Func<string, bool> exists = p => p == path1;
            string path2 = SeriesSettings.BuildPath(folder, longT, "sp", 12345, now, exists, true);
            True(path1 != path2, "path1 != path2");
            True(path2.Length <= 259, "path2 length");
            True(path2.EndsWith("_1.xml"), "path2 ends with _1.xml");

            // 5. A folder that leaves no room
            string huge = @"C:\" + new string('d', 240);
            try
            {
                SeriesSettings.BuildPath(huge, longT, "sp", 1, now, null, true);
                throw new Exception("Expected InvalidOperationException");
            }
            catch (InvalidOperationException ex)
            {
                Contains(ex.Message.ToLowerInvariant(), "too long", "exception message");
            }
            catch (Exception ex)
            {
                throw new Exception("Wrong exception type: " + ex.GetType().Name);
            }

            // 6. FolderTooLong checks
            Eq(false, SeriesSettings.FolderTooLong(@"C:\" + new string('d', 197)), "FolderTooLong 200");
            Eq(true, SeriesSettings.FolderTooLong(@"C:\" + new string('d', 198)), "FolderTooLong 201");
            Eq(false, SeriesSettings.FolderTooLong(null), "FolderTooLong null");
            Eq(false, SeriesSettings.FolderTooLong(""), "FolderTooLong empty");
        }
    }
}
