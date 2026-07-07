using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BATools.SelectionManager.ExternalEvents;
using BATools.SelectionManager.Models;

namespace BATools.SelectionManager.Infrastructure
{
    /// <summary>
    /// Central singleton bridge between WPF ViewModels and Revit API.
    /// Initialize once from BaApplication.OnStartup.
    /// All methods are safe to call from WPF thread.
    /// </summary>
    public sealed class SelectionManagerBridge
    {
        private static readonly SelectionManagerBridge _instance = new();
        public static SelectionManagerBridge Instance => _instance;

        // Handlers
        public RecallSetHandler RecallHandler { get; } = new();
        public SaveCurrentSelectionHandler SaveHandler { get; } = new();
        public ViewOperationHandler ViewOpHandler { get; } = new();
        public SetMutationHandler SetMutationHandler { get; } = new();
        public PostCommandHandler PostCmdHandler { get; } = new();  // <- NEW
        public ReadFamilyTypesHandler FamilyTypesHandler { get; } = new(); // <- NEW
        public PlaceFamilyHandler PlaceFamilyHandler { get; } = new(); // <- NEW
        // External events
        private ExternalEvent? _recallEvent;
        private ExternalEvent? _saveEvent;
        private ExternalEvent? _viewOpEvent;
        private ExternalEvent? _setMutationEvent;
        private ExternalEvent? _postCommandEvent;
        private ExternalEvent? _readFamilyTypesEvent;  // <- NEW
        private ExternalEvent? _placeFamilyEvent;       // <- NEW

        private UIApplication? _uiApp;

        private SelectionManagerBridge() { }

        public void Initialize(UIApplication uiApp)
        {
            _uiApp = uiApp;
            _recallEvent = ExternalEvent.Create(RecallHandler);
            _saveEvent = ExternalEvent.Create(SaveHandler);
            _viewOpEvent = ExternalEvent.Create(ViewOpHandler);
            _setMutationEvent = ExternalEvent.Create(SetMutationHandler);
            _postCommandEvent = ExternalEvent.Create(PostCmdHandler);
            _readFamilyTypesEvent = ExternalEvent.Create(FamilyTypesHandler); // <- NEW
            _placeFamilyEvent = ExternalEvent.Create(PlaceFamilyHandler); // <- NEW
        }

        public void RequestRecall(Guid setId)
        {
            RecallHandler.SetId = setId;
            _recallEvent?.Raise();
        }

        public void RequestSaveCurrentSelection(string name, Action<SelectionSet>? onComplete = null)
        {
            SaveHandler.SetName = name;
            SaveHandler.OnComplete = onComplete;
            _saveEvent?.Raise();
        }

        public void RequestViewOperation(ViewOperationType op, List<ElementId> ids, int colorArgb = 0)
        {
            ViewOpHandler.Operation = op;
            ViewOpHandler.ElementIds = ids;
            ViewOpHandler.ColorArgb = colorArgb;
            _viewOpEvent?.Raise();
        }

        public void RequestAddToSet(Guid setId)
        {
            SetMutationHandler.Operation = SetMutationType.AddCurrentSelection;
            SetMutationHandler.TargetSetId = setId;
            _setMutationEvent?.Raise();
        }

        public void RequestRemoveFromSet(Guid setId)
        {
            SetMutationHandler.Operation = SetMutationType.RemoveCurrentSelection;
            SetMutationHandler.TargetSetId = setId;
            _setMutationEvent?.Raise();
        }
        public void RequestPostCommand(PostableCommand command)        // <- NEW
        {
            PostCmdHandler.Command = command;
            _postCommandEvent?.Raise();
        }
        public void RequestReadFamilyTypes(                           // <- NEW
            Action<List<ExternalEvents.FamilyTypeInfo>> onComplete)
        {
            FamilyTypesHandler.OnComplete = onComplete;
            _readFamilyTypesEvent?.Raise();
        }

        public void RequestPlaceFamily(                               // <- NEW
            string uniqueId, string familyName, string typeName)
        {
            PlaceFamilyHandler.SymbolUniqueId = uniqueId;
            PlaceFamilyHandler.FamilyName = familyName;
            PlaceFamilyHandler.TypeName = typeName;
            _placeFamilyEvent?.Raise();
        }
        public void RequestCurrentSelectionIds(Action<List<string>> callback)
        {
            SetMutationHandler.Operation = SetMutationType.GetCurrentSelection;
            SetMutationHandler.OnSelectionRead = callback;
            _setMutationEvent?.Raise();
        }

        public bool IsInitialized => _uiApp != null;
    }
}