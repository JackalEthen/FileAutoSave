using System;
using System.Threading;
using System.Windows.Forms;

namespace FileAutoSave.Core.Hosting
{
    /// <summary>
    /// 把后台线程（云上传队列等）的提示封送回宿主的 UI 线程再弹窗。
    /// 必须在宿主 UI 线程上构造。
    /// </summary>
    public sealed class UiDispatcher
    {
        private readonly SynchronizationContext? _context;

        public UiDispatcher()
        {
            var current = SynchronizationContext.Current;
            if (current == null || current.GetType() == typeof(SynchronizationContext))
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            }

            _context = SynchronizationContext.Current;
        }

        public void Post(Action action)
        {
            if (_context == null || _context == SynchronizationContext.Current)
            {
                action();
                return;
            }

            _context.Post(_ => action(), null);
        }
    }
}
