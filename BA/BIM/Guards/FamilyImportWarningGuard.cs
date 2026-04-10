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
    /// <summary>
    /// Family Standards Guard.
    ///
    /// Goals:
    /// 1) Try to intercept built-in commands:
    ///    - Project: Insert -> Load Family (ID_LOAD_FAMILY)
    ///    - Family Editor: Load into Project (PostableCommand.LoadIntoProject)
    ///
    /// 2) Reliable fallback:
    ///    - Listen to ControlledApplication.DocumentChanged in project documents.
    ///    - Detect newly added Family elements (new load).
    ///    - On Idling, show warning window and optionally apply fixes (rename in project, create default type, reload).
    ///
    /// Why fallback:
    /// - Some UI contexts rebuild command bindings and BeforeExecuted won't fire consistently.
    ///   Rebinding on context change is a known workaround; fallback guarantees coverage. :contentReference[oaicite:1]{index=1}
    /// </summary>
    public static class FamilyImportWarningGuardV2
    {
        // ---------------- Settings ----------------
        public static bool Enabled { get; set; } = true;

        /// <summary>
        /// If true: show window only when analysis finds issues.
        /// If false: always show.
        /// </summary>
        public static bool ShowOnlyWhenIssues { get; set; } = true;

        public static string RequiredPrefix { get; set; } = "BA_";
        public static double MaxRecommendedSizeMb { get; set; } = 5.0;
        public static int MaxRecommendedFamilyElementCount { get; set; } = 2000; // heuristic "monster detector"
        public static string DefaultTypeName { get; set; } = "BA_Default";

        public static string TempFixRoot => Path.Combine(Path.GetTempPath(), "BA", "FamilyFixes");

        // ---------------- Internal state ----------------
        private const string CmdLoadFamily_Project = "ID_LOAD_FAMILY";

        private static UIControlledApplication? _uiControlledApp;
        private static UIApplication? _cachedUiApp;

        private static bool _suppressForSession;
        private static bool _isHandling;
        private static bool _isAnalyzing; // ADD THIS

        private static readonly List<AddInCommandBinding> _bindings = new();

        // For reliable post-load detection:
        private sealed class PendingFamily
        {
            public Document ProjectDoc { get; set; } = null!;
            public ElementId FamilyId { get; set; } = ElementId.InvalidElementId;
            public DateTime When { get; set; }
            public string Source { get; set; } = "";
        }

        private static readonly Queue<PendingFamily> _pending = new();
        private static readonly HashSet<string> _recentlyProcessed = new(); // key: docGuid|familyId|ticksBucket
        private static Document? _lastActiveProjectDoc;

        // ---------------- Public API ----------------
        public static void Register(UIControlledApplication app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (_uiControlledApp != null) return;

            _uiControlledApp = app;

            // Capture UIApplication for PostCommand, doc discovery, etc.
            app.Idling += OnIdlingCaptureUiApp;
            app.ViewActivated += OnViewActivated;

            // DocumentChanged fallback (reliable)
            app.ControlledApplication.DocumentChanged += OnDocumentChanged;

            // Initial bind attempt
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

            // Process pending post-load events
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

            // Re-bind when context changes (project <-> family editor),
            // because Revit can rebuild the command stack.
            EnsureBindings("ViewActivated");
        }

        // ---------------- Binding management ----------------
        private static void EnsureBindings(string reason)
        {
            if (_uiControlledApp == null) return;

            TryBindCommandId(_uiControlledApp, CmdLoadFamily_Project, $"Project: Load Family ({reason})");

            // Always available in your logs
            TryBindPostable(_uiControlledApp, PostableCommand.LoadIntoProject, $"FamilyEditor: LoadIntoProject ({reason})");

            // Optional (compile-safe): only binds if that enum name exists in this Revit build
            TryBindPostableByName(_uiControlledApp, "LoadIntoProjectAndClose", $"FamilyEditor: LoadIntoProjectAndClose ({reason})");
        }
        private static void TryBindPostableByName(UIControlledApplication app, string postableName, string label)
        {
            // IMPORTANT: This compiles even if the enum member does not exist.
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
            try { app.RemoveAddInCommandBinding(cmd); } catch { /* ignore */ }
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

                // If we can't cancel, don't fight Revit.
                if (!e.Cancellable || doc == null) return;

                if (doc.IsFamilyDocument)
                {
                    // Cancel and replace FamilyEditor load with our own guarded load.
                    e.Cancel = true;
                    GuardedLoadFromFamilyEditor(uiapp, doc);
                }
                else
                {
                    // Cancel and replace project load with our own picker.
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
            // Not critical; useful for debugging whether a binding ever fires.
            try { WriteRuntime("Executed fired"); } catch { }
        }

        // ---------------- Reliable fallback: detect actual load ----------------
        private static void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            if (!Enabled) return;
            if (_suppressForSession) return;  // respect "suppress for session" on all paths
            if (_isHandling) return;          // don't re-enter when the guard itself loads a family
            if (_isAnalyzing) return;

            Document doc = e.GetDocument();
            if (doc == null || !doc.IsValidObject) return;
            if (doc.IsFamilyDocument) return; // we only care about project docs receiving families

            try
            {
                var added = e.GetAddedElementIds();
                if (added == null || added.Count == 0) return;

                // Detect Family elements added
                List<ElementId> familyIds = new List<ElementId>();
                foreach (var id in added)
                {
                    var el = doc.GetElement(id);
                    if (el is Family)
                        familyIds.Add(id);
                }

                if (familyIds.Count == 0) return;

                foreach (var fid in familyIds)
                {
                    EnqueuePending(doc, fid, "DocumentChanged");
                }
            }
            catch (Exception ex)
            {
                WriteRuntime("ERROR DocumentChanged: " + ex.Message);
            }
        }

        private static void EnqueuePending(Document projectDoc, ElementId familyId, string source)
        {
            // De-dupe (bucket by ~3 seconds)
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

            // process one per idling tick to avoid UI spam
            var item = _pending.Dequeue();

            if (item.ProjectDoc == null || !item.ProjectDoc.IsValidObject) return;

            var fam = item.ProjectDoc.GetElement(item.FamilyId) as Family;
            if (fam == null) return;

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

            // Analyze file
            var analysis = FamilyAnalyzer.AnalyzeFamilyFile(uiapp, familyPath, RequiredPrefix, MaxRecommendedSizeMb, DefaultTypeName, MaxRecommendedFamilyElementCount);
            if (!ShouldShow(analysis)) // user prefers silent when clean
            {
                // Just load normally
                projectDoc.LoadFamily(familyPath, new BaFamilyLoadOptions(), out _);
                return;
            }

            var decision = ShowDecisionWindow(uiapp, analysis, familyPath, analysis.FamilyName);

            if (decision.SuppressForSession) _suppressForSession = true;
            if (decision.Mode == FamilyLoadMode.Cancel) return;

            string pathToLoad = familyPath;

            if (decision.Mode == FamilyLoadMode.FixAndLoad && decision.CreateDefaultTypeIfNone && analysis.TypeCount == 0)
                pathToLoad = FamilyAnalyzer.CreateTempFamilyWithDefaultType(uiapp, familyPath, analysis.SuggestedDefaultTypeName);

            bool loaded = projectDoc.LoadFamily(pathToLoad, new BaFamilyLoadOptions(), out Family loadedFamily);
            WriteRuntime($"Project load result: loaded={loaded}, fam={(loadedFamily == null ? "NULL" : loadedFamily.Name)}");

            if (!loaded || loadedFamily == null) return;

            if (decision.AddBaPrefixInProject && !loadedFamily.Name.StartsWith(RequiredPrefix, StringComparison.OrdinalIgnoreCase))
                TryRenameFamilyInProject(projectDoc, loadedFamily.Id, RequiredPrefix + loadedFamily.Name);
        }

        private static void GuardedLoadFromFamilyEditor(UIApplication? uiapp, Document familyDoc)
        {
            // Try to determine a target project doc
            var projectDoc = PickTargetProjectDoc(uiapp);
            if (projectDoc == null)
            {
                TaskDialog.Show("BA | Family Guard",
                    "No project document found.\nOpen a project and activate it once, then try again.");
                return;
            }

            var analysis = FamilyAnalyzer.AnalyzeFamilyDocument(familyDoc, RequiredPrefix, MaxRecommendedSizeMb, DefaultTypeName, MaxRecommendedFamilyElementCount);

            // If clean and user wants only issues, just load.
            if (!ShouldShow(analysis))
            {
                LoadFamilyDocIntoProject(familyDoc, projectDoc, new BaFamilyLoadOptions(), out _);
                return;
            }

            var decision = ShowDecisionWindow(uiapp, analysis,
                filePath: string.IsNullOrWhiteSpace(familyDoc.PathName) ? "(Unsaved family)" : familyDoc.PathName,
                familyName: analysis.FamilyName);

            if (decision.SuppressForSession) _suppressForSession = true;
            if (decision.Mode == FamilyLoadMode.Cancel) return;

            // Fix inside open family doc before loading
            if (decision.Mode == FamilyLoadMode.FixAndLoad && decision.CreateDefaultTypeIfNone && analysis.TypeCount == 0)
            {
                TryCreateDefaultTypeInFamilyDoc(familyDoc, analysis.SuggestedDefaultTypeName);
                analysis = FamilyAnalyzer.AnalyzeFamilyDocument(familyDoc, RequiredPrefix, MaxRecommendedSizeMb, DefaultTypeName, MaxRecommendedFamilyElementCount);
            }

            bool loaded = LoadFamilyDocIntoProject(familyDoc, projectDoc, new BaFamilyLoadOptions(), out Family? loadedFamily);
            WriteRuntime($"FamilyEditor load result: loaded={loaded}, fam={(loadedFamily == null ? "NULL" : loadedFamily.Name)}");

            if (!loaded) return;

            loadedFamily ??= FindFamilyByName(projectDoc, analysis.FamilyName);

            if (decision.AddBaPrefixInProject && !analysis.HasRequiredPrefix && loadedFamily != null)
                TryRenameFamilyInProject(projectDoc, loadedFamily.Id, RequiredPrefix + loadedFamily.Name);
        }

        private static void ShowWarningAndApplyFixes(UIApplication uiapp, Document projectDoc, Family family, string source)
        {
            // Analyze by opening the family doc (best effort)
            _isAnalyzing = true;  // ADD
            FamilyAnalysis analysis;
            try
            {
                analysis = FamilyAnalyzer.AnalyzeLoadedFamilyInProject(projectDoc, family,
                    RequiredPrefix, MaxRecommendedSizeMb, DefaultTypeName, MaxRecommendedFamilyElementCount);
            }
            finally
            {
                _isAnalyzing = false;  // ADD
            }

            if (!ShouldShow(analysis))
            {
                WriteRuntime($"Post-load ({source}): no issues => silent for {analysis.FamilyName}");
                return;
            }

            var decision = ShowDecisionWindow(uiapp, analysis, filePath: "(Loaded into project)", familyName: analysis.FamilyName);

            if (decision.SuppressForSession) _suppressForSession = true;
            if (decision.Mode == FamilyLoadMode.Cancel) return;

            // Rename in project
            if (decision.AddBaPrefixInProject && !analysis.HasRequiredPrefix)
                TryRenameFamilyInProject(projectDoc, family.Id, RequiredPrefix + family.Name);

            // If they want default type and none exist: open family, create, reload into project
            if (decision.Mode == FamilyLoadMode.FixAndLoad && decision.CreateDefaultTypeIfNone && analysis.TypeCount == 0)
            {
                Document? famDoc = null;
                try
                {
                    famDoc = projectDoc.EditFamily(family);
                    if (famDoc != null && famDoc.IsValidObject && famDoc.IsFamilyDocument)
                    {
                        TryCreateDefaultTypeInFamilyDoc(famDoc, analysis.SuggestedDefaultTypeName);

                        // Reload (reflection handles overload differences)
                        LoadFamilyDocIntoProject(famDoc, projectDoc, new BaFamilyLoadOptions(), out _);
                    }
                }
                catch (Exception ex)
                {
                    WriteRuntime("Post-load fix ERROR: " + ex.Message);
                }
                finally
                {
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
            // Best: last active project doc
            if (_lastActiveProjectDoc != null)
            {
                try
                {
                    if (_lastActiveProjectDoc.IsValidObject && !_lastActiveProjectDoc.IsFamilyDocument)
                        return _lastActiveProjectDoc;
                }
                catch { }
            }

            // Fallback: any open project doc
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

            // A) LoadFamily(Document, IFamilyLoadOptions, out Family)
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

            // B) LoadFamily(Document, IFamilyLoadOptions)
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

            // C) LoadFamily(Document)
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

                // Size only if saved
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

                FillFromFamilyDoc(familyDoc, a, maxElemCount);
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
                        FillFromFamilyDoc(famDoc, a, maxElemCount);
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

                // Cannot know original library file size reliably after load.
                // We instead use family element count heuristic from EditFamily.
                Document? famDoc = null;
                try
                {
                    famDoc = projectDoc.EditFamily(family);
                    if (famDoc != null && famDoc.IsFamilyDocument)
                        FillFromFamilyDoc(famDoc, a, maxElemCount);
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

            private static void FillFromFamilyDoc(Document famDoc, FamilyAnalysis a, int maxElemCount)
            {
                try
                {
                    var fm = famDoc.FamilyManager;
                    a.TypeCount = fm?.Types?.Size ?? 0;
                    if (a.TypeCount == 0)
                        a.Flags.Add("No types in family (bad sign).");

                    a.CadImportCount = new FilteredElementCollector(famDoc)
                        .OfClass(typeof(ImportInstance))
                        .Cast<ImportInstance>()
                        .Count();

                    if (a.CadImportCount > 0)
                        a.Flags.Add($"Contains imported CAD ({a.CadImportCount}).");

                    // Monster heuristic: element count inside family doc
                    a.FamilyElementCount = new FilteredElementCollector(famDoc).WhereElementIsNotElementType().GetElementCount();
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
