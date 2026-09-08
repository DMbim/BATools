using System;
using System.Collections.Generic;
using System.Windows.Media;
using Autodesk.Revit.UI;

namespace BaTools.Ribbon
{
    /// <summary>
    /// Fluent builder for a single Revit ribbon PulldownButton with an arbitrary number of
    /// sub push-buttons and pyRevit-style separators between groups. Start with
    /// <see cref="RibbonPanelExtensions.BeginPulldownButton"/>, chain
    /// <see cref="AddPushButton{TCommand}"/> and <see cref="AddSeparator"/>, then call
    /// <see cref="Build"/> to materialize everything on the panel.
    /// </summary>
    public sealed class PulldownButtonBuilder
    {
        private readonly RibbonPanel _panel;
        private readonly PulldownButtonData _pulldownData;
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly HashSet<string> _usedNames = new HashSet<string>(StringComparer.Ordinal);
        private bool _built;

        private abstract class Entry
        {
        }

        private sealed class PushButtonEntry : Entry
        {
            public PushButtonData Data;
        }

        private sealed class SeparatorEntry : Entry
        {
        }

        internal PulldownButtonBuilder(
            RibbonPanel panel,
            string internalName,
            string text,
            string tooltip,
            ImageSource icon16,
            ImageSource icon32)
        {
            _panel = panel ?? throw new ArgumentNullException(nameof(panel));

            if (string.IsNullOrWhiteSpace(internalName))
                throw new ArgumentException("Pulldown internal name must not be null or empty.", nameof(internalName));

            _pulldownData = new PulldownButtonData(internalName, text)
            {
                ToolTip = tooltip,
                Image = icon16,
                LargeImage = icon32
            };

            _usedNames.Add(internalName);
        }

        /// <summary>
        /// Adds one sub push-button bound to its own IExternalCommand type. Each button needs
        /// its own command class: Execute() has no built in way to identify which RibbonItem
        /// invoked it, so routing many buttons through one shared class requires an out of band
        /// dispatch mechanism. Give each button a thin command class, optionally deriving from a
        /// common abstract base to avoid duplicating try/catch and error handling boilerplate.
        /// </summary>
        public PulldownButtonBuilder AddPushButton<TCommand>(
            string internalName,
            string text,
            string tooltip,
            ImageSource icon16,
            ImageSource icon32,
            string longDescription = null)
            where TCommand : IExternalCommand
        {
            EnsureNotBuilt();
            AddPushButtonData<TCommand>(internalName, text, tooltip, icon16, icon32, longDescription, availabilityClassName: null);
            return this;
        }

        /// <summary>
        /// Adds one sub push-button bound to its own IExternalCommand type, restricting
        /// availability with an IExternalCommandAvailability implementation. TAvailability must
        /// live in the same assembly as TCommand, Revit resolves AvailabilityClassName against
        /// the PushButtonData's AssemblyName, which is set from TCommand's assembly.
        /// </summary>
        public PulldownButtonBuilder AddPushButton<TCommand, TAvailability>(
            string internalName,
            string text,
            string tooltip,
            ImageSource icon16,
            ImageSource icon32,
            string longDescription = null)
            where TCommand : IExternalCommand
            where TAvailability : IExternalCommandAvailability
        {
            EnsureNotBuilt();

            if (typeof(TAvailability).Assembly != typeof(TCommand).Assembly)
            {
                throw new ArgumentException(
                    $"Availability class '{typeof(TAvailability).FullName}' must live in the same assembly as " +
                    $"command '{typeof(TCommand).FullName}'. Revit resolves AvailabilityClassName against the " +
                    "assembly path stored in AssemblyName, which this builder sets from TCommand's assembly.");
            }

            AddPushButtonData<TCommand>(internalName, text, tooltip, icon16, icon32, longDescription, typeof(TAvailability).FullName);
            return this;
        }

        /// <summary>
        /// Inserts a divider line in the pulldown's flyout list, grouping the buttons above and
        /// below it. Leading, trailing, and doubled up separators are silently collapsed.
        /// </summary>
        public PulldownButtonBuilder AddSeparator()
        {
            EnsureNotBuilt();

            if (_entries.Count == 0 || _entries[_entries.Count - 1] is SeparatorEntry)
                return this;

            _entries.Add(new SeparatorEntry());
            return this;
        }

