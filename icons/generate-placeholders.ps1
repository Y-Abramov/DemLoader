# Генератор иконок ленты модуля «Загрузка рельефа» (DemLoader).
# Рисует каждую иконку из одного векторного описания во все требуемые размеры:
#   {name}_{16|32}dp_{1x|1.5x|2x|2.5x|3x}.png
# Техника (helpers, размерная сетка 16/32dp x 1x..3x) взята из runoff/tools/gen_icons.ps1.
# Плацехолдеры (решение юзера, Task 1 плана 2026-09-05-robur-dem-loader.md) - юзер
# перерисует по желанию позже. Правки вносить В ГЕНЕРАТОР, а не подрисовывать PNG вручную.

Add-Type -AssemblyName System.Drawing

$Out = [System.IO.Path]::GetDirectoryName($MyInvocation.MyCommand.Path)

# ── палитра (канон ABR + акценты статусов) ─────────────────────────────────
$BLUE   = '#0891B2'   # канон ABR
$BLUE_L = '#3FC7E3'   # светлый акцент
$GREEN  = '#10AF6A'   # успех/готово
$AMBER  = '#AF8F10'   # параметры
$RED    = '#AF1010'   # удаление/ошибка
$GRAY   = '#939393'   # вспомогательное
$DARK   = '#373636'   # контур
$WHITE  = '#FFFFFF'

function C([string]$hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

# ── примитивы (координаты нормированы 0..1, Y вниз) ───────────────────────
function Line($g, $S, $x1, $y1, $x2, $y2, $hex, $w) {
  $pen = New-Object System.Drawing.Pen((C $hex), [float]($w * $S))
  $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
  $g.DrawLine($pen, [float]($x1*$S), [float]($y1*$S), [float]($x2*$S), [float]($y2*$S))
  $pen.Dispose()
}

function Poly($g, $S, $pts, $hex, $w, [bool]$fill) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $arr = @()
  for ($i = 0; $i -lt $pts.Count; $i += 2) {
    $arr += New-Object System.Drawing.PointF([float]($pts[$i]*$S), [float]($pts[$i+1]*$S))
  }
  if ($fill) {
    $p.AddPolygon($arr)
    $b = New-Object System.Drawing.SolidBrush((C $hex)); $g.FillPath($b, $p); $b.Dispose()
  } else {
    $p.AddLines($arr)
    $pen = New-Object System.Drawing.Pen((C $hex), [float]($w*$S))
    $pen.StartCap='Round'; $pen.EndCap='Round'; $pen.LineJoin='Round'
    $g.DrawPath($pen, $p); $pen.Dispose()
  }
  $p.Dispose()
}

function Disc($g, $S, $cx, $cy, $r, $hex) {
  $b = New-Object System.Drawing.SolidBrush((C $hex))
  $g.FillEllipse($b, [float](($cx-$r)*$S), [float](($cy-$r)*$S), [float](2*$r*$S), [float](2*$r*$S))
  $b.Dispose()
}

function Ring($g, $S, $cx, $cy, $r, $hex, $w) {
  $pen = New-Object System.Drawing.Pen((C $hex), [float]($w*$S))
  $g.DrawEllipse($pen, [float](($cx-$r)*$S), [float](($cy-$r)*$S), [float](2*$r*$S), [float](2*$r*$S))
  $pen.Dispose()
}

# Стрелка: линия + залитый наконечник в конце.
function Arrow($g, $S, $x1, $y1, $x2, $y2, $hex, $w) {
  $dx = $x2-$x1; $dy = $y2-$y1
  $len = [Math]::Sqrt($dx*$dx + $dy*$dy)
  if ($len -lt 1e-6) { return }
  $ux = $dx/$len; $uy = $dy/$len
  $head = $w * 2.6
  $bx = $x2 - $ux*$head; $by = $y2 - $uy*$head
  Line $g $S $x1 $y1 $bx $by $hex $w
  $nx = -$uy; $ny = $ux
  Poly $g $S @($x2,$y2, ($bx+$nx*$head*0.45),($by+$ny*$head*0.45), ($bx-$nx*$head*0.45),($by-$ny*$head*0.45)) $hex 0 $true
}

