using System;
using System.Globalization;
using System.Threading;

namespace BecquerelMonitor
{
    /// <summary>
    /// Provides localized user-visible strings for the measurement series feature.
    /// </summary>
    internal static class SeriesText
    {
        static bool Ru => Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "ru";
        static string T(string ru, string en) => Ru ? ru : en;

        public static string Series => T("Серия", "Series");
        public static string Setup => T("Настроить…", "Settings…");
        public static string SkipDelay => T("Пропустить паузу", "Skip pause");
        public static string FormTitle => T("Серия измерений", "Measurement series");
        public static string LblCount => T("Количество спектров (0 = бесконечно):", "Number of spectra (0 = endless):");
        public static string LblTime => T("Время набора каждого:", "Acquisition time of each:");
        public static string LblDelay => T("Пауза между спектрами:", "Pause between spectra:");
        public static string LblHours => T("ч", "h");
        public static string LblMinutes => T("мин", "min");
        public static string LblSeconds => T("с", "s");
        public static string ChkClear => T("Очищать спектр перед каждым", "Clear the spectrum before each one");
        public static string LblFolder => T("Папка:", "Folder:");
        public static string BtnBrowse => T("Обзор…", "Browse…");
        public static string LblTemplate => T("Шаблон имени ({prefix}, {n}, {n:000}, {date}, {time}):", "File name template ({prefix}, {n}, {n:000}, {date}, {time}):");
        public static string LblPrefix => T("Префикс:", "Prefix:");
        public static string ChkNoOverwrite => T("Не перезаписывать существующие файлы", "Never overwrite existing files");
        public static string LblExample => T("Пример имени файла:", "File name example:");
        public static string LblTotal => T("Общее время:", "Total time:");
        public static string LblEnd => T("Ожидаемое окончание:", "Expected end:");
        public static string Endless => T("бесконечно", "endless");
        public static string BtnOk => T("OK", "OK");
        public static string BtnCancel => T("Отмена", "Cancel");
        public static string ErrFolder => T("Укажите папку для файлов серии.", "Specify the folder for the series files.");
        public static string ErrTemplate => T("Шаблон имени не должен быть пустым.", "The file name template must not be empty.");
        public static string InvalidPreset => T("Для серии нужно время набора больше нуля.", "A series needs an acquisition time greater than zero.");
        public static string ClearFirst => T("Серия начнётся с очистки спектра документа. Продолжить?", "The series will start by clearing the spectrum of the document. Continue?");
        public static string StopQuestion => T("Остановить серию: сохранить текущий неполный спектр?", "Stop the series: save the current incomplete spectrum?");
        public static string TcpUnavailable => T("Серия для плат TCP/REST пока недоступна.", "A series is not yet available for TCP/REST boards.");
        public static string AttachBlocked => T("Идёт серия измерений: присоединять и отсоединять прибор нельзя. Остановите серию кнопкой «Стоп».", "A measurement series is running: the device cannot be attached or detached. Stop the series with the Stop button.");
        public static string CloseQuestion => T("Идёт серия измерений. Остановить серию и закрыть?", "A measurement series is running. Stop the series and close?");
        public static string DeviceLost => T("Прибор прервал набор.", "The device interrupted the acquisition.");
        public static string StartFailed => T("Не удалось начать набор.", "The acquisition could not be started.");
        public static string ClearFailed => T("Не удалось очистить спектр.", "The spectrum could not be cleared.");
        public static string Warning => T("Серия измерений", "Measurement series");
        public static string StartNow => T("Сразу", "Immediately");
        public static string StartAtTime => T("В указанное время", "At the given time");
        public static string StartIn => T("Через", "After");
        public static string LblStart => T("Начать:", "Start:");
        public static string StartPassed => T("Указанное время начала уже прошло. Стартовать сразу? («Нет» — отменить серию.)", "The given start time has already passed. Start at once? (\"No\" cancels the series.)");

        /// <summary>Start target for the status line: the time only for today, with the date otherwise.</summary>
        public static string StartAtText(DateTime t)
        {
            return t.Date == DateTime.Now.Date ? t.ToString("HH:mm", CultureInfo.InvariantCulture) : t.ToString("dd.MM HH:mm", CultureInfo.InvariantCulture);
        }

        public static string Summary(SeriesSettings s, int presetSeconds)
        {
            string count = s.Count == 0 ? "∞" : s.Count.ToString(CultureInfo.InvariantCulture);
            string mult = Ru ? "×" : "x";
            string pause = SeriesSettings.FormatHms(s.DelaySeconds);
            string total = s.TotalSeconds(presetSeconds) < 0 ? Endless : SeriesSettings.FormatHms(s.TotalSeconds(presetSeconds));
            string start = s.StartKind == 1 ? (Ru ? ", старт в " : ", start at ") + StartAtText(s.StartAt)
                : s.StartKind == 2 ? (Ru ? ", старт через " : ", start in ") + SeriesSettings.FormatHms(s.StartInSeconds) : "";
            return Ru
                ? $"{count} × {SeriesSettings.FormatHms(presetSeconds)}, пауза {pause}, всего {total}{start}"
                : $"{count} x {SeriesSettings.FormatHms(presetSeconds)}, pause {pause}, total {total}{start}";
        }

        public static string Status(MeasurementSeries ser)
        {
            if (ser == null || ser.State == SeriesState.Idle) return "";
            if (ser.State == SeriesState.Waiting)
            {
                string left = SeriesSettings.FormatHms(ser.SecondsToStart);
                return Ru ? $"Серия: старт в {StartAtText(ser.StartAt)}, осталось {left}" : $"Series: starts at {StartAtText(ser.StartAt)}, {left} left";
            }

            int k = ser.Index;
            int n = ser.Settings.Count;
            string countStr = n == 0 ? "∞" : n.ToString(CultureInfo.InvariantCulture);
            string of = Ru ? "из" : "of";
            string sep = "·";
            string warn = string.IsNullOrEmpty(ser.Warning) ? "" : " " + sep + " " + ser.Warning;

            switch (ser.State)
            {
                case SeriesState.Acquiring:
                case SeriesState.Saving:
                case SeriesState.Clearing:
                    return Ru
                        ? $"Серия: спектр {k} {of} {countStr} {sep} набор{warn}"
                        : $"Series: spectrum {k} {of} {countStr} {sep} acquiring{warn}";
                case SeriesState.Delay:
                    string resume = SeriesSettings.FormatHms(ser.SecondsToResume);
                    return Ru
                        ? $"Серия: спектр {k} {of} {countStr} {sep} пауза, до продолжения {resume}{warn}"
                        : $"Series: spectrum {k} {of} {countStr} {sep} pause, {resume} until it goes on{warn}";
                case SeriesState.Done:
                    return Ru
                        ? $"Серия завершена: файлов {ser.Files.Count}"
                        : $"Series finished: {ser.Files.Count} file(s)";
                case SeriesState.PausedError:
                    return Ru
                        ? $"Серия остановлена: {ser.LastError}"
                        : $"Series stopped: {ser.LastError}";
                default:
                    return "";
            }
        }
    }
}
