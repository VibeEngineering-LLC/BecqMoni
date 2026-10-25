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

        /// <summary>Test seam: answers every question of the series instead of a message box (null = the real box).</summary>
        internal Func<string, MessageBoxButtons, DialogResult> SeriesDialog;

        private DialogResult SeriesAsk(string text, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            if (this.SeriesDialog != null)
            {
                return this.SeriesDialog(text, buttons);
            }
            // every question goes through the application's door (AppUi): without windows it refuses or prints, it never opens a window
            if (buttons == MessageBoxButtons.YesNoCancel)
            {
                return AppUi.AskYesNoCancel(text, SeriesText.Warning, icon);
            }
            if (buttons == MessageBoxButtons.YesNo)
            {
                return AppUi.AskYesNo(text, SeriesText.Warning, icon, false) ? DialogResult.Yes : DialogResult.No;
            }
            AppUi.Report(text, SeriesText.Warning, icon);
            return DialogResult.OK;

        }

        private void InitializeSeriesStrip()
        {
            seriesCheck = new CheckBox();
            seriesCheck.Text = SeriesText.Series;
            seriesCheck.AutoSize = true;
            seriesCheck.Location = new Point(12, 138);

            seriesSetupBtn = new Button();
            seriesSetupBtn.Text = SeriesText.Setup;
            seriesSetupBtn.Location = new Point(95, 134);
            seriesSetupBtn.Size = new Size(90, 24);

            seriesSkipBtn = new Button();
            seriesSkipBtn.Text = SeriesText.SkipDelay;
            seriesSkipBtn.Location = new Point(192, 134);
            seriesSkipBtn.Size = new Size(130, 24);
            seriesSkipBtn.Visible = false;

            seriesSummaryLbl = new Label();
            seriesSummaryLbl.Location = new Point(12, 162);
            seriesSummaryLbl.Size = new Size(414, 16);
            seriesSummaryLbl.AutoEllipsis = true;

            seriesStatusLbl = new Label();
            seriesStatusLbl.Location = new Point(12, 180);
            seriesStatusLbl.Size = new Size(414, 16);
            seriesStatusLbl.AutoEllipsis = true;

            this.Controls.Add(seriesCheck);
            this.Controls.Add(seriesSetupBtn);
            this.Controls.Add(seriesSkipBtn);
            this.Controls.Add(seriesSummaryLbl);
            this.Controls.Add(seriesStatusLbl);
            this.groupBox1.Top += 71;   // the strip sits between the device rows and the counters group (before it lay inside and BEHIND the group: invisible)
            foreach (Control c in new Control[] { seriesCheck, seriesSetupBtn, seriesSkipBtn, seriesSummaryLbl, seriesStatusLbl })
            {
                c.BringToFront();   // never under the designer controls
            }

            this.AutoScroll = true;
            this.AutoScrollMinSize = new Size(437, 423);

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
            bool editable = SeriesPresetEditable(rd);   // IMPL-7: the time of each spectrum is fixed while a series runs

            using (SeriesSettingsForm form = new SeriesSettingsForm(initial, presetSeconds, folderOfDoc, nameOfDoc, editable))
            {
                if (form.ShowDialog(this) == DialogResult.OK)   // modal: the panel cannot change while the window is open
                {
                    this.ApplySeriesForm(doc, rd, form);
                }
            }
        }

        /// <summary>IMPL-7: the time of each spectrum cannot be changed while a series runs on the document.</summary>
        internal static bool SeriesPresetEditable(ResultData rd)
        {
            MeasurementSeries running = rd != null && rd.MeasurementController != null ? rd.MeasurementController.Series : null;
            return running == null || !running.IsActive;
        }

        /// <summary>The window was accepted: the series settings and the ONE time of each spectrum (the panel's real-time field, through the same path as typing it there).</summary>
        internal void ApplySeriesForm(DocEnergySpectrum doc, ResultData rd, SeriesSettingsForm form)
        {
            SeriesSettings r = form.Result;
            if (r == null)
            {
                return;
            }
            r.Enabled = seriesCheck.Checked;
            rd.Series = r;
            doc.Dirty = true;
            if (form.PresetEditable && form.PresetSeconds != this.PresetTime)
            {
                this.PresetTime = form.PresetSeconds;
                this.PresetTimeChanged();
            }
            RefreshSeriesSummary();
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

        /// <summary>
        /// Delayed start (stage 2): 0 = start at once, 1 = wait (<paramref name="waitSeconds"/> &gt; 0), 2 = cancelled.
        /// A start time that has already passed is asked about, never skipped silently.
        /// </summary>
        internal int SeriesResolveStart(ResultData rd, DateTime now, out long waitSeconds)
        {
            waitSeconds = rd.Series.WaitSecondsFrom(now);
            if (waitSeconds > 0)
            {
                return 1;
            }
            if (waitSeconds == 0)
            {
                return 0;
            }
            waitSeconds = 0;
            return this.SeriesAsk(SeriesText.StartPassed, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes ? 0 : 2;
        }

        private static void SeriesTeardown(ResultData rd)
        {
            if (rd.MeasurementController.SeriesHost != null)
            {
                rd.MeasurementController.SeriesHost.Dispose();
            }
            rd.MeasurementController.SeriesHost = null;
            rd.MeasurementController.Series = null;
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

            string refusal = SeriesDevices.RefusalText(rd);   // IMPL-5/6: a white list of device types; a REST board in observe-only mode
            if (refusal != null)
            {
                this.SeriesAsk(refusal, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            if (!this.SeriesSettingsAccepted(rd.Series))   // IMPL-4 (F2, F4): the settings of a loaded document are checked at the start, not only in the settings window
            {
                return false;
            }
            int t = this.PresetTime;
            if (t <= 0)
            {
                this.SeriesAsk(SeriesText.InvalidPreset, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return false;
            }

            MeasurementSeries s = SeriesHost.Create(this.mainForm, doc, rd, t);

            long waitSeconds;
            int startMode = this.SeriesResolveStart(rd, DateTime.Now, out waitSeconds);
            if (startMode == 2)
            {
                SeriesTeardown(rd);
                return false;
            }

            if (rd.Series.ClearBetween && status.ElapsedTime.TotalSeconds > 0)
            {
                DialogResult dr = this.SeriesAsk(SeriesText.ClearFirst, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
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

                string err = "";
                // a waiting series clears at its start (the question above is the consent), not now
                if (startMode == 0 && !rd.MeasurementController.SeriesHost.ClearSpectrum(out err))
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
            if (startMode == 1)
            {
                s.BeginWaiting(baseSeconds, (int)Math.Min(waitSeconds, int.MaxValue));
                return false;   // the device is not touched while the series waits
            }
            s.Begin(baseSeconds);
            status.PresetTime = s.PresetFor(1);
            return true;
        }

        /// <summary>Присоединение / отсоединение прибора во время серии рассинхронизирует серию и прибор: отказ с пояснением.</summary>
        internal bool SeriesRefusesAttach(ResultData rd)
        {
            MeasurementSeries s = rd != null && rd.MeasurementController != null ? rd.MeasurementController.Series : null;
            if (s == null || !s.IsActive)
            {
                return false;
            }
            this.SeriesAsk(SeriesText.AttachBlocked, MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                string why = SeriesDevices.BoardRefusal(rd);   // IMPL-6: the board's reason (a series gets no box from the device)
                s.UserStop(false);
                if (why.Length > 0) this.SeriesAsk(why, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>The same checks as the settings window (a document may carry settings that never passed it): too long folder, template that overwrites, template that gets suffixes.</summary>
        private bool SeriesSettingsAccepted(SeriesSettings s)
        {
            if (SeriesSettings.FolderTooLong(s.Folder))
            {
                this.SeriesAsk(SeriesText.FolderTooLong, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            int problem = SeriesSettings.TemplateProblem(s.Template, s.Count, s.NoOverwrite);
            if (problem == 2)
            {
                this.SeriesAsk(SeriesText.TemplateRefuse, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return problem != 1 || this.SeriesAsk(SeriesText.TemplateWarn, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
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
                int askedId = s.AcquisitionId;
                DialogResult r = this.SeriesAsk(SeriesText.StopQuestion, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel)
                {
                    return 2;
                }
                if (s.PartialPending && s.AcquisitionId == askedId)
                {
                    // E4: the device was lost while the question was open; the user's answer still counts
                    if (r == DialogResult.Yes)
                    {
                        string lostErr;
                        bool saved = s.SavePartialAfterLoss(out lostErr);
                        this.SeriesAsk(saved ? SeriesText.LostSaved + s.Files[s.Files.Count - 1] : SeriesText.LostNotSaved + lostErr, MessageBoxButtons.OK, saved ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                    }
                    s.UserStop(false);
                    return rd.ResultDataStatus.Recording ? 0 : 2;
                }
                if (!s.StopNeedsQuestion || s.AcquisitionId != askedId)
                {
                    // the window timer went on while the question was open: spectrum k was completed and written by the series itself
                    s.UserStop(false);
                    return rd.ResultDataStatus.Recording ? 0 : 2;
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
                    string shortRefusal = SeriesDevices.ShortText(rd);   // IMPL-5/6: the controls follow the device type and the observe-only mode
                    bool supported = shortRefusal == null;
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

                    string statusText = s != null ? SeriesText.Status(s) : (supported ? "" : shortRefusal);
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
                        attachBtn.Enabled = false;
                        seriesCheck.Enabled = false;
                        seriesSetupBtn.Enabled = false;
                    }
                    else
                    {
                        attachBtn.Enabled = true;
                        seriesCheck.Enabled = supported;
                        seriesSetupBtn.Enabled = supported;
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
