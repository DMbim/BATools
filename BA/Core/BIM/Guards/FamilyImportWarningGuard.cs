using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using BA.UI.Views.Warnings;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using IDD = BA.Core.Overhead.ElementIdValue;

namespace BA.App.Guards
{
    public static class FamilyImportWarningGuardV2
    {
        // ---------------- Settings ----------------
        public static bool Enabled { get; set; } = true;
        public static bool ShowOnlyWhenIssues { get; set; } = true;
        public static string RequiredPrefix { get; set; } = "BA_";
        public static double MaxRecommendedSizeMb { get; set; } = 5.0;
        public static int MaxRecommendedFamilyElementCount { get; set; } = 2000;
        public static string DefaultTypeName { get; set; } = "BA_Default";

        public static string TempFixRoot => Path.Combine(Path.GetTempPath(), "BA", "FamilyFixes");

        // ---------------- Internal state ----------------
        private const string CmdLoadFamily_Project = "ID_LOAD_FAMILY";

        private static UIControlledApplication? _uiControlledApp;
        private static UIApplication? _cachedUiApp;

        private static bool _suppressForSession;
        private static bool _isHandling;
        private static bool _isAnalyzing;
        private static string? _pendingRenamePrefix;
        private static string? _pendingRenameFamilyName;
        private static readonly List<AddInCommandBinding> _bindings = new();

        private sealed class PendingFamily
        {
            public Document ProjectDoc { get; set; } = null!;
            public ElementId FamilyId { get; set; } = ElementId.InvalidElementId;
            public DateTime When { get; set; }
            public string Source { get; set; } = "";
        }

        private static readonly Queue<PendingFamily> _pending = new();
        private static readonly HashSet<string> _recentlyProcessed = new();
        private static Document? _lastActiveProjectDoc;

        // ---------------- Public API ----------------
        public static void Register(UIControlledApplication app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (_uiControlledApp != null) return;

            _uiControlledApp = app;

            app.Idling += OnIdlingCaptureUiApp;
            app.ViewActivated += OnViewActivated;
            app.ControlledApplication.DocumentChanged += OnDocumentChanged;

            EnsureBindings("Register");
            WriteRuntime("Register OK");
        }

        public static void Unregister(UIControlledApplication app)
        {
            if (_uiControlledApp == null) return;

            try { app.Idling -= OnIdlingCaptureUiApp; } catch { }
            try { app.ViewActivated -= OnViewActivated; } catch { }
            try { app.ControlledApplication.DocumentChanged -= OnDocumentChanged; } catch { }

            foreach (var b in _bindings.ToList())
            {
                try { b.BeforeExecuted -= OnBeforeExecuted; } catch { }
                try { b.Executed -= OnExecuted; } catch { }
            }
            _bindings.Clear();

            _pending.Clear();
            _recentlyProcessed.Clear();

            _cachedUiApp = null;
            _uiControlledApp = null;
            _lastActiveProjectDoc = null;
            _suppressForSession = false;
            _isHandling = false;
            _isAnalyzing = false; // <- CHANGED: also reset _isAnalyzing on unregister

            WriteRuntime("Unregister OK");
        }

        // ---------------- UI + context capture ----------------
        private static void OnIdlingCaptureUiApp(object sender, IdlingEventArgs e)
        {
            if (_cachedUiApp == null && sender is UIApplication uiapp)
            {
                _cachedUiApp = uiapp;
                WriteRuntime("Captured UIApplication from Idling");
            }

            if (!Enabled) return;
            if (_suppressForSession) return;
            if (_isHandling) return;

            ProcessPendingFamiliesOnIdling();
        }

        private static void OnViewActivated(object sender, ViewActivatedEventArgs e)
        {
            try
            {
                var uiapp = sender as UIApplication ?? _cachedUiApp;
                var doc = uiapp?.ActiveUIDocument?.Document;

                if (doc != null && doc.IsValidObject && !doc.IsFamilyDocument)
                    _lastActiveProjectDoc = doc;
            }
            catch { }

            EnsureBindings("ViewActivated");
        }

