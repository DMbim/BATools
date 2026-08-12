// Path: BA\Materials\Models\MaterialChannelSet.cs
using System;
using Newtonsoft.Json;

namespace BA.Materials.Models
{
    /// <summary>
    /// Plain in-memory representation of a material's Appearance channels, modeled on
    /// the Generic appearance asset schema (generic_diffuse, generic_glossiness, etc).
    /// This type has no Revit API dependency by design: the WebView2 preview and the
    /// Revit AppearanceAssetEditScope writer both consume this same object, but neither
    /// one blocks on the other. See BA.Materials.MaterialWriteDebouncer for the bridge
    /// between "user is dragging a slider" and "Revit asset actually gets edited".
    /// </summary>
    public sealed class MaterialChannelSet
    {
        public string MaterialName { get; set; } = string.Empty;

        // --- Albedo (maps to generic_diffuse) ---
        public byte AlbedoR { get; set; } = 200;
        public byte AlbedoG { get; set; } = 200;
        public byte AlbedoB { get; set; } = 200;

        // --- Roughness, 0-1. Written into Revit as generic_glossiness = 1 - Roughness ---
        private double _roughness = 0.5;
        public double Roughness
        {
            get => _roughness;
            set => _roughness = Clamp01(value);
        }

        // --- Reflectivity, 0-1. Maps to generic_reflectivity_at_0deg. NOTE: this is a
        // Fresnel reflectance value in the Generic schema, not a metalness switch. Do not
        // treat this as "Metallic" when writing to Revit; the preview renderer maps it to
        // metalness for a cheap approximation only, per the agreed Enscape-style simplification. ---
        private double _reflectivity = 0.0;
        public double Reflectivity
        {
            get => _reflectivity;
            set => _reflectivity = Clamp01(value);
        }

        // --- Bump amount, 0-1. Maps to generic_bump_amount. No bitmap slot in v1,
        // this is a scalar-only control per the agreed v1 scope. ---
        private double _bumpAmount = 0.0;
        public double BumpAmount
        {
            get => _bumpAmount;
            set => _bumpAmount = Clamp01(value);
        }

        // --- Emissive color + luminance. Maps to generic_self_illum_filter_map (color)
        // and generic_self_illum_luminance (photometric value in cd/m^2, NOT 0-1). ---
        public byte EmissiveR { get; set; } = 0;
        public byte EmissiveG { get; set; } = 0;
        public byte EmissiveB { get; set; } = 0;

        private double _emissiveLuminanceCdM2 = 0.0;
        /// <summary>Photometric luminance in cd/m^2. Real range, not normalized.
        /// UI should present this on a log scale, roughly 0 to 20000.</summary>
        public double EmissiveLuminanceCdM2
        {
            get => _emissiveLuminanceCdM2;
            set => _emissiveLuminanceCdM2 = value < 0 ? 0 : value;
        }

        // --- Transparency, 0-1. Maps to generic_transparency. ---
        private double _transparency = 0.0;
        public double Transparency
        {
            get => _transparency;
            set => _transparency = Clamp01(value);
        }

        // --- Cutout opacity, 0-1. Maps to generic_cutout_opacity. ---
        private double _cutoutOpacity = 1.0;
        public double CutoutOpacity
        {
            get => _cutoutOpacity;
            set => _cutoutOpacity = Clamp01(value);
        }

        private static double Clamp01(double value)
        {
            if (double.IsNaN(value)) return 0.0;
            if (value < 0.0) return 0.0;
            if (value > 1.0) return 1.0;
            return value;
        }

        public MaterialChannelSet Clone()
        {
            return new MaterialChannelSet
            {
                MaterialName = MaterialName,
                AlbedoR = AlbedoR,
                AlbedoG = AlbedoG,
                AlbedoB = AlbedoB,
                Roughness = Roughness,
                Reflectivity = Reflectivity,
                BumpAmount = BumpAmount,
                EmissiveR = EmissiveR,
                EmissiveG = EmissiveG,
                EmissiveB = EmissiveB,
                EmissiveLuminanceCdM2 = EmissiveLuminanceCdM2,
                Transparency = Transparency,
                CutoutOpacity = CutoutOpacity
            };
        }

        /// <summary>
        /// Structural equality on all channel values, used by the debouncer to skip a
        /// write if nothing actually changed (e.g. a slider fired an input event with
        /// the same value it already had).
        /// </summary>
        public bool ChannelsEqual(MaterialChannelSet other)
        {
            if (other == null) return false;
            return AlbedoR == other.AlbedoR
                && AlbedoG == other.AlbedoG
                && AlbedoB == other.AlbedoB
                && Math.Abs(Roughness - other.Roughness) < 0.0001
                && Math.Abs(Reflectivity - other.Reflectivity) < 0.0001
                && Math.Abs(BumpAmount - other.BumpAmount) < 0.0001
                && EmissiveR == other.EmissiveR
                && EmissiveG == other.EmissiveG
                && EmissiveB == other.EmissiveB
                && Math.Abs(EmissiveLuminanceCdM2 - other.EmissiveLuminanceCdM2) < 0.01
                && Math.Abs(Transparency - other.Transparency) < 0.0001
                && Math.Abs(CutoutOpacity - other.CutoutOpacity) < 0.0001;
        }

        public string ToJson() => JsonConvert.SerializeObject(this);

        public static MaterialChannelSet FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new MaterialChannelSet();
            return JsonConvert.DeserializeObject<MaterialChannelSet>(json) ?? new MaterialChannelSet();
        }
    }
}