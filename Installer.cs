// Installer to set up 3D Vision Driver to match the currently running
// Nvidia driver version. This will allow DX9 stereo support to work.
//
// Assumptions:
//  No support necessary for any version of Windows except Win10.
//  Only DCH drivers are expected and supported.
//  Drivers older or equal to 452.06 are not supported.

using System.Diagnostics;
using NvAPIWrapper;

namespace MyApp // Note: actual namespace depends on the project name.
{
    internal class Installer
    {
        static int Main(string[] args)
        {
            if (!SanityCheck3DV())
            {
                MessageBox.Show("Not an NVidia system.\n Driver cannot be installed.\n");
                return -1;
            }

            // Install 3D Vision Controller Driver for pyramid.

            // Add DCH fix file for 3D Vision installer. This
            // needs to be done before installer is run.
            //FixDchDriverFor3dVision();

            // Extract 3D Vision installer files, because we need to tweak
            // individual files.
            Extract3DVDriver();

            // Patch 3D Vision DLL to match current driver version.
            Patch3DVDriverVersion();

            // Install the driver without UI.
            Run3DVInstaller();

            // Reset or change the 3DV control keys?

            // Toggle 3D On.

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
            if (false)
                return false;

            // Doesn't run on anything non-NVidia
            if (!IsNVidiaGPU())
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
            string sourceFilePath = Path.Combine(Directory.GetCurrentDirectory(), @"Resource.dat");
            string destFilePath = @"C:\ProgramData\NVIDIA\Resource.dat";

            string nvidiaDirectory = Path.GetDirectoryName(destFilePath);
            Directory.CreateDirectory(nvidiaDirectory);

            File.Copy(sourceFilePath, destFilePath, true);
        }

        // Extract all files from the 3DVision.exe, because we need to patch file versions.
        private static void Extract3DVDriver()
        {
            string sevenZipPath = Path.Combine(Directory.GetCurrentDirectory(), @"Tools\7za.exe");
            string nvidia3DVExe = Path.Combine(Directory.GetCurrentDirectory(), @"NVidia\3DVision.exe");
            string nvidiaFilesPath = Path.Combine(Directory.GetCurrentDirectory(), @"NVidia3DVision");

            Process proc = new Process();
            proc.StartInfo.FileName = sevenZipPath;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory();
            proc.StartInfo.Arguments = "x " + "\"" + nvidia3DVExe + "\"" + " -o" + "\"" + nvidiaFilesPath + "\"" + " -y";

            proc.Start();
            proc.WaitForExit();
        }

        private static void Patch3DVDriverVersion()
        {
            throw new NotImplementedException();
        }

        private static void Run3DVInstaller()
        {
            string _3dVisionSetupPath =  Path.Combine(Directory.GetCurrentDirectory(), @"3DVision\setup.exe");

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

    }
}