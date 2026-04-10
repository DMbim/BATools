namespace BA.Services.Computation
{
    internal static class RevitConstants
    {
        /// <summary>Minimální plocha místnosti v ft² (= ~0.01 m²)</summary>
        public const double MinRoomAreaThresholdFt2 = 0.01 / 0.0929;

        /// <summary>Minimální objem solidu v ft³ pro validní geometrii</summary>
        public const double MinSolidVolumeFt3 = 0.001;
    }
}