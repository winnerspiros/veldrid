using System.Runtime.Versioning;

namespace Veldrid.D3D12
{
    [SupportedOSPlatform("windows")]
    internal class D3D12ResourceSet : ResourceSet
    {
        public new IBindableResource[] Resources { get; }
        public new D3D12ResourceLayout Layout { get; }

        public override bool IsDisposed => disposed;
        public override string Name { get; set; }

        private bool disposed;

        public D3D12ResourceSet(ref ResourceSetDescription description)
            : base(ref description)
        {
            Resources = Util.ShallowClone(description.BoundResources);
            Layout = Util.AssertSubtype<ResourceLayout, D3D12ResourceLayout>(description.Layout);
        }

        #region Disposal

        public override void Dispose()
        {
            disposed = true;
        }

        #endregion
    }
}
