using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ImGuiNET;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Vortice.Direct3D;
using Vortice.D3DCompiler;

namespace Starboard;

/// <summary>
/// Minimal ImGui renderer for Direct3D11. 
/// - Creates device objects (shaders, input layout, sampler, blend/raster/depth states).
/// - Uploads ImGui's font atlas to a shader resource view.
/// - On Render(): copies ImGui draw lists into dynamic buffers and draws them.
/// </summary>
internal sealed class ImGuiRendererD3D11 : IDisposable
{
    // --- Constants / "magic numbers" ---
    private const int InitialVertexCapacity = 5000;
    private const int InitialIndexCapacity = 10000;
    private const float BufferGrowthFactor = 1.5f;
    private const uint ConstantBufferBindSlot = 0;
    private const uint ImGuiFontTextureId = 1;
    private const float SecondaryDeltaTime = 1.0f / 240.0f; // kept for parity with original Math.Max usage
    private const float MinimumDeltaTime = 1.0f / 120.0f;   // effective result of Math.Max(MinimumDeltaTime, SecondaryDeltaTime)

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _deviceContext;

    private ID3D11Buffer _vertexBuffer;
    private ID3D11Buffer _indexBuffer;
    private int _vertexBufferCapacity = InitialVertexCapacity;
    private int _indexBufferCapacity = InitialIndexCapacity;

    private ID3D11VertexShader _vertexShader;
    private ID3D11PixelShader _pixelShader;
    private ID3D11InputLayout _inputLayout;
    private ID3D11SamplerState _fontSampler;
    private ID3D11ShaderResourceView _fontTextureView;
    private ID3D11BlendState _blendState;
    private ID3D11RasterizerState _rasterizerState;
    private ID3D11DepthStencilState _depthState;

    public ImGuiRendererD3D11(ID3D11Device device, ID3D11DeviceContext deviceContext)
    {
        _device = device;
        _deviceContext = deviceContext;

        ImGui.CreateContext();
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;

        RecreateFontTexture();
        CreateDeviceObjects();
    }

    /// <summary>
    /// Begin a new ImGui frame with the specified display size.
    /// </summary>
    public void NewFrame(int displayWidth, int displayHeight)
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(displayWidth, displayHeight);

        // Keep original behavior: Math.Max(1/120, 1/240) → 1/120. Leaves timing decoupled from real frame time.
        io.DeltaTime = Math.Max(MinimumDeltaTime, SecondaryDeltaTime);

