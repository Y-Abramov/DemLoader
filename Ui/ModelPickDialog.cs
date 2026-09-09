using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using Abr.Sdk;
using Topomatic.ApplicationPlatform.Core;
using Topomatic.ApplicationPlatform.Plugins;
using DemLoader.Core.Job;
using DemLoader.Robur;

namespace DemLoader.Ui
{
    /// <summary>Список НАШИХ моделей ЦММ (JobStore.IsOurs уже отфильтровал их в вызывающем коде)
    /// для dem_update/dem_erase: имя, источник, дата загрузки из задания.</summary>
    internal static class ModelPickDialog
    {
        public static IProjectModel Pick(IList<IProjectModel> models, string title)
        {
            using (var form = new Form
            {
                Text = title,
                ClientSize = new Size(440, 260),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                Icon = AbrIcon.Create()
            })
            {
                var list = new ListView
                {
                    Left = 12, Top = 12, Width = 416, Height = 200,
                    View = View.Details,
                    FullRowSelect = true,
                    MultiSelect = false,
                    HideSelection = false
                };
                list.Columns.Add("Модель", 190);
                list.Columns.Add("Источник", 100);
                list.Columns.Add("Дата загрузки", 116);

                foreach (var model in models)
                {
                    DemJob job = JobStore.Load(model);
                    var item = new ListViewItem(new[] { ModelName(model), SourceOf(job), DateOf(job) });
                    item.Tag = model;
                    list.Items.Add(item);
                }
                if (list.Items.Count > 0) list.Items[0].Selected = true;

                var ok = UiTheme.MakeButton("Выбрать", BtnKind.Primary, 85, 26);
                ok.Left = 259; ok.Top = 222; ok.DialogResult = DialogResult.OK;
                var cancel = UiTheme.MakeButton("Отмена", BtnKind.Ghost, 85, 26);
                cancel.Left = 349; cancel.Top = 222; cancel.DialogResult = DialogResult.Cancel;

                form.Controls.AddRange(new Control[] { list, ok, cancel });
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                if (form.ShowDialog() != DialogResult.OK || list.SelectedItems.Count == 0) return null;
                return (IProjectModel)list.SelectedItems[0].Tag;
            }
        }

        private static string ModelName(IProjectModel model)
        {
            string fileName = PluginCoreOps.GetFileName(model);
            return string.IsNullOrEmpty(fileName) ? "(без имени)" : Path.GetFileNameWithoutExtension(fileName);
        }

        private static string SourceOf(DemJob job)
        {
            return job == null ? string.Empty : job.SourceId;
        }

        private static string DateOf(DemJob job)
        {
            if (job == null || string.IsNullOrEmpty(job.CreatedUtc)) return string.Empty;

            DateTime moment;
            if (!DateTime.TryParse(job.CreatedUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out moment))
                return job.CreatedUtc;

            return moment.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        }
    }
}
