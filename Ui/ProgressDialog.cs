using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Abr.Sdk;

namespace DemLoader.Ui
{
    /// <summary>Прогресс построения с отменой. Схема как у AbrBasemap/Civil-версии
    /// (civil3d/demloader/Ui/ProgressDialog.cs): работа идёт в потоке UI, окно оживает на
    /// Application.DoEvents внутри Report. Фонового потока нет намеренно.
    ///
    /// КРИТИЧНО (сохранить порядок при правках): в Report() сначала проверяется, не была ли
    /// отмена запрошена РАНЕЕ (ранний выход без обновления UI), затем UI обновляется и только
    /// потом вызывается DoEvents - именно в этот момент обрабатывается клик по «Отмена». Порядок
    /// не переставлять: DoEvents ДО решения "продолжать ли" оставил бы клик необработанным до
    /// следующего вызова Report, а не немедленно.
    ///
    /// Цена решения: между вызовами Report окно не отвечает. Заметнее всего на чтении тайла из
    /// сети - IByteRange.Read синхронный и токена не принимает (см. HttpTiffSource), так что
    /// нажатая «Отмена» сработает по завершении текущего запроса, а не мгновенно. Уже скачанное
    /// остаётся в кэше, повтор продолжит с того же места.</summary>
    internal sealed class ProgressDialog : Form
    {
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly ProgressBar _bar = new ProgressBar();
        private readonly Label _text = new Label();
        private readonly Button _cancel = new Button();

        public CancellationToken Token { get { return _cancellation.Token; } }

        public ProgressDialog(string caption)
        {
            Text = caption;
            Icon = AbrIcon.Create();
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(380, 110);

            _text.Left = 12; _text.Top = 12; _text.Width = 356;
            _bar.Left = 12; _bar.Top = 38; _bar.Width = 356; _bar.Maximum = 1;

            _cancel.Text = "Отмена";
            _cancel.Left = 283; _cancel.Top = 72; _cancel.Width = 85;
            _cancel.Click += (s, e) =>
            {
                _cancellation.Cancel();
                _cancel.Enabled = false;
                _text.Text = "Отмена: ждём завершения текущего запроса";
            };

            Controls.AddRange(new Control[] { _text, _bar, _cancel });
            Report(0, 1, "Подготовка");
        }

        /// <summary>total передаётся на каждый вызов, а не один раз в конструкторе: у сценария
        /// несколько стадий (тайлы, выборка отметок, построение поверхности) с разным числом шагов,
        /// известным только к началу стадии.</summary>
        public void Report(int done, int total, string stage)
        {
            if (_cancellation.IsCancellationRequested) { Application.DoEvents(); return; }

            _bar.Maximum = Math.Max(1, total);
            _bar.Value = Math.Min(_bar.Maximum, Math.Max(0, done));
            _text.Text = stage + ": " + done + " из " + total;
            Application.DoEvents();
        }
    }
}
