using System;
using System.Runtime.Versioning;
using System.Text;
using Vortice.D3DCompiler;

namespace Veldrid.D3D12
{
    [SupportedOSPlatform("windows")]
    internal class D3D12Shader : Shader
    {
        public byte[] Bytecode { get; }

        public override bool IsDisposed => disposed;

        public override string Name
        {
            get => name;
            set => name = value;
        }

        private string name;
        private bool disposed;

        public D3D12Shader(ShaderDescription description)
            : base(description.Stage, description.EntryPoint)
        {
            if (description.ShaderBytes.Length > 4
                && description.ShaderBytes[0] == 0x44
                && description.ShaderBytes[1] == 0x58
                && description.ShaderBytes[2] == 0x42
                && description.ShaderBytes[3] == 0x43)
                Bytecode = Util.ShallowClone(description.ShaderBytes);
            else
                Bytecode = compileCode(description);
        }

        #region Disposal

        public override void Dispose()
        {
            disposed = true;
        }

        #endregion

        private static byte[] compileCode(ShaderDescription description)
        {
            string profile;

            switch (description.Stage)
            {
                case ShaderStages.Vertex:
                    profile = "vs_5_1";
                    break;

                case ShaderStages.Geometry:
                    profile = "gs_5_1";
                    break;

                case ShaderStages.TessellationControl:
                    profile = "hs_5_1";
                    break;

                case ShaderStages.TessellationEvaluation:
                    profile = "ds_5_1";
                    break;

                case ShaderStages.Fragment:
                    profile = "ps_5_1";
                    break;

                case ShaderStages.Compute:
                    profile = "cs_5_1";
                    break;

                default:
                    throw Illegal.Value<ShaderStages>();
            }

            var flags = description.Debug ? ShaderFlags.Debug : ShaderFlags.OptimizationLevel3;
            Compiler.Compile(description.ShaderBytes, null!, null!,
                description.EntryPoint, null!,
                profile, flags, out var result, out var error);

            if (result == null)
                throw new VeldridException($"Failed to compile HLSL code: {Encoding.ASCII.GetString(error.AsBytes())}");

            return result.AsBytes();
        }
    }
}
