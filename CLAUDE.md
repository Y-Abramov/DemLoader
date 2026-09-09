# DemLoader — детали модуля

> «Загрузка рельефа»: обвести участок на плане (рамкой или замкнутой полилинией) и получить
> готовую ЦММ в дереве проекта из открытых данных (Copernicus DEM, OpenTopography) - без ручного
> поиска, скачивания и сшивки тайлов. Способ «коридором вдоль трассы» был реализован и убран
> решением пользователя на живом гейте Task 13 (2026-09-05) - слишком узкий сценарий для v1.

**Assembly:** `Abr.DemLoader` | **Namespace:** `DemLoader` | **Версия:** 1.0.0 | **net48**
**Статус:** 🚧 разработка - код реализован (Task 1-11), живой гейт конвейера - Task 13

Перенос ядра из линейки Civil (`civil3d/demloader/`), Robur-слой написан заново. Линейки
изолированы: общего кода между `civil3d/` и Robur-модулями нет (копия, не общая сборка).

---

## Зарегистрированные команды

| Команда | Описание |
|---------|----------|
| `dem_load` | Диалог загрузки: область, источник, СК, шаг, поправка Z, узел дерева - строит новую ЦММ |
| `dem_update` | Перекачивает рельеф по сохранённому заданию, заменяет содержимое существующей модели |
| `dem_erase` | Удаляет модель рельефа, созданную модулем (с подтверждением) |
| `dem_export` | Сохраняет сырые GeoTIFF по области на диск, без построения поверхности |
| `dem_settings` | Ключ OpenTopography, лимит точек, папка кэша |
| `about_demloader` | Диалог «О модуле» |

---

## Дорогие факты (см. отчёт спайка Task 2 за полными подробностями)

Спека: `docs/superpowers/specs/2026-09-05-robur-dem-loader-design.md`
Отчёт живого спайка: `docs/superpowers/spikes/2026-09-05-robur-terrain-model-write.md`

### Материализация модели: LockWrite() одного недостаточно

`PluginCoreOps.CreateModel(parent, TerrainModel.MODEL_TYPE, name)` создаёт узел в дереве проекта,
но **`node.Model` остаётся `null` после `LockWrite()` без исключения**. Материализуется только
после `node.LockRead()`. Пропуск этого вызова даёт тихий `null` дальше по каскаду проверок, а не
явную ошибку - живьём подтверждено спайком, воспроизводилось трижды подряд, пока не нашли фикс.

```csharp
node.LockWrite();
try
{
    node.LockRead();                          // обязателен, иначе node.Model == null
    var terrain = node.Model as TerrainModel; // только теперь не null
    ...
}
finally { node.UnlockWrite(); }
```

### surface.BeginUpdate()/EndUpdate() - обязательная пара

**Самая дорогая находка спайка.** Запись без парного `EndUpdate()` (`IUpdatable`, `Surface`
реализует его через `UndoObject`) выглядит полностью успешной в текущей runtime-сессии -
`Points.Count`/`Triangles.Count` совпадают с ожиданием, поверхность рисуется на плане - но **не
переживает `Save`/перезапуск Robur**. После перезапуска `Surface.Points.Count == 0`. Ни одна
проверка в моменте эту потерю не ловит - обнаружилось только вторым живым прогоном
(`dem_spike_check`, ищет модель по URI через дерево файлов после перезапуска).

```csharp
surface.BeginUpdate();
try { /* Points.Add / Triangles.Add */ }
finally { surface.EndUpdate(); }   // без этого - тихая потеря данных при Save
```

### DynamicCachedBuilder не годится для регулярной DEM-сетки

