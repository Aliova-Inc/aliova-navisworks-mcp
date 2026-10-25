using System;
using System.Threading;
using System.Threading.Tasks;

namespace NavisworksMcpPlugin.Mcp
{
    /// <summary>
    /// Marshals work onto the Navisworks UI thread.
    /// Must be initialized from the UI thread (inside AddInPlugin.Execute).
    /// </summary>
    public static class UiThread
    {
        private static SynchronizationContext _ctx;

        public static void Capture()
        {
            _ctx = SynchronizationContext.Current
                ?? throw new InvalidOperationException(
                    "No SynchronizationContext on current thread. Call Capture() from the Navisworks UI thread.");
        }

        public static Task<T> Run<T>(Func<T> work)
        {
            if (_ctx == null)
                throw new InvalidOperationException("UiThread not initialized. Call Capture() first.");

            var tcs = new TaskCompletionSource<T>();
            _ctx.Post(_ =>
            {
                try
                {
                    tcs.SetResult(work());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }, null);
            return tcs.Task;
        }

        public static Task Run(Action work)
        {
            if (_ctx == null)
                throw new InvalidOperationException("UiThread not initialized. Call Capture() first.");

            var tcs = new TaskCompletionSource<bool>();
            _ctx.Post(_ =>
            {
                try
                {
                    work();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }, null);
            return tcs.Task;
        }
    }
}
