using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace JRunner.Classes
{
    class xebuild
    {
        public enum XebuildError
        {
            none = 0,
            nocpukey,
            nofile,
            filemissing,
            nobinfile,
            nodash,
            noconsole,
            noinis,
            nobootloaders,
            wrongcpukey
        }

        private bool success = false;
        private string _cpukey;
        private variables.hacktypes _ttype;
        private string _dash;
        private consoles _ctype;
        private bool _audclamp;
        private bool _CR4;
        private bool _SMCP;
        private bool _CleanSMC;
        private bool _rgh3;
        private bool _bigffs;
        private bool _zfuse;
        private bool _xdkbuild;
        private bool _fullDataClean;
        private bool _altoptions;
        private bool _DLpatches;
        private bool _includeLaunch;
        private bool _rjtag;
        private bool _nowrite;
        private bool _noava;
        private bool _clean;
        private bool _noreeb;
        private bool _xlusb;
        private bool _xlhdd;
        private bool _xlboth;
        private bool _usbdsec;
        private bool _coronakeyfix;
        private Nand.PrivateN _nand;
        private List<string> _patches;

        // Methods for extracting the different sections of the input
        // xeBuild patch data
        //
        // xeBuild patch files contain multiple sections, with a DWORD of
        // 0xFFFFFFFF as the section terminator.
        //
        // 1st section is CB/CB_B/SB patches
        //
        // 2nd section is CD/SD patches
        //
        // 3rd section is the kernel patches, applied by the patching engine
        // in the SD to the kernel. This is the SE in a devkit image, or the
        // CE after CF/CG patching has taken place. XeBuild drops these as-is
        // in to the NAND image 
        //
        // The terminating 0xFFFFFFFF is included in each patch section
        //
        public static int getLengthOfPatchSection(byte[] bytes, int offset)
        {
            // Stop at the last DWORD of the byte array, no point searching further
            for (int i = offset; i < bytes.Length - 3; i++)
            {
                if (bytes[i] == 0xFF &&
                    bytes[i + 1] == 0xFF &&
                    bytes[i + 2] == 0xFF &&
                    bytes[i + 3] == 0xFF)
                {
                    return i + 4 - offset;
                }
            }

            return -1;
        }

        public static byte[] return2blPatchSet(byte[] xeBuildPatchData)
        {
            int sbPatchOffset = 0;
            int sbPatchLength = getLengthOfPatchSection(xeBuildPatchData, sbPatchOffset);

            if(sbPatchLength == -1)
            {
                return new byte[0];
            }

            return xeBuildPatchData.Take(sbPatchLength).ToArray();
        }

        public static byte[] return4blPatchSet(byte[] xeBuildPatchData)
        {
            int sbPatchOffset = 0;
            int sbPatchLength = getLengthOfPatchSection(xeBuildPatchData, sbPatchOffset);

            if (sbPatchLength == -1)
            {
                return new byte[0];
            }

            int sdPatchOffset = sbPatchOffset + sbPatchLength;
            int sdPatchLength = getLengthOfPatchSection(xeBuildPatchData, sdPatchOffset);

            if (sdPatchLength == -1)
            {
                return new byte[0];
            }

            return xeBuildPatchData.Skip(sdPatchOffset).Take(sdPatchLength).ToArray();
        }

        public static byte[] returnKernelHvPatchSet(byte[] xeBuildPatchData)
        {
            int sbPatchOffset = 0;
            int sbPatchLength = getLengthOfPatchSection(xeBuildPatchData, sbPatchOffset);

            if (sbPatchLength == -1)
            {
                return new byte[0];
            }

            int sdPatchOffset = sbPatchOffset + sbPatchLength;
            int sdPatchLength = getLengthOfPatchSection(xeBuildPatchData, sdPatchOffset);

            if (sdPatchLength == -1)
            {
                return new byte[0];
            }

            int khvPatchOffset = sdPatchOffset + sdPatchLength;
            int khvPatchLength = getLengthOfPatchSection(xeBuildPatchData, khvPatchOffset);

            if (khvPatchLength == -1)
            {
                return new byte[0];
            }

            return xeBuildPatchData.Skip(khvPatchOffset).Take(khvPatchLength).ToArray();
        }

        /// <summary>
        /// Prepares bootloader data for CRC calculation in the same way xeBuild
        /// does, e.g.
        /// - File is truncated to the DWORD size at 0xC
        /// - CB/CB_A/CB_B: fill with zeros from 0x10 for 0x30 bytes
        /// - SC/CD/SD/CE/SE/CG: fill with zeros from 0x10 for 0x10 bytes
        /// - CF: fill with zeros from 0x20 for 0x210 bytes
        /// 
        /// We don't need to look at the first byte for this, second
        /// byte is enough to tell if it's 2BL, 3BL, etc.
        /// 
        /// </summary>
        /// <param name="bl"></param>
        /// <returns></returns>
        private static byte[] prepBlForCrcCalc(byte[] bl)
        {
            int length = Oper.ByteArrayToInt(Oper.returnportion(bl, 0xC, 4));
            if ( bl[1] == 0x42 ) //CB, SB
            {
                for (int i = 0x10; i < 0x40; i++) bl[i] = 0x0;
            }
            else if ( bl[1] == 0x43 || // SC
                      bl[1] == 0x44 || // CD/SD
                      bl[1] == 0x45 || // CE/SE
                      bl[1] == 0x47 )  // CG
            {
                for (int i = 0x10; i < 0x20; i++) bl[i] = 0x0;
            }
            else if (bl[1] == 0x46) // CF
            {
                for (int i = 0x20; i < 0x230; i++) bl[i] = 0x0;
            }
            return Oper.returnportion(bl, 0, length);
        }

        /// <summary>
        /// Calculates the CRC32 of a bootloader in the same way xeBuild
        /// does for the purposes of integrity verification
        /// </summary>
        /// <param name="filename">Path to a BL file</param>
        /// <returns></returns>
        public static long calculateBlCrc(string filename)
        {
            return calculateBlCrc(File.ReadAllBytes(filename));
        }

        /// <summary>
        /// Calculates the CRC32 of a bootloader in the same way xeBuild
        /// does for the purposes of integrity verification
        /// </summary>
        /// <param name="blData">Byte buffer containing BL data</param>
        /// <returns></returns>
        public static long calculateBlCrc(byte[] blData)
        {
            byte[] processedBl = prepBlForCrcCalc(blData);

            crc32 crc = new crc32();

            return crc.CRC(processedBl);
        }

        public static byte[] patchBootloader(byte[] blData, byte[] patchData)
        {
            // xeBuild patch file format is the following:
            //
            // DWORD patch address
            // DWORD patch size
            // patch bytes
            //
            // repeated until an address of 0xFFFFFFFF is found
            Stream s = new MemoryStream(patchData);

            byte[] addressBytes = new byte[4];
            UInt32 address = 0;

            byte[] lengthBytes = new byte[4];
            UInt32 length = 0;

            byte[] patchSectionBytes = { };

            while(0 != s.Read(addressBytes, 0, 4))
            {
                address = BitConverter.ToUInt32(addressBytes.Reverse().ToArray(), 0);

                if (addressBytes[0] == 0xFF &&
                    addressBytes[1] == 0xFF &&
                    addressBytes[2] == 0xFF &&
                    addressBytes[3] == 0xFF)
                {
                    // We've found the terminating 0xFF, stop looping
                    break;
                }

                s.Read(lengthBytes, 0, 4);
                length = BitConverter.ToUInt32(lengthBytes.Reverse().ToArray(), 0) * 4;

                Array.Resize(ref patchSectionBytes, (int)length);
                s.Read(patchSectionBytes, 0, (int)length);

                // If the patch address and length would make us run off 
                // the end of the buffer we need to resize the buffer to fit.
                if (address + length > blData.Length)
                {
                    Array.Resize(ref blData, (int)(address + length));
                }

                Buffer.BlockCopy(patchSectionBytes, 0, blData, (int)address, (int)length);
            }

            // We've finished patching the bootloader. In case the patch made it an odd size
            // extend it to the nearest multiple of 0x10 bytes.
            int paddedLen = (blData.Length + 0xF) & ~0xF;

            if(paddedLen > blData.Length)
            {
                Array.Resize(ref blData, paddedLen);
            }

            // Set the size of the bootloader at 0xC 
            byte[] sizeBytes = BitConverter.GetBytes(paddedLen).Reverse().ToArray();
            Buffer.BlockCopy(sizeBytes, 0, blData, 0xC, 4);

            // We're all done!
            return blData;
        }

        public static bool devgl64PreBuildActions(string boardtype, string iniFilePath, string xeBuildPatchFilePath, string cpukey)
        {
            byte[] xeBuildPatchFileBytes = { };
            byte[] xeBuildSdPatchSectionBytes = { };
            byte[] xeBuildKernelHvPatchSectionBytes = { };

            try
            {
                xeBuildPatchFileBytes = File.ReadAllBytes(xeBuildPatchFilePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("DevGL image creation error: couldn't read patch file " + xeBuildPatchFilePath);
                if (variables.debugMode) Console.WriteLine(ex.ToString());
                return false;
            }

            xeBuildSdPatchSectionBytes = return4blPatchSet(xeBuildPatchFileBytes);
            xeBuildKernelHvPatchSectionBytes = returnKernelHvPatchSet(xeBuildPatchFileBytes);

            // Grab the board type from the ini file
            string[] iniLines = File.ReadAllLines(iniFilePath);
            int iniSdEntryLine = 0;

            for (int i = 0; i < iniLines.Length; i++)
            {
                // xeBuild ini format for devkit/devgl goes like this:
                // [{boardtype}bl]
                // SB
                // SC
                // CD/SD
                // CE/SE
                // CF
                // CG
                // 
                // The 2nd index line returned *should* be our SD
                if (iniLines[i] == "[" + boardtype + "bl]")
                {
                    iniSdEntryLine = i + 3;
                }
            }

            if (iniSdEntryLine == 0)
            {
                Console.WriteLine("DevGL image creation error: couldn't find [" + boardtype + "bl] section in " + iniFilePath);
                return false;
            }

            // Split it by the comma so we get the filename and CRC
            string[] sdLine = iniLines[iniSdEntryLine].Split(',');

            if (variables.debugMode) Console.WriteLine("SD expected pre-patching: " + iniLines[iniSdEntryLine]);

            string sdFileName = sdLine[0];
            long sdIniCrc = Convert.ToInt64(sdLine[1], 16);

            if(sdFileName.StartsWith("PATCH"))
            {
                Console.WriteLine("DevGL image creation error: can't patch an already-patched BL. Restore " + iniFilePath + " from a backup!");
                return false;
            }

            // Now, search for the SD in one of two places:
            // - In the same directory as the ini (likely here)
            // - In the xeBuild\common folder (though for a devkit SD it's probably not there)
            string sdFilePath = Path.Combine(Path.GetDirectoryName(iniFilePath), sdFileName);

            if (!File.Exists(sdFilePath))
            {
                // Okay, it's not in the same directory as the ini
                // Lets try the xeBuild common folder just in case
                sdFilePath = Path.Combine(variables.rootfolder, @"xeBuild\common\" + sdFileName);

                if (!File.Exists(sdFilePath))
                {
                    // It's not in one of the two places it could be.
                    // We can't continue
                    Console.WriteLine("DevGL image creation error: couldn't find " + sdFileName);
                    return false;
                }
            }

            // SD exists, calculate the CRC so we know we're starting from a known good binary
            long sdFileCrc = calculateBlCrc(sdFilePath);

            if (variables.debugMode) Console.WriteLine("SD calculated pre-patching: " + sdFileCrc.ToString("x"));

            if (sdFileCrc != sdIniCrc)
            {
                Console.WriteLine("DevGL image creation error: " + sdFileName + " integrity check failed.");
                Console.WriteLine("Calculated CRC: " + sdFileCrc.ToString("x"));
                Console.WriteLine("Expected CRC: " + sdIniCrc.ToString("x"));
                return false;
            }

            // We've got a good CRC, do the bootloader patch
            byte[] patchedBlData = { };

            try
            {
                patchedBlData = patchBootloader(File.ReadAllBytes(sdFilePath), xeBuildSdPatchSectionBytes);
            }
            catch (Exception ex)
            {
                Console.WriteLine("DevGL image creation error: couldn't patch " + sdFileName);
                if (variables.debugMode) Console.WriteLine(ex.ToString());
                return false;
            }

            // Now, recalculate the CRC and generate the string for the ini
            string patchedSdFileName = "PATCH_" + sdFileName;
            long patchedCRC = calculateBlCrc(patchedBlData);
            string patchedIniString = patchedSdFileName + "," + patchedCRC.ToString("x");

            if (variables.debugMode) Console.WriteLine("SD calculated post-patching: " + patchedIniString);

            // Change the line in the ini file
            iniLines[iniSdEntryLine] = patchedIniString;

            // Write the BL to the dashboard folder
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(iniFilePath), patchedSdFileName), patchedBlData);

            // Write the new ini file
            File.WriteAllLines(iniFilePath, iniLines);

            // We've patched the bootloader, now generate fuses+kernel patch binary file
            byte[] vfuseAndKernelPatchData = new byte[0x60 + xeBuildKernelHvPatchSectionBytes.Length];

            // bytes we use for making up the vfuses
            // Fuseline 0 and 1 are always the same for a devkit
            // 3/4 and 5/6 make up the CPU key
            // CB LDV and CF/CG LDV can be left blank
            byte[] fuseline0 = { 0xC0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
            byte[] fuseline1_dev = { 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F };
            byte[] fuseline3_4 = Oper.StringToByteArray(cpukey.Substring(0, 16));
            byte[] fuseline5_6 = Oper.StringToByteArray(cpukey.Substring(16, 16));

            // Build the fake fuseset
            Buffer.BlockCopy(fuseline0, 0, vfuseAndKernelPatchData, 0, 0x8);
            Buffer.BlockCopy(fuseline1_dev, 0, vfuseAndKernelPatchData, 0x8, 0x8);
            Buffer.BlockCopy(fuseline3_4, 0, vfuseAndKernelPatchData, 0x18, 0x8);
            Buffer.BlockCopy(fuseline3_4, 0, vfuseAndKernelPatchData, 0x20, 0x8);
            Buffer.BlockCopy(fuseline5_6, 0, vfuseAndKernelPatchData, 0x28, 0x8);
            Buffer.BlockCopy(fuseline5_6, 0, vfuseAndKernelPatchData, 0x30, 0x8);

            // Build the vfuse/kernel patch section
            Buffer.BlockCopy(xeBuildKernelHvPatchSectionBytes, 0, vfuseAndKernelPatchData, 0x60, xeBuildKernelHvPatchSectionBytes.Length);

            // Write the file to disk so xeBuild can pick it up
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(iniFilePath), "vfuses_khv.bin"), vfuseAndKernelPatchData);

            // XeBuild does not set the XeLL startup reason for a devkit image.
            // For the SD patches to know when to jump to XeLL, it must be set
            // manually and can be patched in to the NAND image. Here, the XeLL
            // startup reason is being set to 0x00 (ignore) and 0x12 (eject)
            #region valid xell startup reasons
            // IGNORE          = 0x00
            // POWER           = 0x11
            // EJECT           = 0x12
            // UNDOCUMENTED_15 = 0x15
            // UNDOCUMENTED_16 = 0x16
            // REMOPOWER       = 0x20
            // UNDOCUMENTED_21 = 0x21
            // REMOX           = 0x22
            // WINBUTTON       = 0x24
            // UNDOCUMENTED_30 = 0x30
            // UNDOCUMENTED_31 = 0x31
            // KIOSK           = 0x41
            // WIRELESSX       = 0x55
            // WIREDXF1        = 0x56
            // WIREDXF2        = 0x57
            // WIREDXB2        = 0x58
            // WIREDXB1        = 0x59
            // WIREDXB3        = 0x5A
            #endregion

            byte[] xellStartupReason = { 0x00, 0x12 };
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(iniFilePath), "xell_reason.bin"), xellStartupReason);

            return true;
        }

        public static void devgl64PostBuildActions(string iniFilePath)
        {
            // Delete all the patches we created in the pre-build step
            string khv_path = Path.Combine(Path.GetDirectoryName(iniFilePath), "vfuses_khv.bin");
            string xell_reason_path = Path.Combine(Path.GetDirectoryName(iniFilePath), "xell_reason.bin");

            if(File.Exists(khv_path)) File.Delete(khv_path);
            if(File.Exists(xell_reason_path)) File.Delete(xell_reason_path);

            // When building a 64mb DevGL NAND, we need to manually zero-pair the SB as the last step
            Nand.Nand.zeroPairDevkitSb(Path.Combine(variables.xefolder, variables.updflash), true);
        }

        public void loadvariables(string cpukey, variables.hacktypes ttype, string dash, consoles ctype, List<string> patches, Nand.PrivateN nand, bool altoptions, bool DLpatches, bool includeLaunch, bool audclamp, bool rjtag, bool cleansmc, bool cr4, bool smcp, bool rgh3, bool bigffs, bool zfuse, bool xdkbuild, bool xlusb, bool xlhdd, bool xlboth, bool usbdsec, bool coronakeyfix, bool fullDataClean)
        {
            this._cpukey = cpukey;
            this._ttype = ttype;
            this._dash = dash;
            this._ctype = ctype;
            this._patches = patches;
            this._nand = nand;
            this._altoptions = altoptions;
            this._DLpatches = DLpatches;
            this._includeLaunch = includeLaunch;
            this._audclamp = audclamp;
            this._rjtag = rjtag;
            this._CleanSMC = cleansmc;
            this._CR4 = cr4;
            this._SMCP = smcp;
            this._rgh3 = rgh3;
            this._bigffs = bigffs;
            this._zfuse = zfuse;
            this._xdkbuild = xdkbuild;
            this._xlusb = xlusb;
            this._xlhdd = xlhdd;
            this._xlboth = xlboth;
            this._usbdsec = usbdsec;
            this._coronakeyfix = coronakeyfix;
            this._fullDataClean = fullDataClean;
        }

        public List<int> getCBs()
        {
            List<int> cbs = new List<int>();
            string[] ommit = { "version", "security", "flashfs" };
            foreach (string s in parse_ini.getlabels(Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\_" + _ttype + ".ini")))
            {
                if (!ommit.Contains(s) && s.Contains(_ctype.Ini))
                {
                    int cb;
                    if (int.TryParse(s.Replace(_ctype.Ini + "bl_", ""), out cb)) cbs.Add(cb);
                }
            }
            //int cba;
            //string normalc = (parse_ini.parselabel(Path.Combine(variables.pathforit, @"xeBuild\" + _dash + @"\_" + _ttype + ".ini"), _ctype.Ini + "bl")[0]);
            //string normalcb = normalc.Substring(normalc.IndexOf("_") + 1, normalc.IndexOf(".") - normalc.IndexOf("_") - 1);
            //if (int.TryParse(normalcb, out cba)) cbs.Add(cba);

            return cbs;
        }

        void copySMC()
        {
            if (_ttype == variables.hacktypes.jtag && !File.Exists(Path.Combine(variables.xepath, "SMC.bin")))
            {
                if (_ctype.ID == 7 || _ctype.ID == 8)
                {
                    File.Copy(variables.xepath + "SMCx.bin", variables.xepath + "SMC.bin", true);
                }
                else
                {
                    if (_audclamp) File.Copy(variables.xepath + "SMCaud.bin", variables.xepath + "SMC.bin", true);
                    else File.Copy(variables.xepath + "SMCfzj.bin", variables.xepath + "SMC.bin", true);
                }

                if (_rjtag)
                {
                    File.WriteAllBytes(variables.xepath + "SMC.bin", Nand.Nand.patch_SMC((File.ReadAllBytes(variables.xepath + "SMC.bin"))));
                }
                variables.copiedSMC = true;
            }
            else if (_ttype != variables.hacktypes.jtag && _CleanSMC)
            {
                if (_ctype.ID == 1 || _ctype.ID == 12)
                {
                    File.Copy(variables.xepath + "TRINITY_CLEAN.bin", variables.xepath + "SMC.bin", true);
                }
                else if (_ctype.ID == 2 || _ctype.ID == 14)
                {
                    File.Copy(variables.xepath + "FALCON_CLEAN.bin", variables.xepath + "SMC.bin", true);
                }
                else if (_ctype.ID == 3 || _ctype.ID == 13)
                {
                    File.Copy(variables.xepath + "ZEPHYR_CLEAN.bin", variables.xepath + "SMC.bin", true);
                }
                else if (_ctype.ID == 4 || _ctype.ID == 5 || _ctype.ID == 6)
                {
                    File.Copy(variables.xepath + "JASPER_CLEAN.bin", variables.xepath + "SMC.bin", true);
                }
                else if (_ctype.ID == 7 || _ctype.ID == 8)
                {
                    File.Copy(variables.xepath + "XENON_CLEAN.bin", variables.xepath + "SMC.bin", true);
                }
                else if (_ctype.ID == 9 ||_ctype.ID == 10 || _ctype.ID == 11)
                {
                    File.Copy(variables.xepath + "CORONA_CLEAN.bin", variables.xepath + "SMC.bin", true);
                }
                else if (_ctype.ID == 15 ||_ctype.ID == 16 || _ctype.ID == 17)
                {
                    File.Copy(variables.xepath + "WINCHESTER_CLEAN.bin", variables.xepath + "SMC.bin", true);
                }
                variables.copiedSMC = true;
            }
            else if ((_ttype == variables.hacktypes.glitch2 || _ttype == variables.hacktypes.glitch2m) && _CR4)
            {
                if (_ctype.ID == 1 || _ctype.ID == 12)
                {
                    File.Copy(variables.xepath + "TRINITY_CR4.bin", variables.xepath + "SMC.bin", true);
                }
                else if (_ctype.ID == 2 || _ctype.ID == 3 || _ctype.ID == 13 || _ctype.ID == 14)
                {
                    File.Copy(variables.xepath + "FALCON_CR4.bin", variables.xepath + "SMC.bin", true);
                }
                else if (_ctype.ID == 4 || _ctype.ID == 5 || _ctype.ID == 6)
                {
                    File.Copy(variables.xepath + "JASPER_CR4.bin", variables.xepath + "SMC.bin", true);
                }
                else if (_ctype.ID == 9 || _ctype.ID == 10 || _ctype.ID == 11)
                {
                    File.Copy(variables.xepath + "CORONA_CR4.bin", variables.xepath + "SMC.bin", true);
                }
                variables.copiedSMC = true;
            }
            else if ((_ttype == variables.hacktypes.glitch2 || _ttype == variables.hacktypes.glitch2m) && _SMCP)
            {
                if (_ctype.ID == 1 || _ctype.ID == 12)
                {
                    File.Copy(variables.xepath + "TRINITY_SMC+.bin", variables.xepath + "SMC.bin", true);
                }
                else if (_ctype.ID == 2 || _ctype.ID == 3 || _ctype.ID == 13 || _ctype.ID == 14)
                {
                    File.Copy(variables.xepath + "FALCON_SMC+.bin", variables.xepath + "SMC.bin", true);
                }
                else if (_ctype.ID == 4 || _ctype.ID == 5 || _ctype.ID == 6)
                {
                    File.Copy(variables.xepath + "JASPER_SMC+.bin", variables.xepath + "SMC.bin", true);
                }
                else if (_ctype.ID == 9 || _ctype.ID == 10 || _ctype.ID == 11)
                {
                    File.Copy(variables.xepath + "CORONA_SMC+.bin", variables.xepath + "SMC.bin", true);
                }
                variables.copiedSMC = true;
            }
            else
            {
                variables.copiedSMC = false;
            }
        }
        void copySMCcustom()
        {
            if (_ttype == variables.hacktypes.jtag)
            {
                if (_ctype.ID == 7 || _ctype.ID == 8)
                {
                    File.Copy(variables.xepath + "SMCx.bin", variables.xepath + "SMC.bin", true);
                }
                else
                {
                    if (_audclamp) File.Copy(variables.xepath + "SMCaud.bin", variables.xepath + "SMC.bin", true);
                    else File.Copy(variables.xepath + "SMCfzj.bin", variables.xepath + "SMC.bin", true);
                }

                if (_rjtag)
                {
                    File.WriteAllBytes(variables.xepath + "SMC.bin", Nand.Nand.patch_SMC((File.ReadAllBytes(variables.xepath + "SMC.bin"))));
                }
                variables.copiedSMC = true;
            }
            else if (_ttype != variables.hacktypes.jtag && _CleanSMC)
            {
                if (_ctype.ID == 1 || _ctype.ID == 12)
                {
                    File.Copy(variables.xepath + "TRINITY_CLEAN.bin", variables.xepath + "SMC.bin", true);
                }
                else if (_ctype.ID == 2 || _ctype.ID == 14)
                {
                    File.Copy(variables.xepath + "FALCON_CLEAN.bin", variables.xepath + "SMC.bin", true);
                }
                else if (_ctype.ID == 3 || _ctype.ID == 13)
                {
                    File.Copy(variables.xepath + "ZEPHYR_CLEAN.bin", variables.xepath + "SMC.bin", true);
                }
                else if (_ctype.ID == 4 || _ctype.ID == 5 || _ctype.ID == 6)
                {
                    File.Copy(variables.xepath + "JASPER_CLEAN.bin", variables.xepath + "SMC.bin", true);
                }
                else if (_ctype.ID == 7 || _ctype.ID == 8)
                {
                    File.Copy(variables.xepath + "XENON_CLEAN.bin", variables.xepath + "SMC.bin", true);
                }
                else if (_ctype.ID == 9 || _ctype.ID == 10 || _ctype.ID == 11)
                {
                    File.Copy(variables.xepath + "CORONA_CLEAN.bin", variables.xepath + "SMC.bin", true);
                }
                else if (_ctype.ID == 15 || _ctype.ID == 16 || _ctype.ID == 17)
                {
                    File.Copy(variables.xepath + "WINCHESTER_CLEAN.bin", variables.xepath + "SMC.bin", true);
                }
                variables.copiedSMC = true;
            }
            else if ((_ttype == variables.hacktypes.glitch2 || _ttype == variables.hacktypes.glitch2m) && _CR4)
            {
                if (_ctype.ID == 1 || _ctype.ID == 12)
                {
                    File.Copy(variables.xepath + "TRINITY_CR4.bin", variables.xepath + "SMC.bin", true);
                }
                else if (_ctype.ID == 2 || _ctype.ID == 3 || _ctype.ID == 13 || _ctype.ID == 14)
                {
                    File.Copy(variables.xepath + "FALCON_CR4.bin", variables.xepath + "SMC.bin", true);
                }
                else if (_ctype.ID == 4 || _ctype.ID == 5 || _ctype.ID == 6)
                {
                    File.Copy(variables.xepath + "JASPER_CR4.bin", variables.xepath + "SMC.bin", true);
                }
                else if (_ctype.ID == 9 || _ctype.ID == 10 || _ctype.ID == 11)
                {
                    File.Copy(variables.xepath + "CORONA_CR4.bin", variables.xepath + "SMC.bin", true);
                }
                variables.copiedSMC = true;
            }
            else if ((_ttype == variables.hacktypes.glitch2 || _ttype == variables.hacktypes.glitch2m) && _SMCP)
            {
                if (_ctype.ID == 1 || _ctype.ID == 12)
                {
                    File.Copy(variables.xepath + "TRINITY_SMC+.bin", variables.xepath + "SMC.bin", true);
                }
                else if (_ctype.ID == 2 || _ctype.ID == 3 || _ctype.ID == 13 || _ctype.ID == 14)
                {
                    File.Copy(variables.xepath + "FALCON_SMC+.bin", variables.xepath + "SMC.bin", true);
                }
                else if (_ctype.ID == 4 || _ctype.ID == 5 || _ctype.ID == 6)
                {
                    File.Copy(variables.xepath + "JASPER_SMC+.bin", variables.xepath + "SMC.bin", true);
                }
                else if (_ctype.ID == 9 || _ctype.ID == 10 || _ctype.ID == 11)
                {
                    File.Copy(variables.xepath + "CORONA_SMC+.bin", variables.xepath + "SMC.bin", true);
                }
                variables.copiedSMC = true;
            }
            else
            {
                variables.copiedSMC = false;
            }
        }

        private void copyXLUsb()
        {
            if (File.Exists(Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\xl_usb\xam.xex")))
            {
                if (File.Exists(Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\xam.xex")))
                {
                    File.Move(Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\xam.xex"), Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\xam.xex.tmp"));
                }

                File.Copy(Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\xl_usb\xam.xex"), Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\xam.xex"), true);

                string buildIni = Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\_" + variables.ttyp.ToString() + ".ini");
                string xlUsbIni = Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\xl_usb\_" + variables.ttyp.ToString() + ".ini");
                if (File.Exists(xlUsbIni))
                {
                    if (File.Exists(buildIni))
                    {
                        File.Move(buildIni, buildIni + ".tmp");
                    }

                    File.Copy(xlUsbIni, buildIni, true);
                }

                variables.copiedXLDrive = true;
            }
            else
            {
                variables.copiedXLDrive = false;
            }
        }

        private void copyXLHdd()
        {
            if (File.Exists(Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\xl_hdd\xam.xex")))
            {
                if (File.Exists(Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\xam.xex")))
                {
                    File.Move(Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\xam.xex"), Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\xam.xex.tmp"));
                }

                File.Copy(Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\xl_hdd\xam.xex"), Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\xam.xex"), true);

                string buildIni = Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\_" + variables.ttyp.ToString() + ".ini");
                string xlHddIni = Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\xl_hdd\_" + variables.ttyp.ToString() + ".ini");
                if (File.Exists(xlHddIni))
                {
                    if (File.Exists(buildIni))
                    {
                        File.Move(buildIni, buildIni + ".tmp");
                    }

                    File.Copy(xlHddIni, buildIni, true);
                }

                variables.copiedXLDrive = true;
            }
            else
            {
                variables.copiedXLDrive = false;
            }
        }

        private void copyXLBoth()
        {
            if (File.Exists(Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\xl_both\xam.xex")))
            {
                if (File.Exists(Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\xam.xex")))
                {
                    File.Move(Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\xam.xex"), Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\xam.xex.tmp"));
                }

                File.Copy(Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\xl_both\xam.xex"), Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\xam.xex"), true);

                string buildIni = Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\_" + variables.ttyp.ToString() + ".ini");
                string xlBothIni = Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\xl_both\_" + variables.ttyp.ToString() + ".ini");
                if (File.Exists(xlBothIni))
                {
                    if (File.Exists(buildIni))
                    {
                        File.Move(buildIni, buildIni + ".tmp");
                    }

                    File.Copy(xlBothIni, buildIni, true);
                }

                variables.copiedXLDrive = true;
            }
            else
            {
                variables.copiedXLDrive = false;
            }
        }

        void checkDashLaunch()
        {
            if (_DLpatches && _includeLaunch)
            {
                if (!File.Exists(Path.Combine(variables.launchpath, _dash + @"\launch.ini")))
                {
                    if (File.Exists(Path.Combine(variables.launchpath, "launch.ini")))
                        System.IO.File.Copy(Path.Combine(variables.launchpath, "launch.ini"), Path.Combine(variables.launchpath, _dash + @"\launch.ini"), true);
                    else if (File.Exists(Path.Combine(variables.launchpath, "launch_default.ini")))
                        System.IO.File.Copy(Path.Combine(variables.launchpath, @"launch_default.ini"), Path.Combine(variables.launchpath, _dash + @"\launch.ini"), true);
                }
            }
            edittheini();
        }
        void edittheini()
        {
            if (variables.debugMode) Console.WriteLine(_dash);
            foreach (variables.hacktypes type in Enum.GetValues(typeof(variables.hacktypes)))
            {
                if (type == variables.hacktypes.retail || type == variables.hacktypes.nothing) continue;
                string file = Path.Combine(variables.rootfolder, @"xeBuild\" + _dash + @"\_" + type + ".ini");
                string[] writepatches = { @"..\launch.xex", @"..\lhelper.xex", @"..\launch.ini" };
                string[] writepatches2 = { @"..\launch.xex", @"..\lhelper.xex" };
                string[] empty = { };

                if (File.Exists(file))
                {
                    if (_DLpatches)
                    {
                        parse_ini.edit_ini(file, _includeLaunch ? writepatches : writepatches2, empty);
                    }
                    else
                    {
                        parse_ini.edit_ini(file, empty, writepatches);
                    }
                }
                else if (variables.debugMode) Console.WriteLine("Couldn't add dashlaunch patches to {0}", file);
            }
        }
        void savekvinfo(string savefile)
        {
            try
            {
                if (!_nand.ok) return;
                TextWriter tw = new StreamWriter(savefile);
                tw.WriteLine("*******************************************");
                tw.WriteLine("*******************************************");
                string console_type = _ctype.Text;
                tw.WriteLine("Console Type: {0}", console_type);
                tw.WriteLine("");
                tw.WriteLine("Cpu Key: {0}", _cpukey);
                tw.WriteLine("");
                tw.WriteLine("KV Type: {0}", _nand.ki.kvtype.Replace("0", ""));
                tw.WriteLine("");
                tw.WriteLine("MFR Date: {0}", _nand.ki.mfdate);
                tw.WriteLine("");
                tw.WriteLine("Console ID: {0}", _nand.ki.consoleid);
                tw.WriteLine("");
                tw.WriteLine("Serial: {0}", _nand.ki.serial);
                tw.WriteLine("");
                string region = "";
                if (_nand.ki.region == "02FE") region = "PAL/EU";
                else if (_nand.ki.region == "00FF") region = "NTSC/US";
                else if (_nand.ki.region == "01FE") region = "NTSC/JAP";
                else if (_nand.ki.region == "01FF") region = "NTSC/JAP";
                else if (_nand.ki.region == "01FC") region = "NTSC/KOR";
                else if (_nand.ki.region == "0101") region = "NTSC/HK";
                else if (_nand.ki.region == "0201") region = "PAL/AUS";
                else if (_nand.ki.region == "7FFF") region = "DEVKIT";
                tw.WriteLine("Region: {0} | {1}", _nand.ki.region, region);
                tw.WriteLine("");
                tw.WriteLine("Osig: {0}", _nand.ki.osig);
                tw.WriteLine("");
                tw.WriteLine("DVD Key: {0}", _nand.ki.dvdkey);
                tw.WriteLine("");
                tw.WriteLine("*******************************************");
                tw.WriteLine("*******************************************");
                tw.Close();
                Console.WriteLine("KV Info saved to file");
            }
            catch (Exception ex) { if (variables.debugMode) Console.WriteLine(ex.ToString()); Console.WriteLine("Failed"); Console.WriteLine(""); }
        }

        XebuildError doSomeChecks()
        {
            if (string.IsNullOrEmpty(_cpukey)) return XebuildError.nocpukey;

            if (_ctype.ID == -1) return XebuildError.noconsole;
            if (_dash.Equals("")) return XebuildError.nodash;
            string ini = (variables.launchpath + @"\" + _dash + @"\_" + _ttype + ".ini");

            // Type overrides, check doSomeChecks() if changing
            string boardtype = _ctype.Ini;
            string ctypebtldr = boardtype + "bl";
            if (!File.Exists(ini)) return XebuildError.noinis;
            if (!parse_ini.getlabels(ini).Contains(ctypebtldr)) return XebuildError.nobootloaders;

            return XebuildError.none;
        }
        void moveOptions()
        {
            if (_altoptions)
            {
                Console.WriteLine("Using edited settings");
                File.Copy(Path.Combine(variables.rootfolder, @"xebuild\options_edited.ini"), Path.Combine(variables.rootfolder, @"xebuild\data\options.ini"), true);
            }
            else
            {
                File.Copy(Path.Combine(variables.rootfolder, @"xebuild\options.ini"), Path.Combine(variables.rootfolder, @"xebuild\data\options.ini"), true);
            }

            // Don't patch the SMC reset limit if it's not a glitch
            // type image. JTAG doesn't reset the processor
            // and DevGL/Devkit/Testkit/Retail images boot directly
            // in to the kernel so if it takes more than one try
            // and the SMC is resetting, something is wrong and
            // we should be getting a red ring of some sort
            if( ! (_ttype == variables.hacktypes.glitch||
                   _ttype == variables.hacktypes.glitch2||
                   _ttype == variables.hacktypes.glitch2m) )
            {
                string[] dontPatchSmc = { "patchsmc=false" };
                string[] delete = { };
                parse_ini.edit_ini(Path.Combine(variables.rootfolder, @"xeBuild\data\options.ini"), dontPatchSmc, delete);
            }

        }

        public XebuildError createxebuild(bool custom)
        {
            XebuildError result = XebuildError.none;
            result = doSomeChecks();
            if (result != XebuildError.none) return result;
            moveOptions();
            if (variables.changeldv != 0)
            {
                string cfldv = "cfldv=" + variables.highldv.ToString();
                string[] edit = { cfldv };
                string[] delete = { };
                parse_ini.edit_ini(Path.Combine(variables.rootfolder, @"xeBuild\data\options.ini"), edit, delete);
            }

            Console.WriteLine("XeBuild Initialized");
            if (!custom) copySMC();
            else copySMCcustom();

            if (_xlusb) copyXLUsb();
            else if (_xlhdd) copyXLHdd();
            else if (_xlboth) copyXLBoth();

            variables.fullDataClean = _fullDataClean;

            checkDashLaunch();

            if (variables.changeldv != 0)
            {
                string cfldv = "cfldv=" + variables.highldv.ToString();
                string[] edit = { cfldv };
                string[] delete = { };
                parse_ini.edit_ini(Path.Combine(variables.rootfolder, @"xeBuild\data\options.ini"), edit, delete);
            }

            Console.WriteLine("Kernel Selected: {0}", _dash);

            variables.xefolder = Path.Combine(Directory.GetParent(variables.outfolder).FullName, _nand.ki.serial);
            if (variables.debugMode) Console.WriteLine("outfolder: {0}", variables.xefolder);
            if (!Directory.Exists(variables.xefolder)) Directory.CreateDirectory(variables.xefolder);
            File.WriteAllText(System.IO.Path.Combine(variables.xefolder, variables.cpukeypath), _cpukey);
            savekvinfo(Path.Combine(variables.xefolder, "KV_Info.txt"));
            if (variables.changeldv != 0) variables.changeldv = 2;

            return result;
        }

        private bool postBuildActionsAreRequired()
        {
            // So far, the following image types require post-build actions
            // any others should be added here to prevent errors and file conflicts
            //
            // 1) XDKBuild when the hack type is NOT DevGL
            // 2) Any type of RGH3 image
            // 3) DevGL images for console types 3 (64mb Xenon), 13 (64mb Zephyr), and 14 (64mb Falcon)
            //
            if( (_xdkbuild && _ttype != variables.hacktypes.devgl) ||
                _rgh3 ||
                isDevglFor64MbConsoles() ||
                isAffectedByXeBuildImageBug() )
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private bool isDevglFor64MbConsoles()
        {
            // If we've selected the following options, we're building
            // a 64mb DevGL image with the XeBuild devkit option
            //
            // 1) User selected DevGL
            // 2) User did NOT enable "devkit instead of devgl" mode
            // 3) The console is a 64mb xenon, zephyr, or falcon
            //
            if( _ttype == variables.hacktypes.devgl &&
                 false == variables.devkitnotdevgl &&
                 (_ctype.ID == 7 || _ctype.ID == 13 || _ctype.ID == 14) )
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private bool isAffectedByXeBuildImageBug()
        {
            // If we've selected the following options, we're building an image
            // that is affected by a bug in XeBuild that doesn't set the patch slot
            // size correctly and need to patch the resulting image.
            if ( (_ttype == variables.hacktypes.glitch2m || _ttype == variables.hacktypes.devgl) &&
                 ( _ctype.ID == 2 || _ctype.ID == 3 || _ctype.ID == 8 ) )
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public void build()
        {
            success = false;
            System.Diagnostics.Process pProcess = new System.Diagnostics.Process();
            pProcess.StartInfo.FileName = variables.rootfolder + @"\xeBuild\xeBuild.exe";
            string arguments = "";
            string boardtype = _ctype.XeBuild;
            string patchFileBaseName = "";
            string patchFilePath = "";
            string iniFilePath = "";
            string iniFileBackupPath = "";
            string[] iniFileContentsBackup = { };

            if (_ttype == variables.hacktypes.devgl)
            {
                if (isDevglFor64MbConsoles())
                {
                    Console.WriteLine("Building 64mb DevGL image using XeBuild devkit mode");

                    // To build a DevGL image for 64mb consoles successfully we need to
                    // ensure the patch file exists so the post-build patch step can inject it
                    patchFileBaseName = "patches_dev" + boardtype + ".bin";
                    patchFilePath = variables.rootfolder + @"\xeBuild\" + _dash + "\\bin\\" + patchFileBaseName;
                    if (!File.Exists(patchFilePath))
                    {
                        Console.WriteLine("Could not create 64mb DevGL image, " + patchFileBaseName + " for dashboard " + _dash + " missing.");
                        return;
                    }

                    // We also need to make sure the ini file exists, because we'll need to pre-patch the SD
                    iniFilePath = variables.rootfolder + @"\xeBuild\" + _dash + "\\_devkit.ini";
                    iniFileBackupPath = iniFilePath + ".bak";

                    if (!File.Exists(iniFilePath))
                    {
                        Console.WriteLine("Could not create 64mb DevGL image, _devgl.ini for dashboard " + _dash + " missing.");
                        return;
                    }

                    // Make a backup of the ini file contents before we patch the ini and run XeBuild
                    iniFileContentsBackup = File.ReadAllLines(iniFilePath);
                    File.WriteAllLines(iniFileBackupPath, iniFileContentsBackup);

                    if (!devgl64PreBuildActions(boardtype,iniFilePath,patchFilePath, _cpukey))
                    {
                        // If we failed to patch the SD and/or update the ini,
                        // restore the ini contents from the backup and then bail
                        File.WriteAllLines(iniFilePath, iniFileContentsBackup);
                        if (File.Exists(iniFileBackupPath)) File.Delete(iniFileBackupPath);

                        variables.xefinished = true;
                        MainForm.mainForm.xPanel.xeExitActual(false);
                        return;
                    }

                    arguments = "-t " + variables.hacktypes.devkit;
                }
                else if (variables.devkitnotdevgl)
                {
                    Console.WriteLine("Using devkit image type instead of DevGL");
                    arguments = "-t " + variables.hacktypes.devkit;
                }
                else
                {
                    arguments = "-t " + variables.hacktypes.devgl;
                }
            }
            else
            {
                arguments = "-t " + _ttype;
            }

            if (_xdkbuild)
            {
                if (boardtype == "jasperbb") // requires bigffs
                {
                    arguments += " -c " + "jasperbigffs -i flash";
                }
                else if (boardtype == "trinitybb") // requires bigffs
                {
                    arguments += " -c " + "trinitybigffs -i flash";
                }
                else if (boardtype == "coronabb") // requires bigffs
                {
                    arguments += " -c " + "coronabigffs -i flash";
                }
                else if (boardtype == "corona4g")
                {
                    arguments += " -c " + boardtype + " -i flash";
                }
                else if (boardtype == "winchesterbb") // requires bigffs
                {
                    arguments += " -c " + "winchesterbigffs -i flash";
                }
                else if (boardtype == "winchester4g")
                {
                    arguments += " -c " + boardtype + " -i flash";
                }
                else // no bigffs!
                {
                    arguments += " -c " + boardtype;
                }
            }
            else if (_bigffs)
            {
                if (boardtype == "jasperbb")
                {
                    arguments += " -c " + "jasperbigffs";
                }
                else if (boardtype == "trinitybb")
                {
                    arguments += " -c " + "trinitybigffs";
                }
                else if (boardtype == "coronabb")
                {
                    arguments += " -c " + "coronabigffs";
                }
                else if (boardtype == "winchesterbb")
                {
                    arguments += " -c " + "winchesterbigffs";
                }
                else
                {
                    arguments += " -c " + boardtype + "bigffs";
                }
            }
            else
            {
                arguments += " -c " + boardtype;
            }

            if (_zfuse && _ttype == variables.hacktypes.devgl)
            {
                arguments += " -a hvfixkeys";
            }

            if (_xlusb) arguments += " -a xl_usb";
            else if (_xlhdd) arguments += " -a xl_hdd";
            else if (_xlboth) arguments += " -a xl_both";

            if (_usbdsec) arguments += " -a usbdsec";
            if (_coronakeyfix) arguments += " -a corona_key_fix";

            foreach (String patch in _patches)
            {
                arguments += " " + patch;
            }
            if (variables.debugMode) arguments += " -v";
            arguments += " -noenter";
            arguments += " -f " + _dash;
            arguments += " -d data";
            arguments += " \"" + variables.xefolder + "\\" + variables.updflash + "\" ";

            RegexOptions options = RegexOptions.None;
            Regex regex = new Regex(@"[ ]{2,}", options);
            arguments = regex.Replace(arguments, @" ");

            if (variables.debugMode) Console.WriteLine(variables.rootfolder);
            if (variables.debugMode) Console.WriteLine("---" + variables.rootfolder + @"\xeBuild\xeBuild.exe");
            if (variables.debugMode) Console.WriteLine(arguments);
            pProcess.StartInfo.Arguments = arguments;
            pProcess.StartInfo.UseShellExecute = false;
            pProcess.StartInfo.WorkingDirectory = variables.rootfolder;
            pProcess.StartInfo.RedirectStandardInput = true;
            pProcess.StartInfo.RedirectStandardOutput = true;
            pProcess.StartInfo.CreateNoWindow = true;
            if (!postBuildActionsAreRequired()) pProcess.Exited += new EventHandler(xeExit);
            //pProcess.OutputDataReceived += new System.Diagnostics.DataReceivedEventHandler(DataReceived);
            //pProcess.Exited += new EventHandler(xe_Exited);
            //pProcess.OutputDataReceived += new System.Diagnostics.DataReceivedEventHandler(process_OutputDataReceived);
            try
            {
                AutoResetEvent outputWaitHandle = new AutoResetEvent(false);
                pProcess.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data == null)
                    {
                        outputWaitHandle.Set();
                    }
                    else
                    {
                        Console.WriteLine(e.Data);
                        if (e.Data != null && e.Data.Contains("image built")) {
                            success = true;
                            if (!postBuildActionsAreRequired())
                            {
                                variables.xefinished = true;
                            }
                        }
                    }
                };
                pProcess.Start();
                pProcess.BeginOutputReadLine();
                pProcess.StandardInput.WriteLine("enter");
                pProcess.WaitForExit();

                if (pProcess.HasExited)
                {
                    pProcess.CancelOutputRead();
                }

                //
                // Do any mandatory post build actions here
                //
                if (isDevglFor64MbConsoles())
                {
                    // Now that XeBuild is done, we can restore the contents of the
                    // ini file and delete the backup. We have to do this regardless
                    // of whether the build succeeded or failed
                    File.WriteAllLines(iniFilePath, iniFileContentsBackup);
                    if (File.Exists(iniFileBackupPath)) File.Delete(iniFileBackupPath);
                }

                //
                // Any post-build actions on success here
                //
                // Ensure the postBuildActionsAreRequired
                // function is updated if anything is added
                //
                if (success && postBuildActionsAreRequired())
                {
                    // For DevGL and Glitch2m, there is a bug in XeBuild that breaks falcon board types.
                    // To fix the image, we need to set the DWORD at 0x70 in NAND (the patch slot address)
                    // otherwise the CB/CD/CE patches won't be able to find the vfuses
                    if(isAffectedByXeBuildImageBug())
                    {
                        Nand.Nand.fixBuggyXeBuildImage(Path.Combine(variables.xefolder, variables.updflash));
                    }

                    if (_xdkbuild && _rgh3)
                    {
                        MainForm.mainForm.XDKbuild.create(boardtype, true);
                        MainForm.mainForm.rgh3Build.create(_ctype.Text, "00000000000000000000000000000000", true);
                    }
                    else if (_xdkbuild && _ttype != variables.hacktypes.devgl)
                    {
                        MainForm.mainForm.XDKbuild.create(boardtype);
                    }
                    else if (_ttype == variables.hacktypes.glitch2m && _rgh3)
                    {
                        MainForm.mainForm.rgh3Build.create(_ctype.Text, "00000000000000000000000000000000", true);
                    }    
                    else if (_rgh3)
                    {
                        MainForm.mainForm.rgh3Build.create(_ctype.Text, _cpukey, true);
                    }
                    else if (isDevglFor64MbConsoles())
                    {
                        devgl64PostBuildActions(iniFilePath);
                    }
                    else
                    {
                        variables.xefinished = true;
                        MainForm.mainForm.xPanel.xeExitActual();
                    }
                }
            }
            catch (Exception objException)
            {
                Console.WriteLine(objException.Message);
            }
        }

        public void build(string arguments) // This takes the input from the custom command, don't apply arguments
        {
            success = false;
            System.Diagnostics.Process pProcess = new System.Diagnostics.Process();
            pProcess.StartInfo.FileName = variables.rootfolder + @"\xeBuild\xeBuild.exe";

            if (!arguments.Contains("-noenter")) arguments += " -noenter";
            if (!arguments.Contains("-p") && variables.cpukey.Length > 0) arguments += " -p " + variables.cpukey;
            if (!arguments.Contains("-d")) arguments += " -d data"; // This breaks sequencing in how J-Runner wraps XeBuild
            else
            {
                Console.WriteLine("Remove -d from your command and try again");
                return;
            }
            
            arguments += " \"" + variables.xefolder + "\\" + variables.updflash + "\" ";

            if (variables.debugMode) Console.WriteLine(variables.rootfolder);
            if (variables.debugMode) Console.WriteLine("---" + variables.rootfolder + @"\xeBuild\xeBuild.exe");
            if (variables.debugMode) Console.WriteLine(arguments);
            pProcess.StartInfo.Arguments = arguments;
            pProcess.StartInfo.UseShellExecute = false;
            pProcess.StartInfo.WorkingDirectory = variables.rootfolder;
            pProcess.StartInfo.RedirectStandardInput = true;
            pProcess.StartInfo.RedirectStandardOutput = true;
            pProcess.StartInfo.CreateNoWindow = true;
            pProcess.Exited += new EventHandler(xeExit);
            //pProcess.OutputDataReceived += new System.Diagnostics.DataReceivedEventHandler(DataReceived);
            //pProcess.Exited += new EventHandler(xe_Exited);
            //pProcess.OutputDataReceived += new System.Diagnostics.DataReceivedEventHandler(process_OutputDataReceived);
            try
            {
                AutoResetEvent outputWaitHandle = new AutoResetEvent(false);
                pProcess.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data == null)
                    {
                        outputWaitHandle.Set();
                    }
                    else
                    {
                        Console.WriteLine(e.Data);
                        if (e.Data != null && e.Data.Contains("image built"))
                        {
                            success = true;
                            if (!(_xdkbuild && _ttype != variables.hacktypes.devgl) && !_rgh3) variables.xefinished = true;
                        }
                    }
                };
                pProcess.Start();
                pProcess.BeginOutputReadLine();
                pProcess.StandardInput.WriteLine("enter");
                pProcess.WaitForExit();

                if (pProcess.HasExited)
                {
                    pProcess.CancelOutputRead();
                }
            }
            catch (Exception objException)
            {
                Console.WriteLine(objException.Message);
            }
        }


        ////////////////////////////////////////////////


        public void Uloadvariables(string dash, variables.hacktypes ttype, List<string> patches, bool altoptions, bool nowrite, bool noava, bool clean, bool noreeb, bool DLpatches, bool includeLaunch)
        {
            this._dash = dash;
            this._ttype = ttype;
            this._patches = patches;
            this._nowrite = nowrite;
            this._noava = noava;
            this._clean = clean;
            this._altoptions = altoptions;
            this._noreeb = noreeb;
            this._DLpatches = DLpatches;
            this._includeLaunch = includeLaunch;
        }

        public XebuildError createxebuild()
        {
            XebuildError result = XebuildError.none;
            if (_dash.Equals("")) result = XebuildError.nodash;
            if (result != XebuildError.none) return result;
            moveOptions();

            Console.WriteLine("Load Files Initiliazation Finished");
            checkDashLaunch();

            Console.WriteLine("Started Updating Console to {0}", _dash);
            variables.xefolder = variables.outfolder;

            return result;
        }

        public void update()
        {
            success = false;
            System.Diagnostics.Process pProcess = new System.Diagnostics.Process();
            pProcess.StartInfo.FileName = variables.rootfolder + @"\xeBuild\xeBuild.exe";
            string arguments = "update ";
            foreach (String patch in _patches)
            {
                arguments += " " + patch;
            }
            if (variables.debugMode) arguments += " -v";
            if (_noava) arguments += " -noava";
            if (_nowrite) arguments += " -nowrite";
            if (_clean) arguments += " -clean";
            if (_noreeb) arguments += " -noreeb";
            arguments += " -noenter";
            arguments += " -f " + _dash;
            arguments += " -d ";
            arguments += "\"" + variables.outfolder + "\"";

            RegexOptions options = RegexOptions.None;
            Regex regex = new Regex(@"[ ]{2,}", options);
            arguments = regex.Replace(arguments, @" ");

            if (variables.debugMode) Console.WriteLine(variables.rootfolder);
            if (variables.debugMode) Console.WriteLine("---" + variables.rootfolder + @"\xeBuild\xeBuild.exe");
            if (variables.debugMode) Console.WriteLine(arguments);
            pProcess.StartInfo.Arguments = arguments;
            pProcess.StartInfo.UseShellExecute = false;
            pProcess.StartInfo.WorkingDirectory = variables.rootfolder;
            pProcess.StartInfo.RedirectStandardInput = true;
            pProcess.StartInfo.RedirectStandardOutput = true;
            pProcess.StartInfo.CreateNoWindow = true;
            pProcess.Exited += new EventHandler(xeExit);
            try
            {
                AutoResetEvent outputWaitHandle = new AutoResetEvent(false);
                pProcess.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data == null)
                    {
                        outputWaitHandle.Set();
                    }
                    else
                    {
                        Console.WriteLine(e.Data);
                        if (e.Data != null && e.Data.Contains("image built"))
                        {
                            success = true;
                            variables.xefinished = true;
                        }
                    }
                };
                pProcess.Start();
                pProcess.BeginOutputReadLine();
                pProcess.WaitForExit();

                if (pProcess.HasExited)
                {
                    pProcess.CancelOutputRead();
                }
            }
            catch (Exception objException)
            {
                Console.WriteLine(objException.Message);
            }
        }

        public void client(string args)
        {
            System.Diagnostics.Process pProcess = new System.Diagnostics.Process();
            pProcess.StartInfo.FileName = variables.rootfolder + @"\xeBuild\xeBuild.exe";
            string arguments = "client ";
            arguments += args;
            if (variables.debugMode) arguments += " -v";
            arguments += " -noenter";

            RegexOptions options = RegexOptions.None;
            Regex regex = new Regex(@"[ ]{2,}", options);
            arguments = regex.Replace(arguments, @" ");

            if (variables.debugMode) Console.WriteLine(variables.rootfolder);
            if (variables.debugMode) Console.WriteLine("---" + variables.rootfolder + @"\xeBuild\xeBuild.exe");
            if (variables.debugMode) Console.WriteLine(arguments);
            pProcess.StartInfo.Arguments = arguments;
            pProcess.StartInfo.UseShellExecute = false;
            pProcess.StartInfo.WorkingDirectory = variables.rootfolder;
            pProcess.StartInfo.RedirectStandardInput = true;
            pProcess.StartInfo.RedirectStandardOutput = true;
            pProcess.StartInfo.CreateNoWindow = true;
            //pProcess.Exited += new EventHandler(xeExit);
            try
            {
                AutoResetEvent outputWaitHandle = new AutoResetEvent(false);
                pProcess.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data == null)
                    {
                        outputWaitHandle.Set();
                    }
                    else
                    {
                        Console.WriteLine(e.Data);
                    }
                };
                pProcess.Start();
                pProcess.BeginOutputReadLine();
                pProcess.WaitForExit();

                if (pProcess.HasExited)
                {
                    pProcess.CancelOutputRead();
                }
            }
            catch (Exception objException)
            {
                Console.WriteLine(objException.Message);
            }


        }

        public delegate void xeExited(object sender, EventArgs e);
        public event xeExited xeExit;


    }
}
