using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    internal sealed class SeriesSettingsForm : Form
    {
        private readonly SeriesSettings _initial;
        private readonly int _presetSeconds;
        private readonly string _docFolder;
        private readonly string _docName;

        private NumericUpDown _numCount;
        private NumericUpDown _numHours;
        private NumericUpDown _numMinutes;
        private NumericUpDown _numSeconds;
        private CheckBox _chkClear;
        private TextBox _txtFolder;
        private TextBox _txtTemplate;
        private TextBox _txtPrefix;
        private CheckBox _chkNoOverwrite;
        private Label _lblExample;
        private Label _lblTotal;
        private Label _lblEnd;

        public SeriesSettings Result { get; private set; }

        public SeriesSettingsForm(SeriesSettings initial, int presetSeconds, string docFolder, string docName)
        {
            _initial = initial;
            _presetSeconds = presetSeconds;
            _docFolder = docFolder;
            _docName = docName;

            Text = SeriesText.FormTitle;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(520, 0);

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(12),
                ColumnCount = 2,
                RowCount = 12
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 12; i++)
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Row 1: Count
            _numCount = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 9999,
                Value = Math.Max(0, Math.Min(9999, initial.Count)),
                Dock = DockStyle.Fill
            };
            table.Controls.Add(MakeLabel(SeriesText.LblCount), 0, 0);
            table.Controls.Add(_numCount, 1, 0);

            // Row 2: Time
            var lblTime = MakeLabel(SeriesText.LblTime);
            var lblPreset = MakeValueLabel(SeriesSettings.FormatHms(presetSeconds));
            table.Controls.Add(lblTime, 0, 1);
            table.Controls.Add(lblPreset, 1, 1);

            // Row 3: Delay
            var flowDelay = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0)
            };
            _numHours = new NumericUpDown { Minimum = 0, Maximum = 99, Value = Math.Max(0, Math.Min(99, initial.DelaySeconds / 3600)), Width = 50 };
            _numMinutes = new NumericUpDown { Minimum = 0, Maximum = 59, Value = Math.Max(0, Math.Min(59, (initial.DelaySeconds % 3600) / 60)), Width = 50 };
            _numSeconds = new NumericUpDown { Minimum = 0, Maximum = 59, Value = Math.Max(0, Math.Min(59, initial.DelaySeconds % 60)), Width = 50 };
            flowDelay.Controls.Add(_numHours);
            flowDelay.Controls.Add(MakeLabel(SeriesText.LblHours));
            flowDelay.Controls.Add(_numMinutes);
            flowDelay.Controls.Add(MakeLabel(SeriesText.LblMinutes));
            flowDelay.Controls.Add(_numSeconds);
            flowDelay.Controls.Add(MakeLabel(SeriesText.LblSeconds));
            table.Controls.Add(MakeLabel(SeriesText.LblDelay), 0, 2);
            table.Controls.Add(flowDelay, 1, 2);

            // Row 4: Clear
            _chkClear = new CheckBox
            {
                Text = SeriesText.ChkClear,
                Checked = initial.ClearBetween,
                AutoSize = true,
                Margin = new Padding(0)
            };
            table.Controls.Add(_chkClear, 0, 3);
            table.SetColumnSpan(_chkClear, 2);

            // Row 5: Folder
            var panelFolder = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            panelFolder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            panelFolder.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panelFolder.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _txtFolder = new TextBox
            {
                Text = string.IsNullOrEmpty(initial.Folder) ? docFolder : initial.Folder,
                Dock = DockStyle.Fill
            };
            var btnBrowse = new Button
            {
                Text = SeriesText.BtnBrowse,
                AutoSize = true,
                Margin = new Padding(3, 0, 0, 0)
            };
            btnBrowse.Click += BtnBrowse_Click;
            panelFolder.Controls.Add(_txtFolder, 0, 0);
            panelFolder.Controls.Add(btnBrowse, 1, 0);
            table.Controls.Add(MakeLabel(SeriesText.LblFolder), 0, 4);
            table.Controls.Add(panelFolder, 1, 4);

            // Row 6: Template
            _txtTemplate = new TextBox
            {
                Text = initial.Template,
                Dock = DockStyle.Fill
            };
            table.Controls.Add(MakeLabel(SeriesText.LblTemplate), 0, 5);
            table.Controls.Add(_txtTemplate, 1, 5);

            // Row 7: Prefix
            _txtPrefix = new TextBox
            {
                Text = string.IsNullOrEmpty(initial.Prefix) ? docName : initial.Prefix,
                Dock = DockStyle.Fill
            };
            table.Controls.Add(MakeLabel(SeriesText.LblPrefix), 0, 6);
            table.Controls.Add(_txtPrefix, 1, 6);

            // Row 8: NoOverwrite
            _chkNoOverwrite = new CheckBox
            {
                Text = SeriesText.ChkNoOverwrite,
                Checked = initial.NoOverwrite,
                AutoSize = true,
                Margin = new Padding(0)
            };
            table.Controls.Add(_chkNoOverwrite, 0, 7);
            table.SetColumnSpan(_chkNoOverwrite, 2);

            // Row 9: Example
            _lblExample = MakeValueLabel("");
            table.Controls.Add(MakeLabel(SeriesText.LblExample), 0, 8);
            table.Controls.Add(_lblExample, 1, 8);

            // Row 10: Total
            _lblTotal = MakeValueLabel("");
            table.Controls.Add(MakeLabel(SeriesText.LblTotal), 0, 9);
            table.Controls.Add(_lblTotal, 1, 9);

            // Row 11: End
            _lblEnd = MakeValueLabel("");
            table.Controls.Add(MakeLabel(SeriesText.LblEnd), 0, 10);
            table.Controls.Add(_lblEnd, 1, 10);

            // Row 12: Buttons
            var flowButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Margin = new Padding(0)
            };
            var btnCancel = new Button
            {
                Text = SeriesText.BtnCancel,
                DialogResult = DialogResult.Cancel,
                AutoSize = true
            };
            var btnOk = new Button
            {
                Text = SeriesText.BtnOk,
                AutoSize = true
            };
            btnOk.Click += BtnOk_Click;
            flowButtons.Controls.Add(btnCancel);
            flowButtons.Controls.Add(btnOk);
            table.Controls.Add(flowButtons, 0, 11);
            table.SetColumnSpan(flowButtons, 2);

            Controls.Add(table);
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            HookEvents();
            Recalc();
        }

        private void HookEvents()
        {
            _numCount.ValueChanged += (s, e) => Recalc();
            _numHours.ValueChanged += (s, e) => Recalc();
            _numMinutes.ValueChanged += (s, e) => Recalc();
            _numSeconds.ValueChanged += (s, e) => Recalc();
            _chkClear.CheckedChanged += (s, e) => Recalc();
            _txtFolder.TextChanged += (s, e) => Recalc();
            _txtTemplate.TextChanged += (s, e) => Recalc();
            _txtPrefix.TextChanged += (s, e) => Recalc();
            _chkNoOverwrite.CheckedChanged += (s, e) => Recalc();
        }

        private void Recalc()
        {
            int count = (int)_numCount.Value;
            int h = (int)_numHours.Value;
            int m = (int)_numMinutes.Value;
            int s = (int)_numSeconds.Value;
            int delay = h * 3600 + m * 60 + s;

            string template = _txtTemplate.Text;
            string prefix = _txtPrefix.Text;

            _lblExample.Text = SeriesSettings.BuildName(template, prefix, 1, DateTime.Now) + ".xml";

            if (count == 0)
            {
                _lblTotal.Text = SeriesText.Endless;
                _lblEnd.Text = SeriesText.Endless;
            }
            else
            {
                var temp = new SeriesSettings
                {
                    Enabled = true,
                    Count = count,
                    DelaySeconds = delay,
                    ClearBetween = _chkClear.Checked,
                    Folder = _txtFolder.Text,
                    Template = template,
                    Prefix = prefix,
                    NoOverwrite = _chkNoOverwrite.Checked
                };
                long totalSecs = temp.TotalSeconds(_presetSeconds);
                if (totalSecs == -1)
                {
                    _lblTotal.Text = SeriesText.Endless;
                    _lblEnd.Text = SeriesText.Endless;
                }
                else
                {
                    _lblTotal.Text = SeriesSettings.FormatHms(totalSecs);
                    _lblEnd.Text = DateTime.Now.AddSeconds(totalSecs).ToString("g");
                }
            }
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                string current = _txtFolder.Text.Trim();
                if (Directory.Exists(current))
                    fbd.SelectedPath = current;
                if (fbd.ShowDialog(this) == DialogResult.OK)
                {
                    _txtFolder.Text = fbd.SelectedPath;
                    Recalc();
                }
            }
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            string folder = _txtFolder.Text.Trim();
            if (string.IsNullOrEmpty(folder))
            {
                MessageBox.Show(this, SeriesText.ErrFolder, SeriesText.Warning, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtFolder.Focus();
                return;
            }

            string template = _txtTemplate.Text.Trim();
            if (string.IsNullOrEmpty(template))
            {
                MessageBox.Show(this, SeriesText.ErrTemplate, SeriesText.Warning, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtTemplate.Focus();
                return;
            }

            int h = (int)_numHours.Value;
            int m = (int)_numMinutes.Value;
            int s = (int)_numSeconds.Value;

            Result = _initial.Clone();
            Result.Count = (int)_numCount.Value;
            Result.DelaySeconds = h * 3600 + m * 60 + s;
            Result.ClearBetween = _chkClear.Checked;
            Result.Folder = folder;
            Result.Template = template;
            Result.Prefix = _txtPrefix.Text.Trim();
            Result.NoOverwrite = _chkNoOverwrite.Checked;
            // Enabled is kept from initial (Clone preserves it)
            Result.Normalize();

            DialogResult = DialogResult.OK;
            Close();
        }

        private static Label MakeLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Margin = new Padding(0, 3, 6, 3),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static Label MakeValueLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(400, 0),
                Margin = new Padding(0, 3, 0, 3),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }
    }
}
