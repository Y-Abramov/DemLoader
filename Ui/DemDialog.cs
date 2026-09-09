using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using Abr.Sdk;
using Topomatic.ApplicationPlatform.Core;
using Topomatic.ApplicationPlatform.Plugins;
using Topomatic.Proj.CoordinateSystems;
using Topomatic.Proj.Runtime;
using DemLoader.Core.Job;
using DemLoader.Core.Sources;
using DemLoader.Core.Surface;
using DemLoader.Robur;

namespace DemLoader.Ui
{
    /// <summary>Что диалог просит сделать на плане, прежде чем продолжить.</summary>
    internal enum DemAreaRequest { None, Window, Polyline }

    /// <summary>Главное окно: источник, СК, область, шаг с живой оценкой, поправка Z, узел
    /// дерева проекта для новой модели, дисклеймер о характере данных.
    ///
    /// Область задаётся отсюда, а не до открытия окна: иначе пользователь указывает участок,
    /// ещё не выбрав источник и не увидев, во сколько точек он выльется. Указание объекта на
    /// плане при открытом модальном окне невозможно (CadCursors/DrawingLayer требуют
    /// интерактивного клика), поэтому кнопка области закрывает диалог с DialogResult.Retry,
    /// команда делает указание (AreaPicker, Task 6) и открывает окно снова через Run(), сохранив
    /// состояние. Тот же приём, что в Civil-версии (civil3d/demloader/Ui/DemDialog.cs) и в
    /// AbrBasemap, там уже проверен живьём.
    ///
    /// Оценка числа точек считается только локально (GridPlan.Create по габариту контура) -
    /// ни одного обращения в сеть до нажатия «Загрузить».
    ///
    /// Способ «Коридором вдоль трассы» убран - решение пользователя, живой гейт Task 13.
    ///
    /// ЖИВЬЁМ НЕ ПРОВЕРЯЛСЯ - полный гейт Task 13.</summary>
    internal sealed class DemDialog : Form
    {
        private readonly IList<DemSource> _sources;
        private readonly IList<IProjectModel> _parents;
        private readonly string _apiKey;
        private readonly int _maxPoints;

        private readonly ComboBox _sourceBox = new ComboBox();
        private readonly Button _settingsButton = new Button();
        private readonly Label _sourceInfo = new Label();
        private readonly Label _copyright = new Label();

        private readonly CRSSelectorButton _crs = new CRSSelectorButton();
        private readonly Label _crsLabel = new Label();

        private readonly RadioButton _byWindow = new RadioButton();
        private readonly RadioButton _byPolyline = new RadioButton();
        private readonly Button _pickButton = new Button();
        private readonly Label _areaLabel = new Label();

        private readonly NumericUpDown _step = new NumericUpDown();
        private readonly NumericUpDown _zShift = new NumericUpDown();
        private readonly TextBox _surfaceName = new TextBox();
        private readonly ComboBox _parentBox = new ComboBox();
        private readonly Label _estimate = new Label();

        private readonly Label _hint = new Label();
        private readonly Button _ok;

        private bool _hasArea;
        private double _areaWidth;
        private double _areaHeight;

        /// <summary>Способ, которым область РЕАЛЬНО указана, а не выбранный переключателем на
        /// момент нажатия «Загрузить». Разница не косметическая: смена переключателя сбрасывает
        /// область (см. AreaGroup), так что разойтись эти два значения могут только через ошибку
        /// в самом окне.</summary>
        private string _pickedKind = DemJob.KindWindow;
        private string _requestedKind = DemJob.KindWindow;

        /// <summary>Пока форма собирается, обработчики уже висят на контролах - пересчитывать
        /// оценку по недостроенному окну нельзя.</summary>
        private readonly bool _ready;

        public DemSource SelectedSource { get; private set; }
        public double Step { get { return (double)_step.Value; } }
        public double ZShift { get { return (double)_zShift.Value; } }
        public string SurfaceName { get { return _surfaceName.Text.Trim(); } }
        public IProjectModel SelectedParent { get { return Current(_parentBox, _parents); } }

        /// <summary>СК выбрана в этом же окне (родной CRSSelectorButton) - публичного API «СК
        /// проекта» в Robur нет (см. CoordSystem.cs, Task 5).</summary>
        public HorizontalCoordinateSystem SelectedCoordSystem { get { return _crs.CRS; } }

        public DemAreaRequest Request { get; private set; }
        public string AreaKind { get { return _pickedKind; } }

        public DemDialog(IList<DemSource> sources, IList<IProjectModel> parents, string apiKey, int maxPoints)
        {
            if (sources == null || sources.Count == 0) throw new ArgumentException("Каталог источников пуст.");
            if (parents == null || parents.Count == 0) throw new ArgumentException("Дерево проекта пусто.");

            _sources = sources;
            _parents = parents;
            _apiKey = apiKey;
            _maxPoints = maxPoints;

            Text = "Загрузка рельефа";
            Icon = AbrIcon.Create();
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 540);

