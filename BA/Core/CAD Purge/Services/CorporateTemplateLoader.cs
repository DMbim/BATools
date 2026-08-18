// File: BA_Tools/CadPurge/Services/CorporateTemplateLoader.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using BA.CadPurge.Models;

namespace BA.CadPurge.Services
{
    /// <summary>
    /// Opens BA_RevitTemplate_v26.rte (or whatever templateFilePath points to) in the background
    /// to either snapshot its LinePatternElement/TextNoteType names (LoadBaseline, used by the
    /// scan-time filter) or copy one specific element into the active document
    /// (CopyStandardElement, used when a mapping target doesn't exist yet).
    ///
    /// Must run on the Revit API thread — call only from inside an AppExternalInvoker.Instance.Run
    /// callback. LoadBaseline is read-only (no Transaction needed on the template doc; opening/
    /// closing a document is not itself a transacted operation). CopyStandardElement requires an
    /// ALREADY-OPEN Transaction on targetDoc — it does not open its own, to avoid nested-transaction
    /// conflicts with whatever batch operation is calling it (Stage 3).
    /// </summary>
    public sealed class CorporateTemplateLoader
    {
        public TemplateBaselineSnapshot LoadBaseline(Application application, string templateFilePath)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));
            ValidateTemplatePath(templateFilePath);

            Document templateDoc = FindAlreadyOpen(application, templateFilePath);
            bool weOpenedIt = templateDoc == null;

            if (weOpenedIt)
                templateDoc = OpenDetached(application, templateFilePath);

            try
            {
                HashSet<string> lineNames = CollectNames<LinePatternElement>(templateDoc);
                HashSet<string> textNames = CollectNames<TextNoteType>(templateDoc);
                return new TemplateBaselineSnapshot(lineNames, textNames);
            }
            finally
            {
                if (weOpenedIt)
                    templateDoc.Close(false);
            }
        }

        /// <summary>
        /// Copies a single named LinePatternElement or TextNoteType from the reference template
        /// into targetDoc. Returns its new ElementId in targetDoc. Throws if the named element
        /// isn't found in the template (typo in corporate_standards.json's targetName) or if
        /// targetDoc has no active Transaction.
        /// </summary>
        public ElementId CopyStandardElement(Document targetDoc, string templateFilePath, PurgeItemType itemType, string elementName)
        {
            if (targetDoc == null) throw new ArgumentNullException(nameof(targetDoc));
            ValidateTemplatePath(templateFilePath);
            if (string.IsNullOrWhiteSpace(elementName))
                throw new ArgumentException("elementName is empty.", nameof(elementName));
            if (itemType != PurgeItemType.LinePattern && itemType != PurgeItemType.TextStyle)
                throw new ArgumentOutOfRangeException(nameof(itemType), itemType,
                    "CopyStandardElement only supports LinePattern and TextStyle.");
            if (!targetDoc.IsModifiable)
                throw new InvalidOperationException(
                    "CopyStandardElement requires an active Transaction on targetDoc — the caller " +
                    "must open one before calling this (see PurgeBatchExecutor, Stage 3).");

            Application application = targetDoc.Application;
            Document templateDoc = FindAlreadyOpen(application, templateFilePath);
            bool weOpenedIt = templateDoc == null;

            if (weOpenedIt)
                templateDoc = OpenDetached(application, templateFilePath);

            try
            {
                ElementId sourceId = itemType == PurgeItemType.LinePattern
                    ? FindByName<LinePatternElement>(templateDoc, elementName)
                    : FindByName<TextNoteType>(templateDoc, elementName);

                if (sourceId == null || sourceId == ElementId.InvalidElementId)
                    throw new InvalidOperationException(
                        $"'{elementName}' ({itemType}) was not found in the reference template " +
                        $"'{templateFilePath}'. Check corporate_standards.json for a typo in targetName, " +
                        "or add the missing standard element to the template.");

                ICollection<ElementId> copied = ElementTransformUtils.CopyElements(
                    templateDoc,
                    new List<ElementId> { sourceId },
                    targetDoc,
                    Transform.Identity,
                    null);

                if (copied == null || copied.Count == 0)
                    throw new InvalidOperationException(
                        $"ElementTransformUtils.CopyElements returned no elements for '{elementName}' ({itemType}). " +
                        "This can happen if Revit silently merged the copy into an existing conflicting type — " +
                        "verify no element with this name already exists in the target document under a different case.");

                return copied.First();
            }
            finally
            {
                if (weOpenedIt)
                    templateDoc.Close(false);
            }
        }

        private static void ValidateTemplatePath(string templateFilePath)
        {
            if (string.IsNullOrWhiteSpace(templateFilePath))
                throw new ArgumentException("templateFilePath is empty.", nameof(templateFilePath));
            if (!File.Exists(templateFilePath))
                throw new FileNotFoundException(
                    $"Reference standards template not found at '{templateFilePath}'. Check " +
                    "corporate_standards.json's templateFilePath and that the path is reachable " +
                    "from this machine (e.g. a network share that may be offline).", templateFilePath);
        }

        /// <summary>
        /// Reuses an already-open Document for the template instead of opening a second copy —
        /// avoids doubling memory/IO if the BIM manager also happens to have the template open,
        /// and avoids any file-lock contention on a workshared template.
        /// </summary>
        private static Document FindAlreadyOpen(Application application, string templateFilePath)
        {
            string targetFullPath = Path.GetFullPath(templateFilePath);

            foreach (Document doc in application.Documents)
            {
                if (doc.IsLinked) continue;
                if (string.IsNullOrEmpty(doc.PathName)) continue;

                if (string.Equals(Path.GetFullPath(doc.PathName), targetFullPath, StringComparison.OrdinalIgnoreCase))
                    return doc;
            }

            return null;
        }

        /// <summary>
        /// Opens the template detached and with all worksets closed, so a workshared template
        /// never triggers a worksharing/missing-link dialog in this headless-ish background open —
        /// there is no user present to answer one from inside an ExternalEvent handler.
        /// </summary>
        private static Document OpenDetached(Application application, string templateFilePath)
        {
            ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(templateFilePath);

            var openOptions = new OpenOptions
            {
                DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets,
                Audit = false
            };
            openOptions.SetOpenWorksetsConfiguration(
                new WorksetConfiguration(WorksetConfigurationOption.CloseAllWorksets));

            return application.OpenDocumentFile(modelPath, openOptions);
        }

        private static HashSet<string> CollectNames<T>(Document doc) where T : Element
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (T element in new FilteredElementCollector(doc).OfClass(typeof(T)).Cast<T>())
            {
                if (!string.IsNullOrEmpty(element.Name))
                    names.Add(element.Name);
            }
            return names;
        }

        private static ElementId FindByName<T>(Document doc, string name) where T : Element
        {
            foreach (T element in new FilteredElementCollector(doc).OfClass(typeof(T)).Cast<T>())
            {
                if (string.Equals(element.Name, name, StringComparison.OrdinalIgnoreCase))
                    return element.Id;
            }
            return ElementId.InvalidElementId;
        }
    }
}