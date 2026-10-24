using System;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // #BM-12: серия измерений и закрытие документа / программы.
    public partial class MainForm
    {
        /// <summary>
        /// Документ закрывают (или закрывается программа), пока идёт серия: спросить ОДИН раз на документ.
        /// «Нет» — закрытие отменяется. Сам вопрос серию НЕ останавливает: закрытие ещё может быть отменено
        /// (вопрос о сохранении, соседний документ) — серию останавливает <see cref="StopSeriesOf"/> из
        /// <c>StopRecordingOrTesting</c>, когда закрытие подтверждено. Нет активной серии — вопроса нет.
        /// </summary>
        internal bool ConfirmCloseDuringSeries(DocEnergySpectrum doc, Func<bool> ask = null)
        {
            bool active = false;
            foreach (ResultData rd in doc.ResultDataFile.ResultDataList)
            {
                MeasurementSeries s = rd.MeasurementController != null ? rd.MeasurementController.Series : null;
                active = active || (s != null && s.IsActive);
            }
            if (!active)
            {
                return true;
            }
            return ask != null ? ask() : MessageBox.Show(SeriesText.CloseQuestion, SeriesText.Warning, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        /// <summary>Закрытие подтверждено: остановить серии всех спектров документа (неполный спектр не пишется).</summary>
        internal void StopSeriesOf(DocEnergySpectrum doc)
        {
            foreach (ResultData rd in doc.ResultDataFile.ResultDataList)
            {
                MeasurementSeries s = rd.MeasurementController != null ? rd.MeasurementController.Series : null;
                if (s != null && s.IsActive)
                {
                    s.UserStop(false);
                }
            }
        }
    }
}
