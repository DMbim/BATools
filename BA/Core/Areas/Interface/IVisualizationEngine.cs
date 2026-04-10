using System.Collections.Generic;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using BA.Core.Enums;
using BA.Core.Models;

namespace BA.Core.Interfaces
{
    public interface IVisualizationEngine
    {
        /// <summary>
        /// Vykreslí výsledky jako FilledRegion v aktivním pohledu.
        /// Volat pouze z ExternalEventHandler (main Revit thread).
        /// </summary>
        void VisualizeResults(
            IReadOnlyList<AreaComputationResult> results,
            View activeView,
            Document document);

        /// <summary>
        /// Odstraní všechny CzechArea FilledRegion elementy.
        /// Volat pouze z ExternalEventHandler.
        /// </summary>
        void ClearVisualizations(AreaType? filterByType, Document document);
    }
}