        /// <summary>
        /// Materializes the pulldown and every queued sub-button/separator on the panel.
        /// Returns a lookup keyed by internal name, callers never depend on argument position.
        /// </summary>
        public PulldownButtonResult Build()
        {
            EnsureNotBuilt();
            _built = true;

            while (_entries.Count > 0 && _entries[_entries.Count - 1] is SeparatorEntry)
                _entries.RemoveAt(_entries.Count - 1);

            RibbonItem item = _panel.AddItem(_pulldownData);
            var pulldown = item as PulldownButton;

            if (pulldown == null)
            {
                throw new InvalidOperationException(
                    $"RibbonPanel.AddItem did not return a PulldownButton for '{_pulldownData.Name}'. " +
                    "Check whether an item with this internal name already exists on the panel.");
            }

            var buttons = new Dictionary<string, PushButton>(StringComparer.Ordinal);

            foreach (Entry entry in _entries)
            {
                if (entry is SeparatorEntry)
                {
                    pulldown.AddSeparator();
                    continue;
                }

                var pushButtonEntry = (PushButtonEntry)entry;
                PushButton pushButton = pulldown.AddPushButton(pushButtonEntry.Data);
                buttons.Add(pushButtonEntry.Data.Name, pushButton);
            }

            return new PulldownButtonResult(pulldown, buttons);
        }

        private void AddPushButtonData<TCommand>(
            string internalName,
            string text,
            string tooltip,
            ImageSource icon16,
            ImageSource icon32,
            string longDescription,
            string availabilityClassName)
            where TCommand : IExternalCommand
        {
            if (string.IsNullOrWhiteSpace(internalName))
                throw new ArgumentException("Push button internal name must not be null or empty.", nameof(internalName));

            if (!_usedNames.Add(internalName))
            {
                throw new InvalidOperationException(
                    $"Duplicate internal name '{internalName}' in pulldown '{_pulldownData.Name}'. " +
                    "Every button in a pulldown needs a unique internal name.");
            }

            Type commandType = typeof(TCommand);

            var data = new PushButtonData(
                internalName,
                text,
                commandType.Assembly.Location,
                commandType.FullName)
            {
                ToolTip = tooltip,
                Image = icon16,
                LargeImage = icon32
            };

            if (!string.IsNullOrEmpty(longDescription))
                data.LongDescription = longDescription;

            if (!string.IsNullOrEmpty(availabilityClassName))
                data.AvailabilityClassName = availabilityClassName;

            _entries.Add(new PushButtonEntry { Data = data });
        }

        private void EnsureNotBuilt()
        {
            if (_built)
                throw new InvalidOperationException("This PulldownButtonBuilder has already been built.");
        }
    }

    /// <summary>
    /// Result of building a pulldown: the PulldownButton itself plus every sub-button, keyed by
    /// the internal name it was registered with. The indexer throws KeyNotFoundException on a
    /// typo'd name, failing fast at startup instead of silently binding the wrong PushButton.
    /// </summary>
    public sealed class PulldownButtonResult
    {
        private readonly IReadOnlyDictionary<string, PushButton> _buttons;

        internal PulldownButtonResult(PulldownButton pulldownButton, IReadOnlyDictionary<string, PushButton> buttons)
        {
            PulldownButton = pulldownButton;
            _buttons = buttons;
        }

        public PulldownButton PulldownButton { get; }

        public PushButton this[string internalName] => _buttons[internalName];

        public bool TryGet(string internalName, out PushButton button) => _buttons.TryGetValue(internalName, out button);

        public IReadOnlyDictionary<string, PushButton> AllButtons => _buttons;
    }

    public static class RibbonPanelExtensions
    {
        /// <summary>
        /// Starts a fluent build of a single pulldown button on this panel. Chain
        /// AddPushButton / AddSeparator calls, then call Build().
        /// </summary>
        public static PulldownButtonBuilder BeginPulldownButton(
            this RibbonPanel panel,
            string internalName,
            string text,
            string tooltip,
            ImageSource icon16,
            ImageSource icon32)
        {
            return new PulldownButtonBuilder(panel, internalName, text, tooltip, icon16, icon32);
        }
    }
}