# Шестерня: диск + N залитых радиальных зубьев (трапеция, шире у основания) + кольцо-паз + центр.
function Gear($g, $S, $cx, $cy, $rOuter, $toothW, $toothH, $hex, $accentHex, $rInner, $w) {
  $rBase = $rOuter - $toothH*0.35
  Disc $g $S $cx $cy $rBase $hex
  $yTop = -($rBase + $toothH*0.65)
  $yBot = -($rBase - $toothH*0.35)
  for ($i = 0; $i -lt 8; $i++) {
    $angle = $i * 45 * [Math]::PI / 180.0
    $state = $g.Save()
    $g.TranslateTransform([float]($cx*$S), [float]($cy*$S))
    $g.RotateTransform([float]($angle * 180.0 / [Math]::PI))
    Poly $g $S @(($toothW*-0.35),$yTop, ($toothW*0.35),$yTop, ($toothW*0.5),$yBot, ($toothW*-0.5),$yBot) $hex 0 $true
    $g.Restore($state)
  }
  Disc $g $S $cx $cy $rInner $accentHex
}

# ── описания иконок (0..1) ─────────────────────────────────────────────────
$Icons = @{

  # Модуль: силуэт рельефа (горная гряда).
  'dem_module' = {
    param($g,$S)
    Poly $g $S @(0.10,0.82, 0.30,0.40, 0.42,0.58, 0.58,0.30, 0.90,0.82) $DARK 0.075 $false
    Disc $g $S 0.72 0.24 0.10 $BLUE_L
  }

  # Загрузка: рельеф + стрелка вниз (данные приходят в проект).
  'dem_load' = {
    param($g,$S)
    Poly $g $S @(0.10,0.86, 0.30,0.52, 0.42,0.66, 0.58,0.44, 0.90,0.86) $DARK 0.075 $false
    Arrow $g $S 0.50 0.08 0.50 0.32 $BLUE 0.095
  }

  # Обновление: круговая стрелка поверх намёка на рельеф.
  'dem_update' = {
    param($g,$S)
    Line $g $S 0.14 0.82 0.86 0.82 $GRAY 0.055
    $pen = New-Object System.Drawing.Pen((C $BLUE), [float](0.11*$S))
    $cap = New-Object System.Drawing.Drawing2D.AdjustableArrowCap([float]2.5, [float]2.5)
    $pen.CustomEndCap = $cap
    $rect = New-Object System.Drawing.RectangleF([float](0.20*$S), [float](0.14*$S), [float](0.60*$S), [float](0.60*$S))
    $g.DrawArc($pen, $rect, 35, 270)
    $pen.Dispose()
  }

  # Удаление: рельеф + крест поверх.
  'dem_erase' = {
    param($g,$S)
    Poly $g $S @(0.10,0.82, 0.30,0.48, 0.42,0.62, 0.58,0.40, 0.90,0.82) $GRAY 0.070 $false
    Line $g $S 0.28 0.28 0.72 0.72 $RED 0.110
    Line $g $S 0.72 0.28 0.28 0.72 $RED 0.110
  }

  # Выгрузка тайлов: рельеф + стрелка вверх из проекта наружу.
  'dem_export' = {
    param($g,$S)
    Poly $g $S @(0.10,0.90, 0.30,0.62, 0.42,0.74, 0.58,0.56, 0.90,0.90) $GRAY 0.070 $false
    Arrow $g $S 0.50 0.52 0.50 0.10 $BLUE 0.095
  }

  # Настройки: шестерня.
  'dem_settings' = {
    param($g,$S)
    Gear $g $S 0.50 0.50 0.26 0.16 0.14 $DARK $BLUE 0.11 0.065
  }
}

# ── отрисовка во все размеры ──────────────────────────────────────────────
$Variants = @(
  @{ dp = 16; scale = '1x';   px = 16 }, @{ dp = 16; scale = '1.5x'; px = 24 }
  @{ dp = 16; scale = '2x';   px = 32 }, @{ dp = 16; scale = '2.5x'; px = 40 }
  @{ dp = 16; scale = '3x';   px = 48 }
  @{ dp = 32; scale = '1x';   px = 32 }, @{ dp = 32; scale = '1.5x'; px = 48 }
  @{ dp = 32; scale = '2x';   px = 64 }, @{ dp = 32; scale = '2.5x'; px = 80 }
  @{ dp = 32; scale = '3x';   px = 96 }
)

$made = 0
foreach ($name in $Icons.Keys) {
  foreach ($v in $Variants) {
    $S  = $v.px
    $bm = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g  = [System.Drawing.Graphics]::FromImage($bm)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    & $Icons[$name] $g $S

    $g.Dispose()
    $file = Join-Path $Out ("{0}_{1}dp_{2}.png" -f $name, $v.dp, $v.scale)
    $bm.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $bm.Dispose()
    $made++
  }
}

Write-Host ("Готово: " + $made + " файлов")
