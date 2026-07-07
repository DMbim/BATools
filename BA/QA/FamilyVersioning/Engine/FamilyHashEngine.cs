using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using BA.QA.FamilyVersioning.Models;

namespace BA.QA.FamilyVersioning.Engine
{
    /// <summary>
    /// Metadata snapshot of a Revit Family element as loaded in a project document.
    /// Contains only the structural metadata relevant to drift detection: type names,
    /// shared parameter definitions, and family category. Parameter values are
    /// deliberately excluded, see architecture discussion: value edits are routine
    /// per-building operations and should not trigger version detection events.
    ///
    /// All collections are sorted deterministically so two snapshots of the same
    /// family state always produce the same hash regardless of the order Revit
    /// happens to return elements from its collectors.
    /// </summary>
    public sealed class FamilyMetadataSnapshot
    {
        public string FamilyName { get; }
        public string CategoryName { get; }

        /// <summary>
        /// Sorted list of type (FamilySymbol) names present in this family.
        /// </summary>
        public IReadOnlyList<string> TypeNames { get; }

        /// <summary>
        /// Sorted list of shared parameter descriptors extracted from FamilySymbol
        /// instances. Each descriptor captures the GUID, storage type, and parameter
        /// group so structural changes (rename, type change, group change, removal)
        /// are all detected. Built from all unique shared parameters found across all
        /// types, not just the first type, since different types can expose different
        /// shared parameters in some family designs.
        /// </summary>
        public IReadOnlyList<SharedParameterDescriptor> SharedParameters { get; }

        /// <summary>
        /// SHA256 hash of the concatenated sorted metadata. Deterministic for a given
        /// family state. Two snapshots with the same hash have identical structural
        /// metadata (though not necessarily identical parameter values, which are not
        /// hashed).
        /// </summary>
        public string Hash { get; }

        public FamilyMetadataSnapshot(
            string familyName,
            string categoryName,
            IReadOnlyList<string> typeNames,
            IReadOnlyList<SharedParameterDescriptor> sharedParameters)
        {
            FamilyName = familyName ?? throw new ArgumentNullException(nameof(familyName));
            CategoryName = categoryName ?? throw new ArgumentNullException(nameof(categoryName));
            TypeNames = typeNames ?? throw new ArgumentNullException(nameof(typeNames));
            SharedParameters = sharedParameters ?? throw new ArgumentNullException(nameof(sharedParameters));
            Hash = ComputeHash(familyName, categoryName, typeNames, sharedParameters);
        }

        private static string ComputeHash(
            string familyName,
            string categoryName,
            IReadOnlyList<string> typeNames,
            IReadOnlyList<SharedParameterDescriptor> sharedParameters)
        {
            // Build a canonical string representation of the metadata. Sorted
            // collections ensure the same state always produces the same string
            // regardless of Revit's internal iteration order. Pipe-delimited fields
            // within a record, newline-delimited records between entries. The format
            // itself is versioned implicitly by this class, a format change here
            // would produce a different hash for the same family state, which is
            // equivalent to a forced re-detection on all existing catalog entries,
            // acceptable since it would only happen on a deliberate code change.
            var sb = new StringBuilder();

            sb.AppendLine($"FAMILY|{familyName}");
            sb.AppendLine($"CATEGORY|{categoryName}");

            foreach (var typeName in typeNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"TYPE|{typeName}");
            }

            foreach (var param in sharedParameters.OrderBy(p => p.Guid.ToString()))
            {
                sb.AppendLine($"PARAM|{param.Guid}|{param.StorageType}|{param.GroupName}|{param.ParameterName}");
            }

