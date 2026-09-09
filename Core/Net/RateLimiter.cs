using System.Threading;

namespace DemLoader.Core.Net
{
    /// <summary>Пауза между запросами к одному источнику. Политика tile.openstreetmap.org
    /// запрещает массовую предвыборку - задержка не декоративная.</summary>
    internal sealed class RateLimiter
    {
        private readonly int _delayMs;
        private readonly IClock _clock;
        private long _lastMs = long.MinValue;

        public RateLimiter(int delayMs, IClock clock)
        {
            _delayMs = delayMs;
            _clock = clock;
        }

        public void Wait(CancellationToken token)
        {
            if (_lastMs != long.MinValue && _delayMs > 0)
            {
                long elapsed = _clock.NowMs - _lastMs;
                if (elapsed < _delayMs) _clock.Sleep((int)(_delayMs - elapsed), token);
            }

            _lastMs = _clock.NowMs;
        }
    }
}
