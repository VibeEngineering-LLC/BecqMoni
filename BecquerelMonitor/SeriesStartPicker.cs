using System;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    internal sealed class SeriesStartPicker : FlowLayoutPanel
    {
        private readonly ComboBox _combo;
        private readonly DateTimePicker _dtp;
        private readonly NumericUpDown _numH;
        private readonly NumericUpDown _numM;
        private readonly NumericUpDown _numS;
        private Label _lblH, _lblM, _lblS;
        private bool _loading;

        public event EventHandler Changed;

        public SeriesStartPicker()
        {
            AutoSize = true;
            WrapContents = false;
            Margin = new Padding(0);

            _combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 120
            };
            _combo.Items.Add(SeriesText.StartNow);
            _combo.Items.Add(SeriesText.StartAtTime);
            _combo.Items.Add(SeriesText.StartIn);
            _combo.SelectedIndex = 0;
            _combo.SelectedIndexChanged += (s, e) => OnUserChanged();

            _dtp = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "dd.MM.yyyy HH:mm:ss",
                ShowUpDown = false,
                Width = 150,
                Visible = false
            };
            _dtp.ValueChanged += (s, e) => OnUserChanged();

            _numH = MakeNum(0, 99);
            _numM = MakeNum(0, 59);
            _numS = MakeNum(0, 59);

            _lblH = MakeLbl(SeriesText.LblHours);
            _lblM = MakeLbl(SeriesText.LblMinutes);
            _lblS = MakeLbl(SeriesText.LblSeconds);

            Controls.Add(_combo);
            Controls.Add(_dtp);
            Controls.Add(_numH);
            Controls.Add(_lblH);
            Controls.Add(_numM);
            Controls.Add(_lblM);
            Controls.Add(_numS);
            Controls.Add(_lblS);

            UpdateVisibility();
        }

        private NumericUpDown MakeNum(int min, int max)
        {
            var n = new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Width = 50,
                Visible = false
            };
            n.ValueChanged += (s, e) => OnUserChanged();
            return n;
        }

        private static Label MakeLbl(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Visible = false
            };
        }

        public void Load(SeriesSettings s)
        {
            _loading = true;
            try
            {
                int kind = Math.Max(0, Math.Min(2, s.StartKind));
                _combo.SelectedIndex = kind;

                DateTime dt = s.StartAt;
                if (dt == DateTime.MinValue || dt < DateTimePicker.MinimumDateTime || dt > DateTimePicker.MaximumDateTime)
                {
                    dt = DateTime.Now.AddHours(1);
                    dt = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0);
                }
                _dtp.Value = dt;

                int total = Math.Max(0, Math.Min(359999, s.StartInSeconds));
                int h = Math.Min(99, total / 3600);
                int m = (total % 3600) / 60;
                int sec = total % 60;

                _numH.Value = h;
                _numM.Value = m;
                _numS.Value = sec;

                UpdateVisibility();
            }
            finally
            {
                _loading = false;
            }
        }

        public void Save(SeriesSettings s)
        {
            s.StartKind = _combo.SelectedIndex;
            s.StartAt = _dtp.Value;
            s.StartInSeconds = (int)_numH.Value * 3600 + (int)_numM.Value * 60 + (int)_numS.Value;
        }

        private void OnUserChanged()
        {
            if (_loading) return;
            UpdateVisibility();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateVisibility()
        {
            int idx = _combo.SelectedIndex;
            bool showDtp = idx == 1;
            bool showNums = idx == 2;

            _dtp.Visible = showDtp;
            _numH.Visible = showNums;
            _numM.Visible = showNums;
            _numS.Visible = showNums;

            _lblH.Visible = showNums;
            _lblM.Visible = showNums;
            _lblS.Visible = showNums;
        }
    }
}
