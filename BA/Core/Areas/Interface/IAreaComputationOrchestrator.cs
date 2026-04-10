using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core.Enums;
using BA.Core.Models;

namespace BA.Core.Interfaces
{
    public interface IAreaComputationOrchestrator
    {
        Task<IReadOnlyList<AreaComputationResult>> ComputeAsync(
            IReadOnlyList<AreaType> areaTypes,
            IReadOnlyList<ElementId> targetElementIds,
            UIApplication uiApplication,
            CancellationToken cancellationToken);
    }
}