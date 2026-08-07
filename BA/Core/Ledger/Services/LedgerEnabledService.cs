using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace BA.Core.Ledger
{
    /// <summary>
    /// Manually-assigned per-central on/off switch for the Type Data Ledger sync engine.
    /// Stored directly in the document (ExtensibleStorage on ProjectInformation), same
    /// pattern as CentralIdentifierService and ProjectSetService, so it travels with every
    /// local copy of a given central regardless of machine, user, or session context.
    ///
    /// DEFAULT IS OFF. A central with no entity set at all (a brand-new central, or an
    /// existing one that has never had this explicitly turned on) reads as disabled. This is
    /// deliberate: enabling Ledger sync for a building is a one-time, explicit opt-in action
    /// taken once per central, not something that silently starts writing shared parameter
    /// values across a model the first time someone opens it with a newer BA.dll installed.
    ///
    /// Boolean stored as a "1"/"0" string, not Entity.Set&lt;bool&gt;, since Revit 2026's
    /// ExtensibleStorage throws InternalException on bool fields without a ForgeTypeId. Same
    /// workaround already applied elsewhere in this project for boolean ExtensibleStorage
    /// fields; kept consistent here.
    /// </summary>
    public static class LedgerEnabledService
    {
        private static readonly Guid SchemaGuid = new Guid("A4C7F1E2-3B6D-4F8A-9E1C-5D2A8B7C4F91");
        private const string FieldName = "LedgerSyncEnabled";
        private static Schema _schema;

        /// <summary>
        /// True only if this central has been explicitly turned on. No entity, an invalid
        /// entity, or any stored value other than the literal string "1" all resolve to false.
        /// </summary>
        public static bool IsEnabled(Document doc)
        {
            ProjectInfo info = doc?.ProjectInformation;
            if (info == null)
            {
                return false;
            }

            Entity entity = info.GetEntity(GetSchema());
            if (!entity.IsValid())
            {
                return false;
            }

            string value = entity.Get<string>(FieldName);
            return string.Equals(value, "1", StringComparison.Ordinal);
        }

        /// <summary>
        /// Must be called from within an active Transaction on doc. Caller's responsibility,
        /// same convention as CentralIdentifierService.SetIdentifier and
        /// ProjectSetService.SetProjectSetName.
        /// </summary>
        public static void SetEnabled(Document doc, bool enabled)
        {
            ProjectInfo info = doc?.ProjectInformation;
            if (info == null)
            {
                throw new InvalidOperationException("Document has no ProjectInformation element to store the Ledger enabled flag on.");
            }

            var entity = new Entity(GetSchema());
            entity.Set(FieldName, enabled ? "1" : "0");
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
            builder.SetSchemaName("BA_LedgerSyncEnabled");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(FieldName, typeof(string));

            _schema = builder.Finish();
            return _schema;
        }
    }
}