`Topomatic.Cad.Foundation.Triangulation.DynamicCachedBuilder` - Делоне-триангулятор общего
назначения. На регулярной сетке 10x10 живой прогон дал 202 треугольника вместо ожидаемых
`2*(10-1)*(10-1)=162` и уронил `Invalidate()`/`Regen()` исключением `ArgumentOutOfRangeException`
- похоже, добавляет служебные вершины сверх переданных точек, и их индексы вылетают за границы
`surface.Points`. Заменён на свой `Core/Surface/GridMesh.BuildTriangles` (Task 4, 6/6 тестов) -
для регулярной сетки с возможными "дырками" (узлы вне контура/без данных источника) он и
предназначен, гарантирует индексы 1:1 со списком точек по построению.

### Лицензионный гейт Topomatic.Sfc - тихий отказ, не исключение

Методы записи `Surface.Points`/`Triangles` при отсутствии нужной лицензионной фичи **молча не
делают ничего**, без исключения. `TerrainWriter.Fill` поэтому сверяет счётчики после каждой
пакетной записи и бросает `TerrainWriteException` при расхождении - иначе в дереве проекта
появилась бы пустая модель без единого сообщения об ошибке. Живьём (спайк) лицензия не резала
запись - 100/100 точек, 162/162 треугольника.

### IProjectModel.Remove - метод родителя, не удаляемого узла

`IProjectModel.Remove(IProjectModel model, bool removeFile)` вызывается на РОДИТЕЛЬСКОМ узле,
принимает удаляемого ребёнка аргументом (`parent.Remove(node, true)`), не на самой модели.

**`PluginCoreOps.FindFolderModel(node)` не годится для поиска этого родителя** - живой гейт
Task 13 дал «Не найден родительский узел модели» для ЦММ, созданной прямо в корне проекта.
Внутри `FindFolderModel` сравнивает `node.Uri.DirectoryUri` (путь к папке) с `Uri` кандидатов -
но `Uri` самого КОРНЯ проекта - это путь к файлу `.rbprojx`, не к папке, и буквально не совпадает
с `DirectoryUri` дочерней модели. `TerrainWriter.Erase(root, node)` ищет родителя своим
рекурсивным обходом `GetChilds()` от явно переданного корня (`FindParent`, сравнение по
`Uri.AsAbsoluteUri`) - тот же приём, что уже проверен в `dem_spike_check`.

### Атрибуты модели переживают перезапуск - фолбэк на файл не нужен

`ModelProject.SetUserModelAttributes`/`GetUserModelAttributes` подтверждены живьём: атрибут,
записанный до перезапуска Robur, читается после без изменений. `JobStore` (Task 7) хранит задание
загрузки (`DemJob` через `DemJobJson`) прямо в атрибутах модели, без резервного JSON-файла рядом.

### Публичного API «СК проекта» в Robur нет

СК спрашивается у пользователя в диалоге (родной `Topomatic.Proj.Runtime.CRSSelectorButton`), а
не читается из проекта - единственный реализатор `ICRSSettings` (`Topomatic.Proj.Runtime`) не
даёт получить активную СК проекта программно. `CoordSystem.Create`/`ById` (Task 5, `Robur/
CoordSystem.cs`) строят преобразование по явно выбранной пользователем СК; идентификатор
(`HorizontalCoordinateSystem.Id`, Guid) хранится в задании, чтобы `dem_update` работал в той же
СК, что и первая загрузка. `ConversedCoordinateSystem` (тип `CRSSelectorButton.CRS`) наследует
`HorizontalCoordinateSystem` - upcast без проблем.

### Поиск моделей после перезапуска - не через FilterModels/FilterOpenedModels

`PluginCoreOps.FilterModels`/`FilterOpenedModels` видят только уже открытые/закешированные модели
текущей сессии. После перезапуска Robur, до клика по узлу в дереве, "холодная" модель им
неизвестна. Живой поиск после перезапуска - только рекурсивный обход дерева ФАЙЛОВ проекта через
`IProjectModel.GetChilds()` (осторожно: может вернуть `null`, а не пустой массив, для части типов
моделей - см. `Robur/TerrainWriter.cs`, `FindParent`).

