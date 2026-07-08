// Installer to set up 3D Vision Driver to match the currently running
// Nvidia driver version. This will allow DX9 stereo support to work.
//
// Assumptions:
//  No support necessary for any version of Windows except Win10.
//  Only DCH drivers are expected and supported.
//  Drivers older or equal to 452.06 are not supported.

using System;
using System.Diagnostics;
using Microsoft.Win32;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using NvAPIWrapper;

namespace Install3DV
{
    internal class Installer
    {
        // These are extracted during build process, so no need for 7z here.
        static string extracted3DFilesPath = Path.Combine(Directory.GetCurrentDirectory(), @"3DVisionDriver");

        // Always prints, unlike Debug.WriteLine which is compiled out in Release.
        private static void Log(string format, params object?[] args)
        {
            string message = args.Length > 0 ? string.Format(format, args) : format;
            Console.WriteLine($"{message}");
        }

        // Only pause on failure, so the user can read what went wrong before the
        // window closes. Success exits immediately with a Ta-Da sound instead.
        private static int Fail(string message)
        {
            Log("-------Failure----------");
            Log(message);
            Log("------------------------");
            Log("");
            Console.Write("Press Enter to close this window...");
            Console.ReadLine();
            return -1;
        }

        static int Main(string[] args)
        {
            Console.WriteLine("=========================");
            Console.WriteLine("   3D Vision Installer   ");
            Console.WriteLine("=========================");
            Console.WriteLine();
            Console.WriteLine("Install 3D Vision Driver on any driver after 425.31.");
            Console.WriteLine("Works on DCH drivers, and fixes Control Panel stalls.");
            Console.WriteLine();

            if (!Is3DVisionDriverAvailable())
                return Fail($"3D Vision Driver files not found at: {extracted3DFilesPath}\nMake sure the 3DVisionDriver folder is present next to this installer.");

            if (!SanityCheck3DV())
                return Fail("This system does not support 3D Vision (no NVIDIA GPU detected, or driver version not supported). Nothing was installed.");

            (string Description, Action Step)[] steps =
            { // Maybe emitter driver?
                ("Applying DCH fix for 3D Vision installer", FixDchDriverFor3dVision),
                ("Patching 3D Vision driver version", Patch3DVDriverVersion),
                ("Installing 3D Vision driver", Run3DVInstaller),
                ("Fixing NVidia Control Panel stalls", FixControlPanelStalls),
                ("Setting up 3D Vision parameters", Setup3DVParams),
                ("Enabling 3D", Enable3D),
            };

            for (int i = 0; i < steps.Length; i++)
            {
                Log("");
                Log("[{0}/{1}] {2}...", i + 1, steps.Length, steps[i].Description);
                try
                {
                    steps[i].Step();
                }
                catch (Exception ex)
                {
                    return Fail($"Installation FAILED at step {i + 1}/{steps.Length}: {steps[i].Description}\n{ex}");
                }
            }

            Log("Installation complete — 3D Vision is enabled.");
            PlaySuccessSound();
            Thread.Sleep(3000);
            return 0;
        }

        // Two simple beeps to signal a successful install.
        private static void PlaySuccessSound()
        {
            //Console.Beep(523, 50);
            Console.Beep(784, 150);
        }

        // ------------------------------------------------------------------------------------------------

        // The 3DVisionDriver folder is extracted next to this exe during the build/publish
        // step; if it's missing, every later step would fail anyway, so check up front.
        private static bool Is3DVisionDriverAvailable()
        {
            return Directory.Exists(extracted3DFilesPath) &&
                File.Exists(Path.Combine(extracted3DFilesPath, "setup.exe"));
        }

