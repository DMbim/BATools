namespace BA.Core.AreaSchemes.Models
{
    public enum AreaSchemeStep
    {
        LA = 1,
        NLA = 2,
        GFA = 3,
        ECA = 4,
        IFA = 5,
        ICA = 6,
        NFA = 7,
        PWA = 8,
        NRA = 9
    }

    public enum AreaSchemeStepType
    {
        /// <summary>User draws boundaries manually in the view.</summary>
        UserDrawn,

        /// <summary>Plugin draws boundaries from user-picked elements.</summary>
        ElementPick,

        /// <summary>Plugin draws boundaries automatically, computed from previous steps.</summary>
        Computed
    }

    public sealed class AreaSchemeDefinition
    {
        public AreaSchemeStep Step { get; init; }
        public string SchemeName { get; init; } = string.Empty;
        public string Code { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public AreaSchemeStepType StepType { get; init; }
        public string? AreaTypeTag { get; init; }
        public string ResultParamName { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
    }
}