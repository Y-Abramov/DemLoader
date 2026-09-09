using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Abr.Sdk;
using Topomatic.Proj.CoordinateSystems;
using Topomatic.Proj.Runtime;
using DemLoader.Core.Sources;
using DemLoader.Robur;

namespace DemLoader.Ui
{
    /// <summary>Окно dem_export: источник, СК, область, папка для сохранения файлов. Тот же
    /// Retry-приём с областью, что в DemDialog (Task 9) - выбор объекта на плане при открытом
    /// модальном окне невозможен.
    ///
    /// ЖИВЬЁМ НЕ ПРОВЕРЯЛСЯ - полный гейт Task 13.</summary>
    internal sealed class ExportDialog : Form
    {
        private readonly IList<DemSource> _sources;
        private readonly string _apiKey;

        private readonly ComboBox _sourceBox = new ComboBox();
        private readonly Label _sourceInfo = new Label();

        private readonly CRSSelectorButton _crs = new CRSSelectorButton();
        private readonly Label _crsLabel = new Label();

        private readonly RadioButton _byWindow = new RadioButton();
        private readonly RadioButton _byPolyline = new RadioButton();
        private readonly Button _pickButton = new Button();
        private readonly Label _areaLabel = new Label();

        private readonly TextBox _folder = new TextBox();

        private readonly Label _hint = new Label();
        private readonly Button _ok;

        private bool _hasArea;

        private readonly bool _ready;

        public DemSource SelectedSource { get; private set; }
        public string Folder { get { return _folder.Text.Trim(); } }
        public HorizontalCoordinateSystem SelectedCoordSystem { get { return _crs.CRS; } }
        public DemAreaRequest Request { get; private set; }

        public ExportDialog(IList<DemSource> sources, string apiKey)
        {
            if (sources == null || sources.Count == 0) throw new ArgumentException("Каталог источников пуст.");

            _sources = sources;
            _apiKey = apiKey;

            Text = "Выгрузка тайлов рельефа";
            Icon = AbrIcon.Create();
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(500, 330);

            var sourceLabel = new Label { Text = "Источник:", Left = 12, Top = 15, Width = 70 };
            _sourceBox.Left = 88; _sourceBox.Top = 12; _sourceBox.Width = 400;
            _sourceBox.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (var source in sources) _sourceBox.Items.Add(Caption(source));
            _sourceBox.SelectedIndex = 0;
            _sourceBox.SelectedIndexChanged += (s, e) => SourceChanged();

            _sourceInfo.Left = 12; _sourceInfo.Top = 44; _sourceInfo.Width = 476; _sourceInfo.Height = 18;
            _sourceInfo.ForeColor = SystemColors.GrayText;

            var crsGroup = new GroupBox { Text = "Система координат", Left = 12, Top = 68, Width = 476, Height = 50 };
            _crs.Left = 12; _crs.Top = 18; _crs.Width = 200;
            _crs.SelectedCRSChanged += (s, e) =>
            {
                _crsLabel.Text = _crs.CRS != null ? _crs.CRS.Name : "не выбрана";
                ApplyState();
            };
            _crsLabel.Left = 220; _crsLabel.Top = 22; _crsLabel.Width = 240;
            _crsLabel.Text = "не выбрана";
            crsGroup.Controls.AddRange(new Control[] { _crs, _crsLabel });

            var areaGroup = new GroupBox { Text = "Область", Left = 12, Top = 124, Width = 476, Height = 80 };
            _byWindow.Text = "Рамкой"; _byWindow.Left = 12; _byWindow.Top = 22; _byWindow.Width = 190;
            _byWindow.Checked = true;
            _byPolyline.Text = "По замкнутой полилинии"; _byPolyline.Left = 12; _byPolyline.Top = 46; _byPolyline.Width = 190;

            EventHandler reset = (s, e) => { if (((RadioButton)s).Checked) ForgetArea(); };
            _byWindow.CheckedChanged += reset;
            _byPolyline.CheckedChanged += reset;

            _pickButton.Text = "Указать на плане";
            _pickButton.Left = 215; _pickButton.Top = 20; _pickButton.Width = 170; _pickButton.Height = 26;
            UiTheme.ApplyButtonKind(_pickButton, BtnKind.Secondary);
            _pickButton.Click += (s, e) => RequestArea();

            _areaLabel.Left = 215; _areaLabel.Top = 52; _areaLabel.Width = 250; _areaLabel.Height = 18;
            _areaLabel.Text = "Область не задана";

            areaGroup.Controls.AddRange(new Control[] { _byWindow, _byPolyline, _pickButton, _areaLabel });

            var folderLabel = new Label { Text = "Папка:", Left = 12, Top = 240, Width = 60 };
            _folder.Left = 12; _folder.Top = 262; _folder.Width = 396;
            var browse = UiTheme.MakeButton("...", BtnKind.Ghost, 60, 23);
            browse.Left = 416; browse.Top = 260;
            browse.Click += (s, e) =>
            {
                using (var dialog = new FolderBrowserDialog())
                    if (dialog.ShowDialog(this) == DialogResult.OK) { _folder.Text = dialog.SelectedPath; ApplyState(); }
            };
            _folder.TextChanged += (s, e) => ApplyState();

            _hint.Left = 12; _hint.Top = 292; _hint.Width = 476; _hint.Height = 20;
            _hint.ForeColor = Color.FromArgb(120, 60, 40);

            _ok = UiTheme.MakeButton("Выгрузить", BtnKind.Primary, 85, 26);
            _ok.Left = 312; _ok.Top = 292; _ok.DialogResult = DialogResult.OK;
            var cancel = UiTheme.MakeButton("Отмена", BtnKind.Ghost, 85, 26);
            cancel.Left = 403; cancel.Top = 292; cancel.DialogResult = DialogResult.Cancel;

            Controls.AddRange(new Control[] { sourceLabel, _sourceBox, _sourceInfo, crsGroup, areaGroup,
                                              folderLabel, _folder, browse, _hint, _ok, cancel });
            AcceptButton = _ok;
            CancelButton = cancel;

            _ready = true;
            SourceChanged();
        }