`FindOurTerrainModels` (Task 10, для `dem_update`/`dem_erase`) использует `FilterModels` - в
рамках одной рабочей сессии (юзер только что создал модель) этого достаточно; "холодный" сценарий
там не актуален.

### WGS-84 в реестре СК - искать по EPSG:4326, не по имени датума

Живой гейт Task 13: «В реестре систем координат Robur не найдена WGS-84» - поиск по имени
(`HorizontalDatum.Name` содержит `"WGS"` и `"84"`) не нашёл геосистему в РЕАЛЬНОМ реестре, хотя
рефлексия по SDK подтверждала структуру API. Та же ловушка, что уже была на AbrBasemap
(`feedback_reflection_shows_shape_not_failure`) - сигнатуры типов не гарантируют, что реальные
данные названы ожидаемо. `CoordSystem.FindWgs84` теперь ищет по `Authority.Code == "4326"`
(`Authority.Name == "EPSG"`) - language/format-независимый идентификатор; поиск по имени датума
оставлен запасным путём. Если оба не сработают, исключение перечисляет весь реестр геосистем.

### FrameCursor - резинка при выборе рамки

Два прямых вызова `CadCursors.GetPoint` подряд не дают пользователю визуальной резинки
(rubber-band) между первым и вторым углом рамки - живой гейт Task 13 отметил «не показывается
рамка при выборе». `Topomatic.Cad.View.Hints.FrameCursor(cadView, prompt, firstPoint).GetFrame()`
рисует резинку сам; результат `GetPointResult.Accept`/`Cancel`, углы - `FirstPoint`/`SecondPoint`.

---

## Архитектура

```
demloader/Core/          — чистое ядро, НИ ОДНОЙ ссылки на Topomatic.* или Robur; тестируется
                            без Robur (перенос из civil3d/demloader/Core, копия, не общий код)
demloader/Core/Surface/  — GridMesh (триангуляция регулярной сетки), Polygon (point-in-polygon) -
                            новые для Robur-версии, покрыты тестами
demloader/Robur/         — мост: Core <-> Topomatic.* (CoordSystem, AreaPicker, TerrainWriter,
                            JobStore, DemLoaderService)
demloader/Ui/            — диалоги (DemDialog, ExportDialog, SettingsDialog, ModelPickDialog,
                            ProgressDialog, AboutDialog)
```

**Core (`DemLoader.Core.*`):** без изменений логики при переносе, кроме namespace
(`AbrCivil.DemLoader.Core.*` → `DemLoader.Core.*`, `AbrCivil.Geo.*` → `DemLoader.Core.Geo`/`Net`).
Полный список - Task 3 плана. `SourceCatalog.BuiltInRaw` ищет встроенный ресурс по СУФФИКСУ имени
(`builtin-sources.json`), не по точному логическому имени - тестовый проект линкует Core
напрямую, без общего `RootNamespace` с основной сборкой.

**Robur/CoordSystem.cs** - реестр СК Robur (`ProjEngine.Current.HorizontalCoordinateSystems`) и
преобразование в WGS-84/обратно. Три расхождения с черновиком плана, найденные рефлексией по
SDK 16.0.62.12 (не спайком): `GeographicCoordinateSystem.HorizontalDatum`, не `.Datum`;
`CoordinateTransformationFactory` - статический класс; `MathTransform.Transform(double,double)`
как единственная скалярная перегрузка (нет `Transform(XY)`).

**Robur/AreaPicker.cs** - рамка (`CadCursors.GetPoint` + `FrameCursor` для резинки, см. находку
живого гейта выше), полилиния (`DrawingLayer.SelectOneEntity` + `DwgPolyline.ConvertToPosArray` -
свойства `Vertices` у `DwgPolyline` нет). `PickedArea` - неизменяемый результат: вид и геометрия
фиксируются в момент выбора. Способ «коридор вдоль трассы» (`PickCorridor`, требовал выбора дороги
через `RoadAccess.cs` - перенос `AbrRunoff.Robur.RoadAccess`) реализован и удалён решением
пользователя на живом гейте Task 13 вместе с `Core/Surface/Corridor.cs` и его тестами.

