namespace Veldrid
{
    /// <summary>
    ///     Specifies the per-draw shading rate for Variable Rate Shading (VRS).
    ///     The notation NxM means N pixels wide by M pixels tall are shaded by a single fragment shader invocation.
    /// </summary>
    public enum ShadingRate : byte
    {
        /// <summary>
        ///     Standard 1×1 shading: one shader invocation per pixel (default).
        /// </summary>
        Rate1x1 = 0,

        /// <summary>
        ///     1×2 shading: one shader invocation per 1×2 pixel block.
        /// </summary>
        Rate1x2 = 1,

        /// <summary>
        ///     2×1 shading: one shader invocation per 2×1 pixel block.
        /// </summary>
        Rate2x1 = 4,

        /// <summary>
        ///     2×2 shading: one shader invocation per 2×2 pixel block (quarter-rate).
        /// </summary>
        Rate2x2 = 5,

        /// <summary>
        ///     2×4 shading: one shader invocation per 2×4 pixel block.
        /// </summary>
        Rate2x4 = 6,

        /// <summary>
        ///     4×2 shading: one shader invocation per 4×2 pixel block.
        /// </summary>
        Rate4x2 = 9,

        /// <summary>
        ///     4×4 shading: one shader invocation per 4×4 pixel block (sixteenth-rate).
        /// </summary>
        Rate4x4 = 10,
    }
}
