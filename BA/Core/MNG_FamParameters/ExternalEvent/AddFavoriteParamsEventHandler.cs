// BA/Core/AddFavoriteParamsEventHandler.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Text;

namespace BA.Core
{
    /// <summary>
    /// One resolved "add as new parameter" request built from a favorite entry.
    /// DisplayName is used only for logging and the summary dialog; the actual
    /// parameter name that ends up in the family comes from SharedDefinition.Name
    /// once AddParameter runs.
    /// </summary>
    public sealed class FavoriteAddRequest
    {
        public string DisplayName { get; set; } = "";
        public ExternalDefinition SharedDefinition { get; set; }
        public ForgeTypeId TargetGroupTypeId { get; set; }
        public bool IsInstance { get; set; } = true;
    }

    /// <summary>
    /// ExternalEvent handler that adds a batch of favorite shared parameters to the
    /// active family document as brand new FamilyParameters. No replace and no name
    /// match against an existing parameter is required, unlike HarmonizerEventHandler.
    /// Mirrors DuplicateParamEventHandler's TransactionGroup plus per item Transaction
    /// pattern so a single user click can safely add many parameters in one Execute call.
    /// After execution, invokes OnComplete on the UI thread with the list of new
    /// parameter names. A null entry marks a failed item; check Log for the reason.
    /// </summary>
    public class AddFavoriteParamsEventHandler : IExternalEventHandler
    {
        public List<FavoriteAddRequest> Requests { get; } = new();
        public Document Document { get; set; }
        public StringBuilder Log { get; } = new();

        /// <summary>
        /// Invoked on the WPF Dispatcher thread after execution.
        /// Receives one entry per request, in order, null where the add failed.
        /// </summary>
        public Action<List<string>> OnComplete { get; set; }

        public void Execute(UIApplication app)
        {
            Log.Clear();
            var doc = Document ?? app.ActiveUIDocument?.Document;

            if (doc == null || !doc.IsFamilyDocument)
            {
                Log.AppendLine("ERROR: Active document is not a family document.");
                NotifyComplete(null);
                return;
            }

            var fm = doc.FamilyManager;
            var newNames = new List<string>();

            using (var tg = new TransactionGroup(doc, "Add Favorite Parameters"))
            {
                tg.Start();

                foreach (var req in Requests)
                {
                    if (req?.SharedDefinition == null)
                    {
                        Log.AppendLine($"SKIP: '{req?.DisplayName}' - no shared definition resolved.");
                        newNames.Add(null);
                        continue;
                    }

                    using (var t = new Transaction(doc, $"Add Favorite '{req.DisplayName}'"))
                    {
                        t.Start();
                        try
                        {
                            var groupId = req.TargetGroupTypeId ?? GroupTypeId.Data;
                            var newFp = fm.AddParameter(req.SharedDefinition, groupId, req.IsInstance);

                            t.Commit();
                            var addedName = newFp.Definition.Name;
                            newNames.Add(addedName);
                            Log.AppendLine($"ADDED: '{addedName}'");
                        }
                        catch (Exception ex)
                        {
                            Log.AppendLine($"ADD FAILED: '{req.DisplayName}' - {ex.Message}");
                            try { t.RollBack(); } catch { }
                            newNames.Add(null);
                        }
                    }
                }

                tg.Assimilate();
            }

            NotifyComplete(newNames);
        }

        private void NotifyComplete(List<string> names)
        {
            if (OnComplete == null) return;
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                () => OnComplete(names ?? new List<string>()));
        }

        public string GetName() => "BA Add Favorite Parameters";
    }
}