        // ---------------- Binding management ----------------
        private static void EnsureBindings(string reason)
        {
            if (_uiControlledApp == null) return;

            TryBindCommandId(_uiControlledApp, CmdLoadFamily_Project, $"Project: Load Family ({reason})");
            TryBindPostable(_uiControlledApp, PostableCommand.LoadIntoProject, $"FamilyEditor: LoadIntoProject ({reason})");
            TryBindPostableByName(_uiControlledApp, "LoadIntoProjectAndClose", $"FamilyEditor: LoadIntoProjectAndClose ({reason})");
        }

        private static void TryBindPostableByName(UIControlledApplication app, string postableName, string label)
        {
            if (!Enum.TryParse(postableName, ignoreCase: true, out PostableCommand pc))
            {
                WriteRuntime($"Bind SKIP: {label} PostableCommand.{postableName} not present in this Revit build");
                return;
            }
            TryBindPostable(app, pc, label);
        }

        private static void TryBindCommandId(UIControlledApplication app, string cmdId, string label)
        {
            var cmd = RevitCommandId.LookupCommandId(cmdId);
            if (cmd == null)
            {
                WriteRuntime($"Bind SKIP: {label} {cmdId} => NULL");
                return;
            }

            TryRemoveBinding(app, cmd);

            var binding = app.CreateAddInCommandBinding(cmd);
            binding.BeforeExecuted += OnBeforeExecuted;
            binding.Executed += OnExecuted;
            _bindings.Add(binding);

            WriteRuntime($"Bind OK: {label} ({cmdId})");
        }

        private static void TryBindPostable(UIControlledApplication app, PostableCommand pc, string label)
        {
            var cmd = RevitCommandId.LookupPostableCommandId(pc);
            if (cmd == null)
            {
                WriteRuntime($"Bind SKIP: {label} PostableCommand.{pc} not found");
                return;
            }

            TryRemoveBinding(app, cmd);

            var binding = app.CreateAddInCommandBinding(cmd);
            binding.BeforeExecuted += OnBeforeExecuted;
            binding.Executed += OnExecuted;
            _bindings.Add(binding);

            WriteRuntime($"Bind OK: {label} (PostableCommand.{pc})");
        }

        private static void TryRemoveBinding(UIControlledApplication app, RevitCommandId cmd)
        {
            try { app.RemoveAddInCommandBinding(cmd); } catch { }
        }

        // ---------------- Command interception ----------------
        private static void OnBeforeExecuted(object sender, BeforeExecutedEventArgs e)
        {
            if (!Enabled) return;
            if (_suppressForSession) return;
            if (_isHandling) return;

            _isHandling = true;
            try
            {
                var uiapp = _cachedUiApp ?? (sender as UIApplication);
                var doc = uiapp?.ActiveUIDocument?.Document;

                WriteRuntime($"BeforeExecuted fired | Cancellable={e.Cancellable} | doc={(doc == null ? "NULL" : (doc.IsFamilyDocument ? "Family" : "Project"))}");

                if (!e.Cancellable || doc == null) return;

                if (doc.IsFamilyDocument)
                {
                    // <- CHANGED: only cancel if we actually need to intervene.
                    // If no issues, let Revit run the native command so the family
                    // editor stays open exactly as the user expects.
                    var analysis = FamilyAnalyzer.AnalyzeFamilyDocument(
                        doc, RequiredPrefix, MaxRecommendedSizeMb, DefaultTypeName, MaxRecommendedFamilyElementCount);

                    if (!ShouldShow(analysis))
                    {
                        // No issues — do not cancel, let native command proceed.
                        WriteRuntime("FamilyEditor load: no issues, passing through to native command.");
                        return;
                    }

                    // Has issues — cancel and handle ourselves.
                    e.Cancel = true;
                    GuardedLoadFromFamilyEditor(uiapp, doc, analysis);
                }
                else
                {
                    e.Cancel = true;
                    GuardedLoadFromProject(uiapp, doc);
                }
            }
            catch (Exception ex)
            {
                WriteRuntime("ERROR OnBeforeExecuted: " + ex);
            }
            finally
            {
                _isHandling = false;
            }
        }

        private static void OnExecuted(object sender, ExecutedEventArgs e)
        {
            try { WriteRuntime("Executed fired"); } catch { }
        }

        // ---------------- Reliable fallback: detect actual load ----------------
        private static void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            if (!Enabled) return;
            if (_suppressForSession) return;
            if (_isHandling) return;   // guards GuardedLoadFromProject and GuardedLoadFromFamilyEditor load calls
            if (_isAnalyzing) return;  // guards EditFamily inside AnalyzeLoadedFamilyInProject

