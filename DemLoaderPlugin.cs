using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Topomatic.ApplicationPlatform.Core;
using Topomatic.ApplicationPlatform.Plugins;
using Topomatic.Controls.Dialogs;
using Topomatic.Cad.View;
using Topomatic.Dtm;
using Topomatic.Proj.CoordinateSystems;
using DemLoader.Core.Job;
using DemLoader.Core.Sources;
using DemLoader.Core.Surface;
using DemLoader.Robur;
using DemLoader.Ui;

namespace DemLoader
{
    public partial class DemLoaderPlugin : PluginInitializator
    {
        public override void Initialize(PluginFactory factory)
        {
            base.Initialize(factory);
            try { Abr.Bootstrap.AbrBootstrap.Attach("Загрузка рельефа", null, null); }
            catch { }
        }

        [cmd("about_demloader")]
        private void About()
        {
            using (var dlg = new AboutDialog())
            {
                dlg.ShowDialog();
            }
        }

        [cmd("dem_load")]
        private void Load()
        {
            IProjectModel root = FindProjectRoot();
            if (root == null) { MessageDlg.Show("Проект не открыт."); return; }

            var sources = SourceCatalog.Load(SourceCatalog.DefaultUserPath);
            var settings = new DemSettingsStore(DemSettingsStore.DefaultPath).Load();
            var parents = CollectParents(root);

            using (var dialog = new DemDialog(sources, parents, settings.OpenTopoApiKey, settings.MaxPoints))
            {
                PickedArea area = null;

                while (true)
                {
                    DialogResult result = dialog.Run();

                    if (result == DialogResult.Cancel) return;

                    if (result == DialogResult.Ignore)
                    {
                        // "Настройки..." - полноценный SettingsDialog появится в Task 11; пока
                        // перечитываем файл настроек на случай ручной правки между показами.
                        settings = new DemSettingsStore(DemSettingsStore.DefaultPath).Load();
                        continue;
                    }

                    if (result == DialogResult.Retry)
                    {
                        var picked = PickArea(dialog.Request);
                        if (picked != null)
                        {
                            area = picked;
                            dialog.SetArea(picked, DescribeArea(picked));
                        }
                        continue;
                    }

                    if (result != DialogResult.OK) return;
                    if (area == null) continue;

                    HorizontalCoordinateSystem selectedCs = dialog.SelectedCoordSystem;
                    CoordTransform cs;
                    try
                    {
                        cs = CoordSystem.Create(selectedCs);
                    }
                    catch (CoordSystemException error)
                    {
                        MessageDlg.Show(error.Message, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
                        continue;
                    }

                    var request = new DemBuildRequest
                    {
                        Source = dialog.SelectedSource,
                        Area = area,
                        Step = dialog.Step,
                        ZShift = dialog.ZShift,
                        SurfaceName = dialog.SurfaceName,
                        CoordSystemId = selectedCs.Id,
                        ApiKey = settings.OpenTopoApiKey,
                        Parent = dialog.SelectedParent,
                        MaxPoints = settings.MaxPoints,
                        TimeoutMs = settings.TimeoutMs,
                        CacheRoot = settings.CacheRoot
                    };

                    RunLoad(request, cs);
                    return;
                }
            }
        }

        private void RunLoad(DemBuildRequest request, CoordTransform cs)
        {
            BuildResult built;
            using (var progress = new ProgressDialog("Загрузка рельефа"))
            {
                var service = new DemLoaderService(progress.Report);
                try
                {
                    built = service.Run(request, cs, progress.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            if (!built.Ok)
            {
                MessageDlg.Show(built.Error, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
                return;
            }

            MessageDlg.Show(built.Describe(), MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1);
        }

        [cmd("dem_update")]
        private void Update()
        {
            var models = FindOurTerrainModels();
            if (models.Count == 0)
            {
                MessageDlg.Show("В проекте нет моделей рельефа, созданных этим модулем.");
                return;
            }

            IProjectModel target = ModelPickDialog.Pick(models, "Обновить рельеф");
            if (target == null) return;

            DemJob job = JobStore.Load(target);
            if (job == null)
            {
                MessageDlg.Show("Не удалось прочитать задание загрузки этой модели.",
                    MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
                return;
            }

            CoordTransform cs;
            try
            {
                Guid csId = new Guid(job.CoordSystemId);
                cs = CoordSystem.Create(CoordSystem.ById(csId));
            }
            catch (FormatException)
            {
                MessageDlg.Show("В задании этой модели не сохранена система координат - обновление невозможно.",
                    MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
                return;
            }
            catch (CoordSystemException error)
            {
                MessageDlg.Show(error.Message, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
                return;
            }

            var settings = new DemSettingsStore(DemSettingsStore.DefaultPath).Load();
            var sources = SourceCatalog.Load(SourceCatalog.DefaultUserPath);

            BuildResult built;
            using (var progress = new ProgressDialog("Обновление рельефа"))
            {
                var service = new DemLoaderService(progress.Report);
                try
                {
                    built = service.RunReplace(job, sources, cs, target, settings.OpenTopoApiKey,
                        settings.MaxPoints, settings.TimeoutMs, settings.CacheRoot, progress.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            if (!built.Ok)
            {
                MessageDlg.Show(built.Error, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
                return;
            }

            MessageDlg.Show(built.Describe(), MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1);
        }

        [cmd("dem_erase")]
        private void Erase()
        {
            IProjectModel root = FindProjectRoot();
            if (root == null) { MessageDlg.Show("Проект не открыт."); return; }

            var models = FindOurTerrainModels();
            if (models.Count == 0)
            {
                MessageDlg.Show("В проекте нет моделей рельефа, созданных этим модулем.");
                return;
            }

            IProjectModel target = ModelPickDialog.Pick(models, "Удалить рельеф");
            if (target == null) return;

            string name = System.IO.Path.GetFileNameWithoutExtension(PluginCoreOps.GetFileName(target) ?? string.Empty);
            if (MessageDlg.Show("Удалить модель рельефа «" + name + "» из проекта?",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1) != DialogResult.Yes)
                return;

            try
            {
                TerrainWriter.Erase(root, target);
            }
            catch (TerrainWriteException error)
            {
                MessageDlg.Show(error.Message, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
            }
        }

        [cmd("dem_export")]
        private void Export()
        {
            var sources = SourceCatalog.Load(SourceCatalog.DefaultUserPath);
            var settings = new DemSettingsStore(DemSettingsStore.DefaultPath).Load();

            using (var dialog = new ExportDialog(sources, settings.OpenTopoApiKey))
            {
                PickedArea area = null;

                while (true)
                {
                    DialogResult result = dialog.Run();

                    if (result == DialogResult.Cancel) return;

                    if (result == DialogResult.Retry)
                    {
                        var picked = PickArea(dialog.Request);
                        if (picked != null)
                        {
                            area = picked;
                            dialog.SetArea(picked, DescribeArea(picked));
                        }
                        continue;
                    }

                    if (result != DialogResult.OK) return;
                    if (area == null) continue;

                    CoordTransform cs;
                    try
                    {
                        cs = CoordSystem.Create(dialog.SelectedCoordSystem);
                    }
                    catch (CoordSystemException error)
                    {
                        MessageDlg.Show(error.Message, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
                        continue;
                    }

                    ExportResult exported;
                    using (var progress = new ProgressDialog("Выгрузка тайлов рельефа"))
                    {
                        var service = new DemLoaderService(progress.Report);
                        try
                        {
                            exported = service.Export(area.Loop, dialog.SelectedSource, cs,
                                settings.OpenTopoApiKey, dialog.Folder, settings.TimeoutMs, progress.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                    }

                    if (!exported.Ok)
                    {
                        MessageDlg.Show(exported.Error, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
                        return;
                    }

                    MessageDlg.Show(exported.Describe(), MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1);
                    return;
                }
            }
        }

        [cmd("dem_settings")]
        private void Settings()
        {
            var store = new DemSettingsStore(DemSettingsStore.DefaultPath);
            using (var dialog = new SettingsDialog(store.Load()))
            {
                if (dialog.ShowDialog() == DialogResult.OK) store.Save(dialog.Result);
            }
        }

        // Только модели ЦММ, у которых есть наше задание загрузки: чужой рельеф
        // проекта модуль не трогает ни при обновлении, ни при удалении.
        private static List<IProjectModel> FindOurTerrainModels()
        {
            var result = new List<IProjectModel>();
            PluginCoreOps.FilterModels((Predicate<IProjectModel>)delegate (IProjectModel pm)
            {
                if (pm != null && pm.ModelType == TerrainModel.MODEL_TYPE && JobStore.IsOurs(pm))
                    result.Add(pm);
                return false;
            });
            return result;
        }

        private PickedArea PickArea(DemAreaRequest request)
        {
            CadView cadView = CadView;
            switch (request)
            {
                case DemAreaRequest.Polyline:
                    return AreaPicker.PickPolyline(cadView);
                case DemAreaRequest.Window:
                    return AreaPicker.PickFrame(cadView);
                default:
                    return null;
            }
        }

        private static string DescribeArea(PickedArea area)
        {
            switch (area.Kind)
            {
                case AreaKind.Polyline:
                    return "Контур: замкнутая полилиния, " + area.Loop.Count + " точек";
                default:
                    return "Контур: рамка";
            }
        }

        /// <summary>Все модели проекта как кандидаты в родителя новой ЦММ - корень первым
        /// (по умолчанию в комбо DemDialog).</summary>
        private static List<IProjectModel> CollectParents(IProjectModel root)
        {
            var result = new List<IProjectModel> { root };
            PluginCoreOps.FilterModels((Predicate<IProjectModel>)delegate (IProjectModel pm)
            {
                if (pm != null && pm != root) result.Add(pm);
                return false;
            });
            return result;
        }

        // Корень проекта: любая модель знает свой ModelProject, а тот - корневой узел.
        // Обход через FilterModels: предикат возвращает false - ничего не отбираем.
        private static IProjectModel FindProjectRoot()
        {
            IProjectModel root = null;
            PluginCoreOps.FilterModels((Predicate<IProjectModel>)delegate (IProjectModel pm)
            {
                if (root == null && pm != null && pm.Project != null) root = pm.Project.Model;
                return false;
            });
            return root;
        }
    }
}