            var sourceLabel = new Label { Text = "Источник:", Left = 12, Top = 15, Width = 70 };
            _sourceBox.Left = 88; _sourceBox.Top = 12; _sourceBox.Width = 380;
            _sourceBox.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (var source in sources) _sourceBox.Items.Add(Caption(source));
            _sourceBox.SelectedIndex = 0;
            _sourceBox.SelectedIndexChanged += (s, e) => SourceChanged();

            _settingsButton.Text = "Настройки...";
            _settingsButton.Left = 474; _settingsButton.Top = 11; _settingsButton.Width = 74; _settingsButton.Height = 24;
            UiTheme.ApplyButtonKind(_settingsButton, BtnKind.Secondary);
            _settingsButton.Visible = false;
            _settingsButton.Click += (s, e) => { Request = DemAreaRequest.None; DialogResult = DialogResult.Ignore; };

            _sourceInfo.Left = 12; _sourceInfo.Top = 44; _sourceInfo.Width = 536; _sourceInfo.Height = 18;

            _copyright.Left = 12; _copyright.Top = 64; _copyright.Width = 536; _copyright.Height = 18;
            _copyright.ForeColor = SystemColors.GrayText;

            _ok = UiTheme.MakeButton("Загрузить", BtnKind.Primary, 85, 26);
            _ok.Left = 372; _ok.Top = 500;
            _ok.DialogResult = DialogResult.OK;

            var cancel = UiTheme.MakeButton("Отмена", BtnKind.Ghost, 85, 26);
            cancel.Left = 463; cancel.Top = 500;
            cancel.DialogResult = DialogResult.Cancel;

            Controls.AddRange(new Control[] { sourceLabel, _sourceBox, _settingsButton, _sourceInfo, _copyright,
                                              CrsGroup(), AreaGroup(), ParametersGroup(), Disclaimer(), _hint });

            _hint.Left = 12; _hint.Top = 458; _hint.Width = 536; _hint.Height = 34;
            _hint.ForeColor = Color.FromArgb(120, 60, 40);

            Controls.Add(_ok);
            Controls.Add(cancel);
            AcceptButton = _ok;
            CancelButton = cancel;

            _ready = true;
            SourceChanged();
        }

        // ---------- разметка ----------

        private GroupBox CrsGroup()
        {
            var group = new GroupBox { Text = "Система координат", Left = 12, Top = 88, Width = 536, Height = 50 };

            _crs.Left = 12; _crs.Top = 18; _crs.Width = 200;
            _crs.SelectedCRSChanged += (s, e) =>
            {
                _crsLabel.Text = _crs.CRS != null ? _crs.CRS.Name : "не выбрана";
                ApplyState();
            };

            _crsLabel.Left = 220; _crsLabel.Top = 22; _crsLabel.Width = 300;
            _crsLabel.Text = "не выбрана";

            group.Controls.AddRange(new Control[] { _crs, _crsLabel });
            return group;
        }

        private GroupBox AreaGroup()
        {
            var group = new GroupBox { Text = "Область", Left = 12, Top = 144, Width = 536, Height = 80 };

            _byWindow.Text = "Рамкой"; _byWindow.Left = 12; _byWindow.Top = 22; _byWindow.Width = 190;
            _byWindow.Checked = true;
            _byPolyline.Text = "По замкнутой полилинии"; _byPolyline.Left = 12; _byPolyline.Top = 46; _byPolyline.Width = 190;

            // Смена способа обнуляет область: она была задана прежним способом, и оставить её -
            // значит построить поверхность по одному контуру, а записать в задание другой способ.
            EventHandler reset = (s, e) => { if (((RadioButton)s).Checked) ForgetArea(); };
            _byWindow.CheckedChanged += reset;
            _byPolyline.CheckedChanged += reset;

            _pickButton.Text = "Указать на плане";
            _pickButton.Left = 215; _pickButton.Top = 20; _pickButton.Width = 170; _pickButton.Height = 26;
            UiTheme.ApplyButtonKind(_pickButton, BtnKind.Secondary);
            _pickButton.Click += (s, e) => RequestArea();

            _areaLabel.Left = 215; _areaLabel.Top = 52; _areaLabel.Width = 310; _areaLabel.Height = 18;
            _areaLabel.Text = "Область не задана";

            group.Controls.AddRange(new Control[] { _byWindow, _byPolyline, _pickButton, _areaLabel });
            return group;
        }

