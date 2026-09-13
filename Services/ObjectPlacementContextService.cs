using Ensemble.Models;

namespace Ensemble.Services
{
    /// <summary>
    /// Carries the Object Browser placement choice across the modal dialog
    /// boundary. MainWindow consumes it after the object has actually been
    /// inserted so the new object can be grounded against the target map.
    /// </summary>
    internal static class ObjectPlacementContextService
    {
        private static readonly object Sync =
            new();

        private static ObjectCatalogEntry?
            _pendingEntry;

        private static bool
            _preserveDonorPosition;

        private static long
            _createdAtMs;

        public static void SetPending(
            ObjectCatalogEntry entry,
            bool preserveDonorPosition)
        {
            ArgumentNullException.ThrowIfNull(
                entry);

            lock (Sync)
            {
                _pendingEntry =
                    entry;

                _preserveDonorPosition =
                    preserveDonorPosition;

                _createdAtMs =
                    Environment.TickCount64;
            }
        }

        public static bool TryTake(
            out ObjectCatalogEntry? entry,
            out bool preserveDonorPosition)
        {
            lock (Sync)
            {
                entry =
                    null;

                preserveDonorPosition =
                    false;

                if (_pendingEntry ==
                    null)
                {
                    return false;
                }

                // A placement import is synchronous once Object Browser closes.
                // Expire stale context so a failed import can never affect a
                // later unrelated selection.
                if (Environment.TickCount64 -
                        _createdAtMs >
                    5000)
                {
                    _pendingEntry =
                        null;

                    return false;
                }

                entry =
                    _pendingEntry;

                preserveDonorPosition =
                    _preserveDonorPosition;

                _pendingEntry =
                    null;

                return true;
            }
        }
    }
}