        ImGui.NewFrame();
    }

    /// <summary>
    /// Render ImGui into the provided swap chain's back buffer.
    /// </summary>
    public void Render(IDXGISwapChain1 swapchain)
    {
        ImGui.Render();
        var drawData = ImGui.GetDrawData();
        if (drawData.CmdListsCount == 0) return;

        // --- Ensure dynamic buffers are large enough (grow by BufferGrowthFactor if needed) ---
        int totalVertexCount = drawData.TotalVtxCount;
        int totalIndexCount = drawData.TotalIdxCount;

        if (_vertexBuffer == null || _vertexBufferCapacity < totalVertexCount)
        {
            _vertexBuffer?.Dispose();
            _vertexBufferCapacity = Math.Max(InitialVertexCapacity, (int)(totalVertexCount * BufferGrowthFactor));
            _vertexBuffer = _device.CreateBuffer(new BufferDescription((uint)(_vertexBufferCapacity * Unsafe.SizeOf<ImDrawVert>()),
                                                                      BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        }

        if (_indexBuffer == null || _indexBufferCapacity < totalIndexCount)
        {
            _indexBuffer?.Dispose();
            _indexBufferCapacity = Math.Max(InitialIndexCapacity, (int)(totalIndexCount * BufferGrowthFactor));
            _indexBuffer = _device.CreateBuffer(new BufferDescription((uint)(_indexBufferCapacity * sizeof(ushort)),
                                                                      BindFlags.IndexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        }

        // --- Copy ImGui's CPU draw data into our dynamic GPU buffers ---
        var mappedVertices = _deviceContext.Map(_vertexBuffer, 0, MapMode.WriteDiscard);
        var mappedIndices = _deviceContext.Map(_indexBuffer, 0, MapMode.WriteDiscard);
        unsafe
        {
            byte* vertexDst = (byte*)mappedVertices.DataPointer;
            byte* indexDst = (byte*)mappedIndices.DataPointer;

            for (int listIndex = 0; listIndex < drawData.CmdListsCount; listIndex++)
            {
                ImDrawListPtr cmdList = drawData.CmdLists[listIndex];

                int vertexBytes = cmdList.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>();
                int indexBytes = cmdList.IdxBuffer.Size * sizeof(ushort);

                Buffer.MemoryCopy((void*)cmdList.VtxBuffer.Data, vertexDst, vertexBytes, vertexBytes);
                vertexDst += vertexBytes;

                Buffer.MemoryCopy((void*)cmdList.IdxBuffer.Data, indexDst, indexBytes, indexBytes);
                indexDst += indexBytes;
            }
        }
        _deviceContext.Unmap(_vertexBuffer, 0);
        _deviceContext.Unmap(_indexBuffer, 0);

        // Define a viewport that exactly matches the ImGui draw data size.
        // Without this, you'll often render into a 0x0 (or stale) viewport.
        var viewport = new Viewport(0, 0, drawData.DisplaySize.X, drawData.DisplaySize.Y, 0.0f, 1.0f);
        _deviceContext.RSSetViewports(new[] { viewport });

        using var backBuffer = swapchain.GetBuffer<ID3D11Texture2D>(0);
        using var renderTargetView = _device.CreateRenderTargetView(backBuffer);
        _deviceContext.OMSetRenderTargets(renderTargetView, null);

        // Orthographic projection that maps ImGui's pixel coordinates directly to NDC.
        // Note the b/t order is flipped for Direct3D's clip space.
        var drawPos = drawData.DisplayPos;
        var drawSize = drawData.DisplaySize;
        float left = drawPos.X;
        float top = drawPos.Y;
        float right = drawPos.X + drawSize.X;
        float bottom = drawPos.Y + drawSize.Y;
        var projection = Matrix4x4.CreateOrthographicOffCenter(left, right, bottom, top, -1.0f, 1.0f);

        _deviceContext.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);
        _deviceContext.IASetInputLayout(_inputLayout);
        _deviceContext.VSSetShader(_vertexShader);
        _deviceContext.PSSetShader(_pixelShader);
        _deviceContext.PSSetSamplers(0, new[] { _fontSampler });
        _deviceContext.PSSetShaderResource(0, _fontTextureView);
        _deviceContext.OMSetBlendState(_blendState, new Color4(0, 0, 0, 0), 0xFFFFFFFF);
        _deviceContext.RSSetState(_rasterizerState);
        _deviceContext.OMSetDepthStencilState(_depthState, 0);

        // Upload the projection matrix to a transient constant buffer.
        using var constantBuffer = _device.CreateBuffer(new BufferDescription((uint)Unsafe.SizeOf<Matrix4x4>(),
                                                                             BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        var mappedCB = _deviceContext.Map(constantBuffer, 0, MapMode.WriteDiscard);
        unsafe { *(Matrix4x4*)mappedCB.DataPointer = projection; }
        _deviceContext.Unmap(constantBuffer, 0);
        _deviceContext.VSSetConstantBuffer(ConstantBufferBindSlot, constantBuffer);

        // Bind geometry and draw each command list with scissor rectangles.
        int vertexStride = Unsafe.SizeOf<ImDrawVert>();
        _deviceContext.IASetVertexBuffer(0, _vertexBuffer, (uint)vertexStride, 0);
        _deviceContext.IASetIndexBuffer(_indexBuffer, Format.R16_UInt, 0);

        int vertexOffset = 0;
        int indexOffset = 0;
        for (int listIndex = 0; listIndex < drawData.CmdListsCount; listIndex++)
        {
            var cmdList = drawData.CmdLists[listIndex];
            for (int cmdIndex = 0; cmdIndex < cmdList.CmdBuffer.Size; cmdIndex++)
            {
                var drawCmd = cmdList.CmdBuffer[cmdIndex];
                var clip = drawCmd.ClipRect;

                // Scissor rectangle is specified in framebuffer-space pixels.
                _deviceContext.RSSetScissorRect((int)clip.X, (int)clip.Y, (int)clip.Z, (int)clip.W);
                _deviceContext.DrawIndexed(drawCmd.ElemCount, (uint)indexOffset, vertexOffset);

                indexOffset += (int)drawCmd.ElemCount;
            }
            vertexOffset += cmdList.VtxBuffer.Size;
        }
    }

    /// <summary>
    /// Builds the ImGui font atlas as a DX11 texture and a SRV.
    /// </summary>
    private void RecreateFontTexture()
    {
        var io = ImGui.GetIO();
        io.Fonts.AddFontDefault();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bytesPerPixel);

        var textureDescription = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
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
            var dataBox = new SubresourceData(pixels.ToPointer(), (uint)(width * bytesPerPixel), 0);
            using var texture = _device.CreateTexture2D(textureDescription, dataBox);
            _fontTextureView = _device.CreateShaderResourceView(texture);
        }

        // Store a fake texture ID in ImGui (we aren't using it in this renderer).
        io.Fonts.SetTexID(new IntPtr(ImGuiFontTextureId));
    }

    /// <summary>
    /// Create shaders, input layout, sampler, and the fixed pipeline states.
    /// </summary>
    private void CreateDeviceObjects()
    {
        // Simple shader pair that transforms vertices by the projection matrix and samples the font texture.
        string vertexShaderSource = @"cbuffer vertexBuffer : register(b0) { float4x4 ProjectionMatrix; };
struct VS_INPUT { float2 pos : POSITION; float2 uv : TEXCOORD0; float4 col : COLOR0; };
struct PS_INPUT { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : COLOR0; };
PS_INPUT main(VS_INPUT input) { PS_INPUT output;
output.pos = mul( ProjectionMatrix, float4(input.pos.xy, 0.f, 1.f));
output.uv = input.uv; output.col = input.col; return output; }";

        string pixelShaderSource = @"struct PS_INPUT { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : COLOR0; };
sampler sampler0; Texture2D texture0;
float4 main(PS_INPUT input) : SV_Target { return input.col * texture0.Sample(sampler0, input.uv); }";

        // Vortice.D3DCompiler 3.6.x returns ReadOnlyMemory<byte> here.
        var vsBytes = Compiler.Compile(vertexShaderSource, null, "main", "imgui_vs", "vs_5_0",
            ShaderFlags.OptimizationLevel3, EffectFlags.None);

        var psBytes = Compiler.Compile(pixelShaderSource, null, "main", "imgui_ps", "ps_5_0",
            ShaderFlags.OptimizationLevel3, EffectFlags.None);

        _vertexShader = _device.CreateVertexShader(vsBytes.Span);
        _pixelShader = _device.CreatePixelShader(psBytes.Span);

        var inputElements = new[]
        {
            new InputElementDescription("POSITION", 0, Format.R32G32_Float,   0, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float,   8, 0),
            new InputElementDescription("COLOR",    0, Format.R8G8B8A8_UNorm, 16, 0),
        };
        _inputLayout = _device.CreateInputLayout(inputElements, vsBytes.Span);

        var samplerDescription = new SamplerDescription
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Wrap,
            AddressV = TextureAddressMode.Wrap,
            AddressW = TextureAddressMode.Wrap,
            ComparisonFunc = ComparisonFunction.Never,
            MinLOD = 0,
            MaxLOD = float.MaxValue
        };
        _fontSampler = _device.CreateSamplerState(samplerDescription);

        var blendDescription = new BlendDescription
        {
            AlphaToCoverageEnable = false,
            IndependentBlendEnable = false
        };

        // Premultiplied alpha blending setup: out = src.rgb * src.a + dst.rgb * (1 - src.a)
        var renderTargetBlend = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = Blend.SourceAlpha,
            DestinationBlend = Blend.InverseSourceAlpha,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.One,
            DestinationBlendAlpha = Blend.InverseSourceAlpha,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All
        };

        blendDescription.RenderTarget[0] = renderTargetBlend;
        _blendState = _device.CreateBlendState(blendDescription);

        var rasterizerDescription = new RasterizerDescription(CullMode.None, FillMode.Solid)
        {
            ScissorEnable = true,   // ImGui relies on per-draw scissor rectangles
            DepthClipEnable = true
        };
        _rasterizerState = _device.CreateRasterizerState(rasterizerDescription);

        var depthDescription = new DepthStencilDescription(false, DepthWriteMask.All, ComparisonFunction.Always);
        _depthState = _device.CreateDepthStencilState(depthDescription);
    }

    public void Dispose()
    {
        _depthState?.Dispose();
        _rasterizerState?.Dispose();
        _blendState?.Dispose();
        _fontSampler?.Dispose();
        _fontTextureView?.Dispose();
        _inputLayout?.Dispose();
        _pixelShader?.Dispose();
        _vertexShader?.Dispose();
        _indexBuffer?.Dispose();
        _vertexBuffer?.Dispose();
        ImGui.DestroyContext();
    }
}