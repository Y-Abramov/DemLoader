using System;

namespace DemLoader.Core.Surface
{
    /// <summary>Шаг целевой сетки и число узлов.
    ///
    /// Сетка регулярна в системе координат чертежа, а не в градусах: на 56 градусах широты
    /// градусная сетка даёт шаг по X вдвое мельче, чем по Y - лишние точки и вытянутые
    /// треугольники. Шаг мельче родного разрешения источника запрещён: интерполяция DEM до 5 м
    /// производит мусор с видом точности.</summary>
    internal sealed class GridPlan
    {
        public readonly double Step;
        public readonly int Columns;
        public readonly int Rows;
        public readonly bool StepRaisedToNative;
        public readonly bool StepRaisedForLimit;
        public readonly double RequestedStep;

        private GridPlan(double step, int columns, int rows, bool toNative, bool forLimit, double requested)
        {
            Step = step;
            Columns = columns;
            Rows = rows;
            StepRaisedToNative = toNative;
            StepRaisedForLimit = forLimit;
            RequestedStep = requested;
        }

        public int PointCount { get { return Columns * Rows; } }

        public static GridPlan Create(double width, double height, double requestedStep,
            double nativeStep, int maxPoints)
        {
            if (width <= 0 || height <= 0) throw new ArgumentException("Область имеет нулевой размер.");
            if (nativeStep <= 0) throw new ArgumentException("Разрешение источника не задано.");
            if (maxPoints < 4) throw new ArgumentException("Лимит точек слишком мал.");

            double step = requestedStep;
            bool toNative = false;

            if (step < nativeStep) { step = nativeStep; toNative = true; }

            bool forLimit = false;
            int columns = Count(width, step), rows = Count(height, step);

            // Шаг растёт кратно родному: 30, 60, 90. Дробный множитель дал бы узлы, не совпадающие
            // с узлами источника, и лишнюю интерполяцию без выигрыша в точности.
            //
            // Итерации ограничены: при вменяемом nativeStep (30-90 м, площадки км-масштаба) цикл
            // завершается за десятки шагов - рост step быстро схлопывает columns*rows до 1. Но при
            // патологически малом nativeStep (битый каталог разрешений источника) шагов может
            // понадобиться неприлично много, а на самом краю - когда nativeStep падает ниже ULP
            // текущего step - "step += nativeStep" молча перестаёт менять step, и без предела цикл
            // не завершился бы никогда. Метод планируется дёргать на каждое нажатие клавиши в
            // живой оценке числа точек в диалоге настроек (Task 13) - зависший цикл там означает
            // не сбой, а насмерть замороженный диалог.
            int iterations = 0;
            const int MaxIterations = 2000000;

            while ((long)columns * rows > maxPoints)
            {
                step += nativeStep;
                forLimit = true;
                columns = Count(width, step);
                rows = Count(height, step);

                if (++iterations > MaxIterations)
                    throw new InvalidOperationException(
                        "Не удалось подобрать шаг сетки за " + MaxIterations +
                        " итераций - проверьте разрешение источника (nativeStep).");
            }

            return new GridPlan(step, columns, rows, toNative, forLimit, requestedStep);
        }

        private static int Count(double size, double step)
        {
            return (int)Math.Floor(size / step) + 1;
        }
    }
}
