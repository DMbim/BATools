using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using BA.Core.Models;

namespace BA.Core.Interfaces
{
    public interface IAreaComputationStrategy
    {
        BA.Core.Enums.AreaType SupportedAreaType { get; }

        Task<IReadOnlyList<AreaComputationResult>> ComputeAsync(
            IReadOnlyList<ElementId> targetElementIds,
            Document document,
            ProjectContext context,
            CancellationToken cancellationToken);
    }
}
