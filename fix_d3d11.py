import re

def fix_file(path, replacements):
    with open(path, 'r') as f:
        content = f.read()
    for old, new in replacements:
        if old not in content:
            print(f"WARNING: not found in {path}: {repr(old[:80])}")
        content = content.replace(old, new, 1)
    with open(path, 'w') as f:
        f.write(content)
    print(f"Fixed {path}")

# 1. D3D11Util.cs
fix_file('src/Veldrid/D3D11/D3D11Util.cs', [
    ('public static int ComputeSubresource(uint mipLevel, uint mipLevelCount, uint arrayLayer)\n        {\n            return (int)(arrayLayer * mipLevelCount + mipLevel);\n        }',
     'public static uint ComputeSubresource(uint mipLevel, uint mipLevelCount, uint arrayLayer)\n        {\n            return arrayLayer * mipLevelCount + mipLevel;\n        }'),
    ('srvDesc.TextureCube.MostDetailedMip = (int)baseMipLevel;', 'srvDesc.TextureCube.MostDetailedMip = baseMipLevel;'),
    ('srvDesc.TextureCube.MipLevels = (int)levelCount;', 'srvDesc.TextureCube.MipLevels = levelCount;'),
    ('srvDesc.TextureCubeArray.MostDetailedMip = (int)baseMipLevel;', 'srvDesc.TextureCubeArray.MostDetailedMip = baseMipLevel;'),
    ('srvDesc.TextureCubeArray.MipLevels = (int)levelCount;', 'srvDesc.TextureCubeArray.MipLevels = levelCount;'),
    ('srvDesc.TextureCubeArray.First2DArrayFace = (int)baseArrayLayer;', 'srvDesc.TextureCubeArray.First2DArrayFace = baseArrayLayer;'),
    ('srvDesc.TextureCubeArray.NumCubes = (int)tex.ArrayLayers;', 'srvDesc.TextureCubeArray.NumCubes = tex.ArrayLayers;'),
    ('srvDesc.Texture1D.MostDetailedMip = (int)baseMipLevel;', 'srvDesc.Texture1D.MostDetailedMip = baseMipLevel;'),
    ('srvDesc.Texture1D.MipLevels = (int)levelCount;', 'srvDesc.Texture1D.MipLevels = levelCount;'),
    ('srvDesc.Texture2D.MostDetailedMip = (int)baseMipLevel;', 'srvDesc.Texture2D.MostDetailedMip = baseMipLevel;'),
    ('srvDesc.Texture2D.MipLevels = (int)levelCount;', 'srvDesc.Texture2D.MipLevels = levelCount;'),
    ('srvDesc.Texture1DArray.MostDetailedMip = (int)baseMipLevel;', 'srvDesc.Texture1DArray.MostDetailedMip = baseMipLevel;'),
    ('srvDesc.Texture1DArray.MipLevels = (int)levelCount;', 'srvDesc.Texture1DArray.MipLevels = levelCount;'),
    ('srvDesc.Texture1DArray.FirstArraySlice = (int)baseArrayLayer;', 'srvDesc.Texture1DArray.FirstArraySlice = baseArrayLayer;'),
    ('srvDesc.Texture1DArray.ArraySize = (int)layerCount;', 'srvDesc.Texture1DArray.ArraySize = layerCount;'),
    ('srvDesc.Texture2DArray.MostDetailedMip = (int)baseMipLevel;', 'srvDesc.Texture2DArray.MostDetailedMip = baseMipLevel;'),
    ('srvDesc.Texture2DArray.MipLevels = (int)levelCount;', 'srvDesc.Texture2DArray.MipLevels = levelCount;'),
    ('srvDesc.Texture2DArray.FirstArraySlice = (int)baseArrayLayer;', 'srvDesc.Texture2DArray.FirstArraySlice = baseArrayLayer;'),
    ('srvDesc.Texture2DArray.ArraySize = (int)layerCount;', 'srvDesc.Texture2DArray.ArraySize = layerCount;'),
    ('srvDesc.Texture3D.MostDetailedMip = (int)baseMipLevel;', 'srvDesc.Texture3D.MostDetailedMip = baseMipLevel;'),
    ('srvDesc.Texture3D.MipLevels = (int)levelCount;', 'srvDesc.Texture3D.MipLevels = levelCount;'),
    ('internal static int GetSyncInterval(bool syncToVBlank)', 'internal static uint GetSyncInterval(bool syncToVBlank)'),
])

