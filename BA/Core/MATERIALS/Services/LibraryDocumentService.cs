// Path: BA\Materials\LibraryDocumentService.cs
using System;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using BA.BAApplication;

namespace BA.Materials
{
    /// <summary>
    /// Owns the hidden background Document session for BA_MaterialLibrary.rvt.
    ///
    /// THREADING: every public method on this class touches the Revit API and MUST be
    /// called from Revit's API thread. This class does not raise or wait on an
    /// ExternalEvent itself, it assumes it is being called from inside an
    /// IExternalEventHandler.Execute implementation (per BA's existing
    /// RevitExternalInvoker / ExternalEvent handler pair pattern). Calling any method
    /// here from the WPF window's UI thread directly will throw or corrupt state.
    ///
    /// LIFETIME: one instance per editing session (i.e. per open MaterialLibraryWindow).
    /// OpenForEditing should be called once when the window is shown, SaveAndClose once
    /// when the window closes. Do not share one instance across multiple windows.
    ///
    /// ASSUMPTION: BA_MaterialLibrary.rvt is a standalone, non-workshared file. If it
    /// needs to become a workshared central model later, Save() below must be replaced
    /// with SynchronizeWithCentral and the open path needs a relinquish-on-close step.
    /// </summary>
    public sealed class LibraryDocumentService : IDisposable
    {
        /// <summary>
        /// Network path to the shared library file. Confirm/adjust against the actual
        /// resource location before first deployment, this mirrors the existing
        /// S:\CAD\Autodesk Revit\_admin\BA_tools\ convention used elsewhere in BA Tools.
        /// </summary>
        public const string DefaultLibraryPath =
            @"S:\CAD\Autodesk Revit\_admin\BA_tools\MaterialLibrary\BA_MaterialLibrary.rvt";

        private readonly string _libraryPath;
        private readonly MaterialLibraryLock _lock;
        private Document _libraryDocument;
        private bool _lockOwnedByThisSession;

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
        /// Opens the library document. If requestWriteAccess is true, attempts to
        /// acquire MaterialLibraryLock first, failing that opens read-only so browsing
        /// still works while someone else has it locked for editing. Must run on
        /// Revit's API thread.
        /// </summary>
        public OpenResult OpenForEditing(Application application, bool requestWriteAccess, bool allowStaleOverride)
        {
            if (application == null)
                throw new ArgumentNullException(nameof(application));

            if (IsOpen)
            {
                return new OpenResult { Success = true, ReadOnly = !_lockOwnedByThisSession };
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
                ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(_libraryPath);

                OpenOptions openOptions = new OpenOptions
                {
                    DetachFromCentralOption = DetachFromCentralOption.DoNotDetach,
                    Audit = false
                };

                _libraryDocument = application.OpenDocumentFile(modelPath, openOptions);

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

                AppLogger.LogInfo($"BA.Materials: opened library document at {_libraryPath}, readOnly={openedReadOnly}");

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
        /// Saves (if this session holds write access and saveChanges is true) and closes
        /// the background document, releasing the lock file. Must run on Revit's API thread.
        /// Safe to call even if OpenForEditing was never called or already failed.
        /// </summary>
        public void SaveAndClose(bool saveChanges)
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
                // Fall through to close regardless, we do not want to leak an open
                // background document/session even if the save itself failed.
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
        }

        public void Dispose()
        {
            // Best-effort safety net only. Dispose is not guaranteed to run on Revit's
            // API thread, callers must not rely on this for the normal close path, use
            // SaveAndClose explicitly from within an ExternalEvent handler instead.
            if (IsOpen)
            {
                AppLogger.LogInfo("BA.Materials: LibraryDocumentService disposed with an open document, this indicates SaveAndClose was not called explicitly.");
            }
        }
    }
}