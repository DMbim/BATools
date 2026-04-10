using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using BA.Core.Enums;
using BA.Core.Interfaces;
using BA.Core.Models;
using BA.Services.Computation;
using Autodesk.Revit.DB.Architecture;  // Room je zde


namespace BA.Services.Parameters
{
    /// <summary>
    /// Spravuje sdílené parametry CZA add-inu.
    ///
    /// Shared parameter soubor je extrahován z embedded resource do %TEMP%
    /// při každém volání EnsureSharedParametersExist.
    ///
    /// Všechny metody musí být volány uvnitř transakce na main Revit threadu.
    /// </summary>
    public sealed class SharedParameterService : ISharedParameterService
    {
        private const string EmbeddedSpFileResource =
            "BA.Services.Resources.CZA_SharedParameters.txt";

        private const string SpGroupName = "Czech Area Compliance";

        public void EnsureSharedParametersExist(Document document)
        {
            var app = document.Application;
            var originalSpFile = app.SharedParametersFilename;

            try
            {
                var spFilePath = ExtractEmbeddedSpFile();
                app.SharedParametersFilename = spFilePath;
                var spFile = app.OpenSharedParameterFile()
                    ?? throw new InvalidOperationException(
                        "Nelze otevřít soubor sdílených parametrů.");

                var group = spFile.Groups.get_Item(SpGroupName)
                            ?? spFile.Groups.Create(SpGroupName);

                // Definice parametrů: (název, ForgeTypeId spec)
                var paramDefs = new (string Name, ForgeTypeId Spec)[]
                {
                    (SharedParameterConstants.PodlahovaPlochaNV366,  SpecTypeId.Area),
                    (SharedParameterConstants.HPPNadzemni,           SpecTypeId.Area),
                    (SharedParameterConstants.HPPPodzemni,           SpecTypeId.Area),
                    (SharedParameterConstants.PodlahovaPlochaSZ,     SpecTypeId.Area),
                    (SharedParameterConstants.ZastavenaPlochaSZ,     SpecTypeId.Area),
                    (SharedParameterConstants.LastComputationDate,   SpecTypeId.String.Text),
                    (SharedParameterConstants.AppliedNormCitation,   SpecTypeId.String.Text),
                    (SharedParameterConstants.NormValidFrom,         SpecTypeId.String.Text),
                    (SharedParameterConstants.ComputationMethod,     SpecTypeId.String.Text),
                    (SharedParameterConstants.ComputationStatus,     SpecTypeId.String.Text),
                    (SharedParameterConstants.SpaceTypeCzech,        SpecTypeId.String.Text),
                    (SharedParameterConstants.UpravenyTerenMmNN,     SpecTypeId.Number),
                };

                foreach (var (name, spec) in paramDefs)
                    EnsureDefinitionExists(group, name, spec);

                // Bindování na kategorie
                BindParametersToCategories(document, app, group);
            }
            finally
            {
                // Vždy obnovíme původní SP soubor
                app.SharedParametersFilename = originalSpFile;
            }
        }

        public void WriteResults(
            IReadOnlyList<AreaComputationResult> results,
            Document document)
        {
            foreach (var result in results)
            {
                if (result.SourceElementId == ElementId.InvalidElementId)
                    continue;

                var element = document.GetElement(result.SourceElementId);
                if (element is null)
                    continue;

                // Název sdíleného parametru pro tento AreaType
                var paramName = GetParamNameForAreaType(result.AreaType);
                if (paramName is null)
                    continue;

                var areaParam = element.LookupParameter(paramName);
                if (areaParam is not null &&
                    !areaParam.IsReadOnly &&
                    areaParam.StorageType == StorageType.Double)
                {
                    areaParam.Set(UnitUtils.ConvertToInternalUnits(
                        result.ComputedAreaM2, UnitTypeId.SquareMeters));
                }

                // Audit parametry
                SetStringParam(element, SharedParameterConstants.LastComputationDate,
                    result.Audit.ComputedAtUtc.ToString("yyyy-MM-dd HH:mm:ss") + " UTC");

                SetStringParam(element, SharedParameterConstants.AppliedNormCitation,
                    result.Audit.AppliedNormCitation);

                SetStringParam(element, SharedParameterConstants.NormValidFrom,
                    result.Audit.NormValidFrom.ToString("yyyy-MM-dd"));

                SetStringParam(element, SharedParameterConstants.ComputationMethod,
                    result.Audit.ComputationMethod);

                SetStringParam(element, SharedParameterConstants.ComputationStatus,
                    result.Status.ToString());
            }
        }