# 2. D3D11Sampler.cs
fix_file('src/Veldrid/D3D11/D3D11Sampler.cs', [
    ('MaxAnisotropy = (int)description.MaximumAnisotropy,', 'MaxAnisotropy = description.MaximumAnisotropy,'),
])

# 3. D3D11Framebuffer.cs
fix_file('src/Veldrid/D3D11/D3D11Framebuffer.cs', [
    ('dsvDesc.Texture2D.MipSlice = (int)description.DepthTarget.Value.MipLevel;',
     'dsvDesc.Texture2D.MipSlice = description.DepthTarget.Value.MipLevel;'),
    ('dsvDesc.Texture2DArray.FirstArraySlice = (int)description.DepthTarget.Value.ArrayLayer;',
     'dsvDesc.Texture2DArray.FirstArraySlice = description.DepthTarget.Value.ArrayLayer;'),
    ('dsvDesc.Texture2DArray.MipSlice = (int)description.DepthTarget.Value.MipLevel;',
     'dsvDesc.Texture2DArray.MipSlice = description.DepthTarget.Value.MipLevel;'),
    ('dsvDesc.Texture2DMSArray.FirstArraySlice = (int)description.DepthTarget.Value.ArrayLayer;',
     'dsvDesc.Texture2DMSArray.FirstArraySlice = description.DepthTarget.Value.ArrayLayer;'),
    ('FirstArraySlice = (int)description.ColorTargets[i].ArrayLayer,\n                                MipSlice = (int)description.ColorTargets[i].MipLevel',
     'FirstArraySlice = description.ColorTargets[i].ArrayLayer,\n                                MipSlice = description.ColorTargets[i].MipLevel'),
    ('FirstArraySlice = (int)description.ColorTargets[i].ArrayLayer\n',
     'FirstArraySlice = description.ColorTargets[i].ArrayLayer\n'),
    ('rtvDesc.Texture2D.MipSlice = (int)description.ColorTargets[i].MipLevel;',
     'rtvDesc.Texture2D.MipSlice = description.ColorTargets[i].MipLevel;'),
])

# 4. D3D11TextureView.cs
fix_file('src/Veldrid/D3D11/D3D11TextureView.cs', [
    ('uavDesc.Texture1D.MipSlice = (int)description.BaseMipLevel;', 'uavDesc.Texture1D.MipSlice = description.BaseMipLevel;'),
    ('uavDesc.Texture2D.MipSlice = (int)description.BaseMipLevel;', 'uavDesc.Texture2D.MipSlice = description.BaseMipLevel;'),
    ('uavDesc.Texture1DArray.MipSlice = (int)description.BaseMipLevel;', 'uavDesc.Texture1DArray.MipSlice = description.BaseMipLevel;'),
    ('uavDesc.Texture1DArray.FirstArraySlice = (int)description.BaseArrayLayer;', 'uavDesc.Texture1DArray.FirstArraySlice = description.BaseArrayLayer;'),
    ('uavDesc.Texture1DArray.ArraySize = (int)description.ArrayLayers;', 'uavDesc.Texture1DArray.ArraySize = description.ArrayLayers;'),
    ('uavDesc.Texture2DArray.MipSlice = (int)description.BaseMipLevel;', 'uavDesc.Texture2DArray.MipSlice = description.BaseMipLevel;'),
    ('uavDesc.Texture2DArray.FirstArraySlice = (int)description.BaseArrayLayer;', 'uavDesc.Texture2DArray.FirstArraySlice = description.BaseArrayLayer;'),
    ('uavDesc.Texture2DArray.ArraySize = (int)description.ArrayLayers;', 'uavDesc.Texture2DArray.ArraySize = description.ArrayLayers;'),
    ('uavDesc.Texture3D.MipSlice = (int)description.BaseMipLevel;', 'uavDesc.Texture3D.MipSlice = description.BaseMipLevel;'),
    ('uavDesc.Texture3D.WSize = (int)d3dTex.Depth;', 'uavDesc.Texture3D.WSize = d3dTex.Depth;'),
])

