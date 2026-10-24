using System;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public partial class MainForm
    {
        /// <summary>
        /// Test seam for UI questions. When set, all yes/no and ok/cancel questions
        /// in the series/delete flow are routed through this delegate instead of
        /// showing a window.
        /// </summary>
        internal Func<string, MessageBoxButtons, DialogResult> UiAsk;

        private bool AskSeries(string text)
        {
            if (UiAsk != null)
            {
                return UiAsk(text, MessageBoxButtons.YesNo) == DialogResult.Yes;
            }
            return AppUi.AskYesNo(text, SeriesText.Warning, MessageBoxIcon.Question, true);
        }

        private DialogResult AskDeleteSpectrum(string text, string caption)
        {
            if (UiAsk != null)
            {
                return UiAsk(text, MessageBoxButtons.OKCancel);
            }
            return MessageBox.Show(text, caption, MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation);
        }

        private static MeasurementSeries ActiveSeriesOf(ResultData rd)
        {
            if (rd == null || rd.MeasurementController == null)
            {
                return null;
            }
            var series = rd.MeasurementController.Series;
            if (series != null && series.IsActive)
            {
                return series;
            }
            return null;
        }

        /// <summary>
        /// Подтверждение закрытия документа во время активной серии.
        /// Задаёт вопрос один раз на документ. Если ни одна спектрограмма
        /// документа не имеет активной серии, возвращает true без вопроса.
        /// Сам вопрос НЕ останавливает серию (закрытие может быть отменено позже).
        /// </summary>
        internal bool ConfirmCloseDuringSeries(DocEnergySpectrum doc, Func<bool> ask = null)
        {
            if (doc == null || doc.ResultDataFile == null || doc.ResultDataFile.ResultDataList == null)
            {
                return true;
            }

            bool hasActiveSeries = false;
            foreach (var rd in doc.ResultDataFile.ResultDataList)
            {
                if (ActiveSeriesOf(rd) != null)
                {
                    hasActiveSeries = true;
                    break;
                }
            }

            if (!hasActiveSeries)
            {
                return true;
            }

            return ask != null ? ask() : this.AskSeries(SeriesText.CloseQuestion);
        }

        internal bool ConfirmDeleteDuringSeries(ResultData rd, Func<bool> ask = null)
        {
            if (ActiveSeriesOf(rd) == null)
            {
                return true;
            }
            return ask != null ? ask() : this.AskSeries(SeriesText.DeleteQuestion);
        }

        internal void StopSeriesOf(DocEnergySpectrum doc)
        {
            if (doc == null || doc.ResultDataFile == null || doc.ResultDataFile.ResultDataList == null)
            {
                return;
            }

            foreach (var rd in doc.ResultDataFile.ResultDataList)
            {
                var series = ActiveSeriesOf(rd);
                if (series != null)
                {
                    series.UserStop(false);
                }
            }
        }

        internal void StopSeriesOfSpectrum(ResultData rd)
        {
            var series = ActiveSeriesOf(rd);
            if (series != null)
            {
                series.UserStop(false);
            }
        }
    }
}
