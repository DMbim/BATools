// Path: BA\Materials\LibraryDocumentService.cs
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;

namespace BA.Materials
{
    /// <summary>
    /// Owns BA_MaterialLibrary.rvt as a normal, VISIBLE, ACTIVE document, opened via
    /// UIApplication.OpenAndActivateDocument. This is a deliberate reversal from an
    /// earlier hidden-background design: native Revit commands (Material Browser,
    /// Asset Browser, Texture Alignment) only operate on documents that have an actual
    /// window, a background-opened Document has none, so native editing was flatly
    /// unreachable under the old design. This version trades that for the real cost of
    /// taking over Revit's active document while the library is open, the caller's
    /// project is pushed to the background (still open, not closed, Revit's own Switch
    /// Windows gets the user back to it), and SaveAndClose attempts to hand focus back
    /// automatically on close.
    ///
    /// THREADING: every public method here touches the Revit API and MUST be called
    /// from Revit's API thread, via an IExternalEventHandler.Execute implementation
    /// (BA's existing RevitExternalInvoker / ExternalEvent handler pair).
    ///
    /// LIFETIME: one instance per editing session (one per open MaterialLibraryWindow).
    /// Do not share one instance across multiple windows.
    /// </summary>
    public sealed class LibraryDocumentService : IDisposable
    {
        /// <summary>
        /// Network path to the shared library file. Confirm/adjust against the actual
        /// resource location before first deployment.
        /// </summary>
        public const string DefaultLibraryPath =
            @"S:\CAD\Autodesk Revit\_admin\BA_tools\MaterialLibrary\BA_MaterialLibrary.rvt";

        private readonly string _libraryPath;
        private readonly MaterialLibraryLock _lock;
        private Document _libraryDocument;
        private bool _lockOwnedByThisSession;
        private string _previouslyActiveDocumentPath;

        public Document LibraryDocument => _libraryDocument;
        public bool IsOpen => _libraryDocument != null && _libraryDocument.IsValidObject;
        public string LibraryPath => _libraryPath;

        public LibraryDocumentService(string libraryPath = DefaultLibraryPath)
        {
            _libraryPath = libraryPath;
            _lock = new MaterialLibraryLock(_libraryPath);
        }

        public sealed class OpenResult
        {
            public bool Success { get; set; }
            public bool ReadOnly { get; set; }
            public string FailureReason { get; set; } = string.Empty;
        }

        /// <summary>
        /// Opens and activates the library document. If requestWriteAccess is true,
        /// attempts to acquire MaterialLibraryLock first, failing that opens read-only
        /// so browsing still works while someone else has it locked for editing. Must
        /// run on Revit's API thread.
        /// </summary>
        public OpenResult OpenForEditing(UIApplication uiApp, bool requestWriteAccess, bool allowStaleOverride)
        {
            if (uiApp == null)
                throw new ArgumentNullException(nameof(uiApp));

            if (IsOpen)
            {
                return new OpenResult { Success = true, ReadOnly = !_lockOwnedByThisSession };
            }

            // Capture what was active before we take over, stored as a path rather
            // than a live Document/UIDocument reference, those can go stale if
            // anything else happens to that document while the library is open.
            UIDocument previousUIDoc = uiApp.ActiveUIDocument;
            if (previousUIDoc?.Document != null && !string.IsNullOrEmpty(previousUIDoc.Document.PathName))
            {
                _previouslyActiveDocumentPath = previousUIDoc.Document.PathName;
            }
            else
            {
                _previouslyActiveDocumentPath = null;
                if (previousUIDoc?.Document != null)
                {
                    AppLogger.LogInfo("BA.Materials: previously active document has no saved path (new/unsaved document), cannot reactivate it automatically on close.");
                }
            }

            bool openedReadOnly = true;

            if (requestWriteAccess)
            {
                MaterialLibraryLock.AcquireResult lockResult = _lock.TryAcquire(allowStaleOverride);
                if (lockResult.Success)
                {
                    openedReadOnly = false;
                    _lockOwnedByThisSession = true;
                }
                else
                {
                    AppLogger.LogInfo($"BA.Materials: opening library read-only, lock unavailable: {lockResult.Reason}");
                }
            }

            try
            {
                UIDocument openedUIDoc = uiApp.OpenAndActivateDocument(_libraryPath);
                _libraryDocument = openedUIDoc?.Document;

                if (_libraryDocument == null)
                {
                    if (_lockOwnedByThisSession)
                    {
                        _lock.Release();
                        _lockOwnedByThisSession = false;
                    }

                    return new OpenResult
                    {
                        Success = false,
                        FailureReason = "Revit returned no document for the library path. Verify the file exists and is a valid Revit model."
                    };
                }

                AppLogger.LogInfo($"BA.Materials: opened and activated library document at {_libraryPath}, readOnly={openedReadOnly}");

                return new OpenResult { Success = true, ReadOnly = openedReadOnly };
            }
            catch (Exception ex)
            {
                if (_lockOwnedByThisSession)
                {
                    _lock.Release();
                    _lockOwnedByThisSession = false;
                }

                AppLogger.LogError("LibraryDocumentService.OpenForEditing", ex);

                return new OpenResult
                {
                    Success = false,
                    FailureReason = "Failed to open the material library document. See BA Tools log for details."
                };
            }
        }