# 5. D3D11Texture.cs
fix_file('src/Veldrid/D3D11/D3D11Texture.cs', [
    ('int arraySize = (int)description.ArrayLayers;', 'uint arraySize = description.ArrayLayers;'),
    ('int roundedWidth = (int)description.Width;', 'uint roundedWidth = description.Width;'),
    ('int roundedHeight = (int)description.Height;', 'uint roundedHeight = description.Height;'),
    ('MipLevels = (int)description.MipLevels,\n                    ArraySize = arraySize,\n                    Format = TypelessDxgiFormat,\n                    BindFlags = bindFlags,\n                    CPUAccessFlags = cpuFlags,\n                    Usage = resourceUsage,\n                    MiscFlags = optionFlags',
     'MipLevels = description.MipLevels,\n                    ArraySize = arraySize,\n                    Format = TypelessDxgiFormat,\n                    BindFlags = bindFlags,\n                    CPUAccessFlags = cpuFlags,\n                    Usage = resourceUsage,\n                    MiscFlags = optionFlags'),
    ('MipLevels = (int)description.MipLevels,\n                    ArraySize = arraySize,\n                    Format = TypelessDxgiFormat,\n                    BindFlags = bindFlags,\n                    CPUAccessFlags = cpuFlags,\n                    Usage = resourceUsage,\n                    SampleDescription = new SampleDescription((int)FormatHelpers.GetSampleCountUInt32(SampleCount), 0),',
     'MipLevels = description.MipLevels,\n                    ArraySize = arraySize,\n                    Format = TypelessDxgiFormat,\n                    BindFlags = bindFlags,\n                    CPUAccessFlags = cpuFlags,\n                    Usage = resourceUsage,\n                    SampleDescription = new SampleDescription(FormatHelpers.GetSampleCountUInt32(SampleCount), 0),'),
    ('Depth = (int)description.Depth,\n                    MipLevels = (int)description.MipLevels,',
     'Depth = description.Depth,\n                    MipLevels = description.MipLevels,'),
    ('Width = (uint)existingTexture.Description.Width;', 'Width = existingTexture.Description.Width;'),
    ('Height = (uint)existingTexture.Description.Height;', 'Height = existingTexture.Description.Height;'),
    ('MipLevels = (uint)existingTexture.Description.MipLevels;', 'MipLevels = existingTexture.Description.MipLevels;'),
    ('ArrayLayers = (uint)existingTexture.Description.ArraySize;', 'ArrayLayers = existingTexture.Description.ArraySize;'),
    ('SampleCount = FormatHelpers.GetSampleCount((uint)existingTexture.Description.SampleDescription.Count);',
     'SampleCount = FormatHelpers.GetSampleCount(existingTexture.Description.SampleDescription.Count);'),
])