        // We want double check that the current system is capable of
        // doing 3DV. No point in installing if it won't work.
        private static bool SanityCheck3DV()
        {
            // If it's a laptop, 3DV can't work on the main screen because
            // of some freaking stupid choice NVidia made. It can work on
            // an external screen though. TODO: hard to check for main screen.
            //if (false)
            //    return false;

            // Doesn't run on anything non-NVidia
            if (!IsNVidiaGPU())
                return false;

            // Don't run on old drivers that are supported by 3D vision,
            // and don't need version matching.
            if (NVIDIA.DriverVersion <= 42531)
                return false;

            return true;
        }

        private static bool IsNVidiaGPU()
        {
            try
            {
                NVIDIA.Initialize();
            }
            catch (Exception)
            {
                return false;
            }

            float ver = (NVIDIA.DriverVersion / 100f);
            Log("NVidia Driver Version: {0}", ver);

            return true;
        }

        // ------------------------------------------------------------------------------------------------

        // Fix a DCH driver so that 3DV can work. Without this Resource.dat file the
        // installer fails, and the file is no longer included in DCH builds. 
        // Requires admin privileges.
        public static void FixDchDriverFor3dVision()
        {
            string sourceFilePath = Path.Combine(Directory.GetCurrentDirectory(), @"Extras\Resource.dat");
            string destFilePath = @"C:\ProgramData\NVIDIA\Resource.dat";

            string nvidiaDirectory = Path.GetDirectoryName(destFilePath)!;
            Directory.CreateDirectory(nvidiaDirectory);

            File.Copy(sourceFilePath, destFilePath, true);
        }

        // Patch the 3D Vision files so that they have a version that matches the
        // currently installed NVidia video driver. Otherwise installer will fail.
        private static void Patch3DVDriverVersion()
        {
            string _RcEditExe = Path.Combine(Directory.GetCurrentDirectory(), @"Extras\rcedit.exe");
            string[] _3DVisionDriverFiles = { "NvSCPAPI.dll", "NvSCPAPI64.dll" };
            string version = NVIDIA.DriverVersion.ToString();
            string str_version = "7.17.1" + version.Insert(1, ".");  // Bizarre formatting like 7.17.14.2531 for 425.31

            foreach (string driverFile in _3DVisionDriverFiles)
            {
                Log("Patch {0} version into: {1}", version, driverFile);

                string filePath = Path.Combine(extracted3DFilesPath, driverFile);

                Process process = new Process();
                process.StartInfo.FileName = _RcEditExe;
                process.StartInfo.WorkingDirectory = Path.GetDirectoryName(_RcEditExe);
                process.StartInfo.Arguments = "\"" + filePath + "\"" +
                                                " --set-product-version " + "\"" + str_version + "\"" +
                                                " --set-file-version " + "\"" + str_version + "\"";
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.UseShellExecute = false;
                process.Start();
                process.WaitForExit();
            }
        }

        private static void Run3DVInstaller()
        {
            string _3dVisionSetupPath = Path.Combine(extracted3DFilesPath, @"setup.exe");

            Log("Starting 3D Vision setup: {0}", _3dVisionSetupPath);

            // Since this is an InstallShield app, we can pass the "/s" flag for a silent install.
            // This is what the full driver install does.  That leaves 3D installed, but disabled.
            // But also does not run the setup wizard, which we don't want anyway.

            Process proc = new Process();
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.Arguments = "/s";
            proc.StartInfo.FileName = _3dVisionSetupPath;
            proc.Start();

            Log("This can take up to a minute; please wait...");
            proc.WaitForExit();
        }

        // ------------------------------------------------------------------------------------------------

