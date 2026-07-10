using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace BA.Core.Ledger
{
    /// <summary>
    /// Manually-assigned identifier for this central model, stored directly in the document
    /// (ExtensibleStorage on ProjectInformation) rather than derived from any Revit API
    /// property. This travels with every local copy of a given central regardless of machine,
    /// user, or session context, and is set once, deliberately, by whoever administers each
    /// building's model (e.g. "BuildingA", "BuildingB"). PersonalLedgerService prefers this
    /// over WorksharingCentralGUID when present.
    /// </summary>
    public static class CentralIdentifierService
    {
        private static readonly Guid SchemaGuid = new Guid("6B2E1A4D-9F0C-4E2B-8B7A-3D1F5C9E7A22");
        private const string FieldName = "ManualCentralIdentifier";
        private static Schema _schema;

        public static string GetIdentifier(Document doc)
        {
            ProjectInfo info = doc?.ProjectInformation;
            if (info == null)
            {
                return null;
            }

            Entity entity = info.GetEntity(GetSchema());
            if (!entity.IsValid())
            {
                return null;
            }

            string value = entity.Get<string>(FieldName);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>
        /// Must be called from within an active Transaction on doc. Caller's responsibility,
        /// this service does not open its own transaction since callers may want to batch it
        /// with other changes.
        /// </summary>
        public static void SetIdentifier(Document doc, string identifier)
        {
            ProjectInfo info = doc?.ProjectInformation;
            if (info == null)
            {
                throw new InvalidOperationException("Document has no ProjectInformation element to store the identifier on.");
            }

            var entity = new Entity(GetSchema());
            entity.Set(FieldName, identifier ?? string.Empty);
            info.SetEntity(entity);
        }

        private static Schema GetSchema()
        {
            if (_schema != null)
            {
                return _schema;
            }

            _schema = Schema.Lookup(SchemaGuid);
            if (_schema != null)
            {
                return _schema;
            }

            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName("BA_LedgerManualCentralIdentifier");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(FieldName, typeof(string));

            _schema = builder.Finish();
            return _schema;
        }
    }
}
