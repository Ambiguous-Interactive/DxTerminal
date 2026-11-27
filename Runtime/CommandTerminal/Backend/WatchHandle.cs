namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;

    /// <summary>
    ///     Removes exactly the watch registration that produced it when
    ///     disposed. Disposing twice is safe; a disposed handle never
    ///     removes a later registration that reused the watch name.
    /// </summary>
    public sealed class WatchHandle : IDisposable
    {
        private readonly CommandWatchRegistry _registry;
        private readonly long _registrationId;
        private bool _disposed;

        internal WatchHandle(CommandWatchRegistry registry, long registrationId)
        {
            _registry = registry;
            _registrationId = registrationId;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _registry.RemoveRegistration(_registrationId);
        }
    }
}