        // The NVidia Control Panel talks to a stereo server over the named pipe
        // \\.\pipe\stereosvrpipe. That server (nvstapisvr64.dll) runs as a plugin
        // inside the "NVIDIA Display Container LS" service. Drivers after 452.06
        // dropped 3D Vision, so the 3DV setup no longer manages to deploy the
        // plugin (the PluginFolderPath registry breadcrumb it needs is gone), and
        // every stereo call in the control panel stalls for seconds waiting on a
        // pipe that nobody serves.
        //
        // We deploy the plugin ourselves: find the container's plugin folder from
        // its service command line, and copy the server DLLs in with the
        // underscore prefix that marks them as optional plugins. The container
        // watches that folder and starts the pipe server within a second or two,
        // no reboot needed.
        //
        // The plugin folder lives in the DriverStore and is owned by SYSTEM with
        // no write access for Administrators, so we have to take ownership, add
        // ourselves, copy, then put the ACL back the way we found it.
        private static void FixControlPanelStalls()
        {
            // Already deployed and running (e.g. a re-run of this installer) - the
            // plugin DLL is locked by the container while loaded, so copying over
            // it again would throw. Nothing to fix if the pipe is already up.
            if (File.Exists(@"\\.\pipe\stereosvrpipe"))
            {
                Log("Stereo pipe server is already running; nothing to do.");
                return;
            }

            string? imagePath = (string?)Registry.LocalMachine
                .OpenSubKey(@"SYSTEM\CurrentControlSet\Services\NVDisplay.ContainerLocalSystem")?
                .GetValue("ImagePath");
            if (imagePath == null)
            {
                Log("No NVDisplay.ContainerLocalSystem service, cannot fix stalls.");
                return;
            }

            // Plugin folder is the -d argument, quoted or bare depending on driver.
            Match match = Regex.Match(imagePath, @"-d\s+(?:""(?<dir>[^""]+)""|(?<dir>\S+))");
            if (!match.Success || !Directory.Exists(match.Groups["dir"].Value))
            {
                Log("No container plugin folder found in: {0}", imagePath);
                return;
            }
            string pluginDir = match.Groups["dir"].Value;

            Log("Installing stereo pipe server into: {0}", pluginDir);

            EnablePrivilege("SeTakeOwnershipPrivilege");
            EnablePrivilege("SeRestorePrivilege");

            DirectoryInfo dir = new DirectoryInfo(pluginDir);
            SecurityIdentifier admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            SecurityIdentifier system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

            // Inheritable, so the copied DLLs pick it up too and a re-run can overwrite them.
            FileSystemAccessRule adminsFullControl = new FileSystemAccessRule(admins, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow);

            DirectorySecurity acl = dir.GetAccessControl();
            acl.SetOwner(admins);
            acl.AddAccessRule(adminsFullControl);
            dir.SetAccessControl(acl);

            try
            {
                // 32-bit DLL only loads on a 32-bit OS, but deploy both like NVidia does.
                File.Copy(Path.Combine(extracted3DFilesPath, "nvstapisvr.dll"), Path.Combine(pluginDir, "_nvstapisvr.dll"), true);
                File.Copy(Path.Combine(extracted3DFilesPath, "nvstapisvr64.dll"), Path.Combine(pluginDir, "_nvstapisvr64.dll"), true);
            }
            finally
            {
                acl = dir.GetAccessControl();
                acl.RemoveAccessRule(adminsFullControl);
                acl.SetOwner(system);
                dir.SetAccessControl(acl);
            }

            // The container notices the new plugin on its own; just confirm.
            for (int i = 0; i < 20; i++)
            {
                if (File.Exists(@"\\.\pipe\stereosvrpipe"))
                {
                    Console.WriteLine();
                    Log("Stereo pipe server is up.");
                    return;
                }
                Console.Write(".");
                Thread.Sleep(500);
            }
            Console.WriteLine();
            Log("Stereo pipe server did not start; control panel stalls will remain until reboot.");
        }

        // Elevated processes hold these privileges but disabled; ownership
        // changes need them switched on for this process.
        private static void EnablePrivilege(string privilege)
        {
            LUID luid = new LUID();
            LookupPrivilegeValue(null, privilege, ref luid);

            TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
            OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr token);
            AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }

        const uint SE_PRIVILEGE_ENABLED = 0x00000002;
        const uint TOKEN_ADJUST_PRIVILEGES = 0x00000020;
        const uint TOKEN_QUERY = 0x00000008;

