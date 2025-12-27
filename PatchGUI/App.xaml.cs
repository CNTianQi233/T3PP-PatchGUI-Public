using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using PatchGUI.Core;

namespace PatchGUI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            SessionLog.Initialize();

            try
            {
                // Ensure native DLLs (e.g. T3ppNative.dll) are resolved from the executable directory,
                // even when working directory differs or single-file extraction behavior varies.
                NativeLibrary.SetDllImportResolver(typeof(T3ppDiff).Assembly, (libraryName, assembly, searchPath) =>
                {
                    if (!string.Equals(libraryName, "T3ppNative.dll", StringComparison.OrdinalIgnoreCase))
                        return IntPtr.Zero;

                    try
                    {
                        string? exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty);
                        if (string.IsNullOrWhiteSpace(exeDir))
                            exeDir = AppDomain.CurrentDomain.BaseDirectory;

                        string fullPath = Path.Combine(exeDir, "T3ppNative.dll");
                        if (!File.Exists(fullPath))
                            return IntPtr.Zero;

                        return NativeLibrary.Load(fullPath);
                    }
                    catch
                    {
                        return IntPtr.Zero;
                    }
                });
            }
            catch
            {
                // ignore
            }

            DispatcherUnhandledException += (_, args) =>
            {
                try
                {
                    SessionLog.Write("EX", "DispatcherUnhandledException");
                    SessionLog.Write("EX", args.Exception.ToString());
                }
                catch { }
            };

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                try
                {
                    SessionLog.Write("EX", "AppDomain.UnhandledException");
                    if (args.ExceptionObject is Exception ex)
                        SessionLog.Write("EX", ex.ToString());
                    else
                        SessionLog.Write("EX", $"ExceptionObject={args.ExceptionObject}");
                }
                catch { }
            };

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                SessionLog.Shutdown();
            }
            catch
            {
                // ignore
            }

            base.OnExit(e);
        }
    }
}