# 6. D3D11Buffer.cs
fix_file('src/Veldrid/D3D11/D3D11Buffer.cs', [
    ('var bd = new Vortice.Direct3D11.BufferDescription(\n                (int)sizeInBytes,',
     'var bd = new Vortice.Direct3D11.BufferDescription(\n                sizeInBytes,'),
    ('bd.StructureByteStride = (int)structureByteStride;',
     'bd.StructureByteStride = structureByteStride;'),
    ('''var srvDesc = new ShaderResourceViewDescription(Buffer,
                    Format.R32_Typeless,
                    (int)offset / 4,
                    (int)size / 4,
                    BufferExtendedShaderResourceViewFlags.Raw);

                return device.CreateShaderResourceView(Buffer, srvDesc);''',
     '''var srvDesc = new ShaderResourceViewDescription
                {
                    Format = Format.R32_Typeless,
                    ViewDimension = ShaderResourceViewDimension.BufferExtended,
                    BufferEx = new BufferExShaderResourceView
                    {
                        FirstElement = offset / 4,
                        NumElements = size / 4,
                        Flags = BufferExtendedShaderResourceViewFlags.Raw
                    }
                };

                return device.CreateShaderResourceView(Buffer, srvDesc);'''),
    ('srvDesc.Buffer.NumElements = (int)(size / structureByteStride);',
     'srvDesc.Buffer.NumElements = size / structureByteStride;'),
    ('srvDesc.Buffer.ElementOffset = (int)(offset / structureByteStride);',
     'srvDesc.Buffer.ElementOffset = offset / structureByteStride;'),
    ('''var uavDesc = new UnorderedAccessViewDescription(Buffer,
                    Format.R32_Typeless,
                    (int)offset / 4,
                    (int)size / 4,
                    BufferUnorderedAccessViewFlags.Raw);

                return device.CreateUnorderedAccessView(Buffer, uavDesc);''',
     '''var uavDesc = new UnorderedAccessViewDescription
                {
                    Format = Format.R32_Typeless,
                    ViewDimension = UnorderedAccessViewDimension.Buffer,
                    Buffer = new BufferUnorderedAccessView
                    {
                        FirstElement = offset / 4,
                        NumElements = size / 4,
                        Flags = BufferUnorderedAccessViewFlags.Raw
                    }
                };

                return device.CreateUnorderedAccessView(Buffer, uavDesc);'''),
    ('''var uavDesc = new UnorderedAccessViewDescription(Buffer,
                    Format.Unknown,
                    (int)(offset / structureByteStride),
                    (int)(size / structureByteStride)
                );

                return device.CreateUnorderedAccessView(Buffer, uavDesc);''',
     '''var uavDesc = new UnorderedAccessViewDescription
                {
                    Format = Format.Unknown,
                    ViewDimension = UnorderedAccessViewDimension.Buffer,
                    Buffer = new BufferUnorderedAccessView
                    {
                        FirstElement = offset / structureByteStride,
                        NumElements = size / structureByteStride,
                    }
                };

                return device.CreateUnorderedAccessView(Buffer, uavDesc);'''),
])

# 7. D3D11Swapchain.cs
fix_file('src/Veldrid/D3D11/D3D11Swapchain.cs', [
    ('public int SyncInterval { get; private set; }', 'public uint SyncInterval { get; private set; }'),
    ('int size = 1024 - 1;', 'uint size = 1024 - 1;'),
    ('DxgiSwapChain.SetPrivateData(CommonGuid.DebugObjectName, value.Length, namePtr);',
     'DxgiSwapChain.SetPrivateData(CommonGuid.DebugObjectName, (uint)value.Length, namePtr);'),
    ('DxgiSwapChain.ResizeBuffers(2, (int)actualWidth, (int)actualHeight, colorFormat, flags)',
     'DxgiSwapChain.ResizeBuffers(2, actualWidth, actualHeight, colorFormat, flags)'),
    ('new ModeDescription(\n                        (int)width, (int)height, colorFormat)',
     'new ModeDescription(\n                        width, height, colorFormat)'),
    ('Height = (int)(height * pixelScale),\n                    Width = (int)(width * pixelScale),',
     'Height = (uint)(height * pixelScale),\n                    Width = (uint)(width * pixelScale),'),
])