        [StructLayout(LayoutKind.Sequential)]
        struct LUID { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential)]
        struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID Luid; public uint Attributes; }

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool LookupPrivilegeValue(string? systemName, string name, ref LUID luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges, ref TOKEN_PRIVILEGES newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);

        // ------------------------------------------------------------------------------------------------

        // Set up dictionary with a.. stereo3DKeyNames and their default values
        // as they would be after wizard finishes.
        public static Dictionary<string, uint> DefaultStereoKeys = new Dictionary<string, uint>()
        {
            {"LaserSightEnabled", 1},
            {"MonitorSize", 9999},
            {"SnapShotQuality", 50},
            {"StereoAdvancedHKConfig", 0},
            {"StereoImageType", 0},
            {"StereoRefreshRate", 0},
            {"StereoSeparation", 15},
            {"StereoDefaultOn", 1},
            {"EnableWindowedMode", 5},
            //{"StereoIROutput", 3},
            //{"StereoFlywheelCycleState", 0},          
            {"LaserSightProperty", 4010061924},
            {"LaserSightIndex", 0},
            {"StereoAnaglyphType", 1},
            {"LeftAnaglyphFilter", 4294901760},
            {"RightAnaglyphFilter", 4278255615},
                           
            // Hotkeys
            {"CycleFrustumAdjust", 634},
            {"SaveStereoImage", 1136},
            {"StereoConvergenceAdjustLess", 628},
            {"StereoConvergenceAdjustMore", 629},
            {"StereoSeparationAdjustLess", 626},
            {"StereoSeparationAdjustMore", 627},
            {"StereoToggle", 596},
            {"StereoToggleMode", 1658},
            {"ToggleLaserSight", 635},
            {"ToggleMemo", 3629},
            {"WriteConfig", 630}
        };

        // Set up all the 3DV params that we care about, that are normally set up by the
        // 3DV Wizard. We don't want to run the wizard, it's faster to just silently set these.
        //
        // Not exactly sure why, but we need to use the Registry32, as Registry64 returns null,
        // even on x64 system.
        private static void Setup3DVParams()
        {
            string nvidiaKeyPath = @"SOFTWARE\NVIDIA Corporation\Global\Stereo3D";
            RegistryKey nvidiaRoot = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(nvidiaKeyPath, RegistryKeyPermissionCheck.ReadWriteSubTree)!;

            foreach (var stereoKey in DefaultStereoKeys)
            {
                nvidiaRoot.SetValue(stereoKey.Key, (int)stereoKey.Value, RegistryValueKind.DWord);
            }

            // Tweak a few like 3DVision.bat preferences.
            nvidiaRoot.SetValue("StereoSeparation", 20, RegistryValueKind.DWord);
            nvidiaRoot.SetValue("StereoAdvancedHKConfig", 1, RegistryValueKind.DWord);
            nvidiaRoot.SetValue("LaserSightEnabled", 0, RegistryValueKind.DWord);
            nvidiaRoot.SetValue("SnapShotQuality", 85, RegistryValueKind.DWord);
            nvidiaRoot.SetValue("EnableWindowedMode", 5, RegistryValueKind.DWord);

            // Mark the medical tests as complete.
            nvidiaRoot.SetValue("StereoVisionConfirmed", 1, RegistryValueKind.DWord);

            // Change from default Discover red/blue 3D.
            nvidiaRoot.SetValue("StereoViewerType", 1, RegistryValueKind.DWord);
        }

        private static void Enable3D()
        {
            string enable3DPath = @"C:\Program Files (x86)\NVIDIA Corporation\3D Vision\nvstlink.exe";
            Process proc = new Process();
            proc.StartInfo.FileName = enable3DPath;
            proc.StartInfo.Arguments = "/enable";
            proc.StartInfo.WorkingDirectory = Path.GetDirectoryName(enable3DPath);
            proc.Start();
            proc.WaitForExit();
        }
    }
}