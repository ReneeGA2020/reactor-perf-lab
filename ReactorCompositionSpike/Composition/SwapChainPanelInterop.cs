using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;

namespace ReactorCompositionSpike.Composition;

/// <summary>The WinUI interop interface that binds a DXGI swap chain to a XAML SwapChainPanel.</summary>
[ComImport]
[Guid("63aad0b8-7c24-40ff-85a8-640d944cc325")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISwapChainPanelNative
{
    [PreserveSig] int SetSwapChain(IntPtr swapChain);
}

internal static class SwapChainPanelInterop
{
    /// <summary>Queries the panel's ISwapChainPanelNative and binds the given DXGI swap chain.</summary>
    public static void SetSwapChain(SwapChainPanel panel, IntPtr swapChain)
    {
        IntPtr unknown = Marshal.GetIUnknownForObject(panel);
        try
        {
            Guid iid = typeof(ISwapChainPanelNative).GUID;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, ref iid, out IntPtr ptr));
            try
            {
                var native = (ISwapChainPanelNative)Marshal.GetObjectForIUnknown(ptr);
                Marshal.ThrowExceptionForHR(native.SetSwapChain(swapChain));
            }
            finally { Marshal.Release(ptr); }
        }
        finally { Marshal.Release(unknown); }
    }
}
