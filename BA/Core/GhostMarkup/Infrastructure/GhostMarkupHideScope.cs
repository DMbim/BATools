// BA/Core/GhostMarkup/GhostMarkupHideScope.cs
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using BA.BAApplication;

namespace BA.Core.GhostMarkup
{
    /// <summary>
    /// Wraps a TransactionGroup that hides ghost markup elements across one
    /// or more views for the duration of an export, then rolls back on
    /// dispose so the hide never persists in the saved model. RollBack on a
    /// TransactionGroup only reverts transactions committed inside that
    /// group, any unsaved edits made before Begin was called are untouched,
    /// so this is safe to call mid session without forcing a save first.
    ///
    /// Usage:
    ///   var ghostMap = GhostMarkupCollector.CollectForSheet(doc, sheet);
    ///   using (GhostMarkupHideScope.Begin(doc, ghostMap))
    ///   {
    ///       // existing export call goes here, unchanged
    ///   }
    /// </summary>
    public sealed class GhostMarkupHideScope : IDisposable
    {
        private readonly TransactionGroup _group;
        private bool _disposed;

        private GhostMarkupHideScope(TransactionGroup group)
        {
            _group = group;
        }

        public static GhostMarkupHideScope Begin(
            Document doc,
            Dictionary<ElementId, List<ElementId>> ghostElementsByView,
            string groupName = "Ghost Markup Hide")
        {
            var group = new TransactionGroup(doc, groupName);
            group.Start();

            try
            {
                foreach (var kvp in ghostElementsByView)
                {
                    if (kvp.Value.Count == 0)
                    {
                        continue;
                    }

                    if (doc.GetElement(kvp.Key) is not View view)
                    {
                        continue;
                    }

                    using var tx = new Transaction(doc, "Hide ghost markup elements");
                    tx.Start();

                    try
                    {
                        view.HideElements(kvp.Value);
                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();
                        AppLogger.LogError($"Ghost markup hide failed for view {view.Name}", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Ghost markup hide scope failed to start", ex);
                group.RollBack();
                throw;
            }

            return new GhostMarkupHideScope(group);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                _group.RollBack();
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Ghost markup hide scope failed to roll back", ex);
            }
            finally
            {
                _group.Dispose();
            }
        }
    }
}