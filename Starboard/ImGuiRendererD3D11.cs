using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ImGuiNET;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Starboard;

internal sealed class ImGuiRendererD3D11 : IDisposable
{
    private const int InitialVertexCapacity = 5000;
    private const int InitialIndexCapacity = 10000;
    private const float BufferGrowthFactor = 1.5f;
    private const uint ConstantBufferBindSlot = 0;
    private const uint ImGuiFontTextureId = 1;
    private const float SecondaryDeltaTime = 1.0f / 240.0f;
    private const float MinimumDeltaTime = 1.0f / 120.0f;

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _ctx;

    private ID3D11Buffer _vb, _ib;
    private int _vbCap = InitialVertexCapacity, _ibCap = InitialIndexCapacity;

    private ID3D11VertexShader _vs;
    private ID3D11PixelShader _ps;
    private ID3D11InputLayout _il;
    private ID3D11SamplerState _sampler;
    private ID3D11BlendState _blend;
    private ID3D11RasterizerState _rast;
    private ID3D11DepthStencilState _depth;

    private ID3D11ShaderResourceView _fontSRV;

    private struct TexInfo { public ID3D11ShaderResourceView SRV; public int W; public int H; }
    private readonly Dictionary<IntPtr, TexInfo> _textures = new();
    private long _nextTexId = 2; // 1 is reserved for the font

    public ImGuiRendererD3D11(ID3D11Device device, ID3D11DeviceContext ctx)
    {
        _device = device;
        _ctx = ctx;

        ImGui.CreateContext();
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;

        RecreateFontTexture();
        CreateDeviceObjects();
    }

