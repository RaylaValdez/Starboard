using System;
using System.Runtime.InteropServices;

// === D3D11 ===
[ComImport, Guid("db6f6ddb-ac77-4e88-8253-819df9bbf140"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ID3D11Device
{
    // vtable trimmed to methods we actually call
    int CreateRenderTargetView(IntPtr pResource, IntPtr pDesc, out ID3D11RenderTargetView ppRTV);
    // many methods omitted…
}
[ComImport, Guid("c0bfa96c-e089-44fb-8eaf-26f8796190da"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ID3D11DeviceContext
{
    void VSSetConstantBuffers(); // placeholder to keep vtable index alignment (not used)
    // … many omitted …
    void OMSetRenderTargets(uint NumViews, [MarshalAs(UnmanagedType.Interface)] ID3D11RenderTargetView pRTV, IntPtr pDSV);
    void ClearRenderTargetView([MarshalAs(UnmanagedType.Interface)] ID3D11RenderTargetView pRTV, float[] ColorRGBA);
    // … many omitted …
}
[ComImport, Guid("9b7e4c8f-342c-4106-a19f-4f2704f689f0"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ID3D11RenderTargetView { }

[ComImport, Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ID3D11Texture2D { }

// === DXGI ===
[ComImport, Guid("50c83a1c-e072-4c48-87b0-3630fa36a6d0"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDXGIFactory2
{
    // IDXGIObject (4)
    int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
    int SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
    int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
    int GetParent(ref Guid riid, out IntPtr ppParent);

    // IDXGIFactory (8)
    int EnumAdapters(uint Adapter, out IntPtr ppAdapter);
    int MakeWindowAssociation(IntPtr WindowHandle, uint Flags);
    int GetWindowAssociation(out IntPtr pWindowHandle);
    int CreateSwapChain([MarshalAs(UnmanagedType.IUnknown)] object pDevice, IntPtr pDesc /* DXGI_SWAP_CHAIN_DESC* */, out IntPtr ppSwapChain);
    int CreateSoftwareAdapter(IntPtr Module, out IntPtr ppAdapter);
    int EnumAdapters1(uint Adapter, out IntPtr ppAdapter1);
    int IsCurrent(); // BOOL -> int
    // note: On some headers IsCurrent is on Factory1; still one slot

    // IDXGIFactory2 (the ones we actually need; order matters)
    int IsWindowedStereoEnabled(); // BOOL -> int
    int CreateSwapChainForHwnd([MarshalAs(UnmanagedType.Interface)] ID3D11Device pDevice,
        IntPtr hWnd, ref DXGI_SWAP_CHAIN_DESC1 pDesc,
        IntPtr pFullscreenDesc, IntPtr pRestrictToOutput,
        out IDXGISwapChain1 ppSwapChain);
    int CreateSwapChainForCoreWindow([MarshalAs(UnmanagedType.Interface)] ID3D11Device pDevice,
        IntPtr pWindow, ref DXGI_SWAP_CHAIN_DESC1 pDesc,
        IntPtr pRestrictToOutput, out IDXGISwapChain1 ppSwapChain);
    int GetSharedResourceAdapterLuid(IntPtr hResource, out long pLuid /*LUID*/);
    int RegisterOcclusionStatusWindow(IntPtr WindowHandle, uint wMsg, out uint pdwCookie);
    int RegisterOcclusionStatusEvent(IntPtr hEvent, out uint pdwCookie);
    void UnregisterOcclusionStatus(uint dwCookie);
    int CreateSwapChainForComposition([MarshalAs(UnmanagedType.Interface)] ID3D11Device pDevice,
        ref DXGI_SWAP_CHAIN_DESC1 pDesc, IntPtr pRestrictToOutput,
        out IDXGISwapChain1 ppSwapChain);
}


[ComImport, Guid("790a45f7-0d42-4876-983a-0a55cfe6f4aa"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDXGISwapChain1
{
    int GetBuffer(uint Buffer, ref Guid riid, out IntPtr ppSurface);
    int Present(uint SyncInterval, uint Flags);
    int ResizeBuffers(uint BufferCount, uint Width, uint Height, uint NewFormat, uint Flags);
}

// === structs/enums we actually need ===
[StructLayout(LayoutKind.Sequential)]
internal struct DXGI_SAMPLE_DESC { public uint Count, Quality; }

[StructLayout(LayoutKind.Sequential)]
internal struct DXGI_SWAP_CHAIN_DESC1
{
    public uint Width, Height;
    public uint Format;              // 87 = DXGI_FORMAT_B8G8R8A8_UNORM
    public int Stereo;               // bool as 4-byte
    public DXGI_SAMPLE_DESC SampleDesc;
    public uint BufferUsage;         // 0x20 = RENDER_TARGET_OUTPUT
    public uint BufferCount;         // 2
    public uint Scaling;             // 0 = STRETCH
    public uint SwapEffect;          // 4 = FLIP_DISCARD
    public uint AlphaMode;           // 2 = PREMULTIPLIED
    public uint Flags;               // 0
}

internal static class DXGI
{
    public const uint FORMAT_B8G8R8A8_UNORM = 87;
    public const uint USAGE_RENDER_TARGET_OUTPUT = 0x20;
    public const uint SCALING_STRETCH = 0;
    public const uint SWAPEFFECT_FLIP_DISCARD = 4;
    public const uint ALPHA_PREMULTIPLIED = 2;
}

internal enum D3D_DRIVER_TYPE { HARDWARE = 1 }
internal enum D3D_FEATURE_LEVEL : uint { Level_11_0 = 0xb000 }