# 8. D3D11GraphicsDevice.cs
fix_file('src/Veldrid/D3D11/D3D11GraphicsDevice.cs', [
    ('DeviceId = desc.DeviceId;', 'DeviceId = (int)desc.DeviceId;'),
    ('''immediateContext.Map(
                                texture.DeviceTexture,
                                (int)mipLevel,
                                (int)arrayLayer,
                                D3D11Formats.VdToD3D11MapMode(false, mode),
                                MapFlags.None,
                                out int _,
                                out MappedSubresource msr);''',
     '''uint mapSubresource = D3D11Util.ComputeSubresource(mipLevel, texture.MipLevels, arrayLayer);
                            var msr = immediateContext.Map(
                                texture.DeviceTexture,
                                mapSubresource,
                                D3D11Formats.VdToD3D11MapMode(false, mode));'''),
    ('immediateContext.Unmap(texture.DeviceTexture, (int)subresource);',
     'immediateContext.Unmap(texture.DeviceTexture, subresource);'),
    ('private bool checkFormatMultisample(Format format, int sampleCount)',
     'private bool checkFormatMultisample(Format format, uint sampleCount)'),
    ('immediateContext.CopySubresourceRegion(\n                        d3dBuffer.Buffer, 0, (int)bufferOffsetInBytes, 0, 0,',
     'immediateContext.CopySubresourceRegion(\n                        d3dBuffer.Buffer, 0, bufferOffsetInBytes, 0, 0,'),
    ('int subresource = D3D11Util.ComputeSubresource(mipLevel, texture.MipLevels, arrayLayer);',
     'uint subresource = D3D11Util.ComputeSubresource(mipLevel, texture.MipLevels, arrayLayer);'),
    ('''immediateContext.UpdateSubresource(
                        d3dTex.DeviceTexture,
                        subresource,
                        resourceRegion,
                        source,
                        (int)srcRowPitch,
                        (int)srcDepthPitch);''',
     '''immediateContext.UpdateSubresource(
                        source,
                        d3dTex.DeviceTexture,
                        subresource,
                        srcRowPitch,
                        srcDepthPitch,
                        resourceRegion);'''),
])

# 9. D3D11ResourceCache.cs
fix_file('src/Veldrid/D3D11/D3D11ResourceCache.cs', [
    ('int currentOffset = 0;', 'uint currentOffset = 0;'),
    ("desc.Offset != 0 ? (int)desc.Offset : currentOffset,\n                        slot,",
     "desc.Offset != 0 ? desc.Offset : currentOffset,\n                        (uint)slot,"),
    ('(int)stepRate);', 'stepRate);'),
    ('currentOffset += (int)FormatSizeHelpers.GetSizeInBytes(desc.Format);',
     'currentOffset += FormatSizeHelpers.GetSizeInBytes(desc.Format);'),
    ('private int position;\n            private int texCoord;\n            private int normal;\n            private int color;',
     'private uint position;\n            private uint texCoord;\n            private uint normal;\n            private uint color;'),
    ('public static int GetAndIncrement(ref SemanticIndices si, VertexElementSemantic type)',
     'public static uint GetAndIncrement(ref SemanticIndices si, VertexElementSemantic type)'),
])

# 10. D3D11Pipeline.cs
fix_file('src/Veldrid/D3D11/D3D11Pipeline.cs', [
    ('public int[] VertexStrides { get; }', 'public uint[] VertexStrides { get; }'),
    ('VertexStrides = new int[numVertexBuffers];', 'VertexStrides = new uint[numVertexBuffers];'),
    ('VertexStrides[i] = (int)description.ShaderSet.VertexLayouts[i].Stride;',
     'VertexStrides[i] = description.ShaderSet.VertexLayouts[i].Stride;'),
    ('VertexStrides = Array.Empty<int>();', 'VertexStrides = Array.Empty<uint>();'),
])

