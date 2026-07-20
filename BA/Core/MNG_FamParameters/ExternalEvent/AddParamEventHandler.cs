// BA/Core/AddParamEventHandler.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Text;

namespace BA.Core
{
    /// <summary>
    /// ExternalEvent handler that adds a single family parameter.
    /// Supports both shared (ExternalDefinition) and non-shared (name + SpecTypeId) creation.
    /// After execution, invokes OnComplete on the UI thread with the new parameter name,
    /// or null on failure.
    /// </summary>
    public class AddParamEventHandler : IExternalEventHandler
    {
        // Set before raising the event
        public string ParamName { get; set; }
        public ForgeTypeId SpecTypeId { get; set; }
        public ForgeTypeId TargetGroupTypeId { get; set; }
        public bool IsInstance { get; set; } = true;
        public bool IsShared { get; set; }

        /// <summary>Required when IsShared = true.</summary>
        public ExternalDefinition SharedDefinition { get; set; }

        public Document Document { get; set; }
        public StringBuilder Log { get; } = new();

        /// <summary>
        /// Invoked on the WPF Dispatcher thread after execution.
        /// Receives the name of the created parameter, or null on failure.
        /// </summary>
        public Action<string> OnComplete { get; set; }

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
            var groupId = TargetGroupTypeId ?? GroupTypeId.Data;

            using (var t = new Transaction(doc, $"Add Parameter '{ParamName}'"))
            {
                t.Start();
                try
                {
                    FamilyParameter newFp;

                    if (IsShared && SharedDefinition != null)
                    {
                        newFp = fm.AddParameter(SharedDefinition, groupId, IsInstance);
                    }
                    else
                    {
                        if (SpecTypeId == null)
                            throw new InvalidOperationException(
                                "SpecTypeId is required for non-shared parameters.");

                        newFp = FamilyParamUtils.AddFamilyParameterCompat(
                            fm, ParamName, groupId, SpecTypeId, IsInstance);
                    }

                    t.Commit();
                    var addedName = newFp.Definition.Name;
                    Log.AppendLine($"ADDED: '{addedName}'");
                    NotifyComplete(addedName);
                }
                catch (Exception ex)
                {
                    Log.AppendLine($"ADD FAILED: {ex.Message}");
                    try { t.RollBack(); } catch { }
                    NotifyComplete(null);
                }
            }
        }

        private void NotifyComplete(string name)
        {
            if (OnComplete == null) return;
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                () => OnComplete(name));
        }

        public string GetName() => "BA Add Family Parameter";
    }
}