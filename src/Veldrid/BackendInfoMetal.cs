#if !EXCLUDE_METAL_BACKEND
using System.Collections.ObjectModel;
using System.Linq;
using Veldrid.MetalBindings;
using Veldrid.MTL;

namespace Veldrid
{
    /// <summary>
    ///     Exposes Metal-specific functionality,
    ///     useful for interoperating with native components which interface directly with Metal.
    ///     Can only be used on <see cref="GraphicsBackend.Metal" />.
    /// </summary>
    public class BackendInfoMetal
    {
        /// <summary>
        ///     Gets the set of Metal feature sets supported by the device.
        /// </summary>
        public ReadOnlyCollection<MTLFeatureSet> FeatureSet { get; }

        /// <summary>
        ///     Gets the maximum Metal feature set supported by the device.
        /// </summary>
        public MTLFeatureSet MaxFeatureSet => gd.MetalFeatures.MaxFeatureSet;
        private readonly MtlGraphicsDevice gd;

        internal BackendInfoMetal(MtlGraphicsDevice gd)
        {
            this.gd = gd;
            FeatureSet = new ReadOnlyCollection<MTLFeatureSet>(this.gd.MetalFeatures.ToArray());
        }
    }
}
#endif
