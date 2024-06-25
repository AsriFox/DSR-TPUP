using Octokit;
using Semver;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using TeximpNet.DDS;

namespace DSR_TPUP.Core
{
    public static class Main
    {
        public static readonly string UPDATE_LINK = "https://github.com/AsriFox/DSR-TPUP/releases";

        /// TODO: use Microsoft.Extensions.Configuration package
        // private static Properties.Settings settings = Properties.Settings.Default;

        // public static void ReadSettings() {}
        // public static void WriteSettings() {}

        public static readonly List<DXGIFormat> DXGI_FORMATS_COMMON = new List<DXGIFormat>() {
            DXGIFormat.BC1_UNorm,
            DXGIFormat.BC2_UNorm,
            DXGIFormat.BC3_UNorm,
            DXGIFormat.BC5_UNorm,
            DXGIFormat.BC7_UNorm,
        };

        public static List<DXGIFormat> SortFormatsCustom()
        {
            List<DXGIFormat> inOrder = new List<DXGIFormat>();
            foreach (DXGIFormat format in Enum.GetValues(typeof(DXGIFormat)))
                if (!DXGI_FORMATS_COMMON.Contains(format) && format != DXGIFormat.Unknown)
                    inOrder.Add(format);

            inOrder.Sort((f1, f2) => TPUP.PrintDXGIFormat(f1).CompareTo(TPUP.PrintDXGIFormat(f2)));
            return inOrder;
        }

        public static async Task<(bool, string)?> CheckForUpdates(string currentVersion)
        {
            GitHubClient client = new GitHubClient(new ProductHeaderValue("DSR-TPUP"));
            try
            {
                Release release = await client.Repository.Release.GetLatest("JKAnderson", "DSR-TPUP");
                var latest = SemVersion.Parse(release.TagName, SemVersionStyles.Any);
                var current = SemVersion.Parse(currentVersion, SemVersionStyles.Any);
                return (latest.ComparePrecedenceTo(current) > 0, release.TagName);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is ApiException || ex is ArgumentException) { }
            return null;
        }

        public static TPUP Unpack(string gameDir, string unpackDir, int threadCount)
        {
            if (!Directory.Exists(gameDir))
                throw new ArgumentException("Game directory not found: " + gameDir);

            if (!Directory.Exists(unpackDir))
                throw new ArgumentException("Output directory not found: " + unpackDir);

            return new TPUP(gameDir, unpackDir, false, false, threadCount);
        }

        public static TPUP Repack(string gameDir, string repackDir, int threadCount, bool preserveConverted)
        {
            if (!Directory.Exists(gameDir))
                throw new ArgumentException("Game directory not found: " + gameDir);

            if (!Directory.Exists(repackDir))
                throw new ArgumentException("Override directory not found: " + repackDir);

            return new TPUP(gameDir, repackDir, true, preserveConverted, threadCount);
        }

        public static async Task Convert(string filename, DXGIFormat format)
        {
            /// TODO: use DirectXTexNet package
            if (!File.Exists("bin\\texconv.exe"))
                throw new ArgumentException("texconv.exe not found");

            string filepath = Path.GetFullPath(filename);
            if (!File.Exists(filepath))
                throw new ArgumentException("File to be converted does not exist: " + filename);

            bool backedUp = false;
            if (Path.GetExtension(filepath) == ".dds" && !File.Exists(filepath + ".bak"))
            {
                File.Copy(filepath, filepath + ".bak");
                backedUp = true;
            }

            string args = string.Format("-f {0} -o \"{1}\" \"{2}\" -y",
                TPUP.PrintDXGIFormat(format), Path.GetDirectoryName(filepath), filepath);
            ProcessStartInfo startInfo = new ProcessStartInfo("bin\\texconv.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = true,
                RedirectStandardOutput = true
            };
            Process texconv = Process.Start(startInfo);
            await Task.Run(texconv.WaitForExit);
            
            if (texconv.ExitCode == 0)
                return;
            if (backedUp)
                File.Move(filepath + ".bak", filepath);
            throw new Exception(string.Format("Conversion failed with code {0}", texconv.ExitCode));
        }

        public static uint Restore(string gameDir)
        {
            if (!Directory.Exists(gameDir))
                throw new ArgumentException("Game directory not found: " + gameDir);

            uint found = 0;
            foreach (string filepath in Directory.GetFiles(gameDir, "*.tpupbak", SearchOption.AllDirectories))
            {
                string newPath = Path.GetDirectoryName(filepath) + Path.PathSeparator + Path.GetFileNameWithoutExtension(filepath);
                if (File.Exists(newPath))
                    File.Delete(newPath);
                File.Move(filepath, newPath);
                found++;
            }
            return found;
        }
    }
}