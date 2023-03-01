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
using NvAPIWrapper;

namespace Install3DV
{
    internal class Installer
    {
        // These are extracted during build process, so no need for 7z here.
        static string extracted3DFilesPath = Path.Combine(Directory.GetCurrentDirectory(), @"NVidia\3DVisionDriver");

        static int Main(string[] args)
        {
            if (!SanityCheck3DV())
            {
                Debug.WriteLine("Not an NVidia system.\n Driver cannot be installed.\n");
                return -1;
            }

            // Install 3D Vision Controller Driver for pyramid.

            // Add DCH fix file for 3D Vision installer. This
            // needs to be done before installer is run.
            FixDchDriverFor3dVision();

            // Patch 3D Vision DLL to match current driver version.
            Patch3DVDriverVersion();

            // Install the driver without UI.
            Run3DVInstaller();

            // Reset or change the 3DV control keys.
            Setup3DVParams();

            // Toggle 3D On.
            Enable3D();

            return 0;
        }

        // ------------------------------------------------------------------------------------------------

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

            Debug.WriteLine("Ver: {0}", NVIDIA.DriverVersion);

            return true;
        }

        // ------------------------------------------------------------------------------------------------

        // Fix a DCH driver so that 3DV can work. Without this Resource.dat file the
        // installer fails, and the file is no longer included in DCH builds. 
        // Requires admin privileges.
        public static void FixDchDriverFor3dVision()
        {
            string sourceFilePath = Path.Combine(Directory.GetCurrentDirectory(), @"NVidia\Resource.dat");
            string destFilePath = @"C:\ProgramData\NVIDIA\Resource.dat";

            string nvidiaDirectory = Path.GetDirectoryName(destFilePath)!;
            Directory.CreateDirectory(nvidiaDirectory);

            File.Copy(sourceFilePath, destFilePath, true);
        }

        // Patch the 3D Vision files so that they have a version that matches the
        // currently installed NVidia video driver. Otherwise installer will fail.
        private static void Patch3DVDriverVersion()
        {
            string _RcEditExe = Path.Combine(Directory.GetCurrentDirectory(), @"Tools\rcedit.exe");
            string[] _3DVisionDriverFiles = { "NvSCPAPI.dll", "NvSCPAPI64.dll" };
            string version = NVIDIA.DriverVersion.ToString();
            string str_version = "7.17.1" + version.Insert(1, ".");    // Bizarre formatting like 7.17.14.2531

            foreach (string driverFile in _3DVisionDriverFiles)
            {
                Debug.WriteLine("Patch {0} version into: {1}", version, driverFile);

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

            Debug.WriteLine("Starting 3D Vision setup: {0}", _3dVisionSetupPath);

            // Since this is an InstallShield app, we can pass the "/s" flag for a silent install.
            // This is what the full driver install does.  That leaves 3D installed, but disabled. 
            // But also does not run the setup wizard, which we don't want anyway.

            Process proc = new Process();
            proc.StartInfo.Arguments = "/s";
            proc.StartInfo.FileName = _3dVisionSetupPath;
            proc.Start();

            proc.WaitForExit();
        }

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