        public DialogResult Run()
        {
            Request = DemAreaRequest.None;
            DialogResult = DialogResult.None;
            return ShowDialog();
        }

        public void SetArea(PickedArea area, string description)
        {
            _hasArea = area.Loop != null && area.Loop.Count >= 3;
            _areaLabel.Text = _hasArea ? description : "Область имеет нулевой размер";
            ApplyState();
        }

        private void ForgetArea()
        {
            if (!_ready) return;
            _hasArea = false;
            _areaLabel.Text = "Область не задана";
            ApplyState();
        }

        private void RequestArea()
        {
            if (_byPolyline.Checked) Request = DemAreaRequest.Polyline;
            else Request = DemAreaRequest.Window;

            DialogResult = DialogResult.Retry;
        }

        private void SourceChanged()
        {
            if (!_ready) return;

            var source = Current();
            SelectedSource = source;
            _sourceInfo.Text = source.Copyright ?? string.Empty;
            ApplyState();
        }

        private void ApplyState()
        {
            if (_crs.CRS == null)
            {
                _ok.Enabled = false;
                _hint.Text = "Выберите систему координат.";
                return;
            }

            var source = Current();
            if (source.RequiresKey && string.IsNullOrEmpty(_apiKey))
            {
                _ok.Enabled = false;
                _hint.Text = "Источник \"" + source.Name + "\" работает только по личному ключу (dem_settings).";
                return;
            }

            if (!_hasArea)
            {
                _ok.Enabled = false;
                _hint.Text = "Задайте область - укажите её на плане кнопкой справа.";
                return;
            }

            if (string.IsNullOrEmpty(Folder))
            {
                _ok.Enabled = false;
                _hint.Text = "Выберите папку для сохранения файлов.";
                return;
            }

            _ok.Enabled = true;
            _hint.Text = string.Empty;
        }

        private DemSource Current()
        {
            int index = _sourceBox.SelectedIndex;
            if (index < 0) index = 0;
            return _sources[index];
        }

        private string Caption(DemSource source)
        {
            return source.RequiresKey && string.IsNullOrEmpty(_apiKey) ? source.Name + " - нужен ключ" : source.Name;
        }
    }
}