        // --------------------------------------------------------
        // Private helpers
        // --------------------------------------------------------

        private static void EnsureDefinitionExists(
            DefinitionGroup group,
            string name,
            ForgeTypeId spec)
        {
            if (group.Definitions.get_Item(name) is not null)
                return;

            var options = new ExternalDefinitionCreationOptions(name, spec)
            {
                Visible = true,
                UserModifiable = false,
                Description = $"Czech Area Compliance — {name}"
            };

            group.Definitions.Create(options);
        }

        private static void BindParametersToCategories(
            Document document,
            Autodesk.Revit.ApplicationServices.Application app,
            DefinitionGroup group)
        {
            // Parametry pro Rooms
            var roomCatSet = app.Create.NewCategorySet();
            roomCatSet.Insert(
                document.Settings.Categories.get_Item(BuiltInCategory.OST_Rooms));

            // Parametry pro ProjectInfo (UpravenyTeren)
            var projectInfoCatSet = app.Create.NewCategorySet();
            projectInfoCatSet.Insert(
                document.Settings.Categories.get_Item(BuiltInCategory.OST_ProjectInformation));

            var roomInstanceBinding = app.Create.NewInstanceBinding(roomCatSet);
            var projectInstanceBinding = app.Create.NewInstanceBinding(projectInfoCatSet);

            var roomParams = new HashSet<string>
            {
                SharedParameterConstants.PodlahovaPlochaNV366,
                SharedParameterConstants.PodlahovaPlochaSZ,
                SharedParameterConstants.LastComputationDate,
                SharedParameterConstants.AppliedNormCitation,
                SharedParameterConstants.NormValidFrom,
                SharedParameterConstants.ComputationMethod,
                SharedParameterConstants.ComputationStatus,
                SharedParameterConstants.SpaceTypeCzech,
            };

            var projectParams = new HashSet<string>
            {
                SharedParameterConstants.HPPNadzemni,
                SharedParameterConstants.HPPPodzemni,
                SharedParameterConstants.ZastavenaPlochaSZ,
                SharedParameterConstants.UpravenyTerenMmNN,
            };

            foreach (Definition def in group.Definitions)
            {
                if (document.ParameterBindings.Contains(def))
                    continue;

                if (roomParams.Contains(def.Name))
                {
                    document.ParameterBindings.Insert(
                        def, roomInstanceBinding,
                        GroupTypeId.AnalysisResults);
                }
                else if (projectParams.Contains(def.Name))
                {
                    document.ParameterBindings.Insert(
                        def, projectInstanceBinding,
                        GroupTypeId.AnalysisResults);
                }
            }
        }

        private static string ExtractEmbeddedSpFile()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var tempPath = Path.Combine(
                Path.GetTempPath(),
                "CZA_SharedParameters.txt");

            using var stream = assembly.GetManifestResourceStream(EmbeddedSpFileResource);

            if (stream is null)
            {
                // Pokud embedded resource neexistuje, vytvoříme prázdný SP soubor
                if (!File.Exists(tempPath))
                    File.WriteAllText(tempPath, "# Czech Area Compliance Shared Parameters\n");

                return tempPath;
            }

            using var fileStream = File.Create(tempPath);
            stream.CopyTo(fileStream);

            return tempPath;
        }

        private static void SetStringParam(Element element, string paramName, string value)
        {
            var param = element.LookupParameter(paramName);
            if (param is not null &&
                !param.IsReadOnly &&
                param.StorageType == StorageType.String)
            {
                param.Set(value);
            }
        }

        private static string? GetParamNameForAreaType(AreaType areaType) => areaType switch
        {
            AreaType.PodlahovaPlochaNV366 => SharedParameterConstants.PodlahovaPlochaNV366,
            AreaType.HPPNadzemni => SharedParameterConstants.HPPNadzemni,
            AreaType.HPPPodzemni => SharedParameterConstants.HPPPodzemni,
            AreaType.PodlahovaPlochaSZ => SharedParameterConstants.PodlahovaPlochaSZ,
            AreaType.ZastavenaPlochaSZ => SharedParameterConstants.ZastavenaPlochaSZ,
            _ => null
        };
    }
}