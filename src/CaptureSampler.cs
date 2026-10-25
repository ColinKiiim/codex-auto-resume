using System;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAutoResume
{
    internal sealed class CaptureDeadlineException : TimeoutException
    { public CaptureDeadlineException() : base("本次捕获耗时较长，保持监测并等待系统恢复") { } }
    internal sealed class CaptureBusyException : InvalidOperationException
    { public CaptureBusyException() : base("上一次系统捕获尚未结束，保持监测并等待，不堆积任务") { } }

    // A native capture can hang during driver work or session teardown. Never
    // create more workers behind it, and never let its eventual result click.
    internal sealed class CaptureSampler
    {
        readonly object sync = new object();
        readonly Func<IntPtr, WindowFrame> capture;
        readonly Func<IntPtr, WindowFrame> actionCapture;
        Task<WindowFrame> pending;
        public CaptureSampler(Func<IntPtr, WindowFrame> capture, Func<IntPtr, WindowFrame> actionCapture = null) { this.capture = capture; this.actionCapture = actionCapture ?? capture; }
        public bool IsBusy { get { lock (sync) return pending != null; } }
        void Clear(Task<WindowFrame> work) { lock (sync) if (pending == work) pending = null; }

        public Task<WindowFrame> ReadAsync(IntPtr window, int deadlineMilliseconds = 3000)
        { return ReadCoreAsync(window, deadlineMilliseconds, capture); }
        internal Task<WindowFrame> ReadActionAsync(IntPtr window, int deadlineMilliseconds = 3000)
        { return ReadCoreAsync(window, deadlineMilliseconds, actionCapture); }
        async Task<WindowFrame> ReadCoreAsync(IntPtr window, int deadlineMilliseconds, Func<IntPtr, WindowFrame> read)
        {
            if (deadlineMilliseconds <= 0) throw new ArgumentOutOfRangeException("deadlineMilliseconds");
            Task<WindowFrame> work;
            lock (sync)
            {
                if (pending != null) throw new CaptureBusyException();
                work = Task.Run(delegate { return read(window); }); pending = work;
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
