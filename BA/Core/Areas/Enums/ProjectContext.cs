namespace BA.Core.Models
{
    /// <summary>
    /// Kontext projektu — nese informace potřebné pro výpočetní strategie.
    /// Zejména výška průměrného upraveného terénu pro HPP klasifikaci dle PSP §2 g).
    /// </summary>
    public sealed record ProjectContext
    {
        /// <summary>
        /// Průměrná výška upraveného terénu v mm nad nulou Revit projektu.
        /// Čteno z TopographySurface nebo ze sdíleného parametru CZA_UpravenyTeren_mmNN.
        /// </summary>
        public double AverageTerenElevationMm { get; init; } = 0.0;

        /// <summary>
        /// Název obce/MČ — pouze informativní, nezasahuje do výpočtu.
        /// </summary>
        public string? Municipality { get; init; }
    }
}