using System;
using System.Collections.Concurrent;

namespace InvectorModKit.Marketplace
{
    /// <summary>Work that must run on Unity's main thread (UnityWebRequest, UI), queued from any thread.</summary>
    internal static class MainThread
    {
        private static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();

        public static void Post(Action action) => Queue.Enqueue(action);

        /// <summary>Called every frame by the plugin.</summary>
        public static void Pump()
        {
            while (Queue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception e) { MarketplacePlugin.Log.LogError(e); }
            }
        }
    }
}
