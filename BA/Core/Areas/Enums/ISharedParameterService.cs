using System.Collections.Generic;
using Autodesk.Revit.DB;
using BA.Core.Models;

namespace BA.Core.Interfaces
{
    public interface ISharedParameterService
    {
        /// <summary>
        /// Zajistí existenci všech CZA sdílených parametrů v dokumentu.
        /// Volat v transakci z ExternalEventHandler.
        /// </summary>
        void EnsureSharedParametersExist(Document document);

        /// <summary>
        /// Zapíše výsledky výpočtu do sdílených parametrů elementů.
        /// Volat v transakci z ExternalEventHandler.
        /// </summary>
        void WriteResults(IReadOnlyList<AreaComputationResult> results, Document document);
    }
}