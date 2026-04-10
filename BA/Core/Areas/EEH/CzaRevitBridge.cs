// BA/Core/Areas/EEH/CzaRevitBridge.cs
// Async/await wrapper nad existujícím RevitExternalInvoker.
// Nahrazuje RevitCommandBridge bez vlastního ExternalEvent.

using System;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using BA.UI.ExternalEvents;

namespace BA.Core.Areas.EEH
{
    /// <summary>
    /// Async/await bridge pro CZA ViewModel vrstvu.
    /// Interně deleguje na existující RevitExternalInvoker — žádný vlastní ExternalEvent.
    /// </summary>
    public sealed class CzaRevitBridge
    {
        private readonly RevitExternalInvoker _invoker;

        public CzaRevitBridge(RevitExternalInvoker invoker)
        {
            _invoker = invoker
                ?? throw new ArgumentNullException(nameof(invoker));
        }

        public Task<T> ExecuteAsync<T>(Func<UIApplication, T> revitFunc)
        {
            // TaskCreationOptions.None — continuation poběží tam kde SetResult,
            // tj. na WPF UI threadu (Dispatcher.BeginInvoke z QueueHandler)
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.None);

            _invoker.Run(
                apiFunc: app => revitFunc(app),
                onCompleted: result => tcs.SetResult((T)result!),
                onError: ex => tcs.SetException(ex));

            return tcs.Task;
        }

        public Task ExecuteAsync(Action<UIApplication> revitAction)
        {
            return ExecuteAsync<bool>(app =>
            {
                revitAction(app);
                return true;
            });
        }
    }
}