            using (var sha256 = SHA256.Create())
            {
                var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return Convert.ToHexString(bytes).ToLowerInvariant();
            }
        }
    }

    /// <summary>
    /// Structural descriptor of a single shared parameter as observed on a
    /// FamilySymbol in the project document. GUID is the stable identity, name is
    /// included for display purposes but name changes ARE detected as drift since
    /// renaming a shared parameter typically indicates a different parameter
    /// definition entirely (a genuine shared parameter rename requires a new GUID
    /// in Revit's shared parameter file, so name-with-same-GUID changes are
    /// unusual and worth flagging).
    /// </summary>
    public sealed class SharedParameterDescriptor : IEquatable<SharedParameterDescriptor>
    {
        public Guid Guid { get; }
        public string ParameterName { get; }
        public StorageType StorageType { get; }
        public string GroupName { get; }

        public SharedParameterDescriptor(Guid guid, string parameterName, StorageType storageType, string groupName)
        {
            Guid = guid;
            ParameterName = parameterName ?? throw new ArgumentNullException(nameof(parameterName));
            StorageType = storageType;
            GroupName = groupName ?? string.Empty;
        }

        public bool Equals(SharedParameterDescriptor? other)
        {
            if (other is null) return false;
            return Guid == other.Guid &&
                   StorageType == other.StorageType &&
                   string.Equals(GroupName, other.GroupName, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(ParameterName, other.ParameterName, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object? obj) => Equals(obj as SharedParameterDescriptor);
        public override int GetHashCode() => HashCode.Combine(Guid, StorageType, GroupName.ToLowerInvariant());
    }

    /// <summary>
    /// Structured diff between two FamilyMetadataSnapshots. Computed by
    /// FamilyHashEngine.Diff() and used both for display in the confirm dialog
    /// and for inferring the semver bump kind (Major/Minor/Patch).
    /// </summary>
    public sealed class FamilyMetadataDiff
    {
        public bool HasChanges =>
            TypesAdded.Count > 0 ||
            TypesRemoved.Count > 0 ||
            ParametersAdded.Count > 0 ||
            ParametersRemoved.Count > 0 ||
            ParametersModified.Count > 0 ||
            CategoryChanged;

        public bool CategoryChanged { get; init; }
        public string? PreviousCategory { get; init; }
        public string? NewCategory { get; init; }

        public IReadOnlyList<string> TypesAdded { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> TypesRemoved { get; init; } = Array.Empty<string>();
        public IReadOnlyList<SharedParameterDescriptor> ParametersAdded { get; init; } = Array.Empty<SharedParameterDescriptor>();
        public IReadOnlyList<SharedParameterDescriptor> ParametersRemoved { get; init; } = Array.Empty<SharedParameterDescriptor>();

        /// <summary>
        /// Parameters whose GUID is the same in both snapshots but whose name,
        /// storage type, or group changed. As noted on SharedParameterDescriptor,
        /// this is unusual since a true shared parameter rename requires a new GUID,
        /// but it can happen with non-standard shared parameter workflows and is
        /// worth surfacing explicitly.
        /// </summary>
        public IReadOnlyList<(SharedParameterDescriptor Previous, SharedParameterDescriptor New)> ParametersModified { get; init; }
            = Array.Empty<(SharedParameterDescriptor, SharedParameterDescriptor)>();

        /// <summary>
        /// Produces a human-readable plain-text summary of the diff suitable for
        /// display in the confirm dialog and storage in AuditLog.Detail and
        /// FamilyBuildingState.LastDiffSummary.
        /// </summary>
        public string ToSummaryString()
        {
            if (!HasChanges)
            {
                return "No structural changes detected.";
            }

            var sb = new StringBuilder();

            if (CategoryChanged)
            {
                sb.AppendLine($"[BREAKING] Category changed: '{PreviousCategory}' → '{NewCategory}'");
            }

            foreach (var typeName in TypesRemoved)
            {
                sb.AppendLine($"[BREAKING] Type removed: '{typeName}'");
            }

            foreach (var param in ParametersRemoved)
            {
                sb.AppendLine($"[BREAKING] Shared parameter removed: '{param.ParameterName}' ({param.Guid})");
            }

            foreach (var (prev, next) in ParametersModified)
            {
                if (prev.StorageType != next.StorageType)
                {
                    sb.AppendLine($"[BREAKING] Shared parameter '{prev.ParameterName}' storage type changed: " +
                        $"{prev.StorageType} → {next.StorageType}");
                }
                else if (!string.Equals(prev.ParameterName, next.ParameterName, StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"[MINOR] Shared parameter renamed: '{prev.ParameterName}' → '{next.ParameterName}' " +
                        $"(GUID: {prev.Guid})");
                }
                else if (!string.Equals(prev.GroupName, next.GroupName, StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"[PATCH] Shared parameter '{prev.ParameterName}' group changed: " +
                        $"'{prev.GroupName}' → '{next.GroupName}'");
                }
            }

            foreach (var typeName in TypesAdded)
            {
                sb.AppendLine($"[MINOR] Type added: '{typeName}'");
            }

            foreach (var param in ParametersAdded)
            {
                sb.AppendLine($"[MINOR] Shared parameter added: '{param.ParameterName}' " +
                    $"({param.StorageType}, {param.GroupName})");
            }

            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// Extracts FamilyMetadataSnapshots from Revit Family elements and computes diffs.
    /// All methods are static since there is no instance state, this class is a pure
    /// function collection over Revit API surfaces. Must be called on the Revit API
    /// thread (i.e. from within a DocumentChanged handler or ExternalEvent), never
    /// from a background thread.
    /// </summary>
    public static class FamilyHashEngine
    {
        /// <summary>
        /// Extracts a FamilyMetadataSnapshot from a Family element loaded in the
        /// given document. Returns null if the family element cannot be read (e.g.
        /// it was deleted between the DocumentChanged event firing and this call,
        /// which can happen if multiple document modifications arrived in the same
        /// batch), callers must handle null without throwing.
        /// </summary>
        public static FamilyMetadataSnapshot? ExtractSnapshot(Document doc, ElementId familyId)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (familyId == null || familyId == ElementId.InvalidElementId) return null;

            var family = doc.GetElement(familyId) as Family;
            if (family == null) return null;

            try
            {
                var categoryName = family.FamilyCategory?.Name ?? "Unknown";
                var typeNames = new List<string>();
                var sharedParamsByGuid = new Dictionary<Guid, SharedParameterDescriptor>();

                foreach (var symbolId in family.GetFamilySymbolIds())
                {
                    if (doc.GetElement(symbolId) is not FamilySymbol symbol)
                    {
                        continue;
                    }

                    typeNames.Add(symbol.Name);

                    foreach (Parameter param in symbol.Parameters)
                    {
                        if (!param.IsShared)
                        {
                            continue;
                        }

                        var guid = param.GUID;
                        if (sharedParamsByGuid.ContainsKey(guid))
                        {
                            continue;
                        }

                        var groupTypeId = param.Definition.GetGroupTypeId();
                        var groupName = LabelUtils.GetLabelForGroup(groupTypeId);

                        sharedParamsByGuid[guid] = new SharedParameterDescriptor(
                            guid,
                            param.Definition.Name,
                            param.StorageType,
                            groupName);
                    }
                }

                return new FamilyMetadataSnapshot(
                    family.Name,
                    categoryName,
                    typeNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
                    sharedParamsByGuid.Values
                        .OrderBy(p => p.Guid.ToString())
                        .ToList());
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.InvalidOperationException ||
                                        ex is Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Family element became invalid mid-extraction (e.g. document closed,
                // element deleted in a concurrent operation). Return null to let the
                // caller skip this family gracefully rather than crashing the handler.
                return null;
            }
        }

        /// <summary>
        /// Computes a structured diff between a previous snapshot (from the catalog)
        /// and a new snapshot (just extracted from the document). If previousSnapshot
        /// is null, this is a first-time observation of this family in this building,
        /// the diff will show all types and parameters as "added" since there is no
        /// prior state to compare against, and the inferred bump kind will be Minor
        /// (new family observed, not a breaking change, just new content).
        /// </summary>
        public static FamilyMetadataDiff Diff(
            FamilyMetadataSnapshot? previousSnapshot,
            FamilyMetadataSnapshot newSnapshot)
        {
            if (newSnapshot == null) throw new ArgumentNullException(nameof(newSnapshot));

            if (previousSnapshot == null)
            {
                // First-time observation: everything is "added", no removals possible.
                return new FamilyMetadataDiff
                {
                    CategoryChanged = false,
                    TypesAdded = newSnapshot.TypeNames.ToList(),
                    TypesRemoved = Array.Empty<string>(),
                    ParametersAdded = newSnapshot.SharedParameters.ToList(),
                    ParametersRemoved = Array.Empty<SharedParameterDescriptor>(),
                    ParametersModified = Array.Empty<(SharedParameterDescriptor, SharedParameterDescriptor)>()
                };
            }

            var categoryChanged = !string.Equals(
                previousSnapshot.CategoryName,
                newSnapshot.CategoryName,
                StringComparison.OrdinalIgnoreCase);

            var previousTypeSet = new HashSet<string>(
                previousSnapshot.TypeNames, StringComparer.OrdinalIgnoreCase);

            var newTypeSet = new HashSet<string>(
                newSnapshot.TypeNames, StringComparer.OrdinalIgnoreCase);

            var typesAdded = newSnapshot.TypeNames
                .Where(t => !previousTypeSet.Contains(t))
                .ToList();

            var typesRemoved = previousSnapshot.TypeNames
                .Where(t => !newTypeSet.Contains(t))
                .ToList();

            var previousParamByGuid = previousSnapshot.SharedParameters
                .ToDictionary(p => p.Guid);

            var newParamByGuid = newSnapshot.SharedParameters
                .ToDictionary(p => p.Guid);

            var parametersAdded = newSnapshot.SharedParameters
                .Where(p => !previousParamByGuid.ContainsKey(p.Guid))
                .ToList();

            var parametersRemoved = previousSnapshot.SharedParameters
                .Where(p => !newParamByGuid.ContainsKey(p.Guid))
                .ToList();

            var parametersModified = new List<(SharedParameterDescriptor Previous, SharedParameterDescriptor New)>();

            foreach (var kvp in newParamByGuid)
            {
                if (!previousParamByGuid.TryGetValue(kvp.Key, out var previousParam))
                {
                    continue;
                }

                if (!previousParam.Equals(kvp.Value))
                {
                    parametersModified.Add((previousParam, kvp.Value));
                }
            }

            return new FamilyMetadataDiff
            {
                CategoryChanged = categoryChanged,
                PreviousCategory = categoryChanged ? previousSnapshot.CategoryName : null,
                NewCategory = categoryChanged ? newSnapshot.CategoryName : null,
                TypesAdded = typesAdded,
                TypesRemoved = typesRemoved,
                ParametersAdded = parametersAdded,
                ParametersRemoved = parametersRemoved,
                ParametersModified = parametersModified
            };
        }

        /// <summary>
        /// Infers a semver bump kind from a computed diff. The rules:
        ///
        /// MAJOR: any breaking change. Category changed, type removed, parameter
        /// removed, parameter storage type changed. A breaking change means existing
        /// scheduled values, tags, or Dynamo scripts referencing this family could
        /// produce wrong results or fail outright without manual review.
        ///
        /// MINOR: additive, non-breaking. New type added, new shared parameter added,
        /// parameter renamed (same GUID, display name changed), first-time observation
        /// of a family not yet in the catalog. Downstream references continue to work.
        ///
        /// PATCH: parameter group reassignment only. Functionally equivalent,
        /// affects only how the parameter appears in the Properties palette grouping,
        /// no impact on schedules, tags, or API reads.
        ///
        /// Unknown: the hash changed but the diff engine found no specific change.
        /// This should not happen in practice given the diff covers all hashed
        /// metadata fields, but is retained as a safe fallback rather than throwing.
        /// </summary>
        public static FamilyBumpKind InferBumpKind(FamilyMetadataDiff diff)
        {
            if (diff == null) throw new ArgumentNullException(nameof(diff));

            if (!diff.HasChanges)
            {
                return FamilyBumpKind.Unknown;
            }

            // Any breaking change escalates immediately to Major regardless of
            // what else changed. Check breaking conditions first.
            if (diff.CategoryChanged)
            {
                return FamilyBumpKind.Major;
            }

            if (diff.TypesRemoved.Count > 0)
            {
                return FamilyBumpKind.Major;
            }

            if (diff.ParametersRemoved.Count > 0)
            {
                return FamilyBumpKind.Major;
            }

            foreach (var (prev, next) in diff.ParametersModified)
            {
                if (prev.StorageType != next.StorageType)
                {
                    return FamilyBumpKind.Major;
                }
            }

            // No breaking changes. Check for additive (Minor) changes.
            if (diff.TypesAdded.Count > 0)
            {
                return FamilyBumpKind.Minor;
            }

            if (diff.ParametersAdded.Count > 0)
            {
                return FamilyBumpKind.Minor;
            }

            foreach (var (prev, next) in diff.ParametersModified)
            {
                if (!string.Equals(prev.ParameterName, next.ParameterName, StringComparison.OrdinalIgnoreCase))
                {
                    return FamilyBumpKind.Minor;
                }
            }

            // Only non-breaking, non-additive changes remain: parameter group
            // reassignment. This is a Patch.
            if (diff.ParametersModified.Count > 0)
            {
                return FamilyBumpKind.Patch;
            }

            return FamilyBumpKind.Unknown;
        }

        /// <summary>
        /// Applies a bump kind to a version string, returning the incremented version.
        /// Input version must be in "MAJOR.MINOR.PATCH" format. For a first-time
        /// observation (previousVersion is "0.0.0"), returns "0.1.0" for Minor and
        /// "1.0.0" for Major, since the family is new to this catalog, not an
        /// update to an existing one.
        /// </summary>
        public static string BumpVersion(string currentVersion, FamilyBumpKind bumpKind)
        {
            if (string.IsNullOrWhiteSpace(currentVersion))
            {
                currentVersion = "0.0.0";
            }

            var parts = currentVersion.Split('.');
            if (parts.Length != 3 ||
                !int.TryParse(parts[0], out var major) ||
                !int.TryParse(parts[1], out var minor) ||
                !int.TryParse(parts[2], out var patch))
            {
                // Malformed version string in catalog. Reset to a clean starting
                // point rather than producing a malformed output. Log-worthy but
                // not throw-worthy since this would block the entire detection flow.
                major = 0; minor = 0; patch = 0;
            }

            return bumpKind switch
            {
                FamilyBumpKind.Major => $"{major + 1}.0.0",
                FamilyBumpKind.Minor => $"{major}.{minor + 1}.0",
                FamilyBumpKind.Patch => $"{major}.{minor}.{patch + 1}",
                _ => currentVersion
            };
        }
    }
}