# 11. D3D11CommandList.cs
fix_file('src/Veldrid/D3D11/D3D11CommandList.cs', [
    ('private int[] vertexStrides = new int[1];', 'private uint[] vertexStrides = new uint[1];'),
    ('private int[] vertexOffsets = new int[1];', 'private uint[] vertexOffsets = new uint[1];'),
    ('private readonly int[] firstConstRef = new int[1];', 'private readonly uint[] firstConstRef = new uint[1];'),
    ('private readonly int[] numConstsRef = new int[1];', 'private readonly uint[] numConstsRef = new uint[1];'),
    # Dispatch
    ('DeviceContext.Dispatch((int)groupCountX, (int)groupCountY, (int)groupCountZ);',
     'DeviceContext.Dispatch(groupCountX, groupCountY, groupCountZ);'),
    # DrawIndirect
    ('int currentOffset = (int)offset;\n\n            for (uint i = 0; i < drawCount; i++)\n            {\n                DeviceContext.DrawInstancedIndirect(d3d11Buffer.Buffer, currentOffset);\n                currentOffset += (int)stride;',
     'uint currentOffset = offset;\n\n            for (uint i = 0; i < drawCount; i++)\n            {\n                DeviceContext.DrawInstancedIndirect(d3d11Buffer.Buffer, currentOffset);\n                currentOffset += stride;'),
    # DrawIndexedIndirect
    ('int currentOffset = (int)offset;\n\n            for (uint i = 0; i < drawCount; i++)\n            {\n                DeviceContext.DrawIndexedInstancedIndirect(d3d11Buffer.Buffer, currentOffset);\n                currentOffset += (int)stride;',
     'uint currentOffset = offset;\n\n            for (uint i = 0; i < drawCount; i++)\n            {\n                DeviceContext.DrawIndexedInstancedIndirect(d3d11Buffer.Buffer, currentOffset);\n                currentOffset += stride;'),
    # DispatchIndirect
    ('DeviceContext.DispatchIndirect(d3d11Buffer.Buffer, (int)offset);',
     'DeviceContext.DispatchIndirect(d3d11Buffer.Buffer, offset);'),
    # CopyBuffer - destinationOffset
    ('DeviceContext.CopySubresourceRegion(dstD3D11Buffer.Buffer, 0, (int)destinationOffset, 0, 0, srcD3D11Buffer.Buffer, 0, region);',
     'DeviceContext.CopySubresourceRegion(dstD3D11Buffer.Buffer, 0, destinationOffset, 0, 0, srcD3D11Buffer.Buffer, 0, region);'),
    # CopyTexture subresources
    ('int srcSubresource = D3D11Util.ComputeSubresource(srcMipLevel, source.MipLevels, srcBaseArrayLayer + i);\n                int dstSubresource = D3D11Util.ComputeSubresource(dstMipLevel, destination.MipLevels, dstBaseArrayLayer + i);',
     'uint srcSubresource = D3D11Util.ComputeSubresource(srcMipLevel, source.MipLevels, srcBaseArrayLayer + i);\n                uint dstSubresource = D3D11Util.ComputeSubresource(dstMipLevel, destination.MipLevels, dstBaseArrayLayer + i);'),
    # CopyTexture dstX/dstY/dstZ
    ('dstSubresource,\n                    (int)dstX,\n                    (int)dstY,\n                    (int)dstZ,',
     'dstSubresource,\n                    dstX,\n                    dstY,\n                    dstZ,'),
    # IASetVertexBuffers
    ('DeviceContext.IASetVertexBuffers(\n                    0, (int)numVertexBindings,',
     'DeviceContext.IASetVertexBuffers(\n                    0, numVertexBindings,'),
    # bindTextureView - all VSSetShaderResource etc with slot
    ('if (bind) DeviceContext.VSSetShaderResource(slot, srv);', 'if (bind) DeviceContext.VSSetShaderResource((uint)slot, srv);'),
    ('DeviceContext.GSSetShaderResource(slot, srv);', 'DeviceContext.GSSetShaderResource((uint)slot, srv);'),
    ('DeviceContext.HSSetShaderResource(slot, srv);', 'DeviceContext.HSSetShaderResource((uint)slot, srv);'),
    ('DeviceContext.DSSetShaderResource(slot, srv);', 'DeviceContext.DSSetShaderResource((uint)slot, srv);'),
    ('if (bind) DeviceContext.PSSetShaderResource(slot, srv!);', 'if (bind) DeviceContext.PSSetShaderResource((uint)slot, srv!);'),
    ('DeviceContext.CSSetShaderResource(slot, srv);', 'DeviceContext.CSSetShaderResource((uint)slot, srv);'),
    # bindStorageBufferView - all SetShaderResource with slot
    ('if ((stages & ShaderStages.Vertex) == ShaderStages.Vertex) DeviceContext.VSSetShaderResource(slot, srv);\n\n            if ((stages & ShaderStages.Geometry) == ShaderStages.Geometry) DeviceContext.GSSetShaderResource(slot, srv);\n\n            if ((stages & ShaderStages.TessellationControl) == ShaderStages.TessellationControl) DeviceContext.HSSetShaderResource(slot, srv);\n\n            if ((stages & ShaderStages.TessellationEvaluation) == ShaderStages.TessellationEvaluation) DeviceContext.DSSetShaderResource(slot, srv);\n\n            if ((stages & ShaderStages.Fragment) == ShaderStages.Fragment) DeviceContext.PSSetShaderResource(slot, srv);\n\n            if (compute) DeviceContext.CSSetShaderResource(slot, srv);',
     'if ((stages & ShaderStages.Vertex) == ShaderStages.Vertex) DeviceContext.VSSetShaderResource((uint)slot, srv);\n\n            if ((stages & ShaderStages.Geometry) == ShaderStages.Geometry) DeviceContext.GSSetShaderResource((uint)slot, srv);\n\n            if ((stages & ShaderStages.TessellationControl) == ShaderStages.TessellationControl) DeviceContext.HSSetShaderResource((uint)slot, srv);\n\n            if ((stages & ShaderStages.TessellationEvaluation) == ShaderStages.TessellationEvaluation) DeviceContext.DSSetShaderResource((uint)slot, srv);\n\n            if ((stages & ShaderStages.Fragment) == ShaderStages.Fragment) DeviceContext.PSSetShaderResource((uint)slot, srv);\n\n            if (compute) DeviceContext.CSSetShaderResource((uint)slot, srv);'),
    # bindUniformBuffer - VSSetConstantBuffer etc
    ('DeviceContext.VSSetConstantBuffer(slot, range.Buffer.Buffer);', 'DeviceContext.VSSetConstantBuffer((uint)slot, range.Buffer.Buffer);'),
    ('DeviceContext.VSUnsetConstantBuffer(slot);', 'DeviceContext.VSUnsetConstantBuffer((uint)slot);'),
    ('context1.VSSetConstantBuffers1(slot, 1,', 'context1.VSSetConstantBuffers1((uint)slot, 1,'),
    ('DeviceContext.GSSetConstantBuffer(slot, range.Buffer.Buffer);', 'DeviceContext.GSSetConstantBuffer((uint)slot, range.Buffer.Buffer);'),
    ('DeviceContext.GSUnsetConstantBuffer(slot);', 'DeviceContext.GSUnsetConstantBuffer((uint)slot);'),
    ('context1.GSSetConstantBuffers1(slot, 1,', 'context1.GSSetConstantBuffers1((uint)slot, 1,'),
    ('DeviceContext.HSSetConstantBuffer(slot, range.Buffer.Buffer);', 'DeviceContext.HSSetConstantBuffer((uint)slot, range.Buffer.Buffer);'),
    ('DeviceContext.HSUnsetConstantBuffer(slot);', 'DeviceContext.HSUnsetConstantBuffer((uint)slot);'),
    ('context1.HSSetConstantBuffers1(slot, 1,', 'context1.HSSetConstantBuffers1((uint)slot, 1,'),
    ('DeviceContext.DSSetConstantBuffer(slot, range.Buffer.Buffer);', 'DeviceContext.DSSetConstantBuffer((uint)slot, range.Buffer.Buffer);'),
    ('DeviceContext.DSUnsetConstantBuffer(slot);', 'DeviceContext.DSUnsetConstantBuffer((uint)slot);'),
    ('context1.DSSetConstantBuffers1(slot, 1,', 'context1.DSSetConstantBuffers1((uint)slot, 1,'),
    ('DeviceContext.PSSetConstantBuffer(slot, range.Buffer.Buffer);', 'DeviceContext.PSSetConstantBuffer((uint)slot, range.Buffer.Buffer);'),
    ('DeviceContext.PSUnsetConstantBuffer(slot);', 'DeviceContext.PSUnsetConstantBuffer((uint)slot);'),
    ('context1.PSSetConstantBuffers1(slot, 1,', 'context1.PSSetConstantBuffers1((uint)slot, 1,'),
    ('DeviceContext.CSSetConstantBuffer(slot, range.Buffer.Buffer);', 'DeviceContext.CSSetConstantBuffer((uint)slot, range.Buffer.Buffer);'),
    ('DeviceContext.CSSetConstantBuffer(slot, null);', 'DeviceContext.CSSetConstantBuffer((uint)slot, null);'),
    ('context1.CSSetConstantBuffers1(slot, 1,', 'context1.CSSetConstantBuffers1((uint)slot, 1,'),
    # packRangeParams
    ('firstConstRef[0] = (int)range.Offset / 16;', 'firstConstRef[0] = range.Offset / 16;'),
    ('numConstsRef[0] = (int)roundedSize / 16;', 'numConstsRef[0] = roundedSize / 16;'),
    # bindUnorderedAccessView
    ('DeviceContext.CSSetUnorderedAccessView(actualSlot, uav);', 'DeviceContext.CSSetUnorderedAccessView((uint)actualSlot, uav);'),
    ('DeviceContext.OMSetUnorderedAccessView(actualSlot, uav!);', 'DeviceContext.OMSetUnorderedAccessView((uint)actualSlot, uav!);'),
    # unbindUavBufferIndividual
    ('DeviceContext.CSUnsetUnorderedAccessView(slot);', 'DeviceContext.CSUnsetUnorderedAccessView((uint)slot);'),
    ('DeviceContext.OMUnsetUnorderedAccessView(slot);', 'DeviceContext.OMUnsetUnorderedAccessView((uint)slot);'),
    # bindSampler
    ('if (bind) DeviceContext.VSSetSampler(slot, sampler.DeviceSampler);', 'if (bind) DeviceContext.VSSetSampler((uint)slot, sampler.DeviceSampler);'),
    ('DeviceContext.GSSetSampler(slot, sampler.DeviceSampler);', 'DeviceContext.GSSetSampler((uint)slot, sampler.DeviceSampler);'),
    ('DeviceContext.HSSetSampler(slot, sampler.DeviceSampler);', 'DeviceContext.HSSetSampler((uint)slot, sampler.DeviceSampler);'),
    ('DeviceContext.DSSetSampler(slot, sampler.DeviceSampler);', 'DeviceContext.DSSetSampler((uint)slot, sampler.DeviceSampler);'),
    ('if (bind) DeviceContext.PSSetSampler(slot, sampler.DeviceSampler);', 'if (bind) DeviceContext.PSSetSampler((uint)slot, sampler.DeviceSampler);'),
    ('DeviceContext.CSSetSampler(slot, sampler.DeviceSampler);', 'DeviceContext.CSSetSampler((uint)slot, sampler.DeviceSampler);'),
    # UpdateSubresource_Workaround
    ('int subresource,\n            Box? region,', 'uint subresource,\n            Box? region,'),
    ('DeviceContext.UpdateSubresource(resource, subresource, region, (IntPtr)pAdjustedSrcData, 0, 0);',
     'DeviceContext.UpdateSubresource((IntPtr)pAdjustedSrcData, resource, subresource, 0u, 0u, region);'),
    # IASetIndexBuffer
    ('DeviceContext.IASetIndexBuffer(d3d11Buffer.Buffer, D3D11Formats.ToDxgiFormat(format), (int)offset);',
     'DeviceContext.IASetIndexBuffer(d3d11Buffer.Buffer, D3D11Formats.ToDxgiFormat(format), offset);'),
    # OMSetDepthStencilState
    ('DeviceContext.OMSetDepthStencilState(depthStencilState, (int)stencilReference);',
     'DeviceContext.OMSetDepthStencilState(depthStencilState, stencilReference);'),
    # SetVertexBuffer offset
    ('vertexOffsets[index] = (int)offset;', 'vertexOffsets[index] = offset;'),
    # Draw
    ("DeviceContext.Draw((int)vertexCount, (int)vertexStart);", "DeviceContext.Draw(vertexCount, vertexStart);"),
    ("DeviceContext.DrawInstanced((int)vertexCount, (int)instanceCount, (int)vertexStart, (int)instanceStart);",
     "DeviceContext.DrawInstanced(vertexCount, instanceCount, vertexStart, instanceStart);"),
    # DrawIndexed
    ("DeviceContext.DrawIndexed((int)indexCount, (int)indexStart, vertexOffset);",
     "DeviceContext.DrawIndexed(indexCount, indexStart, vertexOffset);"),
    ("DeviceContext.DrawIndexedInstanced((int)indexCount, (int)instanceCount, (int)indexStart, vertexOffset, (int)instanceStart);",
     "DeviceContext.DrawIndexedInstanced(indexCount, instanceCount, indexStart, vertexOffset, instanceStart);"),
])

print("All files processed.")
