using System;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAutoResume
{
    internal sealed class CaptureDeadlineException : TimeoutException
    { public CaptureDeadlineException() : base("后台捕获超过 3 秒，监控已停止；可稍后重新开始，若持续等待请重启工具") { } }
    internal sealed class CaptureBusyException : InvalidOperationException
    { public CaptureBusyException() : base("上一次系统捕获尚未结束；不会启动新的捕获，请稍后重试或重启工具") { } }

    // A native capture can hang during driver work or session teardown. Never
    // create more workers behind it, and never let its eventual result click.
    internal sealed class CaptureSampler
    {
        readonly object sync = new object();
        readonly Func<IntPtr, WindowFrame> capture;
        Task<WindowFrame> pending;
        public CaptureSampler(Func<IntPtr, WindowFrame> capture) { this.capture = capture; }
        public bool IsBusy { get { lock (sync) return pending != null; } }
        void Clear(Task<WindowFrame> work) { lock (sync) if (pending == work) pending = null; }

        public async Task<WindowFrame> ReadAsync(IntPtr window, int deadlineMilliseconds = 3000)
        {
            if (deadlineMilliseconds <= 0) throw new ArgumentOutOfRangeException("deadlineMilliseconds");
            Task<WindowFrame> work;
            lock (sync)
            {
                if (pending != null) throw new CaptureBusyException();
                work = Task.Run(delegate { return capture(window); }); pending = work;
            }
            using (CancellationTokenSource deadline = new CancellationTokenSource())
            {
                Task winner = await Task.WhenAny(work, Task.Delay(deadlineMilliseconds, deadline.Token)).ConfigureAwait(false);
                if (winner == work)
                {
                    deadline.Cancel();
                    try { return await work.ConfigureAwait(false); }
                    finally { Clear(work); }
                }
                Task cleanup = work.ContinueWith(delegate(Task<WindowFrame> late)
                {
                    try
                    {
                        if (late.Status == TaskStatus.RanToCompletion) { if (late.Result != null) late.Result.Dispose(); }
                        else if (late.IsFaulted) { AggregateException observed = late.Exception; }
                    }
                    finally { Clear(late); }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                throw new CaptureDeadlineException();
            }
        }
    }
}