        private GroupBox ParametersGroup()
        {
            var group = new GroupBox { Text = "Параметры", Left = 12, Top = 230, Width = 536, Height = 150 };

            var stepLabel = new Label { Text = "Шаг сетки, м:", Left = 12, Top = 26, Width = 120 };
            _step.Left = 140; _step.Top = 24; _step.Width = 80;
            _step.Minimum = 1; _step.Maximum = 10000; _step.DecimalPlaces = 0; _step.Increment = 5;

            // По умолчанию - родное разрешение первого источника: шаг мельче него всё равно
            // запрещён (GridPlan), а крупнее пользователь поставит сам, увидев число точек.
            decimal wanted = (decimal)Math.Round(_sources[0].StepMetres);
            if (wanted < _step.Minimum) wanted = _step.Minimum;
            if (wanted > _step.Maximum) wanted = _step.Maximum;
            _step.Value = wanted;

            _step.ValueChanged += (s, e) => UpdateEstimate();

            _estimate.Left = 240; _estimate.Top = 22; _estimate.Width = 284; _estimate.Height = 44;

            var zLabel = new Label { Text = "Поправка Z, м:", Left = 12, Top = 58, Width = 120 };
            _zShift.Left = 140; _zShift.Top = 56; _zShift.Width = 80;
            _zShift.Minimum = -1000; _zShift.Maximum = 1000; _zShift.Value = 0;
            _zShift.DecimalPlaces = 2; _zShift.Increment = 0.1M;

            var nameLabel = new Label { Text = "Имя поверхности:", Left = 12, Top = 90, Width = 120 };
            _surfaceName.Left = 140; _surfaceName.Top = 88; _surfaceName.Width = 384;
            _surfaceName.Text = "Рельеф " + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var parentLabel = new Label { Text = "Узел дерева:", Left = 12, Top = 122, Width = 120 };
            _parentBox.Left = 140; _parentBox.Top = 120; _parentBox.Width = 384;
            _parentBox.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (var parent in _parents) _parentBox.Items.Add(ParentCaption(parent));
            _parentBox.SelectedIndex = 0;

            group.Controls.AddRange(new Control[] { stepLabel, _step, _estimate, zLabel, _zShift,
                                                    nameLabel, _surfaceName, parentLabel, _parentBox });
            return group;
        }

        /// <summary>Дисклеймер стоит в самом окне, а не только в «О модуле»: решение о том, годятся
        /// ли эти отметки под задачу, принимается здесь и сейчас, а не при чтении справки.</summary>
        private static Label Disclaimer()
        {
            return new Label
            {
                Left = 12, Top = 384, Width = 536, Height = 46,
                Text = "Данные - цифровая модель поверхности (DSM): в отметки входят кроны деревьев и крыши, " +
                       "а не земля под ними. Высоты отсчитываются от геоида EGM2008, а не от Балтийской системы - " +
                       "расхождение систематическое, для него и поправка Z. Подложка для посадки объекта и " +
                       "предварительных объёмов, не основа проектных отметок."
            };
        }

        // ---------- состояние ----------

        /// <summary>Показать окно. Вызывается повторно после указания области на плане, поэтому
        /// сбрасывает прошлый ответ - иначе оставшийся Retry закрыл бы окно сразу.</summary>
        public DialogResult Run()
        {
            Request = DemAreaRequest.None;
            DialogResult = DialogResult.None;
            return ShowDialog();
        }

