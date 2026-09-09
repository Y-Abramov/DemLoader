using System.Threading;

namespace DemLoader.Core.Net
{
    /// <summary>Часы и сон отдельным интерфейсом: тест на задержку между запросами
    /// не должен реально ждать.</summary>
    internal interface IClock
    {
        long NowMs { get; }
        void Sleep(int ms, CancellationToken token);
    }

    internal sealed class SystemClock : IClock
    {
        public long NowMs { get { return System.Environment.TickCount; } }

        public void Sleep(int ms, CancellationToken token)
        {
            if (ms > 0) token.WaitHandle.WaitOne(ms);
            token.ThrowIfCancellationRequested();
        }
    }
}
