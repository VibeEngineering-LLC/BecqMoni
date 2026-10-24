using System;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // #BM-12: серия измерений и закрытие документа / программы.
    public partial class MainForm
    {
        /// <summary>
        /// Документ закрывают (или закрывается программа), пока идёт серия: спросить. «Да» — серия
        /// останавливается без записи неполного спектра (приборы останавливает обычный путь закрытия);
        /// «Нет» — закрытие отменяется. Нет активной серии — вопроса нет.
        /// </summary>
        internal bool ConfirmCloseDuringSeries(DocEnergySpectrum doc, Func<bool> ask = null)
        {
            foreach (ResultData rd in doc.ResultDataFile.ResultDataList)
            {
                MeasurementSeries s = rd.MeasurementController != null ? rd.MeasurementController.Series : null;
                if (s == null || !s.IsActive)
                {
                    continue;
                }
                bool yes = ask != null ? ask() : MessageBox.Show(SeriesText.CloseQuestion, SeriesText.Warning, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
                if (!yes)
                {
                    return false;
                }
                s.UserStop(false);
            }
            return true;
        }
    }
}
