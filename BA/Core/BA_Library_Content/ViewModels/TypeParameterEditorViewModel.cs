using Autodesk.Revit.DB;
using BA.BAApplication;
using BA.Core.Content.Models;
using BA.Core.Content.Services;
using BA.UI.ExternalEvents;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace BA.UI.LoadedFamilyBrowser
{
    public sealed class TypeParameterEditorViewModel : INotifyPropertyChanged
    {
        private readonly ElementId _symbolId;
        private bool _isBusy;
        private string _statusMessage = string.Empty;

        public ObservableCollection<TypeParameterEditItem> Parameters { get; } = new();

        public string TypeName { get; }

        public bool IsBusy
        {
            get => _isBusy;
            private set => SetField(ref _isBusy, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetField(ref _statusMessage, value);
        }

        public BA.Core.Mvvm.RelayCommand SaveCommand { get; }
        public BA.Core.Mvvm.RelayCommand CancelCommand { get; }

        public event Action? RequestClose;

        public TypeParameterEditorViewModel(ElementId symbolId, string typeName, IReadOnlyList<TypeParameterEditItem> initialParameters)
        {
            _symbolId = symbolId;
            TypeName = typeName;

            foreach (var p in initialParameters)
                Parameters.Add(p);

            SaveCommand = new BA.Core.Mvvm.RelayCommand(_ => Save(), _ => !IsBusy && Parameters.Any(p => p.IsDirty));
            CancelCommand = new BA.Core.Mvvm.RelayCommand(_ => RequestClose?.Invoke());
        }

        /// <summary>
        /// Loads the current writable parameters for a FamilySymbol.
        /// Must be called via AppExternalInvoker (this executes inside
        /// Revit API context, invoked from the Run&lt;T&gt; callback).
        /// </summary>
        public static List<TypeParameterEditItem> BuildParameterList(Document doc, ElementId symbolId)
        {
            var result = new List<TypeParameterEditItem>();

            if (doc.GetElement(symbolId) is not FamilySymbol symbol)
                return result;

            foreach (Parameter param in symbol.Parameters.Cast<Parameter>().OrderBy(p => p.Definition.Name))
            {
                if (param.IsReadOnly)
                    continue;

                if (param.StorageType == StorageType.None)
                    continue;

                string display = FormatParameterValue(param);

                result.Add(new TypeParameterEditItem
                {
                    Name = param.Definition.Name,
                    StorageType = param.StorageType,
                    IsReadOnly = param.IsReadOnly,
                    IsShared = param.IsShared,
                    OriginalValueDisplay = display,
                    EditedValueText = display
                });
            }

            return result;
        }

        private static string FormatParameterValue(Parameter param)
        {
            return param.StorageType switch
            {
                StorageType.String => param.AsString() ?? string.Empty,
                StorageType.Integer => param.AsInteger().ToString(),
                StorageType.Double => param.AsValueString() ?? param.AsDouble().ToString(),
                StorageType.ElementId => param.AsElementId().Value.ToString(),
                _ => string.Empty
            };
        }

        private void Save()
        {
            var dirty = Parameters.Where(p => p.IsDirty).ToDictionary(p => p.Name, p => p.EditedValueText);
            if (dirty.Count == 0)
            {
                RequestClose?.Invoke();
                return;
            }

            IsBusy = true;
            StatusMessage = "Saving parameter changes...";

            AppExternalInvoker.Instance.Run(
                uiApp => LoadedFamilyOperations.SetParameterValues(uiApp.ActiveUIDocument.Document, _symbolId, dirty),
                onCompleted: result =>
                {
                    IsBusy = false;
                    if (result.Success)
                    {
                        RequestClose?.Invoke();
                    }
                    else
                    {
                        StatusMessage = $"Failed: {result.Message}";
                    }
                },
                onError: ex =>
                {
                    IsBusy = false;
                    StatusMessage = $"Error: {ex.Message}";
                    BA.BAApplication.AppLogger.LogError(nameof(TypeParameterEditorViewModel), ex);
                });
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}