using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using Abr.Sdk;
using DemLoader.Core.Dem;
using DemLoader.Core.Job;
using DemLoader.Core.Sources;

namespace DemLoader.Ui
{
    /// <summary>Настройки модуля: две вкладки по образцу BASEMAPSETTINGS/Civil-версии
    /// (civil3d/demloader/Ui/SettingsDialog.cs) - «Общие» и «OpenTopography».
    ///
    /// Ключ вынесен на отдельную вкладку не ради красоты: он личный, к нему привязана личная
    /// квота, и рядом с ним должно стоять объяснение, почему модуль не может дать ключ сам.</summary>
    internal sealed class SettingsDialog : Form
    {
        private readonly NumericUpDown _maxPoints = new NumericUpDown();
        private readonly NumericUpDown _timeout = new NumericUpDown();
        private readonly TextBox _cacheRoot = new TextBox();
        private readonly Label _cacheSize = new Label();
        private readonly TextBox _apiKey = new TextBox();

        public DemSettings Result { get; private set; }

        public SettingsDialog(DemSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");

            Result = settings;

            Text = "Настройки загрузки рельефа";
            Icon = AbrIcon.Create();
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(500, 400);

            var tabs = new TabControl { Left = 8, Top = 8, Width = 484, Height = 320 };
            tabs.TabPages.Add(GeneralTab(settings));
            tabs.TabPages.Add(OpenTopoTab(settings));

            var ok = UiTheme.MakeButton("ОК", BtnKind.Primary, 85, 26);
            ok.Left = 312; ok.Top = 344; ok.DialogResult = DialogResult.OK;
            var cancel = UiTheme.MakeButton("Отмена", BtnKind.Ghost, 85, 26);
            cancel.Left = 403; cancel.Top = 344; cancel.DialogResult = DialogResult.Cancel;

            ok.Click += (s, e) =>
            {
                Result = new DemSettings
                {
                    OpenTopoApiKey = _apiKey.Text.Trim(),
                    MaxPoints = (int)_maxPoints.Value,
                    CacheRoot = _cacheRoot.Text.Trim(),
                    TimeoutMs = (int)_timeout.Value
                };

                // Пустое поле папки - не «кэш выключен», а «пользователь стёр строку»: подставляем
                // умолчание здесь же, чтобы он увидел его при следующем открытии окна.
                if (string.IsNullOrEmpty(Result.CacheRoot)) Result.CacheRoot = DemCache.DefaultRoot;
            };

            Controls.AddRange(new Control[] { tabs, ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;
        }

        // ---------- вкладки ----------

        private TabPage GeneralTab(DemSettings settings)
        {
            var pointsLabel = new Label { Text = "Потолок числа точек поверхности:", Left = 12, Top = 16, Width = 220 };
            _maxPoints.Left = 240; _maxPoints.Top = 14; _maxPoints.Width = 100;
            _maxPoints.Minimum = 1000; _maxPoints.Maximum = 5000000; _maxPoints.Increment = 10000;
            _maxPoints.ThousandsSeparator = true;
            _maxPoints.Value = Clamp(_maxPoints, settings.MaxPoints);

            var pointsNote = new Label
            {
                Left = 12, Top = 40, Width = 446, Height = 32,
                ForeColor = SystemColors.GrayText,
                Text = "Больше этого числа сетка не строится: шаг поднимается, и окно построения " +
                       "показывает, на сколько именно. Область при этом не урезается."
            };

            var timeoutLabel = new Label { Text = "Таймаут запроса, мс:", Left = 12, Top = 80, Width = 220 };
            _timeout.Left = 240; _timeout.Top = 78; _timeout.Width = 100;
            _timeout.Minimum = 1000; _timeout.Maximum = 300000; _timeout.Increment = 1000;
            _timeout.ThousandsSeparator = true;
            _timeout.Value = Clamp(_timeout, settings.TimeoutMs);

            var cacheLabel = new Label { Text = "Папка кэша тайлов:", Left = 12, Top = 112, Width = 220 };
            _cacheRoot.Left = 12; _cacheRoot.Top = 134; _cacheRoot.Width = 380;
            _cacheRoot.Text = string.IsNullOrEmpty(settings.CacheRoot) ? DemCache.DefaultRoot : settings.CacheRoot;

            var browse = UiTheme.MakeButton("...", BtnKind.Ghost, 60, 23);
            browse.Left = 398; browse.Top = 132;
            browse.Click += (s, e) =>
            {
                using (var dialog = new FolderBrowserDialog())
                    if (dialog.ShowDialog(this) == DialogResult.OK) _cacheRoot.Text = dialog.SelectedPath;
            };

            _cacheSize.Left = 12; _cacheSize.Top = 166; _cacheSize.Width = 300;
            UpdateCacheSize();

            var clear = UiTheme.MakeButton("Очистить кэш", BtnKind.Ghost, 140, 23);
            clear.Left = 318; clear.Top = 162;
            clear.Click += (s, e) => ClearCache();

            var catalogNote = new Label
            {
                Left = 12, Top = 196, Width = 446, Height = 64,
                Text = "Каталог источников правится файлом " + SourceCatalog.DefaultUserPath +
                       ". Встроенных источников два - Copernicus GLO-30 и GLO-90, ключа они не требуют. " +
                       "Выбор источника и права на данные - зона ответственности пользователя."
            };

            var page = new TabPage("Общие") { UseVisualStyleBackColor = true };
            page.Controls.AddRange(new Control[] { pointsLabel, _maxPoints, pointsNote, timeoutLabel, _timeout,
                                                   cacheLabel, _cacheRoot, browse, _cacheSize, clear, catalogNote });
            return page;
        }

        private TabPage OpenTopoTab(DemSettings settings)
        {
            var keyLabel = new Label { Text = "Личный ключ OpenTopography:", Left = 12, Top = 16, Width = 240 };

            _apiKey.Left = 12; _apiKey.Top = 38; _apiKey.Width = 300;
            _apiKey.Text = settings.OpenTopoApiKey ?? string.Empty;

            var signUp = UiTheme.MakeButton("Получить ключ", BtnKind.Ghost, 140, 23);
            signUp.Left = 318; signUp.Top = 36;
            signUp.Click += (s, e) => OpenSignUp();

            var note = new Label
            {
                Left = 12, Top = 74, Width = 446, Height = 120,
                Text = "Ключ личный, и квота на запросы тоже личная: у бесплатного ключа это " +
                       "несколько десятков запросов в сутки. Условия сервиса прямо запрещают вшивать " +
                       "ключ в приложение и раздавать его третьим лицам, поэтому модуль не может " +
                       "выдать ключ за вас - регистрация бесплатная и занимает минуту.\r\n\r\n" +
                       "Ключ хранится открытым текстом в " + DemSettingsStore.DefaultPath + "."
            };

            var sourceNote = new Label
            {
                Left = 12, Top = 200, Width = 446, Height = 64,
                ForeColor = SystemColors.GrayText,
                Text = "Самого источника OpenTopography во встроенном каталоге нет - по той же причине. " +
                       "Чтобы он появился в списке, добавьте запись с полями \"openTopoType\" и " +
                       "\"requiresKey\": true в файл " + SourceCatalog.DefaultUserPath + "."
            };

            var page = new TabPage("OpenTopography") { UseVisualStyleBackColor = true };
            page.Controls.AddRange(new Control[] { keyLabel, _apiKey, signUp, note, sourceNote });
            return page;
        }

        // ---------- кэш ----------

        private void UpdateCacheSize()
        {
            long bytes;
            try
            {
                bytes = new DemCache(_cacheRoot.Text).TotalBytes();
            }
            catch (IOException error)
            {
                _cacheSize.Text = "Размер кэша не прочитан: " + error.Message;
                return;
            }
            catch (UnauthorizedAccessException error)
            {
                _cacheSize.Text = "Размер кэша не прочитан: " + error.Message;
                return;
            }

            _cacheSize.Text = "В кэше: " + (bytes / 1048576.0).ToString("F1", CultureInfo.InvariantCulture) + " МБ";
        }

        /// <summary>Очистка спрашивает подтверждение: кэш - это уже скачанные мегабайты, и на
        /// медленном канале их повторная загрузка стоит заметного времени.</summary>
        private void ClearCache()
        {
            var answer = MessageBox.Show(this,
                "Удалить всё содержимое папки кэша?\r\n" + _cacheRoot.Text +
                "\r\n\r\nСкачанные куски тайлов придётся качать заново.",
                "Очистка кэша", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);

            if (answer != DialogResult.Yes) return;

            try
            {
                new DemCache(_cacheRoot.Text).Clear();
            }
            catch (IOException error)
            {
                MessageBox.Show(this, "Кэш очищен не полностью: " + error.Message, "Очистка кэша",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (UnauthorizedAccessException error)
            {
                MessageBox.Show(this, "Нет прав на удаление файлов кэша: " + error.Message, "Очистка кэша",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            UpdateCacheSize();
        }

        // ---------- мелочи ----------

        private void OpenSignUp()
        {
            try
            {
                // UseShellExecute обязателен: без него Process.Start(url) может бросить
                // Win32Exception на некоторых конфигурациях.
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(OpenTopoRequest.SignUpUrl) { UseShellExecute = true });
            }
            catch (System.Exception error)
            {
                // Браузер по умолчанию не назначен либо запуск запрещён политикой - показываем
                // адрес, чтобы его можно было скопировать руками, а не молчим.
                MessageBox.Show(this,
                    "Не удалось открыть браузер: " + error.Message + "\r\n\r\nАдрес регистрации:\r\n" +
                    OpenTopoRequest.SignUpUrl,
                    "Получение ключа", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private static decimal Clamp(NumericUpDown box, int value)
        {
            if (value < box.Minimum) return box.Minimum;
            if (value > box.Maximum) return box.Maximum;
            return value;
        }
    }
}
