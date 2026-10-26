using System.Runtime.InteropServices;
namespace Nexus_Recoil.Core;

// Synthetic physical key state; these tests never inject real mouse input.
internal static class NativeMethods
{
    public static short GetAsyncKeyState(int key) => key is 1 or 2 ? unchecked((short)0x8000) : (short)0;
    public static void mouse_event(uint flags, int x, int y, uint data, UIntPtr extra) { }
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    public const uint INPUT_MOUSE = 0, MOUSEEVENTF_MOVE = 1;
    public static uint SendInput(uint count, INPUT[] inputs, int size) => count;
}
