using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace JRunner.Classes
{
    internal class EnableDevGL
    {
        /// <summary>
        /// Holds the result of a single pattern match: the offset where the
        /// pattern was found and the byte section read from that offset.
        /// </summary>
        public class MatchResult
        {
            /// <summary>Absolute byte offset in the file where the pattern was found.</summary>
            public long Offset { get; }

            /// <summary>
            /// Up to 0x390 bytes read from <see cref="Offset"/>.
            /// May be shorter if the match is near the end of the file.
            /// </summary>
            public byte[] Data { get; }

            public MatchResult(long offset, byte[] data)
            {
                Offset = offset;
                Data = data;
            }

            public override string ToString() =>
                $"Offset: 0x{Offset:X8}  Length: 0x{Data.Length:X4} ({Data.Length}) bytes";
        }

        // DevGL key header
        private static readonly byte[] SearchPattern = new byte[]
        {
            0x20, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };

        // Hash of the DevGL key as contained within the SDK
        private static readonly byte[] SearchHash = new byte[]
        {
            0x57, 0xEE, 0x20, 0x83, 0xE6, 0x2C, 0xEC, 0x0C,
            0x3E, 0x1A, 0x97, 0xE3, 0x30, 0x91, 0xFC, 0x24,
            0x8C, 0x82, 0x6F, 0x46, 0xBB, 0x46, 0x5C, 0x91,
            0x42, 0x97, 0xB0, 0xF5, 0x60, 0x66, 0xF7, 0x64
        };

        // Hash of the DevGL key as needed by XeBuild
        private static readonly byte[] DemangledHash = new byte[]
        {
            0xCC, 0xAA, 0x61, 0x54, 0x69, 0x64, 0xCE, 0xEA,
            0xBE, 0x61, 0xA9, 0xDD, 0x37, 0x61, 0x26, 0x4E,
            0xA0, 0x8C, 0xA8, 0x0F, 0x1A, 0xA6, 0xB6, 0x3A,
            0xBA, 0x58, 0xDB, 0xB4, 0x75, 0x82, 0x58, 0x48
        };

        private const int SectionLength = 0x390;

        /// <summary>
        /// Searches the given file for all occurrences of the target hex pattern,
        /// then reads SectionLength (0x390) bytes from each match offset.
        /// </summary>
        /// <param name="filePath">Path to the file to search.</param>
        /// <returns>
        /// A list of <see cref="MatchResult"/> objects, one per match, each
        /// carrying the absolute file offset and up to 0x390 bytes of data.
        /// </returns>
        public static List<MatchResult> FindSections(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("File not found.", filePath);

            byte[] fileBytes = File.ReadAllBytes(filePath);
            List<long> matchOffsets = FindAllPatternOffsets(fileBytes, SearchPattern);

            var results = new List<MatchResult>(matchOffsets.Count);

            foreach (long offset in matchOffsets)
            {
                int remaining = fileBytes.Length - (int)offset;
                int readLength = Math.Min(SectionLength, remaining);

                byte[] section = new byte[readLength];
                Buffer.BlockCopy(fileBytes, (int)offset, section, 0, readLength);
                results.Add(new MatchResult(offset, section));
            }

            return results;
        }

        /// <summary>
        /// Returns the start offset of every occurrence of <paramref name="pattern"/>
        /// inside <paramref name="data"/> using a simple sliding-window search.
        /// </summary>
        private static List<long> FindAllPatternOffsets(byte[] data, byte[] pattern)
        {
            var offsets = new List<long>();
            int limit = data.Length - pattern.Length;

            for (int i = 0; i <= limit; i++)
            {
                if (IsMatch(data, i, pattern))
                    offsets.Add(i);
            }

            return offsets;
        }

        /// <summary>Checks whether <paramref name="pattern"/> starts at position
        /// <paramref name="pos"/> inside <paramref name="data"/>.</summary>
        private static bool IsMatch(byte[] data, int pos, byte[] pattern)
        {
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[pos + j] != pattern[j])
                    return false;
            }
            return true;
        }

        public static byte[] DemangleKey(byte[] input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.Length < 8) throw new ArgumentException("Input must be at least 8 bytes.");

            byte[] result = new byte[input.Length];
            Buffer.BlockCopy(input, 0, result, 0, input.Length);

            // Reverse every 8-byte chunk
            for (int i = 0; i + 8 <= result.Length; i += 8)
            {
                Array.Reverse(result, i, 8);
            }

            // Swap the first two DWORDs (bytes 0-3 and bytes 4-7)
            uint dword0 = BitConverter.ToUInt32(result, 0);
            uint dword1 = BitConverter.ToUInt32(result, 4);

            Buffer.BlockCopy(BitConverter.GetBytes(dword1), 0, result, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(dword0), 0, result, 4, 4);

            return result;
        }

        public static string extractContentDllFileFromExe(string exePath, string workingFolder)
        {
            Process p = new Process();
            ProcessStartInfo psi = new ProcessStartInfo();

            psi.FileName = @"common\7z\7za.exe";
            psi.UseShellExecute = false;
            psi.Arguments = $"e -o\"{workingFolder}\\\" -y \"{exePath}\" XDK\\bin\\win32\\content.dll";
            psi.CreateNoWindow = true;
            p.StartInfo = psi;

            if (variables.debugMode) Console.WriteLine($"Enable DevGL: 7-Zip command line ({psi.FileName} {psi.Arguments})");

            p.Start();

            p.WaitForExit();

            if (p.ExitCode != 0)
            {
                if (variables.debugMode) Console.WriteLine($"Enable DevGL Error: 7-Zip failed with code {p.ExitCode}");
                return "";
            }

            return Path.Combine(workingFolder,"content.dll");
        }

        public static bool enableDevGL(string contentDllPath)
        {
            SHA256 sha256 = SHA256.Create();
            string sbPrivBinPath = Path.Combine(variables.rootfolder, @"xebuild\common\" + "SB_priv.bin");

            // Check and see if SB_priv.bin was provided
            if (contentDllPath.EndsWith("bin") && (new FileInfo(contentDllPath).Length) == 0x390)
            {
                byte[] binData = File.ReadAllBytes(contentDllPath);
                byte[] binHash = sha256.ComputeHash(binData);

                if (binHash.SequenceEqual(DemangledHash))
                {
                    // User provided the actual SB_priv.bin. Copy it to the correct path
                    File.Copy(contentDllPath, sbPrivBinPath, true);
                    return true;
                }
                else
                {
                    Console.WriteLine("Invalid key: " + contentDllPath);
                    return false;
                }
            }

            foreach (MatchResult key in FindSections(contentDllPath))
            {
                if (variables.debugMode) Console.WriteLine("Found key: " + key.ToString());

                byte[] keyHash = sha256.ComputeHash(key.Data);

                if (keyHash.SequenceEqual(SearchHash))
                {
                    // The hash of the key we've found matches what we expect to find
                    if (variables.debugMode) Console.WriteLine($"Key at 0x{key.Offset:X} matches expected hash");

                    // Demangle the key into the form that XeBuild expects
                    byte[] demangledKey = DemangleKey(key.Data);

                    // Write the file to the right directory such that DevGL will function correctly
                    File.WriteAllBytes(sbPrivBinPath, demangledKey);
                    if (variables.debugMode) Console.WriteLine("Enable DevGL: Key written to " + sbPrivBinPath);

                    return true;
                }
            }

            Console.WriteLine("Key not found in " + contentDllPath);
            return false;
        }
    }
}
