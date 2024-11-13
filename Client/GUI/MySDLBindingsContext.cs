using System;
using System.Runtime.InteropServices;
using OpenTK;

public class MySDLBindingsContext : IBindingsContext
{
    [DllImport("SDL2")]
    private static extern IntPtr SDL_GL_GetProcAddress([MarshalAs(UnmanagedType.LPStr)] string procName);

    public IntPtr GetProcAddress(string procName)
    {
        return SDL_GL_GetProcAddress(procName);
    }
}