    public void NewFrame(int displayW, int displayH)
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(displayW, displayH);
        io.DeltaTime = Math.Max(MinimumDeltaTime, SecondaryDeltaTime);
        ImGui.NewFrame();
    }

    public void Render(IDXGISwapChain1 swapchain)
    {
        ImGui.Render();
        var dd = ImGui.GetDrawData();
        if (dd.CmdListsCount == 0) return;

        // ensure buffers
        if (_vb == null || _vbCap < dd.TotalVtxCount)
        {
            _vb?.Dispose();
            _vbCap = Math.Max(InitialVertexCapacity, (int)(dd.TotalVtxCount * BufferGrowthFactor));
            _vb = _device.CreateBuffer(new BufferDescription((uint)(_vbCap * Unsafe.SizeOf<ImDrawVert>()), BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        }
        if (_ib == null || _ibCap < dd.TotalIdxCount)
        {
            _ib?.Dispose();
            _ibCap = Math.Max(InitialIndexCapacity, (int)(dd.TotalIdxCount * BufferGrowthFactor));
            _ib = _device.CreateBuffer(new BufferDescription((uint)(_ibCap * sizeof(ushort)), BindFlags.IndexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        }

        // upload
        var mv = _ctx.Map(_vb, 0, MapMode.WriteDiscard);
        var mi = _ctx.Map(_ib, 0, MapMode.WriteDiscard);
        unsafe
        {
            byte* vdst = (byte*)mv.DataPointer;
            byte* idst = (byte*)mi.DataPointer;

            for (int l = 0; l < dd.CmdListsCount; l++)
            {
                var list = dd.CmdLists[l];
                int vbytes = list.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>();
                int ibytes = list.IdxBuffer.Size * sizeof(ushort);

                Buffer.MemoryCopy((void*)list.VtxBuffer.Data, vdst, vbytes, vbytes);
                vdst += vbytes;

                Buffer.MemoryCopy((void*)list.IdxBuffer.Data, idst, ibytes, ibytes);
                idst += ibytes;
            }
        }
        _ctx.Unmap(_vb, 0);
        _ctx.Unmap(_ib, 0);

        // viewport
        var vp = new Viewport(0, 0, dd.DisplaySize.X, dd.DisplaySize.Y, 0, 1);
        _ctx.RSSetViewports(new[] { vp });

        using var bb = swapchain.GetBuffer<ID3D11Texture2D>(0);
        using var rtv = _device.CreateRenderTargetView(bb);
        _ctx.OMSetRenderTargets(rtv, null);

        // ortho
        var dp = dd.DisplayPos;
        var ds = dd.DisplaySize;
        var proj = Matrix4x4.CreateOrthographicOffCenter(dp.X, dp.X + ds.X, dp.Y + ds.Y, dp.Y, -1, 1);

        _ctx.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);
        _ctx.IASetInputLayout(_il);
        _ctx.VSSetShader(_vs);
        _ctx.PSSetShader(_ps);
        _ctx.PSSetSamplers(0, new[] { _sampler });
        _ctx.OMSetBlendState(_blend, new Color4(0, 0, 0, 0), 0xFFFFFFFF);
        _ctx.RSSetState(_rast);
        _ctx.OMSetDepthStencilState(_depth, 0);

        using var cb = _device.CreateBuffer(new BufferDescription((uint)Unsafe.SizeOf<Matrix4x4>(), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        var m = _ctx.Map(cb, 0, MapMode.WriteDiscard);
        unsafe { *(Matrix4x4*)m.DataPointer = proj; }
        _ctx.Unmap(cb, 0);
        _ctx.VSSetConstantBuffer(ConstantBufferBindSlot, cb);

        int stride = Unsafe.SizeOf<ImDrawVert>();
        _ctx.IASetVertexBuffer(0, _vb, (uint)stride, 0);
        _ctx.IASetIndexBuffer(_ib, Format.R16_UInt, 0);

        int vtxOfs = 0, idxOfs = 0;
        for (int l = 0; l < dd.CmdListsCount; l++)
        {
            var list = dd.CmdLists[l];
            for (int c = 0; c < list.CmdBuffer.Size; c++)
            {
                var cmd = list.CmdBuffer[c];
                var clip = cmd.ClipRect;
                _ctx.RSSetScissorRect((int)clip.X, (int)clip.Y, (int)clip.Z, (int)clip.W);

                // bind texture for this draw
                var id = cmd.TextureId;
                if (id == IntPtr.Zero) id = new IntPtr(ImGuiFontTextureId);
                if (!_textures.TryGetValue(id, out var ti)) ti = new TexInfo { SRV = _fontSRV };
                _ctx.PSSetShaderResource(0, ti.SRV);

                _ctx.DrawIndexed(cmd.ElemCount, (uint)idxOfs, vtxOfs);
                idxOfs += (int)cmd.ElemCount;
            }
            vtxOfs += list.VtxBuffer.Size;
        }
    }

    private void RecreateFontTexture()
    {
        var io = ImGui.GetIO();
        io.Fonts.Clear();
        io.Fonts.AddFontDefault();

        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int w, out int h, out int bpp);

        var td = new Texture2DDescription
        {
            Width = (uint)w,
            Height = (uint)h,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R8G8B8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        };

        unsafe
        {
            var data = new SubresourceData(pixels.ToPointer(), (uint)(w * bpp), 0);
            using var tex = _device.CreateTexture2D(td, data);
            _fontSRV = _device.CreateShaderResourceView(tex);
        }

        // register font as texture id 1
        _textures[new IntPtr(ImGuiFontTextureId)] = new TexInfo { SRV = _fontSRV, W = w, H = h };
        io.Fonts.SetTexID(new IntPtr(ImGuiFontTextureId));
    }

    private void CreateDeviceObjects()
    {
        string vs = @"cbuffer vertexBuffer : register(b0) { float4x4 ProjectionMatrix; }
struct VS_IN { float2 pos : POSITION; float2 uv : TEXCOORD0; float4 col : COLOR0; };
struct PS_IN { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : COLOR0; };
PS_IN main(VS_IN i){ PS_IN o; o.pos = mul(ProjectionMatrix, float4(i.pos.xy,0,1)); o.uv=i.uv; o.col=i.col; return o; }";

        // Premultiply in shader to match Src=ONE blending
        string ps = @"struct PS_IN { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : COLOR0; };
sampler s0; Texture2D t0;
float4 main(PS_IN i):SV_Target { float4 c = i.col * t0.Sample(s0, i.uv); c.rgb *= c.a; return c; }";

        var vsBytes = Compiler.Compile(vs, null, "main", "imgui_vs", "vs_5_0", ShaderFlags.OptimizationLevel3, EffectFlags.None);
        var psBytes = Compiler.Compile(ps, null, "main", "imgui_ps", "ps_5_0", ShaderFlags.OptimizationLevel3, EffectFlags.None);

        _vs = _device.CreateVertexShader(vsBytes.Span);
        _ps = _device.CreatePixelShader(psBytes.Span);

        var input = new[]
        {
            new InputElementDescription("POSITION",0,Format.R32G32_Float,   0,0),
            new InputElementDescription("TEXCOORD",0,Format.R32G32_Float,   8,0),
            new InputElementDescription("COLOR",   0,Format.R8G8B8A8_UNorm,16,0),
        };
        _il = _device.CreateInputLayout(input, vsBytes.Span);

        var samp = new SamplerDescription
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Wrap,
            AddressV = TextureAddressMode.Wrap,
            AddressW = TextureAddressMode.Wrap,
            ComparisonFunc = ComparisonFunction.Never,
            MinLOD = 0,
            MaxLOD = float.MaxValue
        };
        _sampler = _device.CreateSamplerState(samp);

        var bd = new BlendDescription { AlphaToCoverageEnable = false, IndependentBlendEnable = false };
        var rtb = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = Blend.One,                  // premultiplied
            DestinationBlend = Blend.InverseSourceAlpha,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.One,
            DestinationBlendAlpha = Blend.InverseSourceAlpha,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All
        };
        bd.RenderTarget[0] = rtb;
        _blend = _device.CreateBlendState(bd);

        var rd = new RasterizerDescription(CullMode.None, FillMode.Solid) { ScissorEnable = true, DepthClipEnable = true };
        _rast = _device.CreateRasterizerState(rd);

        var dd = new DepthStencilDescription(false, DepthWriteMask.All, ComparisonFunction.Always);
        _depth = _device.CreateDepthStencilState(dd);
    }

    // ----------- Texture API for the app -----------

    public IntPtr CreateTextureFromFile(string path, out int width, out int height)
    {
        using var bmp = new Bitmap(path);
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);

        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb); // BGRA
        try
        {
            // Use B8G8R8A8 to avoid swizzling
            var td = new Texture2DDescription
            {
                Width = (uint)bmp.Width,
                Height = (uint)bmp.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource,
                CPUAccessFlags = CpuAccessFlags.None,
                MiscFlags = ResourceOptionFlags.None
            };

            unsafe
            {
                var srd = new SubresourceData(data.Scan0.ToPointer(), (uint)data.Stride, 0);
                using var tex = _device.CreateTexture2D(td, srd);
                var srv = _device.CreateShaderResourceView(tex);

                var id = new IntPtr(System.Threading.Interlocked.Increment(ref _nextTexId));
                _textures[id] = new TexInfo { SRV = srv, W = bmp.Width, H = bmp.Height };

                width = bmp.Width; height = bmp.Height;
                return id;
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    public (int width, int height) GetTextureSize(IntPtr id)
    {
        return _textures.TryGetValue(id, out var ti) ? (ti.W, ti.H) : (0, 0);
    }

    public void Dispose()
    {
        foreach (var kv in _textures)
            kv.Value.SRV?.Dispose();

        _depth?.Dispose();
        _rast?.Dispose();
        _blend?.Dispose();
        _sampler?.Dispose();
        _il?.Dispose();
        _ps?.Dispose();
        _vs?.Dispose();
        _ib?.Dispose();
        _vb?.Dispose();
        ImGui.DestroyContext();
    }
}
