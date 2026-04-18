using System;
using System.Threading;
using System.Threading.Tasks;

namespace linker.libs.timer
{
    public static class TimerHelper
    {
        public static void SetTimeout(Action action, int delayMs, CancellationToken cts = default)
        {
            Task.Run(async () =>
            {
                await Task.Delay(delayMs, cts).ConfigureAwait(false);
                action();
            }, cts);
        }

        public static void SetIntervalLong(Action action, int delayMs, CancellationToken cts = default)
        {
            Timer timer = null!;
            timer = new Timer(_ =>
            {
                timer?.Change(Timeout.Infinite, Timeout.Infinite);
                action();
                if (cts.IsCancellationRequested)
                {
                    return;
                }
                timer?.Change(delayMs, delayMs);
            }, null, 0, delayMs);
            cts.Register(() => timer?.Dispose());
        }
        public static void SetIntervalLong(Func<bool> action, int delayMs, CancellationToken cts = default)
        {
            Timer timer = null!;
            timer = new Timer(_ =>
            {
                timer?.Change(Timeout.Infinite, Timeout.Infinite);
                if (action() == false)
                {
                    timer?.Dispose();
                    return;
                }
                if (cts.IsCancellationRequested)
                {
                    return;
                }
                timer?.Change(delayMs, delayMs);
            }, null, 0, delayMs);
            cts.Register(() => timer?.Dispose());
        }
        public static void SetIntervalLong(Func<Task> action, int delayMs, CancellationToken cts = default)
        {
            Timer timer = null!;
            timer = new Timer(async _ =>
            {
                timer?.Change(Timeout.Infinite, Timeout.Infinite);
                await action().ConfigureAwait(false);
                if (cts.IsCancellationRequested)
                {
                    return;
                }
                timer?.Change(delayMs, delayMs);
            }, null, 0, delayMs);
            cts.Register(() => timer?.Dispose());
        }
        public static void SetIntervalLong(Action action, Func<int> delayMs, CancellationToken cts = default)
        {
            Timer timer = null!;
            timer = new Timer(async _ =>
            {
                timer?.Change(Timeout.Infinite, Timeout.Infinite);
                action();
                if (cts.IsCancellationRequested)
                {
                    return;
                }
                timer?.Change(delayMs(), delayMs());
            }, null, 0, delayMs());
            cts.Register(() => timer?.Dispose());
        }
        public static void SetIntervalLong(Func<Task> action, Func<int> delayMs, CancellationToken cts = default)
        {
            Timer timer = null!;
            timer = new Timer(async _ =>
            {
                timer?.Change(Timeout.Infinite, Timeout.Infinite);
                await action().ConfigureAwait(false);
                if (cts.IsCancellationRequested)
                {
                    return;
                }
                timer?.Change(delayMs(), delayMs());
            }, null, 0, delayMs());
            cts.Register(() => timer?.Dispose());
        }

        public static void Async(Action action, CancellationToken cts = default)
        {
            Task.Run(action, cts);
        }
        public static void Async(Func<Task> action, CancellationToken cts = default)
        {
            Task.Run(action, cts);
        }
    }
}