            Document doc = e.GetDocument();
            if (doc == null || !doc.IsValidObject) return;
            if (doc.IsFamilyDocument) return;

            try
            {
                var added = e.GetAddedElementIds();
                if (added == null || added.Count == 0) return;

                List<ElementId> familyIds = new List<ElementId>();
                foreach (var id in added)
                {
                    var el = doc.GetElement(id);
                    if (el is Family)
                        familyIds.Add(id);
                }

                if (familyIds.Count == 0) return;

                foreach (var fid in familyIds)
                    EnqueuePending(doc, fid, "DocumentChanged");
            }
            catch (Exception ex)
            {
                WriteRuntime("ERROR DocumentChanged: " + ex.Message);
            }
        }

        private static void EnqueuePending(Document projectDoc, ElementId familyId, string source)
        {
            long bucket = DateTime.UtcNow.Ticks / TimeSpan.FromSeconds(10).Ticks;
            string key = $"{projectDoc.GetHashCode()}|{IDD.Of(familyId)}|{bucket}";
            if (_recentlyProcessed.Contains(key)) return;

            _recentlyProcessed.Add(key);

            _pending.Enqueue(new PendingFamily
            {
                ProjectDoc = projectDoc,
                FamilyId = familyId,
                When = DateTime.Now,
                Source = source
            });

            WriteRuntime($"Enqueued family from {source}: id={IDD.Of(familyId)}");
        }

        private static void ProcessPendingFamiliesOnIdling()
        {
            if (_pending.Count == 0) return;
            if (_cachedUiApp == null) return;

            var item = _pending.Dequeue();

            if (item.ProjectDoc == null || !item.ProjectDoc.IsValidObject) return;

            var fam = item.ProjectDoc.GetElement(item.FamilyId) as Family;
            if (fam == null) return;

            // Apply pending rename from GuardedLoadFromFamilyEditor if applicable.
            if (_pendingRenamePrefix != null
                && _pendingRenameFamilyName != null
                && fam.Name.Equals(_pendingRenameFamilyName, StringComparison.OrdinalIgnoreCase)
                && !fam.Name.StartsWith(_pendingRenamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                TryRenameFamilyInProject(item.ProjectDoc, fam.Id, _pendingRenamePrefix + fam.Name);
                _pendingRenamePrefix = null;
                _pendingRenameFamilyName = null;
                return; // skip ShowWarningAndApplyFixes since we already handled this load
            }

            _pendingRenamePrefix = null;
            _pendingRenameFamilyName = null;

            ShowWarningAndApplyFixes(_cachedUiApp, item.ProjectDoc, fam, source: item.Source);
        }

        // ---------------- Guard workflows ----------------
        private static void GuardedLoadFromProject(UIApplication? uiapp, Document projectDoc)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Load Family (BA Guard)",
                Filter = "Revit Family (*.rfa)|*.rfa",
                Multiselect = false,
                CheckFileExists = true,
                CheckPathExists = true
            };

            if (dlg.ShowDialog() != true) return;

            string familyPath = dlg.FileName;
            if (!File.Exists(familyPath)) return;

            var analysis = FamilyAnalyzer.AnalyzeFamilyFile(uiapp, familyPath, RequiredPrefix, MaxRecommendedSizeMb, DefaultTypeName, MaxRecommendedFamilyElementCount);

            if (!ShouldShow(analysis))
            {
                // <- CHANGED: set _isHandling around LoadFamily to suppress DocumentChanged re-enqueue
                _isHandling = true;
                try
                {
                    projectDoc.LoadFamily(familyPath, new BaFamilyLoadOptions(), out _);
                }
                finally
                {
                    _isHandling = false;
                }
                return;
            }

            var decision = ShowDecisionWindow(uiapp, analysis, familyPath, analysis.FamilyName);

            if (decision.SuppressForSession) _suppressForSession = true;
            if (decision.Mode == FamilyLoadMode.Cancel) return;

            string pathToLoad = familyPath;

            if (decision.Mode == FamilyLoadMode.FixAndLoad && decision.CreateDefaultTypeIfNone && analysis.TypeCount == 0)
                pathToLoad = FamilyAnalyzer.CreateTempFamilyWithDefaultType(uiapp, familyPath, analysis.SuggestedDefaultTypeName);