        /// <summary>Область указана на плане: запоминается только габарит - оценке числа точек
        /// больше ничего не нужно, а сам контур живёт у команды.</summary>
        public void SetArea(PickedArea area, string description)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var p in area.Loop)
            {
                if (p[0] < minX) minX = p[0];
                if (p[0] > maxX) maxX = p[0];
                if (p[1] < minY) minY = p[1];
                if (p[1] > maxY) maxY = p[1];
            }

            _areaWidth = maxX - minX;
            _areaHeight = maxY - minY;
            _hasArea = _areaWidth > 0.0 && _areaHeight > 0.0;
            _areaLabel.Text = _hasArea ? description : "Область имеет нулевой размер";

            // Способ фиксируется в момент, когда контур получен, а не при нажатии «ОК».
            _pickedKind = _requestedKind;

            UpdateEstimate();
        }

        private void ForgetArea()
        {
            if (!_ready) return;

            _hasArea = false;
            _areaWidth = 0.0;
            _areaHeight = 0.0;
            _areaLabel.Text = "Область не задана";
            UpdateEstimate();
        }

        private void RequestArea()
        {
            if (_byPolyline.Checked) { Request = DemAreaRequest.Polyline; _requestedKind = DemJob.KindPolyline; }
            else { Request = DemAreaRequest.Window; _requestedKind = DemJob.KindWindow; }

            DialogResult = DialogResult.Retry;
        }

        private void SourceChanged()
        {
            if (!_ready) return;

            var source = Current(_sourceBox, _sources);
            SelectedSource = source;

            _sourceInfo.Text = "Разрешение источника: " + Metres(source.StepMetres) + ". " +
                               (string.IsNullOrEmpty(source.SurfaceKind) ? "DSM" : source.SurfaceKind) + ", " +
                               (string.IsNullOrEmpty(source.VerticalDatum) ? "EGM2008" : source.VerticalDatum) + ".";
            _copyright.Text = source.Copyright ?? string.Empty;

            _settingsButton.Visible = NeedsKey(source);

            UpdateEstimate();
        }

        /// <summary>Оценка числа точек. Считается локально - GridPlan.Create и арифметика по
        /// габариту области; в сеть на этом шаге не ходим вовсе.</summary>
        private void UpdateEstimate()
        {
            if (!_ready) return;

            var source = Current(_sourceBox, _sources);
            SelectedSource = source;

            if (!_hasArea)
            {
                _estimate.Text = "Оценка появится после выбора области.";
                ApplyState();
                return;
            }

            try
            {
                var plan = GridPlan.Create(_areaWidth, _areaHeight, (double)_step.Value, source.StepMetres, _maxPoints);
                _estimate.Text = Describe(plan);
            }
            catch (ArgumentException error)
            {
                _estimate.Text = "Оценка не считается: " + error.Message;
            }
            catch (InvalidOperationException error)
            {
                _estimate.Text = "Оценка не считается: " + error.Message;
            }

            ApplyState();
        }

        /// <summary>«190 тыс. точек, шаг 30 м», а при автоподъёме шага - «шаг 30 м -> 60 м,
        /// 190 тыс. точек» и строка с причиной. Молча усечь область или подменить шаг нельзя:
        /// пользователь получил бы поверхность не той плотности, о которой просил, и не узнал бы
        /// об этом никогда.</summary>
        private string Describe(GridPlan plan)
        {
            var text = new StringBuilder();

            if (plan.StepRaisedToNative || plan.StepRaisedForLimit)
            {
                text.Append("шаг ").Append(Number(plan.RequestedStep)).Append(" м -> ")
                    .Append(Number(plan.Step)).Append(" м, ").Append(Points(plan.PointCount));
                text.Append(Environment.NewLine);

                text.Append(plan.StepRaisedForLimit
                    ? "область больше лимита в " + Points(_maxPoints)
                    : "мельче разрешения источника (" + Metres(plan.Step) + ") сетку не строим");
            }
            else
            {
                text.Append(Points(plan.PointCount)).Append(", шаг ").Append(Number(plan.Step)).Append(" м");
            }

            return text.ToString();
        }

        /// <summary>Одна причина отказа за раз, в порядке значимости: без системы координат не
        /// поможет ни ключ, ни область.</summary>
        private void ApplyState()
        {
            if (_crs.CRS == null)
            {
                _ok.Enabled = false;
                _hint.Text = "Выберите систему координат, в которой построить рельеф.";
                return;
            }

            var source = Current(_sourceBox, _sources);
            if (NeedsKey(source))
            {
                _ok.Enabled = false;
                _hint.Text = "Источник \"" + source.Name + "\" работает только по личному ключу. " +
                             "Ключ вводится в настройках модуля, регистрация бесплатная: " + OpenTopoRequest.SignUpUrl;
                return;
            }

            if (!_hasArea)
            {
                _ok.Enabled = false;
                _hint.Text = "Задайте область - укажите её на плане кнопкой справа.";
                return;
            }

            _ok.Enabled = true;
            _hint.Text = string.Empty;
        }

        // ---------- мелочи ----------

        private static T Current<T>(ComboBox box, IList<T> items)
        {
            int index = box.SelectedIndex;
            if (index < 0) index = 0;
            return items[index];
        }

        private bool NeedsKey(DemSource source)
        {
            return source.RequiresKey && string.IsNullOrEmpty(_apiKey);
        }

        private string Caption(DemSource source)
        {
            return NeedsKey(source) ? source.Name + " - нужен ключ" : source.Name;
        }

        private static string ParentCaption(IProjectModel model)
        {
            string fileName = PluginCoreOps.GetFileName(model);
            return string.IsNullOrEmpty(fileName)
                ? "(корень проекта)"
                : System.IO.Path.GetFileNameWithoutExtension(fileName);
        }

        private static string Points(int count)
        {
            return count >= 10000
                ? (count / 1000.0).ToString("F0", CultureInfo.InvariantCulture) + " тыс. точек"
                : count.ToString(CultureInfo.InvariantCulture) + " точек";
        }

        private static string Number(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string Metres(double value)
        {
            return Number(value) + " м";
        }
    }
}