        /// <summary>
        /// Saves (if this session holds write access and saveChanges is true), closes
        /// the library document, and attempts to reactivate whatever document was
        /// active before OpenForEditing took over.
        ///
        /// VERIFY BEFORE SHIPPING: reactivation calls OpenAndActivateDocument again
        /// with the previously active document's saved path. This assumes Revit treats
        /// re-passing an already-open path as "activate that existing window" rather
        /// than opening a second duplicate session of the same file, since it's already
        /// open elsewhere in the same Revit instance. I have not confirmed this against
        /// a live session. If it instead throws or opens a duplicate, this needs a
        /// different reactivation mechanism, flag it if you see either on your test.
        /// uiApp may be null (best-effort save/close with no reactivation attempted).
        /// </summary>
        public void SaveAndClose(bool saveChanges, UIApplication uiApp)
        {
            if (!IsOpen)
            {
                if (_lockOwnedByThisSession)
                {
                    _lock.Release();
                    _lockOwnedByThisSession = false;
                }
                return;
            }

            try
            {
                if (saveChanges && _lockOwnedByThisSession)
                {
                    _libraryDocument.Save(new SaveOptions { Compact = false });
                    AppLogger.LogInfo($"BA.Materials: saved library document at {_libraryPath}");
                }
                else if (saveChanges && !_lockOwnedByThisSession)
                {
                    AppLogger.LogInfo("BA.Materials: save requested but this session does not own the write lock, discarding changes instead of saving.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("LibraryDocumentService.SaveAndClose (save step)", ex);
                // Fall through to close regardless, do not leak an open document even
                // if the save itself failed.
            }

            try
            {
                _libraryDocument.Close(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("LibraryDocumentService.SaveAndClose (close step)", ex);
            }
            finally
            {
                _libraryDocument = null;

                if (_lockOwnedByThisSession)
                {
                    _lock.Release();
                    _lockOwnedByThisSession = false;
                }
            }

            if (!string.IsNullOrEmpty(_previouslyActiveDocumentPath) && uiApp != null)
            {
                try
                {
                    uiApp.OpenAndActivateDocument(_previouslyActiveDocumentPath);
                    AppLogger.LogInfo($"BA.Materials: reactivated previous document at {_previouslyActiveDocumentPath}");
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("LibraryDocumentService.SaveAndClose (reactivate previous document)", ex);
                }
            }

            _previouslyActiveDocumentPath = null;
        }

        /// <summary>
        /// True if the given Document is this service's currently-open library
        /// document, by reference. Used to guard actions (Load Into Project) that
        /// assume the active document is the user's real project, not the library
        /// itself, a real possibility now that the library is a normal visible document
        /// the user could have switched focus onto.
        /// </summary>
        public bool IsLibraryDocument(Document doc)
        {
            return IsOpen && doc != null && ReferenceEquals(doc, _libraryDocument);
        }

        public void Dispose()
        {
            // Best-effort safety net only, not guaranteed to run on Revit's API
            // thread, callers must not rely on this for the normal close path.
            if (IsOpen)
            {
                AppLogger.LogInfo("BA.Materials: LibraryDocumentService disposed with an open document, this indicates SaveAndClose was not called explicitly.");
            }
        }
    }
}