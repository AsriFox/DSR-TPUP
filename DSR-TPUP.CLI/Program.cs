using CommandLine;
using DSR_TPUP.Core;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TeximpNet.DDS;

namespace DSR_TPUP.CLI
{
    [Verb("unpack", HelpText = "Dump all of the game\'s textures to the specified directory")]
    public class UnpackArgs
    {
        [Option('g', "game-dir", Required = true, HelpText = "Directory with the game executable")]
        public string? GameDir { get; set; }

        [Option('o', "output-dir", Required = true, HelpText = "Dump output directory")]
        public string? OutputDir { get; set; }

        [Option('y', "confirm-delete", Default = false, Required = false, HelpText = "Skip confirmation when deleting output directory")]
        public bool ConfirmDelete { get; set; }

        [Option('j', "jobs", Default = 0, Required = false, HelpText = "Number of working threads")]
        public int Threads { get; set; }

        public int Run()
        {
            string unpackDir;
            try
            {
                unpackDir = Path.GetFullPath(OutputDir ?? throw new NullReferenceException("Required argument \'output-dir\' must not be null"));
            }
            catch (ArgumentException)
            {
                Console.Error.Write("Invalid output path:\n" + OutputDir + "\n");
                return 1;
            }

            if (!Directory.Exists(GameDir))
            {
                Console.Error.Write("Game directory not found:\n" + GameDir + "\n");
                return 1;
            }

            if (Directory.Exists(OutputDir))
            {
                if (!ConfirmDelete)
                {
                    Console.Write("WARNING! The contents of this directory will be deleted:\n" + OutputDir + "\nProceed? [y/N] ");
                    string? confirm = Console.ReadLine();
                    if (confirm == "y" || confirm == "")
                        ConfirmDelete = true;
                }
                if (!ConfirmDelete)
                {
                    Console.WriteLine("Aborted.");
                    return 0;
                }
                try
                {
                    Console.WriteLine("Deleting unpack directory...");
                    Directory.Delete(unpackDir, true);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Console.Error.Write(
                        "Unpack directory could not be deleted. Try running as Administrator.\n"
                        + "Reason: " + ex.Message + "\n");
                    return 1;
                }
            }
            try
            {
                Directory.CreateDirectory(unpackDir);
                string testFilePath = unpackDir + Path.PathSeparator + "tpup_test.txt";
                File.WriteAllText(testFilePath, "Test file to see if TPUP can write to this directory.");
                File.Delete(testFilePath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.Error.Write(
                    "Unpack directory could not be written to. Try running as Administrator.\n"
                    + "Reason: " + ex.Message + "\n");
                return 1;
            }

            TPUP tpup = Main.Unpack(GameDir, unpackDir, Program.Threads(Threads));
            return Program.Run(tpup);
        }
    }

    [Verb("repack", HelpText = "Repack any files with textures in the override directory")]
    public class RepackArgs
    {
        [Option('g', "game-dir", Required = true, HelpText = "Directory with the game executable")]
        public string? GameDir { get; set; }

        [Option('o', "override-dir", Required = true, HelpText = "File overrides directory")]
        public string? OverrideDir { get; set; }

        [Option('p', "preserve-converted", Default = false, Required = false, HelpText = "Automatically converted textures will not be cleaned up afterwards")]
        public bool PreserveConverted { get; set; }

        [Option('j', "jobs", Default = 0, Required = false, HelpText = "Number of working threads")]
        public int Threads { get; set; }

        public int Run()
        {
            if (!Directory.Exists(GameDir))
            {
                Console.Error.Write("Game directory not found: " + GameDir + "\n");
                return 1;
            }
            else if (!Directory.Exists(OverrideDir))
            {
                Console.Error.Write("Override directory not found: " + OverrideDir + "\n");
                return 1;
            }
            else
            {
                try
                {
                    File.WriteAllText(OverrideDir + "\\tpup_test.txt",
                        "Test file to see if TPUP can write to this directory.");
                    File.Delete(OverrideDir + "\\tpup_test.txt");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Console.Error.Write(
                        "Repack directory could not be written to. Try running as Administrator.\n"
                        + "Reason: " + ex.Message + "\n");
                    return 1;
                }

                TPUP tpup = Main.Repack(GameDir, OverrideDir, Program.Threads(Threads), PreserveConverted);
                Console.CancelKeyPress += tpup.ConsoleCancel;
                return Program.Run(tpup);
            }
        }
    }

    [Verb("convert", HelpText = "Convert the specified file to the specified format")]
    public class ConvertArgs
    {
        [Value(0, MetaName = "filename", Required = true, HelpText = "File to be converted")]
        public string? FileName { get; set; }

        [Option('f', "format", Required = true, HelpText = "Output DXGI format")]
        public DXGIFormat Format { get; set; }

        public int Run()
        {
            try
            {
                Main.Convert(FileName, Format).Wait();
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.Write(ex.Message + "\r\n");
                return 1;
            }
        }
    }

    [Verb("restore", HelpText = "Restore all backups in the game directory")]
    public class RestoreArgs
    {
        [Option('g', "game-dir", Required = true, HelpText = "Directory with the game executable")]
        public string? GameDir { get; set; }

        public int Run()
        {
            try
            {
                uint found = Main.Restore(GameDir);
                if (found > 0)
                    Console.WriteLine(found + " backups restored.");
                else
                    Console.WriteLine("No backups found.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.Write(ex.Message + "\n");
                return 1;
            }
        }
    }

    class Program
    {
        public static int Threads(int jobs)
        {
            if (jobs <= 0 || jobs > System.Environment.ProcessorCount)
                return System.Environment.ProcessorCount;
            else
                return jobs;
        }

        public static int Run(TPUP tpup)
        {
            void printLogs()
            {
                while (tpup.Log.TryDequeue(out string? line))
                    Console.WriteLine(line);
                while (tpup.Error.TryDequeue(out string? line))
                    Console.WriteLine(line);
            }

            Thread tpupThread = new Thread(tpup.Start);
            tpupThread.Start();
            while (tpupThread.IsAlive)
                printLogs();
            tpupThread.Join();
            printLogs();
            return tpup.Error.Count == 0 ? 0 : 1;
        }

        static int Main(string[] args)
        {
            return Parser.Default.ParseArguments<UnpackArgs, RepackArgs, ConvertArgs, RestoreArgs>(args)
                .MapResult(
                    (UnpackArgs args) => args.Run(),
                    (RepackArgs args) => args.Run(),
                    (ConvertArgs args) => args.Run(),
                    (RestoreArgs args) => args.Run(),
                    errs => 1);
        }
    }
}