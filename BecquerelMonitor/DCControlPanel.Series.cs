using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public partial class DCControlPanel
    {
        private CheckBox seriesCheck;
        private Button seriesSetupBtn;
        private Button seriesSkipBtn;
        private Label seriesSummaryLbl;
        private Label seriesStatusLbl;
        private bool seriesUpdating;

        private void InitializeSeriesStrip()
        {
            seriesCheck = new CheckBox();
            seriesCheck.Text = SeriesText.Series;
            seriesCheck.AutoSize = true;
            seriesCheck.Location = new Point(12, 288);

            seriesSetupBtn = new Button();
            seriesSetupBtn.Text = SeriesText.Setup;
            seriesSetupBtn.Location = new Point(95, 284);
            seriesSetupBtn.Size = new Size(90, 24);

            seriesSkipBtn = new Button();
            seriesSkipBtn.Text = SeriesText.SkipDelay;
            seriesSkipBtn.Location = new Point(192, 284);
            seriesSkipBtn.Size = new Size(130, 24);
            seriesSkipBtn.Visible = false;

            seriesSummaryLbl = new Label();
            seriesSummaryLbl.Location = new Point(12, 314);
            seriesSummaryLbl.Size = new Size(414, 16);
            seriesSummaryLbl.AutoEllipsis = true;

            seriesStatusLbl = new Label();
            seriesStatusLbl.Location = new Point(12, 332);
            seriesStatusLbl.Size = new Size(414, 16);
            seriesStatusLbl.AutoEllipsis = true;

            this.Controls.Add(seriesCheck);
            this.Controls.Add(seriesSetupBtn);
            this.Controls.Add(seriesSkipBtn);
            this.Controls.Add(seriesSummaryLbl);
            this.Controls.Add(seriesStatusLbl);

            this.AutoScroll = true;
            this.AutoScrollMinSize = new Size(437, 356);

            seriesCheck.CheckedChanged += (s, e) =>
            {
                try
                {
                    OnSeriesCheckChanged();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine(ex);
                }
            };

            seriesSetupBtn.Click += (s, e) =>
            {
                try
                {
                    OnSeriesSetupClicked();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine(ex);
                }
            };

            seriesSkipBtn.Click += (s, e) =>
            {
                try
                {
                    OnSeriesSkipClicked();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine(ex);
                }
            };

            realTimeLimitTextBox.TextChanged += (s, e) =>
            {
                try
                {
                    RefreshSeriesSummary();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine(ex);
                }
            };
        }

        private void OnSeriesCheckChanged()
        {
            if (seriesUpdating)
            {
                return;
            }

            DocEnergySpectrum doc = mainForm != null ? mainForm.ActiveDocument : null;
            if (doc == null)
            {
                return;
            }

            ResultData rd = doc.ActiveResultData;
            if (rd == null)
            {
                return;
            }

            if (rd.Series == null)
            {
                rd.Series = new SeriesSettings();
            }

            rd.Series.Enabled = seriesCheck.Checked;
            doc.Dirty = true;
            RefreshSeriesSummary();
        }

        private void OnSeriesSetupClicked()
        {
            DocEnergySpectrum doc = mainForm != null ? mainForm.ActiveDocument : null;
            if (doc == null)
            {
                return;
            }

            ResultData rd = doc.ActiveResultData;
            if (rd == null)
            {
                return;
            }

            string folderOfDoc = "";
            string nameOfDoc = "";

            if (!string.IsNullOrEmpty(doc.Filename))
            {
                string dir = Path.GetDirectoryName(doc.Filename);
                if (!string.IsNullOrEmpty(dir))
                {
                    folderOfDoc = dir;
                }

                nameOfDoc = Path.GetFileNameWithoutExtension(doc.Filename);
            }

            SeriesSettings initial = rd.Series != null ? rd.Series : new SeriesSettings();
            int presetSeconds = Math.Max(0, this.PresetTime);

            using (SeriesSettingsForm form = new SeriesSettingsForm(initial, presetSeconds, folderOfDoc, nameOfDoc))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    SeriesSettings r = form.Result;
                    if (r != null)
                    {
                        r.Enabled = seriesCheck.Checked;
                        rd.Series = r;
                        doc.Dirty = true;
                        RefreshSeriesSummary();
                    }
                }
            }
        }

        private void OnSeriesSkipClicked()
        {
            DocEnergySpectrum doc = mainForm != null ? mainForm.ActiveDocument : null;
            if (doc == null)
            {
                return;
            }

            ResultData rd = doc.ActiveResultData;
            if (rd == null || rd.MeasurementController == null)
            {
                return;
            }

            rd.MeasurementController.Series?.SkipDelay();
        }

        private void RefreshSeriesSummary()
        {
            if (seriesSummaryLbl == null)
            {
                return;
            }

            DocEnergySpectrum doc = mainForm != null ? mainForm.ActiveDocument : null;
            if (doc == null)
            {
                seriesSummaryLbl.Text = "";
                return;
            }

            ResultData rd = doc.ActiveResultData;
            if (rd == null)
            {
                seriesSummaryLbl.Text = "";
                return;
            }

            bool enabled = rd.Series != null && rd.Series.Enabled;
            if (enabled)
            {
                int preset = this.PresetTime > 0 ? this.PresetTime : rd.ResultDataStatus.PresetTime;
                seriesSummaryLbl.Text = SeriesText.Summary(rd.Series, preset);
            }
            else
            {
                seriesSummaryLbl.Text = "";
            }
        }

        internal bool SeriesBegin(DocEnergySpectrum doc, ResultData rd, ResultDataStatus status)
        {
            if (rd == null || rd.MeasurementController == null)
            {
                return true;
            }

            MeasurementSeries existing = rd.MeasurementController.Series;
            if (existing != null && !existing.IsActive)
            {
                if (rd.MeasurementController.SeriesHost != null)
                {
                    rd.MeasurementController.SeriesHost.Dispose();
                }
                rd.MeasurementController.SeriesHost = null;
                rd.MeasurementController.Series = null;
            }

            if (existing != null && existing.IsActive)
            {
                return false;
            }

            if (rd.Series == null || !rd.Series.Enabled)
            {
                return true;
            }

            int t = this.PresetTime;
            if (t <= 0)
            {
                MessageBox.Show(SeriesText.InvalidPreset, SeriesText.Warning, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return false;
            }

            MeasurementSeries s = SeriesHost.Create(this.mainForm, doc, rd, t);

            if (rd.Series.ClearBetween && status.ElapsedTime.TotalSeconds > 0)
            {
                DialogResult dr = MessageBox.Show(SeriesText.ClearFirst, SeriesText.Warning, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (dr == DialogResult.No)
                {
                    if (rd.MeasurementController.SeriesHost != null)
                    {
                        rd.MeasurementController.SeriesHost.Dispose();
                    }
                    rd.MeasurementController.SeriesHost = null;
                    rd.MeasurementController.Series = null;
                    return false;
                }

                string err;
                if (!rd.MeasurementController.SeriesHost.ClearSpectrum(out err))
                {
                    AppUi.Report(err, SeriesText.Warning, MessageBoxIcon.Warning);
                    if (rd.MeasurementController.SeriesHost != null)
                    {
                        rd.MeasurementController.SeriesHost.Dispose();
                    }
                    rd.MeasurementController.SeriesHost = null;
                    rd.MeasurementController.Series = null;
                    return false;
                }
            }

            int baseSeconds = rd.Series.ClearBetween ? 0 : (int)status.ElapsedTime.TotalSeconds;
            s.Begin(baseSeconds);
            status.PresetTime = s.PresetFor(1);
            return true;
        }

        internal void SeriesAfterStart(ResultData rd, bool started)
        {
            if (rd == null || rd.MeasurementController == null)
            {
                return;
            }

            MeasurementSeries s = rd.MeasurementController.Series;
            if (!started && s != null && s.IsActive)
            {
                s.UserStop(false);
            }
        }

        internal int SeriesBeforeStop(DocEnergySpectrum doc, ResultData rd, bool manual, out bool saveCurrent)
        {
            saveCurrent = false;

            if (rd == null || rd.MeasurementController == null)
            {
                return 0;
            }

            MeasurementSeries s = rd.MeasurementController.Series;
            if (s == null || !s.IsActive)
            {
                return 0;
            }

            if (!manual)
            {
                s.OnDeviceLost("Stopped by the program");
                return 0;
            }

            if (s.StopNeedsQuestion)
            {
                DialogResult r = MessageBox.Show(SeriesText.StopQuestion, SeriesText.Warning, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel)
                {
                    return 2;
                }
                saveCurrent = (r == DialogResult.Yes);
                return 1;
            }

            s.UserStop(false);
            this.ShowDocumentStatus();
            return 2;
        }

        internal void SeriesAfterStop(ResultData rd, bool saveCurrent)
        {
            if (rd != null && rd.MeasurementController != null)
            {
                MeasurementSeries s = rd.MeasurementController.Series;
                if (s != null && s.IsActive)
                {
                    s.UserStop(saveCurrent);
                }
            }
            this.ShowDocumentStatus();
        }

        internal void ShowSeriesStatus(DocEnergySpectrum doc, ResultData rd)
        {
            try
            {
                seriesUpdating = true;

                if (rd != null)
                {
                    bool enabled = rd.Series != null && rd.Series.Enabled;
                    seriesCheck.Checked = enabled;
                    MeasurementSeries s = rd.MeasurementController != null ? rd.MeasurementController.Series : null;

                    if (enabled && rd.Series != null)
                    {
                        // during a run the time of ONE spectrum is the series' own (the box may show the accumulated limit)
                        int preset = s != null && s.IsActive ? s.PresetSeconds : (this.PresetTime > 0 ? this.PresetTime : rd.ResultDataStatus.PresetTime);
                        seriesSummaryLbl.Text = SeriesText.Summary(rd.Series, preset);
                    }
                    else
                    {
                        seriesSummaryLbl.Text = "";
                    }

                    string statusText = s != null ? SeriesText.Status(s) : "";
                    seriesStatusLbl.Text = statusText;

                    bool isError = s != null && s.State == SeriesState.PausedError;
                    seriesStatusLbl.ForeColor = isError ? Color.Firebrick : SystemColors.ControlText;

                    seriesSkipBtn.Visible = s != null && s.State == SeriesState.Delay;

                    bool active = s != null && s.IsActive;
                    if (active)
                    {
                        startBtn.Enabled = false;
                        stopBtn.Enabled = true;
                        devConfigComboBox.Enabled = false;
                        reloadFromConfigBtn.Enabled = false;
                        seriesCheck.Enabled = false;
                        seriesSetupBtn.Enabled = false;
                    }
                    else
                    {
                        seriesCheck.Enabled = true;
                        seriesSetupBtn.Enabled = true;
                    }
                }
            }
            finally
            {
                seriesUpdating = false;
            }
        }

        internal void ShowSeriesCountdown(DocEnergySpectrum doc, ResultData rd)
        {
            if (rd == null || rd.MeasurementController == null)
            {
                return;
            }

            MeasurementSeries s = rd.MeasurementController.Series;
            string statusText = s != null ? SeriesText.Status(s) : "";
            seriesStatusLbl.Text = statusText;

            bool isError = s != null && s.State == SeriesState.PausedError;
            seriesStatusLbl.ForeColor = isError ? Color.Firebrick : SystemColors.ControlText;

            seriesSkipBtn.Visible = s != null && s.State == SeriesState.Delay;
        }
    }
}