**Robur/TerrainWriter.cs** - создание/замена/удаление ЦММ, сверка счётчиков после каждой пакетной
записи (см. «Лицензионный гейт» выше). Триангуляция - только `GridMesh`, не `DynamicCachedBuilder`.

**Robur/JobStore.cs** - `DemJob` (перенесённый из Civil, поле `CoordSystemId` добавлено в Task 7)
через `SetUserModelAttributes`/`GetUserModelAttributes`. `IsOurs` - есть ли задание у модели.

**Robur/DemLoaderService.cs** - конвейер: контур → `GeoBox` (через `CoordTransform.ToLatLon` -
ПОРЯДОК ВЫХОДНЫХ ПАРАМЕТРОВ `(lat, lon)`, легко перепутать при портировании из Civil, где было
`(lon, lat)`) → `DemTilePlan`/тайлы → `GridPlan` → `WarpGrid` (редкая сетка соответствия, 32x32
узла - точная трансформация Robur дорогая, дёргается только в узлах) → `PointSet` (отбраковка вне
контура/без данных, поправка Z) → `TerrainWriter`. Общая часть (`ComputeSample`) разделяется
между `Run` (создание, из диалога) и `RunReplace` (`dem_update`, из сохранённого `DemJob` - контур
и параметры берутся из задания, диалог не открывается). `Export` - отдельный путь мимо
сетки/поверхности, для `dem_export`.

**Ui/ProgressDialog.cs** - перенесён из Civil-версии как есть (не переписывался), сохраняет
порядок проверки токена отмены и `Application.DoEvents()`: сначала пампинг сообщений (клик по
«Отмена» обрабатывается здесь), решение «продолжать» - после. Переизобретение этого порядка с
нуля - типичный источник бага «Отмена почти никогда не срабатывает вовремя».

---

## Известные упрощения v1 (сознательно, не баги)

- Способ «коридор вдоль трассы» убран решением пользователя (живой гейт Task 13, 2026-09-05) -
  два способа в v1: рамка и замкнутая полилиния. `DemJob.KindCorridor`/`CorridorWidth`/
  `AlignmentId` остались в Core как поля формата (совместимость с Civil-версией и старыми
  заданиями), но Robur-слой их больше не заполняет и не читает активно.
- Узел дерева для новой модели - плоский список всех моделей проекта (`PluginCoreOps.
  FilterModels`), не иерархический выбор папки. Разделение "папка" vs "модель" не наблюдается
  через `IProjectModel` API напрямую.
- Найдена, но не используется: штатный «Импорт GeoTiff...» Robur
  (`Topomatic.Extentions.Controller.GeoTIFF.GeoTIFFProvider.Import(Surface, string)`) - класс
  `internal`, требует рефлексии, не умеет тянуть/сшивать тайлы из Copernicus/OpenTopography.
  Решает только последний шаг конвейера ("готовый локальный файл → поверхность"), не всю задачу
  модуля. Также найден и задокументирован (не используется) `LandXMLFormatProvider.Import` -
  публичный, стандартный формат LandXML, потенциальный запасной путь при проблемах с прямой
  записью Points/Triangles на больших объёмах.

---

## Сборка

```powershell
dotnet build demloader/DemLoader.csproj -c Debug
dotnet test Tests/DemLoader.Tests/DemLoader.Tests.csproj
```

`.tpm` - `build-tpm.ps1` (юзер запускает сам, Claude - никогда). Установка - только через
AbrModules.

## Документы

- Спека: `docs/superpowers/specs/2026-09-05-robur-dem-loader-design.md`
- План: `docs/superpowers/plans/2026-09-05-robur-dem-loader.md`
- Отчёт спайка: `docs/superpowers/spikes/2026-09-05-robur-terrain-model-write.md`
