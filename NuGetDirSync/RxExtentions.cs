using System;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;

namespace NuGetDirSync
{
    public static class RxExtentions
    {
        public static IObservable<TSource> ObserveLatestOn<TSource>(
            this IObservable<TSource> source,
            IScheduler scheduler)
        {
            // Implementation from:  http://stackoverflow.com/a/16638233/164771

            return Observable.Create<TSource>(observer =>
            {
                Notification<TSource> pendingNotification = null;
                var cancelable = new MultipleAssignmentDisposable();

                var sourceSubscription = source.Materialize()
                    .Subscribe(notification =>
                    {
                        var previousNotification = Interlocked.Exchange(
                            ref pendingNotification, notification);

                        if (previousNotification != null) return;

                        cancelable.Disposable = scheduler.Schedule(() =>
                        {
                            var notificationToSend = Interlocked.Exchange(
                                ref pendingNotification, null);
                            notificationToSend.Accept(observer);
                        });
                    });
                return new CompositeDisposable(sourceSubscription, cancelable);
            });
        }
    }
}