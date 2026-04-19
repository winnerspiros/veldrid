using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Veldrid.D3D12
{
    [SupportedOSPlatform("windows")]
    internal class D3D12Swapchain : Swapchain
    {
        public override Framebuffer Framebuffer => framebuffer;

        public IDXGISwapChain3 DxgiSwapChain { get; private set; }

        public PresentFlags PresentFlags
        {
            get
            {
                if (allowTearing && canTear && !SyncToVerticalBlank)
                    return PresentFlags.AllowTearing;

                return PresentFlags.None;
            }
        }

        public uint SyncInterval => SyncToVerticalBlank ? 1u : 0u;

        public override bool IsDisposed => disposed;

        public override string Name
        {
            get => name;
            set
            {
                name = value;

                if (DxgiSwapChain != null)
                    DxgiSwapChain.DebugName = value;
            }
        }

        public override bool SyncToVerticalBlank
        {
            get => vsync;
            set => vsync = value;
        }

        public bool AllowTearing
        {
            get => allowTearing;
            set => allowTearing = value;
        }

        private readonly D3D12GraphicsDevice gd;
        private readonly PixelFormat? depthFormat;
        private readonly bool canTear;
        private readonly Format colorFormat;

        private bool vsync;
        private bool allowTearing;
        private bool disposed;
        private string name;

        private uint width;
        private uint height;

        private D3D12Texture[] backBufferTextures;
        private D3D12Texture depthTexture;
        private D3D12Framebuffer framebuffer;

        public D3D12Swapchain(D3D12GraphicsDevice gd, ref SwapchainDescription description)
        {
            this.gd = gd;
            depthFormat = description.DepthFormat;
            SyncToVerticalBlank = description.SyncToVerticalBlank;

            width = description.Width;
            height = description.Height;

            colorFormat = description.ColorSrgb
                ? Format.B8G8R8A8_UNorm_SRgb
                : Format.B8G8R8A8_UNorm;

            if (description.Source is not Win32SwapchainSource win32Source)
                throw new VeldridException("D3D12 swapchains require a Win32SwapchainSource.");

            // Check for tearing support.
            using (var factory5 = gd.DxgiFactory.QueryInterfaceOrNull<IDXGIFactory5>())
                canTear = factory5?.PresentAllowTearing == true;

            var flags = SwapChainFlags.AllowModeSwitch;

            if (canTear)
                flags |= SwapChainFlags.AllowTearing;

            var swapChainDesc = new SwapChainDescription1
            {
                Width = width,
                Height = height,
                Format = colorFormat,
                BufferUsage = Usage.RenderTargetOutput,
                BufferCount = 2,
                SampleDescription = new SampleDescription(1u, 0),
                SwapEffect = SwapEffect.FlipDiscard,
                Flags = flags
            };

            using (var swapChain1 = gd.DxgiFactory.CreateSwapChainForHwnd(
                gd.CommandQueue,
                win32Source.Hwnd,
                swapChainDesc))
            {
                DxgiSwapChain = swapChain1.QueryInterface<IDXGISwapChain3>();
            }

            gd.DxgiFactory.MakeWindowAssociation(win32Source.Hwnd, WindowAssociationFlags.IgnoreAltEnter);

            createFramebufferResources();
        }

        public override void Resize(uint width, uint height)
        {
            this.width = width;
            this.height = height;

            disposeFramebufferResources();

            var flags = SwapChainFlags.AllowModeSwitch;

            if (canTear)
                flags |= SwapChainFlags.AllowTearing;

            DxgiSwapChain.ResizeBuffers(
                2,
                (uint)width,
                (uint)height,
                colorFormat,
                flags);

            createFramebufferResources();
        }

        public uint GetCurrentBackBufferIndex()
        {
            return DxgiSwapChain.CurrentBackBufferIndex;
        }

        #region Disposal

        public override void Dispose()
        {
            if (!disposed)
            {
                disposeFramebufferResources();
                DxgiSwapChain.Dispose();
                disposed = true;
            }
        }

        #endregion

        private void createFramebufferResources()
        {
            uint bufferCount = DxgiSwapChain.Description1.BufferCount;
            backBufferTextures = new D3D12Texture[bufferCount];

            for (int i = 0; i < bufferCount; i++)
            {
                var backBuffer = DxgiSwapChain.GetBuffer<ID3D12Resource>((uint)i);
                backBufferTextures[i] = new D3D12Texture(
                    backBuffer,
                    TextureType.Texture2D,
                    D3D12Formats.ToVdFormat(colorFormat));
            }

            if (depthFormat != null)
            {
                var depthDesc = new TextureDescription(
                    width, height, 1, 1, 1,
                    depthFormat.Value,
                    TextureUsage.DepthStencil,
                    TextureType.Texture2D);
                depthTexture = new D3D12Texture(gd.Device, ref depthDesc);
            }

            uint currentIndex = DxgiSwapChain.CurrentBackBufferIndex;
            var fbDesc = new FramebufferDescription(depthTexture, backBufferTextures[currentIndex]);
            framebuffer = new D3D12Framebuffer(
                gd.Device,
                gd.RtvAllocator,
                gd.DsvAllocator,
                ref fbDesc);
        }

        private void disposeFramebufferResources()
        {
            framebuffer?.Dispose();
            framebuffer = null;

            depthTexture?.Dispose();
            depthTexture = null;

            if (backBufferTextures != null)
            {
                for (int i = 0; i < backBufferTextures.Length; i++)
                {
                    backBufferTextures[i]?.Dispose();
                    backBufferTextures[i] = null;
                }

                backBufferTextures = null;
            }
        }
    }
}
