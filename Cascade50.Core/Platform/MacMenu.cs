using System;
using System.Runtime.InteropServices;

namespace Cascade50.Core.Platform;

/// <summary>
/// Replaces the menu bar SDL installs on macOS with the app's own: the app name holding a single
/// Quit item. SDL's menu also carries About, Services and Hide; this build only wants Quit.
/// </summary>
public static class MacMenu
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(LibObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(LibObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr self, IntPtr op);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send1(IntPtr self, IntPtr op, IntPtr a);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send3(IntPtr self, IntPtr op, IntPtr a, IntPtr b, IntPtr c);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendV1(IntPtr self, IntPtr op, IntPtr a);

    private static IntPtr Cls(string name) => objc_getClass(name);
    private static IntPtr Sel(string name) => sel_registerName(name);

    /// <summary>An NSString for a C# string (the menu keeps it for the life of the app).</summary>
    private static IntPtr Str(string text)
    {
        var c = Marshal.StringToHGlobalAnsi(text);
        try
        {
            return Send1(Cls("NSString"), Sel("stringWithUTF8String:"), c);
        }
        finally
        {
            Marshal.FreeHGlobal(c);
        }
    }

    private static IntPtr New(string className) => Send(Cls(className), Sel("alloc"));

    /// <summary>Installs the app menu: the app name, opening a menu with one Quit item.</summary>
    public static void Install(string appName)
    {
        var nsApp = Send(Cls("NSApplication"), Sel("sharedApplication"));
        if (nsApp == IntPtr.Zero)
            return;

        var menu = Send1(New("NSMenu"), Sel("initWithTitle:"), Str(appName));
        var quit = Send3(New("NSMenuItem"), Sel("initWithTitle:action:keyEquivalent:"),
            Str("Quit " + appName), Sel("terminate:"), Str("q"));
        SendV1(menu, Sel("addItem:"), quit);

        var appItem = Send(New("NSMenuItem"), Sel("init"));
        SendV1(appItem, Sel("setSubmenu:"), menu);

        var mainMenu = Send(New("NSMenu"), Sel("init"));
        SendV1(mainMenu, Sel("addItem:"), appItem);
        SendV1(nsApp, Sel("setMainMenu:"), mainMenu);
    }
}