            // <- CHANGED: set _isHandling around LoadFamily to suppress DocumentChanged re-enqueue
            bool loaded;
            Family? loadedFamily;
            _isHandling = true;
            try
            {
                loaded = projectDoc.LoadFamily(pathToLoad, new BaFamilyLoadOptions(), out loadedFamily);
            }
            finally
            {
                _isHandling = false;
            }

            WriteRuntime($"Project load result: loaded={loaded}, fam={(loadedFamily == null ? "NULL" : loadedFamily.Name)}");

            if (!loaded || loadedFamily == null) return;

            if (decision.AddBaPrefixInProject && !loadedFamily.Name.StartsWith(RequiredPrefix, StringComparison.OrdinalIgnoreCase))
                TryRenameFamilyInProject(projectDoc, loadedFamily.Id, RequiredPrefix + loadedFamily.Name);
        }

        private static void GuardedLoadFromFamilyEditor(UIApplication? uiapp, Document familyDoc, FamilyAnalysis analysis)
        {
            var projectDoc = PickTargetProjectDoc(uiapp);
            if (projectDoc == null)
            {
                TaskDialog.Show("BA | Family Guard",
                    "No project document found.\nOpen a project and activate it once, then try again.");
                return;
            }

            var decision = ShowDecisionWindow(uiapp, analysis,
                filePath: string.IsNullOrWhiteSpace(familyDoc.PathName) ? "(Unsaved family)" : familyDoc.PathName,
                familyName: analysis.FamilyName);

            if (decision.SuppressForSession) _suppressForSession = true;
            if (decision.Mode == FamilyLoadMode.Cancel) return;

            // Apply fixes to the open family doc before loading.
            if (decision.Mode == FamilyLoadMode.FixAndLoad && decision.CreateDefaultTypeIfNone && analysis.TypeCount == 0)
                TryCreateDefaultTypeInFamilyDoc(familyDoc, analysis.SuggestedDefaultTypeName);

            // <- CHANGED: use PostCommand to trigger the native LoadIntoProject.
            // This preserves Revit's native behavior (family editor stays open or
            // closes exactly as Revit intends) and avoids the reflection-based
            // LoadFamily call which always closes the family editor.
            // The DocumentChanged fallback will catch the resulting family load
            // and handle rename if needed.
            if (decision.AddBaPrefixInProject && !analysis.HasRequiredPrefix)
            {
                // We cannot rename before load since the Family element does not exist yet.
                // Store the pending rename so DocumentChanged / Idling can apply it after load.
                _pendingRenamePrefix = RequiredPrefix;
                _pendingRenameFamilyName = analysis.FamilyName;
            }

            if (uiapp != null)
            {
                var cmd = RevitCommandId.LookupPostableCommandId(PostableCommand.LoadIntoProject);
                if (cmd != null)
                {
                    // Allow DocumentChanged to enqueue this load normally.
                    _isHandling = false;
                    uiapp.PostCommand(cmd);
                    _isHandling = true; // restore for finally block in OnBeforeExecuted
                }
            }
        }
        private static void ShowWarningAndApplyFixes(UIApplication uiapp, Document projectDoc, Family family, string source)
        {
            FamilyAnalysis analysis;

            // <- CHANGED: _isAnalyzing set before AnalyzeLoadedFamilyInProject (which calls EditFamily internally)
            _isAnalyzing = true;
            try
            {
                analysis = FamilyAnalyzer.AnalyzeLoadedFamilyInProject(projectDoc, family,
                    RequiredPrefix, MaxRecommendedSizeMb, DefaultTypeName, MaxRecommendedFamilyElementCount);
            }
            finally
            {
                _isAnalyzing = false;
            }

            if (!ShouldShow(analysis))
            {
                WriteRuntime($"Post-load ({source}): no issues => silent for {analysis.FamilyName}");
                return;
            }

            var decision = ShowDecisionWindow(uiapp, analysis, filePath: "(Loaded into project)", familyName: analysis.FamilyName);

            if (decision.SuppressForSession) _suppressForSession = true;
            if (decision.Mode == FamilyLoadMode.Cancel) return;

            if (decision.AddBaPrefixInProject && !analysis.HasRequiredPrefix)
                TryRenameFamilyInProject(projectDoc, family.Id, RequiredPrefix + family.Name);

            if (decision.Mode == FamilyLoadMode.FixAndLoad && decision.CreateDefaultTypeIfNone && analysis.TypeCount == 0)
            {
                Document? famDoc = null;

                // <- CHANGED: _isAnalyzing guards this second EditFamily call in the fix path
                _isAnalyzing = true;
                try
                {
                    famDoc = projectDoc.EditFamily(family);
                    if (famDoc != null && famDoc.IsValidObject && famDoc.IsFamilyDocument)
                    {
                        TryCreateDefaultTypeInFamilyDoc(famDoc, analysis.SuggestedDefaultTypeName);

                        // <- CHANGED: also guard the reload that follows EditFamily in the fix path
                        _isAnalyzing = false;
                        _isHandling = true;
                        try
                        {
                            LoadFamilyDocIntoProject(famDoc, projectDoc, new BaFamilyLoadOptions(), out _);
                        }
                        finally
                        {
                            _isHandling = false;
                            _isAnalyzing = true; // restore for finally block below
                        }
                    }
                }
                catch (Exception ex)
                {
                    WriteRuntime("Post-load fix ERROR: " + ex.Message);
                }
                finally
                {
                    _isAnalyzing = false;
                    try { famDoc?.Close(false); } catch { }
                }
            }
        }

        private static bool ShouldShow(FamilyAnalysis analysis)
        {
            if (!ShowOnlyWhenIssues) return true;
            return analysis.Flags.Count > 0;
        }

        // ---------------- Window + decision ----------------
        private static FamilyImportDecision ShowDecisionWindow(UIApplication? uiapp, FamilyAnalysis analysis, string filePath, string familyName)
        {
            var w = new FamilyImportWarningWindow(
                filePath: filePath,
                familyName: familyName,
                sizeText: analysis.SizeText,
                typesText: analysis.TypesText,
                cadInsideText: analysis.CadInsideText,
                flags: analysis.Flags.Count > 0 ? analysis.Flags : new List<string> { "No issues detected." },
                defaultAddPrefix: !analysis.HasRequiredPrefix,
                defaultCreateType: analysis.TypeCount == 0,
                thresholdText: $"Recommended max: {MaxRecommendedSizeMb:0.##} MB (or {MaxRecommendedFamilyElementCount} family elements)"
            );

            SetOwnerToRevit(w);
            w.ShowDialog();
            return w.Decision;
        }

        private static void SetOwnerToRevit(System.Windows.Window wpfWindow)
        {
            try
            {
                var handle = Process.GetCurrentProcess().MainWindowHandle;
                if (handle == IntPtr.Zero) return;
                new System.Windows.Interop.WindowInteropHelper(wpfWindow) { Owner = handle };
            }
            catch { }
        }

        // ---------------- Project doc selection ----------------
        private static Document? PickTargetProjectDoc(UIApplication? uiapp)
        {
            if (_lastActiveProjectDoc != null)
            {
                try
                {
                    if (_lastActiveProjectDoc.IsValidObject && !_lastActiveProjectDoc.IsFamilyDocument)
                        return _lastActiveProjectDoc;
                }
                catch { }
            }

            try
            {
                var app = uiapp?.Application;
                if (app == null) return null;

                foreach (Document d in app.Documents)
                    if (d != null && d.IsValidObject && !d.IsFamilyDocument)
                        return d;
            }
            catch { }

            return null;
        }

        // ---------------- Rename / type creation ----------------
        private static bool TryRenameFamilyInProject(Document projectDoc, ElementId familyId, string newName)
        {
            try
            {
                using var t = new Transaction(projectDoc, "BA Rename Family");
                t.Start();

                var mi = projectDoc.GetType().GetMethod("RenameElement", new[] { typeof(ElementId), typeof(string) });
                if (mi == null)
                {
                    t.RollBack();
                    WriteRuntime("RenameElement not available.");
                    return false;
                }

                mi.Invoke(projectDoc, new object[] { familyId, newName });
                t.Commit();

                WriteRuntime($"Renamed family => {newName}");
                return true;
            }
            catch (Exception ex)
            {
                WriteRuntime("Rename ERROR: " + ex.Message);
                return false;
            }
        }

        private static void TryCreateDefaultTypeInFamilyDoc(Document familyDoc, string typeName)
        {
            try
            {
                if (!familyDoc.IsFamilyDocument) return;

                var fm = familyDoc.FamilyManager;
                if (fm == null) return;
                if (fm.Types != null && fm.Types.Size > 0) return;

                using var t = new Transaction(familyDoc, "BA Create Default Type");
                t.Start();
                fm.NewType(string.IsNullOrWhiteSpace(typeName) ? "BA_Default" : typeName);
                t.Commit();

                WriteRuntime("Created default type in family.");
            }
            catch (Exception ex)
            {
                WriteRuntime("CreateDefaultType ERROR: " + ex.Message);
            }
        }

        // ---------------- Load family doc into project (reflection) ----------------
        private static bool LoadFamilyDocIntoProject(Document familyDoc, Document projectDoc, IFamilyLoadOptions options, out Family? loadedFamily)
        {
            loadedFamily = null;

            var methods = familyDoc.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => m.Name == "LoadFamily")
                .ToList();

            var m3 = methods.FirstOrDefault(m =>
            {
                var p = m.GetParameters();
                return p.Length == 3
                    && p[0].ParameterType == typeof(Document)
                    && typeof(IFamilyLoadOptions).IsAssignableFrom(p[1].ParameterType)
                    && p[2].IsOut
                    && p[2].ParameterType == typeof(Family).MakeByRefType();
            });

            if (m3 != null)
            {
                object? outFam = null;
                object?[] args = { projectDoc, options, outFam };
                object? ret = m3.Invoke(familyDoc, args);
                if (args[2] is Family f) loadedFamily = f;
                if (ret is bool b) return b;
                if (ret is Family f2) { loadedFamily = f2; return true; }
                return loadedFamily != null;
            }

            var m2 = methods.FirstOrDefault(m =>
            {
                var p = m.GetParameters();
                return p.Length == 2
                    && p[0].ParameterType == typeof(Document)
                    && typeof(IFamilyLoadOptions).IsAssignableFrom(p[1].ParameterType);
            });

            if (m2 != null)
            {
                object?[] args = { projectDoc, options };
                object? ret = m2.Invoke(familyDoc, args);
                if (ret is bool b) return b;
                if (ret is Family f) { loadedFamily = f; return true; }
                return false;
            }

            var m1 = methods.FirstOrDefault(m =>
            {
                var p = m.GetParameters();
                return p.Length == 1 && p[0].ParameterType == typeof(Document);
            });

            if (m1 != null)
            {
                object?[] args = { projectDoc };
                object? ret = m1.Invoke(familyDoc, args);
                if (ret is bool b) return b;
                if (ret is Family f) { loadedFamily = f; return true; }
                return false;
            }

            WriteRuntime("LoadFamilyDocIntoProject: No compatible overload found.");
            return false;
        }

        private static Family? FindFamilyByName(Document projectDoc, string familyName)
        {
            try
            {
                return new FilteredElementCollector(projectDoc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .FirstOrDefault(f => f.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase));
            }
            catch { return null; }
        }

        private sealed class BaFamilyLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = false;
                return true;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Project;
                overwriteParameterValues = false;
                return true;
            }
        }

        // ================= Analysis =================
        private sealed class FamilyAnalysis
        {
            public string FamilyName { get; set; } = "(Unknown)";
            public bool HasRequiredPrefix { get; set; }
            public long FileSizeBytes { get; set; }
            public bool IsTooLarge { get; set; }
            public int TypeCount { get; set; }
            public int CadImportCount { get; set; }
            public int FamilyElementCount { get; set; }
            public string SuggestedDefaultTypeName { get; set; } = "BA_Default";
            public List<string> Flags { get; } = new();

            public string SizeText => FileSizeBytes <= 0 ? "(unknown)" : $"{(FileSizeBytes / (1024.0 * 1024.0)):0.##} MB";
            public string TypesText => TypeCount == 0 ? "0 (none)" : TypeCount.ToString(CultureInfo.InvariantCulture);
            public string CadInsideText => CadImportCount == 0 ? "None detected" : $"{CadImportCount} import(s)";
        }

        // <- CHANGED: categories that legitimately have zero types — no flag should fire for these
        private static readonly HashSet<BuiltInCategory> _legitimatelyTypelessCategories = new()
        {
            BuiltInCategory.OST_DetailComponents,
            BuiltInCategory.OST_GenericAnnotation,
            BuiltInCategory.OST_TitleBlocks,
            BuiltInCategory.OST_ProfileFamilies,
            BuiltInCategory.OST_DetailComponentTags,
            BuiltInCategory.OST_RoomTags,
            BuiltInCategory.OST_AreaTags,
            BuiltInCategory.OST_DoorTags,
            BuiltInCategory.OST_WindowTags,
            BuiltInCategory.OST_WallTags,
            BuiltInCategory.OST_FloorTags,
            BuiltInCategory.OST_CeilingTags,
            BuiltInCategory.OST_RoofTags,
            BuiltInCategory.OST_StairsTags,
            BuiltInCategory.OST_RampTags,
            BuiltInCategory.OST_MultiCategoryTags,
        };

        private static class FamilyAnalyzer
        {
            public static FamilyAnalysis AnalyzeFamilyDocument(Document familyDoc, string requiredPrefix, double maxMb, string defaultTypeName, int maxElemCount)
            {
                var a = new FamilyAnalysis
                {
                    SuggestedDefaultTypeName = string.IsNullOrWhiteSpace(defaultTypeName) ? "BA_Default" : defaultTypeName
                };

                a.FamilyName = (familyDoc.Title ?? "(Family)").Replace(".rfa", "", StringComparison.OrdinalIgnoreCase);
                a.HasRequiredPrefix = a.FamilyName.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase);

                if (!a.HasRequiredPrefix)
                    a.Flags.Add($"Name does not start with '{requiredPrefix}'.");

                try
                {
                    if (!string.IsNullOrWhiteSpace(familyDoc.PathName) && File.Exists(familyDoc.PathName))
                    {
                        a.FileSizeBytes = new FileInfo(familyDoc.PathName).Length;
                        a.IsTooLarge = (a.FileSizeBytes / (1024.0 * 1024.0)) > maxMb;
                        if (a.IsTooLarge)
                            a.Flags.Add($"Large file ({a.SizeText}) — likely downloaded/heavy.");
                    }
                }
                catch { }

                // <- CHANGED: pass INVALID because we have no project-side Family element here;
                // this preserves original behavior (no category suppression) for open family docs
                FillFromFamilyDoc(familyDoc, a, maxElemCount, BuiltInCategory.INVALID);
                return a;
            }

            public static FamilyAnalysis AnalyzeFamilyFile(UIApplication? uiapp, string familyPath, string requiredPrefix, double maxMb, string defaultTypeName, int maxElemCount)
            {
                var a = new FamilyAnalysis
                {
                    FamilyName = Path.GetFileNameWithoutExtension(familyPath),
                    SuggestedDefaultTypeName = string.IsNullOrWhiteSpace(defaultTypeName) ? "BA_Default" : defaultTypeName
                };

                a.HasRequiredPrefix = a.FamilyName.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase);

                try
                {
                    a.FileSizeBytes = new FileInfo(familyPath).Length;
                    a.IsTooLarge = (a.FileSizeBytes / (1024.0 * 1024.0)) > maxMb;
                    if (a.IsTooLarge)
                        a.Flags.Add($"Large file ({a.SizeText}) — likely downloaded/heavy.");
                }
                catch { }

                if (!a.HasRequiredPrefix)
                    a.Flags.Add($"Name does not start with '{requiredPrefix}'.");

                if (uiapp == null) return a;

                Document? famDoc = null;
                try
                {
                    famDoc = uiapp.Application.OpenDocumentFile(familyPath);
                    if (famDoc != null && famDoc.IsFamilyDocument)
                        // <- CHANGED: pass INVALID — no project-side Family available for file path analysis
                        FillFromFamilyDoc(famDoc, a, maxElemCount, BuiltInCategory.INVALID);
                }
                catch (Exception ex)
                {
                    a.Flags.Add("Could not analyze family: " + ex.Message);
                }
                finally
                {
                    try { famDoc?.Close(false); } catch { }
                }

                return a;
            }

            public static FamilyAnalysis AnalyzeLoadedFamilyInProject(Document projectDoc, Family family, string requiredPrefix, double maxMb, string defaultTypeName, int maxElemCount)
            {
                var a = new FamilyAnalysis
                {
                    FamilyName = family?.Name ?? "(Family)",
                    SuggestedDefaultTypeName = string.IsNullOrWhiteSpace(defaultTypeName) ? "BA_Default" : defaultTypeName
                };

                a.HasRequiredPrefix = a.FamilyName.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase);
                if (!a.HasRequiredPrefix)
                    a.Flags.Add($"Name does not start with '{requiredPrefix}'.");

                // <- CHANGED: read the category from the project-side Family element before opening family doc
                BuiltInCategory familyCategory = BuiltInCategory.INVALID;
                try
                {
                    if (family != null)
                    {
                        var catId = family.FamilyCategory?.Id;
                        if (catId != null && catId != ElementId.InvalidElementId)
                            familyCategory = (BuiltInCategory)catId.Value;
                    }
                }
                catch { }

                Document? famDoc = null;
                try
                {
                    famDoc = projectDoc.EditFamily(family);
                    if (famDoc != null && famDoc.IsFamilyDocument)
                        // <- CHANGED: pass the resolved category so FillFromFamilyDoc can suppress typeless flag
                        FillFromFamilyDoc(famDoc, a, maxElemCount, familyCategory);
                }
                catch (Exception ex)
                {
                    a.Flags.Add("Could not open family for analysis: " + ex.Message);
                }
                finally
                {
                    try { famDoc?.Close(false); } catch { }
                }

                return a;
            }

            // <- CHANGED: familyCategory parameter added; used to suppress "No types" flag for known-safe categories
            private static void FillFromFamilyDoc(Document famDoc, FamilyAnalysis a, int maxElemCount, BuiltInCategory familyCategory)
            {
                try
                {
                    var fm = famDoc.FamilyManager;
                    a.TypeCount = fm?.Types?.Size ?? 0;

                    if (a.TypeCount == 0)
                    {
                        // <- CHANGED: only flag if the category is NOT in the legitimately-typeless set
                        bool categoryIsTypelessByDesign =
                            familyCategory != BuiltInCategory.INVALID
                            && _legitimatelyTypelessCategories.Contains(familyCategory);

                        if (!categoryIsTypelessByDesign)
                            a.Flags.Add("No types in family (bad sign).");
                    }

                    a.CadImportCount = new FilteredElementCollector(famDoc)
                        .OfClass(typeof(ImportInstance))
                        .Cast<ImportInstance>()
                        .Count();

                    if (a.CadImportCount > 0)
                        a.Flags.Add($"Contains imported CAD ({a.CadImportCount}).");

                    a.FamilyElementCount = new FilteredElementCollector(famDoc)
                        .WhereElementIsNotElementType()
                        .GetElementCount();

                    if (a.FamilyElementCount > maxElemCount)
                        a.Flags.Add($"Very heavy family ({a.FamilyElementCount} elements) — likely downloaded monster.");
                }
                catch { }
            }

            public static string CreateTempFamilyWithDefaultType(UIApplication? uiapp, string originalPath, string typeName)
            {
                if (uiapp == null) return originalPath;

                Directory.CreateDirectory(TempFixRoot);

                string baseName = Path.GetFileNameWithoutExtension(originalPath);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                string tempPath = Path.Combine(TempFixRoot, $"{baseName}_FIX_{stamp}.rfa");

                File.Copy(originalPath, tempPath, overwrite: true);

                Document? famDoc = null;
                try
                {
                    famDoc = uiapp.Application.OpenDocumentFile(tempPath);
                    if (famDoc == null || !famDoc.IsFamilyDocument)
                        return originalPath;

                    var fm = famDoc.FamilyManager;
                    if (fm != null && (fm.Types == null || fm.Types.Size == 0))
                    {
                        using var t = new Transaction(famDoc, "BA Create Default Type");
                        t.Start();
                        fm.NewType(string.IsNullOrWhiteSpace(typeName) ? "BA_Default" : typeName);
                        t.Commit();
                        famDoc.Save();
                    }

                    return tempPath;
                }
                catch
                {
                    return originalPath;
                }
                finally
                {
                    try { famDoc?.Close(false); } catch { }
                }
            }
        }

        // ---------------- Logging ----------------
        private static void WriteRuntime(string line)
        {
            try
            {
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BA", "Logs");

                Directory.CreateDirectory(logDir);

                var logPath = Path.Combine(logDir, "BA_FamilyGuardRuntimeLog.txt");
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
            }
            catch { }
        }
    }
}