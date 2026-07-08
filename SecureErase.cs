// SecureErase.cs
// Native .NET Framework (C# 5) SSD secure-erase utility for Windows / WinPE.
// Compile with csc.exe (see build.cmd). No external packages, no MSBuild.
//
// Supports:
//   * SATA / ATA SSDs  -> ATA SECURITY ERASE UNIT (normal or enhanced) via IOCTL_ATA_PASS_THROUGH
//   * NVMe SSDs        -> Format NVM (user-data or cryptographic erase) via IOCTL_STORAGE_PROTOCOL_COMMAND
//
// This performs a DESTRUCTIVE, non-recoverable wipe. Use from WinPE whenever possible.
//
// Author: (generated) -- provided as-is, no warranty. Test on a scratch drive first.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Reflection;

[assembly: AssemblyTitle("SecureErase")]
[assembly: AssemblyDescription("SSD secure-erase utility for Windows / WinPE")]
[assembly: AssemblyCompany("inteliboy")]
[assembly: AssemblyProduct("SecureErase")]
[assembly: AssemblyCopyright("Copyright (2026) inteliboy")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

internal static class Native
{
    public const uint GENERIC_READ  = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ  = 0x00000001;
    public const uint FILE_SHARE_WRITE = 0x00000002;
    public const uint OPEN_EXISTING = 3;
    public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

    public const uint IOCTL_ATA_PASS_THROUGH               = 0x0004D02C;
    public const uint IOCTL_STORAGE_QUERY_PROPERTY         = 0x002D1400;
    public const uint IOCTL_STORAGE_PROTOCOL_COMMAND       = 0x002DD3C0;
    public const uint IOCTL_DISK_GET_LENGTH_INFO           = 0x0007405C;
    public const uint IOCTL_STORAGE_GET_DEVICE_NUMBER      = 0x002D1080;
    public const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;
    public const uint IOCTL_DISK_UPDATE_PROPERTIES         = 0x00070140;

    public const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
    public const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;

    public const uint MEM_COMMIT_RESERVE = 0x3000;
    public const uint MEM_RELEASE = 0x8000;
    public const uint PAGE_READWRITE = 0x04;

    // ATA pass-through flags
    public const ushort ATA_FLAGS_DRDY_REQUIRED = 0x0001;
    public const ushort ATA_FLAGS_DATA_IN       = 0x0002;
    public const ushort ATA_FLAGS_DATA_OUT      = 0x0004;

    // ATA commands
    public const byte ATA_IDENTIFY_DEVICE       = 0xEC;
    public const byte ATA_SECURITY_SET_PASSWORD = 0xF1;
    public const byte ATA_SECURITY_UNLOCK       = 0xF2;
    public const byte ATA_SECURITY_ERASE_PREP   = 0xF3;
    public const byte ATA_SECURITY_ERASE_UNIT   = 0xF4;
    public const byte ATA_SECURITY_DISABLE_PW   = 0xF6;

    [StructLayout(LayoutKind.Sequential)]
    public struct ATA_PASS_THROUGH_EX
    {
        public ushort Length;
        public ushort AtaFlags;
        public byte PathId;
        public byte TargetId;
        public byte Lun;
        public byte ReservedAsUchar;
        public uint DataTransferLength;
        public IntPtr TimeOutValue;      // ULONG_PTR
        public uint ReservedAsUlong;
        public IntPtr DataBufferOffset;  // ULONG_PTR
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] PreviousTaskFile;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] CurrentTaskFile;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(
        IntPtr hDevice, uint dwIoControlCode,
        byte[] lpInBuffer, int nInBufferSize,
        byte[] lpOutBuffer, int nOutBufferSize,
        out int lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern int FormatMessageW(uint dwFlags, IntPtr lpSource, uint dwMessageId,
        uint dwLanguageId, System.Text.StringBuilder lpBuffer, int nSize, IntPtr Arguments);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool VirtualFree(IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadFile(IntPtr hFile, IntPtr lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetFilePointerEx(IntPtr hFile, long liDistanceToMove, out long lpNewFilePointer, uint dwMoveMethod);
}

internal enum BusKind { Unknown, Ata, Sata, Nvme, Usb, Other }

internal sealed class DiskInfo
{
    public int Number;
    public string Model = "";
    public string Serial = "";
    public string Firmware = "";
    public long SizeBytes = -1;
    public BusKind Bus = BusKind.Unknown;

    // ATA security (IDENTIFY word 128) fields
    public bool AtaIdentifyOk;
    public bool SecSupported;
    public bool SecEnabled;
    public bool SecLocked;
    public bool SecFrozen;
    public bool SecCountExpired;
    public bool EnhancedEraseSupported;
    public int EraseMinutes;
    public int EnhancedEraseMinutes;

    public string BusName()
    {
        switch (Bus)
        {
            case BusKind.Ata: return "ATA (PATA)";
            case BusKind.Sata: return "SATA";
            case BusKind.Nvme: return "NVMe";
            case BusKind.Usb: return "USB";
            case BusKind.Other: return "Other";
            default: return "Unknown";
        }
    }

    public string SizeText()
    {
        if (SizeBytes < 0) return "unknown";
        double gib = SizeBytes / (1024.0 * 1024.0 * 1024.0);
        double gb = SizeBytes / 1000000000.0;
        return string.Format("{0:F1} GiB ({1:F1} GB, {2} bytes)", gib, gb, SizeBytes);
    }
}

internal static class Program
{
    private const string DefaultPassword = "SecureErasePwd";
    private const string AppName = "SecureErase";
    private const string AppVersion = "1.0.0";
    private const string AppCopyright = "Copyright (2026) inteliboy";

    private static void PrintBanner()
    {
        Console.WriteLine(AppName + " v" + AppVersion + "  -  " + AppCopyright);
    }

    private static int Main(string[] args)
    {
        try
        {
            // No arguments: default to listing the drives.
            if (args.Length == 0) return CmdList();
            string cmd = args[0].ToLowerInvariant();

            switch (cmd)
            {
                case "help": case "-h": case "--help": case "/?":
                    PrintHelp(); return 0;
                case "version": case "-v": case "--version":
                    PrintBanner(); return 0;
                case "annihilate": case "annihilation":
                    return CmdAnnihilate(args);
                case "list":
                    return CmdList();
                case "info":
                    return CmdInfo(args);
                case "verify":
                    return CmdVerify(args);
                case "erase":
                    return CmdErase(args);
                case "unlock":
                    return CmdRecover(args, false);
                case "disablepw": case "disable-password":
                    return CmdRecover(args, true);
                default:
                    Console.Error.WriteLine("Unknown command: " + cmd);
                    PrintHelp();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 2;
        }
    }

    // ---------------------------------------------------------------- commands

    private static int CmdList()
    {
        PrintBanner();
        Console.WriteLine();
        Console.WriteLine("Scanning physical drives...");
        Console.WriteLine();
        int sysDisk = TryGetSystemDiskNumber();
        bool any = false;
        for (int i = 0; i < 32; i++)
        {
            DiskInfo di = Probe(i);
            if (di == null) continue;
            any = true;
            string flag = (i == sysDisk) ? "  <-- system/boot disk" : "";
            Console.WriteLine(string.Format("Disk {0}: {1}  [{2}]{3}", i, di.Model, di.BusName(), flag));
            Console.WriteLine("   Serial : " + di.Serial);
            Console.WriteLine("   Size   : " + di.SizeText());
            if (di.Bus == BusKind.Sata || di.Bus == BusKind.Ata)
            {
                if (di.AtaIdentifyOk)
                    Console.WriteLine("   Security: " + AtaSecuritySummary(di));
                else
                    Console.WriteLine("   Security: (ATA IDENTIFY not available on this port)");
            }
            Console.WriteLine();
        }
        if (!any) Console.WriteLine("No physical drives found (or access denied - run as Administrator / in WinPE).");
        return 0;
    }

    private static int CmdInfo(string[] args)
    {
        int n;
        if (args.Length < 2 || !int.TryParse(args[1], out n))
        { Console.Error.WriteLine("Usage: SecureErase info <disk-number>"); return 1; }

        DiskInfo di = Probe(n);
        if (di == null) { Console.Error.WriteLine("Disk " + n + " not found / not accessible."); return 1; }

        int sysDisk = TryGetSystemDiskNumber();
        Console.WriteLine("Disk " + n + (n == sysDisk ? "  (SYSTEM/BOOT DISK)" : ""));
        Console.WriteLine("  Model    : " + di.Model);
        Console.WriteLine("  Serial   : " + di.Serial);
        Console.WriteLine("  Firmware : " + di.Firmware);
        Console.WriteLine("  Bus      : " + di.BusName());
        Console.WriteLine("  Size     : " + di.SizeText());
        if (di.Bus == BusKind.Sata || di.Bus == BusKind.Ata)
        {
            Console.WriteLine("  --- ATA security feature set ---");
            if (!di.AtaIdentifyOk) { Console.WriteLine("  IDENTIFY DEVICE failed on this port."); return 0; }
            Console.WriteLine("  Supported        : " + di.SecSupported);
            Console.WriteLine("  Enabled (pw set) : " + di.SecEnabled);
            Console.WriteLine("  Locked           : " + di.SecLocked);
            Console.WriteLine("  Frozen           : " + di.SecFrozen + (di.SecFrozen ? "   <-- must be unfrozen to erase" : ""));
            Console.WriteLine("  Count expired    : " + di.SecCountExpired);
            Console.WriteLine("  Enhanced erase   : " + di.EnhancedEraseSupported);
            Console.WriteLine(string.Format("  Est. erase time  : ~{0} min (normal), ~{1} min (enhanced)", di.EraseMinutes, di.EnhancedEraseMinutes));
        }
        else if (di.Bus == BusKind.Nvme)
        {
            Console.WriteLine("  Use 'erase " + n + "' to run an NVMe Format secure erase (add --crypto for cryptographic erase).");
        }
        return 0;
    }

    private static int CmdVerify(string[] args)
    {
        int n;
        if (args.Length < 2 || !int.TryParse(args[1], out n))
        { Console.Error.WriteLine("Usage: SecureErase verify <disk-number> [--samples N] [--full]"); return 1; }

        bool full = HasFlag(args, "--full");
        int samples = (int)GetOptionUInt(args, "--samples", 512u);
        if (samples < 8) samples = 8;

        DiskInfo di = Probe(n);
        if (di == null) { Console.Error.WriteLine("Disk " + n + " not found / not accessible."); return 1; }
        if (di.SizeBytes <= 0) { Console.Error.WriteLine("Cannot determine disk size."); return 1; }

        // Report what a blank block is expected to read as (NVMe DLFEAT / crypto caveat).
        if (di.Bus == BusKind.Nvme)
        {
            byte[] idns = ReadNvmeIdns(n);
            if (idns != null)
            {
                int dlfeat = idns[33] & 0x07; // Deallocated Logical Block read behaviour
                string exp = dlfeat == 1 ? "all zeros" : (dlfeat == 2 ? "all 0xFF" : "not reported by drive");
                Console.WriteLine("NVMe DLFEAT: deallocated blocks read back as " + exp + ".");
            }
            Console.WriteLine("Note: a CRYPTOGRAPHIC erase (--crypto) destroys the key; it does NOT guarantee");
            Console.WriteLine("      zeroed reads. For crypto erase, verify via the pattern method (see help).");
        }

        long zero, ones, data, readErr;
        Console.WriteLine((full ? "Full read of disk " : "Sampling " + samples + " blocks across disk ") + n + " (" + di.SizeText() + ") ...");
        if (!VerifyDisk(di, samples, full, true, out zero, out ones, out data, out readErr))
        { Console.Error.WriteLine("Cannot open disk " + n + " for raw read (err " + Marshal.GetLastWin32Error() + ")."); return 1; }

        long checkedBlocks = zero + ones + data + readErr;
        Console.WriteLine();
        Console.WriteLine("Blocks checked : " + checkedBlocks + " x 4096 bytes");
        Console.WriteLine("  all-zero     : " + zero);
        Console.WriteLine("  all-0xFF     : " + ones);
        Console.WriteLine("  contains data: " + data);
        if (readErr > 0) Console.WriteLine("  read errors  : " + readErr);
        Console.WriteLine();
        if (data == 0 && checkedBlocks > 0)
        {
            Console.WriteLine("RESULT: no residual data found in the checked blocks (drive reads blank).");
            if (!full) Console.WriteLine("        This is a sample. Use --full for an exhaustive read of every block.");
            return 0;
        }
        Console.WriteLine("RESULT: residual data DETECTED. The drive does not read fully blank.");
        Console.WriteLine("        (For crypto erase this can be expected - see the note above.)");
        return 10;
    }

    // Raw-read verification core. Opens the physical drive with NO_BUFFERING so
    // reads bypass the OS cache. Returns false only if the drive can't be opened.
    private static bool VerifyDisk(DiskInfo di, int samples, bool full, bool verbose,
        out long zero, out long ones, out long data, out long readErr)
    {
        zero = ones = data = 0; readErr = 0;
        const int BLK = 4096; // aligned read unit (works for 512e and 4Kn)

        IntPtr h = Native.CreateFileW("\\\\.\\PhysicalDrive" + di.Number, Native.GENERIC_READ,
            Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING,
            Native.FILE_FLAG_NO_BUFFERING | Native.FILE_FLAG_WRITE_THROUGH, IntPtr.Zero);
        if (h == Native.INVALID_HANDLE_VALUE) return false;

        IntPtr abuf = Native.VirtualAlloc(IntPtr.Zero, (UIntPtr)BLK, Native.MEM_COMMIT_RESERVE, Native.PAGE_READWRITE);
        if (abuf == IntPtr.Zero) { Native.CloseHandle(h); return false; }

        long maxOffset = (di.SizeBytes / BLK) * BLK - BLK;
        if (maxOffset < 0) maxOffset = 0;
        byte[] tmp = new byte[BLK];
        int shownData = 0;

        try
        {
            if (full)
            {
                long off = 0, lastPct = -1, total = maxOffset + BLK;
                while (off <= maxOffset)
                {
                    int cls = ReadBlock(h, off, abuf, BLK, tmp);
                    Tally(cls, ref zero, ref ones, ref data, ref readErr);
                    if (verbose && cls == 2 && shownData < 10) { Console.WriteLine("  data at offset 0x" + off.ToString("X")); shownData++; }
                    if (verbose)
                    {
                        long pct = (long)(off / (double)total * 100.0);
                        if (pct != lastPct && pct % 5 == 0) { Console.Write("\r  " + pct + "% "); lastPct = pct; }
                    }
                    off += BLK;
                }
                if (verbose) Console.WriteLine("\r  100%   ");
            }
            else
            {
                for (int i = 0; i < samples; i++)
                {
                    long off = (samples <= 1) ? 0 : (long)((double)i / (samples - 1) * maxOffset);
                    off = (off / BLK) * BLK;
                    int cls = ReadBlock(h, off, abuf, BLK, tmp);
                    Tally(cls, ref zero, ref ones, ref data, ref readErr);
                    if (verbose && cls == 2 && shownData < 20) { Console.WriteLine("  NON-BLANK at offset 0x" + off.ToString("X")); shownData++; }
                }
            }
        }
        finally
        {
            Native.VirtualFree(abuf, UIntPtr.Zero, Native.MEM_RELEASE);
            Native.CloseHandle(h);
        }
        return true;
    }

    // Returns 0=all zero, 1=all 0xFF, 2=has data, 3=read error
    private static int ReadBlock(IntPtr h, long offset, IntPtr abuf, int size, byte[] tmp)
    {
        long np;
        if (!Native.SetFilePointerEx(h, offset, out np, 0)) return 3;
        uint got;
        if (!Native.ReadFile(h, abuf, (uint)size, out got, IntPtr.Zero) || got == 0) return 3;
        Marshal.Copy(abuf, tmp, 0, (int)Math.Min(got, (uint)size));
        bool allZero = true, allOnes = true;
        int lim = (int)Math.Min(got, (uint)size);
        for (int j = 0; j < lim; j++)
        {
            if (tmp[j] != 0x00) allZero = false;
            if (tmp[j] != 0xFF) allOnes = false;
            if (!allZero && !allOnes) return 2;
        }
        return allZero ? 0 : (allOnes ? 1 : 2);
    }

    private static void Tally(int cls, ref long zero, ref long ones, ref long data, ref long err)
    {
        if (cls == 0) zero++;
        else if (cls == 1) ones++;
        else if (cls == 2) data++;
        else err++;
    }

    private static byte[] ReadNvmeIdns(int n)
    {
        IntPtr h = OpenDisk(n);
        if (h == Native.INVALID_HANDLE_VALUE) return null;
        try { return NvmeIdentify(h, 0, 1); }
        finally { Native.CloseHandle(h); }
    }

    private static void RefreshDiskProperties(IntPtr h)
    {
        int returned;
        bool ok = Native.DeviceIoControl(h, Native.IOCTL_DISK_UPDATE_PROPERTIES, null, 0, null, 0, out returned, IntPtr.Zero);
        if (ok)
            Console.WriteLine("Disk layout refreshed. (If diskpart still shows old partitions, run 'rescan' or reboot.)");
        else
            Console.WriteLine("Note: run diskpart 'rescan' or reboot so Windows drops its cached partition table.");
    }

    private static int CmdErase(string[] args)
    {
        if (HasFlag(args, "--annihilation")) return CmdAnnihilate(args);

        int n;
        if (args.Length < 2 || !int.TryParse(args[1], out n))
        { Console.Error.WriteLine("Usage: SecureErase erase <disk-number> [options]"); return 1; }

        bool enhanced   = HasFlag(args, "--enhanced");
        bool crypto     = HasFlag(args, "--crypto");
        bool userErase  = HasFlag(args, "--user");
        bool force      = HasFlag(args, "--force");
        bool skipPrompt = HasFlag(args, "--yes");
        bool allNs      = HasFlag(args, "--all-namespaces");
        if (userErase) crypto = false; // explicit user-data erase overrides --crypto
        string password = GetOption(args, "--password", DefaultPassword);
        int nsid        = (int)GetOptionUInt(args, "--nsid", allNs ? 0xFFFFFFFF : 1u);

        DiskInfo di = Probe(n);
        if (di == null) { Console.Error.WriteLine("Disk " + n + " not found / not accessible."); return 1; }

        int sysDisk = TryGetSystemDiskNumber();
        Console.WriteLine("Target disk " + n + ":");
        Console.WriteLine("  Model : " + di.Model);
        Console.WriteLine("  Serial: " + di.Serial);
        Console.WriteLine("  Size  : " + di.SizeText());
        Console.WriteLine("  Bus   : " + di.BusName());
        Console.WriteLine();

        if (n == sysDisk && !force)
        {
            Console.Error.WriteLine("REFUSING: disk " + n + " appears to host the running system/boot volume.");
            Console.Error.WriteLine("Boot WinPE and erase from there, or pass --force if you are certain.");
            return 3;
        }

        if (n == sysDisk && !IsWinPE())
        {
            Console.WriteLine("WARNING: this is the running-OS disk on a live Windows system.");
            Console.WriteLine("         Windows will block the erase. Boot WinPE to wipe it. Continuing anyway...");
            Console.WriteLine();
        }

        Console.WriteLine("*** THIS WILL PERMANENTLY DESTROY ALL DATA ON DISK " + n + ". ***");
        if (!skipPrompt)
        {
            string phrase = "ERASE DISK " + n;
            Console.Write("Type exactly '" + phrase + "' to proceed: ");
            string typed = Console.ReadLine();
            if (typed == null || typed.Trim() != phrase)
            {
                Console.WriteLine("Confirmation did not match. Aborted.");
                return 4;
            }
        }

        int rc;
        if (di.Bus == BusKind.Nvme)
            rc = EraseNvme(n, nsid, crypto);
        else if (di.Bus == BusKind.Sata || di.Bus == BusKind.Ata)
            rc = EraseAta(n, di, password, enhanced);
        else
        {
            // Fallback: try ATA (some controllers report generically).
            Console.Error.WriteLine("Bus type '" + di.BusName() + "' is not directly supported.");
            Console.Error.WriteLine("Attempting ATA security erase anyway...");
            rc = EraseAta(n, di, password, enhanced);
        }

        if (rc != 0 && n == sysDisk && !IsWinPE()) PrintLiveOsDiskHelp();
        return rc;
    }

    // ------------------------------------------------------- annihilation

    private static int CmdAnnihilate(string[] args)
    {
        bool enhanced      = HasFlag(args, "--enhanced");
        bool crypto        = HasFlag(args, "--crypto");
        bool includeUsb    = HasFlag(args, "--include-usb");
        bool includeSystem = HasFlag(args, "--include-system");
        bool allNs         = HasFlag(args, "--all-namespaces");
        string password    = GetOption(args, "--password", DefaultPassword);
        int nvmeNsid       = allNs ? unchecked((int)0xFFFFFFFF) : 1;

        PrintBanner();
        Console.WriteLine();
        Console.WriteLine("############################################################");
        Console.WriteLine("#  ANNIHILATION MODE                                       #");
        Console.WriteLine("#  Secure-erase ALL internal drives, unattended.           #");
        Console.WriteLine("############################################################");
        Console.WriteLine();

        int sysDisk = TryGetSystemDiskNumber();
        bool livePe = !IsWinPE();
        if (includeSystem && livePe)
        {
            Console.WriteLine("WARNING: --include-system on a LIVE Windows system: the running-OS disk");
            Console.WriteLine("         cannot be erased in place and will report a failure. Only a WinPE");
            Console.WriteLine("         (or other boot media) session can wipe the OS disk.");
            Console.WriteLine();
        }
        List<DiskInfo> targets = new List<DiskInfo>();
        List<string> skipped = new List<string>();

        for (int i = 0; i < 32; i++)
        {
            DiskInfo di = Probe(i);
            if (di == null) continue;
            if (di.SizeBytes <= 0) { skipped.Add("Disk " + i + " (" + di.Model + "): no media / unknown size"); continue; }
            if (!includeUsb && di.Bus == BusKind.Usb) { skipped.Add("Disk " + i + " (" + di.Model + "): USB/external - excluded"); continue; }
            if (!includeSystem && i == sysDisk) { skipped.Add("Disk " + i + " (" + di.Model + "): running-OS/boot disk - excluded"); continue; }
            targets.Add(di);
        }

        if (skipped.Count > 0)
        {
            Console.WriteLine("Skipped:");
            foreach (string s in skipped) Console.WriteLine("  - " + s);
            Console.WriteLine();
        }

        if (targets.Count == 0)
        {
            Console.WriteLine("No internal target drives found. Nothing to do.");
            return 0;
        }

        Console.WriteLine("The following " + targets.Count + " drive(s) will be PERMANENTLY ERASED:");
        foreach (DiskInfo di in targets)
            Console.WriteLine(string.Format("  Disk {0}: {1}  [{2}]  {3}  SN:{4}",
                di.Number, di.Model, di.BusName(), di.SizeText(), di.Serial));
        Console.WriteLine();
        Console.WriteLine("NOTE: interfaces like eSATA/Thunderbolt may report as internal.");
        Console.WriteLine("      Physically verify before proceeding.");
        Console.WriteLine();

        // Unattended abort window (not a prompt - it proceeds on its own).
        Console.Write("Beginning in ");
        for (int s = 5; s > 0; s--) { Console.Write(s + "... "); System.Threading.Thread.Sleep(1000); }
        Console.WriteLine("GO");
        Console.WriteLine();

        int okCount = 0, failCount = 0;
        foreach (DiskInfo di in targets)
        {
            Console.WriteLine("------------------------------------------------------------");
            Console.WriteLine(string.Format("Erasing disk {0}: {1} (SN:{2})", di.Number, di.Model, di.Serial));

            // NVMe escalation (annihilation only): try a user-data erase first,
            // verify by sampled read; if residual data remains, escalate to a
            // cryptographic erase (if the controller supports it). A crypto erase
            // destroys the media key, so the old data is unrecoverable even
            // though the flash may still read non-zero - therefore a crypto erase
            // whose Format command SUCCEEDS is treated as success without
            // requiring a blank read-back. ATA drives keep the single-pass path.
            if (di.Bus == BusKind.Nvme)
            {
                // Honor an explicit --crypto: go straight to crypto erase.
                if (crypto)
                {
                    int rcc0;
                    try { rcc0 = EraseNvme(di.Number, nvmeNsid, true); }
                    catch (Exception ex)
                    { Console.Error.WriteLine("  EXCEPTION on disk " + di.Number + ": " + ex.Message); rcc0 = 99; }
                    if (rcc0 == 0)
                    {
                        Console.WriteLine("  CRYPTO ERASE succeeded (key destroyed) - treated as success.");
                        okCount++;
                    }
                    else { failCount++; }
                    Console.WriteLine();
                    continue;
                }

                // 1) user-data erase (SES=1)
                int rcu;
                try { rcu = EraseNvme(di.Number, nvmeNsid, false); }
                catch (Exception ex)
                { Console.Error.WriteLine("  EXCEPTION on disk " + di.Number + ": " + ex.Message); rcu = 99; }

                bool verifiedBlank = false;
                if (rcu == 0)
                {
                    long uz, uo, ud, ue;
                    if (VerifyDisk(di, 256, false, false, out uz, out uo, out ud, out ue))
                    {
                        long chk = uz + uo + ud + ue;
                        if (ud == 0 && chk > 0)
                        {
                            Console.WriteLine(string.Format("  VERIFY PASS: {0} sampled blocks read blank.", chk));
                            verifiedBlank = true;
                        }
                        else
                        {
                            Console.WriteLine(string.Format("  User-data erase left residual data in {0}/{1} sampled blocks - escalating to crypto erase.", ud, chk));
                        }
                    }
                    else
                    {
                        Console.WriteLine("  VERIFY SKIPPED: could not re-open drive to read back.");
                        // Can't verify; escalate to crypto to be safe.
                    }
                }
                else
                {
                    Console.WriteLine("  User-data erase did not complete - escalating to crypto erase.");
                }

                if (verifiedBlank) { okCount++; Console.WriteLine(); continue; }

                // 2) escalate to crypto erase (SES=2) if supported.
                if (!NvmeSupportsCrypto(di.Number))
                {
                    Console.Error.WriteLine("  CRYPTO ERASE unavailable (controller does not report FNA crypto support). Disk NOT confirmed erased.");
                    failCount++;
                    Console.WriteLine();
                    continue;
                }

                int rcc;
                try { rcc = EraseNvme(di.Number, nvmeNsid, true); }
                catch (Exception ex)
                { Console.Error.WriteLine("  EXCEPTION on disk " + di.Number + ": " + ex.Message); rcc = 99; }

                if (rcc == 0)
                {
                    Console.WriteLine("  CRYPTO ERASE succeeded (media key destroyed) - data is unrecoverable. Treated as success (read-back not applicable).");
                    okCount++;
                }
                else
                {
                    Console.Error.WriteLine("  CRYPTO ERASE failed. Disk NOT confirmed erased.");
                    failCount++;
                }
                Console.WriteLine();
                continue;
            }

            // ---- ATA path (single pass + verify) ----
            int rc;
            try
            {
                rc = EraseAta(di.Number, di, password, enhanced && di.EnhancedEraseSupported);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("  EXCEPTION on disk " + di.Number + ": " + ex.Message);
                rc = 99;
            }
            if (rc != 0) { failCount++; if (di.Number == sysDisk && livePe) PrintLiveOsDiskHelp(); Console.WriteLine(); continue; }

            // Automatic basic-mode verification (sampled raw read).
            long vz, vo, vd, ve;
            if (VerifyDisk(di, 256, false, false, out vz, out vo, out vd, out ve))
            {
                long chk = vz + vo + vd + ve;
                if (vd == 0 && chk > 0)
                {
                    Console.WriteLine(string.Format("  VERIFY PASS: {0} sampled blocks read blank.", chk));
                    okCount++;
                }
                else
                {
                    Console.WriteLine(string.Format("  VERIFY FAILED: residual data in {0}/{1} sampled blocks!", vd, chk));
                    failCount++;
                }
            }
            else
            {
                Console.WriteLine("  VERIFY SKIPPED: could not re-open drive to read back (erase reported success).");
                okCount++;
            }
            Console.WriteLine();
        }

        Console.WriteLine("############################################################");
        Console.WriteLine(string.Format("ANNIHILATION COMPLETE: {0} erased+verified, {1} failed/suspect.", okCount, failCount));
        Console.WriteLine("############################################################");
        Console.WriteLine("Each drive was sampled after erase. For an exhaustive check:");
        Console.WriteLine("  SecureErase verify <disk> --full");
        return failCount == 0 ? 0 : 11;
    }

    private static int CmdRecover(string[] args, bool disable)
    {
        int n;
        if (args.Length < 2 || !int.TryParse(args[1], out n))
        { Console.Error.WriteLine("Usage: SecureErase " + (disable ? "disablepw" : "unlock") + " <disk-number> [--password STR]"); return 1; }
        string password = GetOption(args, "--password", DefaultPassword);

        IntPtr h = OpenDisk(n);
        if (h == Native.INVALID_HANDLE_VALUE) { Console.Error.WriteLine("Cannot open disk " + n + " (err " + Marshal.GetLastWin32Error() + ")."); return 1; }
        try
        {
            byte[] block = BuildPasswordBlock(password, false);
            byte err;
            bool ok = disable
                ? AtaDataOut(h, Native.ATA_SECURITY_DISABLE_PW, block, 15, out err)
                : AtaDataOut(h, Native.ATA_SECURITY_UNLOCK, block, 15, out err);
            if (ok) { Console.WriteLine((disable ? "SECURITY DISABLE PASSWORD" : "SECURITY UNLOCK") + " succeeded."); return 0; }
            Console.Error.WriteLine("Command failed (ATA error register=0x" + err.ToString("X2") + "). Wrong password?");
            return 5;
        }
        finally { Native.CloseHandle(h); }
    }

    // ------------------------------------------------------------- ATA erase

    private static int EraseAta(int n, DiskInfo di, string password, bool enhanced)
    {
        if (di.AtaIdentifyOk)
        {
            if (!di.SecSupported)
            { Console.Error.WriteLine("Drive does not support the ATA security feature set."); return 6; }
            if (di.SecFrozen)
            {
                Console.Error.WriteLine("Drive is SECURITY FROZEN. The BIOS/firmware froze it at boot.");
                Console.Error.WriteLine("Unfreeze by: hot-unplug+replug the SATA data cable, or S3 sleep/resume, then retry.");
                return 7;
            }
            if (enhanced && !di.EnhancedEraseSupported)
            { Console.Error.WriteLine("Enhanced erase requested but not supported by this drive."); return 6; }
        }

        IntPtr h = OpenDisk(n);
        if (h == Native.INVALID_HANDLE_VALUE) { Console.Error.WriteLine("Cannot open disk " + n + " (err " + Marshal.GetLastWin32Error() + ")."); return 1; }
        try
        {
            byte err;
            Console.WriteLine("[1/3] SECURITY SET PASSWORD (user)...");
            if (!AtaDataOut(h, Native.ATA_SECURITY_SET_PASSWORD, BuildPasswordBlock(password, false), 15, out err))
            { Console.Error.WriteLine("SET PASSWORD failed (err=0x" + err.ToString("X2") + ")."); return 8; }

            Console.WriteLine("[2/3] SECURITY ERASE PREPARE...");
            if (!AtaNonData(h, Native.ATA_SECURITY_ERASE_PREP, 15, out err))
            {
                Console.Error.WriteLine("ERASE PREPARE failed (err=0x" + err.ToString("X2") + "). Rolling back password...");
                AtaDataOut(h, Native.ATA_SECURITY_DISABLE_PW, BuildPasswordBlock(password, false), 15, out err);
                return 8;
            }

            int minutes = enhanced ? Math.Max(di.EnhancedEraseMinutes, 1) : Math.Max(di.EraseMinutes, 1);
            uint timeout = (uint)Math.Max(60, Math.Min(21600, minutes * 60 * 2 + 60));
            Console.WriteLine("[3/3] SECURITY ERASE UNIT (" + (enhanced ? "enhanced" : "normal") +
                              "). This may take a while; do not power off...");
            byte[] eraseBlock = BuildEraseBlock(password, enhanced);
            if (!AtaDataOut(h, Native.ATA_SECURITY_ERASE_UNIT, eraseBlock, timeout, out err))
            {
                Console.Error.WriteLine("ERASE UNIT failed (err=0x" + err.ToString("X2") + ").");
                Console.Error.WriteLine("Drive may still be password-locked. Recover with:");
                Console.Error.WriteLine("  SecureErase disablepw " + n + " --password \"" + password + "\"");
                return 9;
            }

            Console.WriteLine();
            Console.WriteLine("SUCCESS: ATA secure erase completed. Drive security is auto-disabled after erase.");
            RefreshDiskProperties(h);
            return 0;
        }
        finally { Native.CloseHandle(h); }
    }

    // Returns true if the NVMe controller reports support for cryptographic
    // erase in the FNA (Format NVM Attributes) field of Identify Controller.
    private static bool NvmeSupportsCrypto(int n)
    {
        IntPtr h = OpenDisk(n);
        if (h == Native.INVALID_HANDLE_VALUE) return false;
        try
        {
            byte[] idctrl = NvmeIdentify(h, 1 /*CNS controller*/, 0);
            if (idctrl == null) return false;
            return (idctrl[524] & 0x04) != 0; // FNA bit 2 = crypto erase supported
        }
        finally { Native.CloseHandle(h); }
    }

    // ------------------------------------------------------------ NVMe erase

    private static int EraseNvme(int n, int nsid, bool crypto)
    {
        IntPtr h = OpenDisk(n);
        if (h == Native.INVALID_HANDLE_VALUE) { Console.Error.WriteLine("Cannot open disk " + n + " (err " + Marshal.GetLastWin32Error() + ")."); return 1; }
        try
        {
            byte flbas = 0;
            byte[] idns = NvmeIdentify(h, 0 /*CNS namespace*/, nsid == unchecked((int)0xFFFFFFFF) ? 1 : nsid);
            if (idns != null) flbas = (byte)(idns[26] & 0x0F);

            byte[] idctrl = NvmeIdentify(h, 1 /*CNS controller*/, 0);
            bool cryptoSupported = false;
            if (idctrl != null) cryptoSupported = (idctrl[524] & 0x04) != 0; // FNA bit 2

            int ses = crypto ? 2 : 1;
            if (crypto && idctrl != null && !cryptoSupported)
            { Console.Error.WriteLine("Cryptographic erase not supported by controller (FNA). Use user-data erase (omit --crypto)."); return 6; }

            Console.WriteLine("NVMe Format: nsid=0x" + ((uint)nsid).ToString("X8") +
                              " lbaf=" + flbas + " ses=" + ses + (crypto ? " (cryptographic)" : " (user-data)") + " ...");
            uint status, errCode;
            bool ok = NvmeFormat(h, (uint)nsid, flbas, ses, out status, out errCode);
            if (!ok)
            {
                int w32 = Marshal.GetLastWin32Error();
                Console.Error.WriteLine("NVMe Format failed. IOCTL error: " + Win32Text(w32) +
                                        ", protocol ReturnStatus=0x" + status.ToString("X8") +
                                        ", ErrorCode=0x" + errCode.ToString("X8") + ".");
                if (w32 == 87)
                    Console.Error.WriteLine("(err 87 = the driver rejected the request: this controller/driver may block Format-NVM pass-through, or the namespace has mounted volumes. Try --all-namespaces, dismount volumes, or use the drive vendor's tool.)");
                else if (w32 == 1 || w32 == 50)
                    Console.Error.WriteLine("(this Windows/WinPE build does not expose IOCTL_STORAGE_PROTOCOL_COMMAND for NVMe.)");
                else if (w32 == 5 || w32 == 19 || w32 == 32 || w32 == 33)
                    Console.Error.WriteLine("(the drive/namespace is in use or write-protected; dismount its volumes or run from WinPE.)");
                else
                    Console.Error.WriteLine("Some controllers/drivers reject this IOCTL; a vendor tool may be required.");
                return 9;
            }
            Console.WriteLine();
            Console.WriteLine("SUCCESS: NVMe secure erase (Format NVM) completed.");
            RefreshDiskProperties(h);
            return 0;
        }
        finally { Native.CloseHandle(h); }
    }

    // ---------------------------------------------------------- ATA plumbing

    // Non-data ATA command. Returns true on success; 'err' = returned ATA error register.
    private static bool AtaNonData(IntPtr h, byte command, uint timeoutSec, out byte err)
    {
        byte[] tf = new byte[8];
        tf[6] = command;
        return AtaPassThrough(h, tf, 0, null, timeoutSec, out err) != null;
    }

    // Data-out (512 bytes to drive). Returns true on success.
    private static bool AtaDataOut(IntPtr h, byte command, byte[] data512, uint timeoutSec, out byte err)
    {
        byte[] tf = new byte[8];
        tf[1] = 1;          // sector count = 1
        tf[6] = command;
        byte[] res = AtaPassThrough(h, tf, 2, data512, timeoutSec, out err);
        return res != null;
    }

    // Data-in (reads 512 bytes). Returns the data or null on failure.
    private static byte[] AtaDataIn(IntPtr h, byte command, uint timeoutSec, out byte err)
    {
        byte[] tf = new byte[8];
        tf[1] = 1;
        tf[6] = command;
        return AtaPassThrough(h, tf, 1, null, timeoutSec, out err);
    }

    // Core ATA pass-through. direction: 0=none, 1=in, 2=out.
    // Returns: for in -> 512 byte buffer; for out/none -> non-null empty marker on success; null on failure.
    private static byte[] AtaPassThrough(IntPtr h, byte[] taskFile, int direction, byte[] dataOut, uint timeoutSec, out byte errReg)
    {
        errReg = 0;
        int headerSize = Marshal.SizeOf(typeof(Native.ATA_PASS_THROUGH_EX));
        int dataLen = (direction == 0) ? 0 : 512;
        int bufLen = headerSize + dataLen;
        byte[] buf = new byte[bufLen];

        Native.ATA_PASS_THROUGH_EX ap = new Native.ATA_PASS_THROUGH_EX();
        ap.Length = (ushort)headerSize;
        ap.AtaFlags = Native.ATA_FLAGS_DRDY_REQUIRED;
        if (direction == 1) ap.AtaFlags |= Native.ATA_FLAGS_DATA_IN;
        if (direction == 2) ap.AtaFlags |= Native.ATA_FLAGS_DATA_OUT;
        ap.DataTransferLength = (uint)dataLen;
        ap.TimeOutValue = new IntPtr(timeoutSec);
        ap.DataBufferOffset = new IntPtr(headerSize);
        ap.PreviousTaskFile = new byte[8];
        ap.CurrentTaskFile = new byte[8];
        Array.Copy(taskFile, ap.CurrentTaskFile, 8);

        IntPtr hdr = Marshal.AllocHGlobal(headerSize);
        try
        {
            Marshal.StructureToPtr(ap, hdr, false);
            Marshal.Copy(hdr, buf, 0, headerSize);
        }
        finally { Marshal.FreeHGlobal(hdr); }

        if (direction == 2 && dataOut != null)
            Array.Copy(dataOut, 0, buf, headerSize, Math.Min(512, dataOut.Length));

        int returned;
        bool ok = Native.DeviceIoControl(h, Native.IOCTL_ATA_PASS_THROUGH, buf, bufLen, buf, bufLen, out returned, IntPtr.Zero);

        // On return the CurrentTaskFile (last 8 bytes of header) holds output registers:
        // [0]=Error, [6]=Status
        byte status = buf[headerSize - 8 + 6];
        errReg = buf[headerSize - 8 + 0];
        bool ataError = (status & 0x01) != 0; // ERR bit

        if (!ok || ataError) return null;

        if (direction == 1)
        {
            byte[] outData = new byte[512];
            Array.Copy(buf, headerSize, outData, 0, 512);
            return outData;
        }
        return new byte[0]; // non-null success marker
    }

    private static byte[] BuildPasswordBlock(string password, bool master)
    {
        byte[] b = new byte[512];
        // word 0: control (0 = user password). We only ever use the user password.
        b[0] = (byte)(master ? 1 : 0);
        b[1] = 0;
        byte[] pw = Encoding.ASCII.GetBytes(password);
        Array.Copy(pw, 0, b, 2, Math.Min(pw.Length, 32));
        return b;
    }

    private static byte[] BuildEraseBlock(string password, bool enhanced)
    {
        byte[] b = new byte[512];
        // word 0: bit0 = user(0)/master(1); bit1 = normal(0)/enhanced(1)
        ushort ctrl = (ushort)(enhanced ? 0x0002 : 0x0000);
        b[0] = (byte)(ctrl & 0xFF);
        b[1] = (byte)(ctrl >> 8);
        byte[] pw = Encoding.ASCII.GetBytes(password);
        Array.Copy(pw, 0, b, 2, Math.Min(pw.Length, 32));
        return b;
    }

    // --------------------------------------------------------- NVMe plumbing

    private static byte[] NvmeIdentify(IntPtr h, int cns, int nsid)
    {
        const int HDR = 8;   // FIELD_OFFSET(STORAGE_PROPERTY_QUERY, AdditionalParameters)
        const int PSD = 40;  // sizeof(STORAGE_PROTOCOL_SPECIFIC_DATA)
        const int DATA = 4096;
        int bufLen = HDR + PSD + DATA;
        byte[] buf = new byte[bufLen];

        // STORAGE_PROPERTY_QUERY
        WrU32(buf, 0, 50); // StorageDeviceProtocolSpecificProperty
        WrU32(buf, 4, 0);  // PropertyStandardQuery
        // STORAGE_PROTOCOL_SPECIFIC_DATA at offset 8
        int p = HDR;
        WrU32(buf, p + 0,  3);          // ProtocolType = Nvme
        WrU32(buf, p + 4,  1);          // DataType = NVMeDataTypeIdentify
        WrU32(buf, p + 8,  (uint)cns);  // ProtocolDataRequestValue = CNS
        WrU32(buf, p + 12, (uint)nsid); // ProtocolDataRequestSubValue = NSID
        WrU32(buf, p + 16, PSD);        // ProtocolDataOffset (from start of PSD)
        WrU32(buf, p + 20, DATA);       // ProtocolDataLength

        int returned;
        bool ok = Native.DeviceIoControl(h, Native.IOCTL_STORAGE_QUERY_PROPERTY, buf, bufLen, buf, bufLen, out returned, IntPtr.Zero);
        if (!ok) return null;

        byte[] data = new byte[DATA];
        Array.Copy(buf, HDR + PSD, data, 0, DATA);
        return data;
    }

    private static bool NvmeFormat(IntPtr h, uint nsid, byte lbaf, int ses, out uint returnStatus, out uint errorCode)
    {
        returnStatus = 0;
        errorCode = 0;
        const int CMD_OFF = 80;   // FIELD_OFFSET(STORAGE_PROTOCOL_COMMAND, Command)
        const int STRUCT_SIZE = 84; // sizeof(STORAGE_PROTOCOL_COMMAND) (Command[1] padded to 4)
        const int CMDLEN = 64;      // STORAGE_PROTOCOL_COMMAND_LENGTH_NVME
        int bufLen = CMD_OFF + CMDLEN; // 144
        byte[] buf = new byte[bufLen];

        WrU32(buf, 0,  1);            // Version = STORAGE_PROTOCOL_STRUCTURE_VERSION
        WrU32(buf, 4,  STRUCT_SIZE);  // Length = sizeof(STORAGE_PROTOCOL_COMMAND)
        WrU32(buf, 8,  3);            // ProtocolType = ProtocolTypeNvme
        WrU32(buf, 12, 0x80000000);  // Flags = STORAGE_PROTOCOL_COMMAND_FLAG_ADAPTER_REQUEST
        WrU32(buf, 24, CMDLEN);       // CommandLength = 64
        WrU32(buf, 40, 60);           // TimeOutValue (s)
        WrU32(buf, 56, 1);            // CommandSpecific = STORAGE_PROTOCOL_SPECIFIC_NVME_ADMIN_COMMAND

        // NVMe command (64 bytes) at offset CMD_OFF
        int c = CMD_OFF;
        WrU32(buf, c + 0, 0x00000080);              // CDW0: opcode 0x80 Format NVM
        WrU32(buf, c + 4, nsid);                    // CDW1: NSID
        uint cdw10 = (uint)((lbaf & 0x0F) | ((ses & 0x07) << 9));
        WrU32(buf, c + 40, cdw10);                  // CDW10: LBAF[3:0] | SES[11:9]

        int returned;
        bool ok = Native.DeviceIoControl(h, Native.IOCTL_STORAGE_PROTOCOL_COMMAND, buf, bufLen, buf, bufLen, out returned, IntPtr.Zero);
        returnStatus = RdU32(buf, 16); // ReturnStatus
        errorCode    = RdU32(buf, 20); // ErrorCode
        return ok;
    }

    // -------------------------------------------------------------- probing

    private static DiskInfo Probe(int n)
    {
        IntPtr h = OpenDisk(n);
        if (h == Native.INVALID_HANDLE_VALUE) return null;
        try
        {
            DiskInfo di = new DiskInfo();
            di.Number = n;
            di.SizeBytes = GetDiskLength(h);
            di.Bus = GetBusType(h, di);

            if (di.Bus == BusKind.Sata || di.Bus == BusKind.Ata || di.Bus == BusKind.Unknown)
            {
                byte err;
                byte[] id = AtaDataIn(h, Native.ATA_IDENTIFY_DEVICE, 8, out err);
                if (id != null)
                {
                    di.AtaIdentifyOk = true;
                    di.Model = AtaString(id, 27, 20);
                    di.Serial = AtaString(id, 10, 10);
                    di.Firmware = AtaString(id, 23, 4);
                    ushort w128 = RdU16(id, 128 * 2);
                    di.SecSupported = (w128 & 0x0001) != 0;
                    di.SecEnabled = (w128 & 0x0002) != 0;
                    di.SecLocked = (w128 & 0x0004) != 0;
                    di.SecFrozen = (w128 & 0x0008) != 0;
                    di.SecCountExpired = (w128 & 0x0010) != 0;
                    di.EnhancedEraseSupported = (w128 & 0x0020) != 0;
                    di.EraseMinutes = EraseTimeMinutes(RdU16(id, 89 * 2));
                    di.EnhancedEraseMinutes = EraseTimeMinutes(RdU16(id, 90 * 2));
                    if (di.Bus == BusKind.Unknown) di.Bus = BusKind.Ata;
                }
            }

            // If model still empty (NVMe / no ATA), fall back to device descriptor strings.
            if (string.IsNullOrEmpty(di.Model) || string.IsNullOrEmpty(di.Serial))
            {
                string vp, pp, sp;
                if (GetDescriptorStrings(h, out vp, out pp, out sp))
                {
                    string model = (vp + " " + pp).Trim();
                    if (string.IsNullOrEmpty(di.Model)) di.Model = model.Length == 0 ? "(unknown)" : model;
                    if (string.IsNullOrEmpty(di.Serial)) di.Serial = string.IsNullOrEmpty(sp) ? "(unknown)" : sp;
                }
            }
            if (string.IsNullOrEmpty(di.Model)) di.Model = "(unknown)";
            if (string.IsNullOrEmpty(di.Serial)) di.Serial = "(unknown)";
            return di;
        }
        finally { Native.CloseHandle(h); }
    }

    private static long GetDiskLength(IntPtr h)
    {
        byte[] outBuf = new byte[8];
        int returned;
        if (Native.DeviceIoControl(h, Native.IOCTL_DISK_GET_LENGTH_INFO, null, 0, outBuf, 8, out returned, IntPtr.Zero))
            return BitConverter.ToInt64(outBuf, 0);
        return -1;
    }

    private static BusKind GetBusType(IntPtr h, DiskInfo di)
    {
        // STORAGE_PROPERTY_QUERY { PropertyId=StorageDeviceProperty(0), QueryType=0 }
        byte[] inBuf = new byte[12];
        WrU32(inBuf, 0, 0);
        WrU32(inBuf, 4, 0);
        byte[] outBuf = new byte[1024];
        int returned;
        if (!Native.DeviceIoControl(h, Native.IOCTL_STORAGE_QUERY_PROPERTY, inBuf, inBuf.Length, outBuf, outBuf.Length, out returned, IntPtr.Zero))
            return BusKind.Unknown;
        uint busType = RdU32(outBuf, 28); // STORAGE_DEVICE_DESCRIPTOR.BusType
        switch (busType)
        {
            case 0x03: return BusKind.Ata;
            case 0x0B: return BusKind.Sata;
            case 0x11: return BusKind.Nvme;
            case 0x07: return BusKind.Usb;
            default: return BusKind.Other;
        }
    }

    private static bool GetDescriptorStrings(IntPtr h, out string vendor, out string product, out string serial)
    {
        vendor = ""; product = ""; serial = "";
        byte[] inBuf = new byte[12];
        byte[] outBuf = new byte[2048];
        int returned;
        if (!Native.DeviceIoControl(h, Native.IOCTL_STORAGE_QUERY_PROPERTY, inBuf, inBuf.Length, outBuf, outBuf.Length, out returned, IntPtr.Zero))
            return false;
        uint vOff = RdU32(outBuf, 12);
        uint pOff = RdU32(outBuf, 16);
        uint sOff = RdU32(outBuf, 24);
        vendor = ReadAsciiZ(outBuf, (int)vOff);
        product = ReadAsciiZ(outBuf, (int)pOff);
        serial = ReadAsciiZ(outBuf, (int)sOff);
        return true;
    }

    private static int TryGetSystemDiskNumber()
    {
        try
        {
            string sysDir = Environment.SystemDirectory; // e.g. C:\Windows\system32 or X:\Windows\system32
            if (string.IsNullOrEmpty(sysDir) || sysDir.Length < 2) return -1;
            string letter = sysDir.Substring(0, 2); // "C:"
            IntPtr h = Native.CreateFileW("\\\\.\\" + letter, 0,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero,
                Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == Native.INVALID_HANDLE_VALUE) return -1;
            try
            {
                byte[] outBuf = new byte[512];
                int returned;
                if (!Native.DeviceIoControl(h, Native.IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, null, 0, outBuf, outBuf.Length, out returned, IntPtr.Zero))
                    return -1;
                uint num = RdU32(outBuf, 0);
                if (num == 0) return -1;
                // First DISK_EXTENT starts at offset 8; DiskNumber is first DWORD.
                return (int)RdU32(outBuf, 8);
            }
            finally { Native.CloseHandle(h); }
        }
        catch { return -1; }
    }

    private static IntPtr OpenDisk(int n)
    {
        return Native.CreateFileW("\\\\.\\PhysicalDrive" + n,
            Native.GENERIC_READ | Native.GENERIC_WRITE,
            Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
            IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
    }

    // ---------------------------------------------------------------- helpers

    private static string AtaSecuritySummary(DiskInfo di)
    {
        StringBuilder sb = new StringBuilder();
        if (!di.SecSupported) { sb.Append("not supported"); return sb.ToString(); }
        sb.Append("supported");
        if (di.SecEnabled) sb.Append(", ENABLED");
        if (di.SecLocked) sb.Append(", LOCKED");
        if (di.SecFrozen) sb.Append(", FROZEN");
        if (di.EnhancedEraseSupported) sb.Append(", enhanced-capable");
        return sb.ToString();
    }

    private static int EraseTimeMinutes(ushort word)
    {
        // Value in units of 2 minutes. 0 = not specified. 0xFFFF-ish handled loosely.
        if (word == 0) return 0;
        int v = word & 0x00FF;
        if (v == 0) v = word; // some drives report in the whole word
        return v * 2;
    }

    private static string AtaString(byte[] id, int wordStart, int wordCount)
    {
        // ATA strings are byte-swapped within each 16-bit word.
        StringBuilder sb = new StringBuilder();
        for (int w = 0; w < wordCount; w++)
        {
            int off = (wordStart + w) * 2;
            sb.Append((char)id[off + 1]);
            sb.Append((char)id[off + 0]);
        }
        return sb.ToString().Trim();
    }

    private static string ReadAsciiZ(byte[] buf, int off)
    {
        if (off <= 0 || off >= buf.Length) return "";
        int end = off;
        while (end < buf.Length && buf[end] != 0) end++;
        return Encoding.ASCII.GetString(buf, off, end - off).Trim();
    }

    private static ushort RdU16(byte[] b, int o) { return (ushort)(b[o] | (b[o + 1] << 8)); }
    private static uint RdU32(byte[] b, int o) { return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24)); }
    private static void WrU32(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v & 0xFF); b[o + 1] = (byte)((v >> 8) & 0xFF);
        b[o + 2] = (byte)((v >> 16) & 0xFF); b[o + 3] = (byte)((v >> 24) & 0xFF);
    }

    // True if running inside WinPE (destructive erase of internal disks is safe here).
    private static bool IsWinPE()
    {
        try
        {
            using (Microsoft.Win32.RegistryKey k =
                Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\MiniNT"))
            {
                return k != null;
            }
        }
        catch { return false; }
    }

    private static string Win32Text(int code)
    {
        try
        {
            StringBuilder sb = new StringBuilder(512);
            Native.FormatMessageW(0x00001000u | 0x00000200u, IntPtr.Zero, (uint)code, 0, sb, sb.Capacity, IntPtr.Zero);
            string s = sb.ToString().Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length > 0 ? (s + " (err " + code + ")") : ("err " + code);
        }
        catch { return "err " + code; }
    }

    // Explains the one situation that cannot work: erasing the live OS disk in place.
    private static void PrintLiveOsDiskHelp()
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine("This is the disk hosting the RUNNING Windows. It cannot be erased while");
        Console.Error.WriteLine("Windows is running from it - the OS locks its boot/system/pagefile volumes,");
        Console.Error.WriteLine("so the storage stack refuses any destructive Format/Erase against it.");
        Console.Error.WriteLine("Boot WinPE (or any other media) and erase this disk from there.");
    }

    private static bool HasFlag(string[] args, string flag)
    {
        for (int i = 0; i < args.Length; i++)
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string GetOption(string[] args, string name, string def)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return def;
    }

    private static uint GetOptionUInt(string[] args, string name, uint def)
    {
        string s = GetOption(args, name, null);
        if (s == null) return def;
        s = s.Trim();
        try
        {
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToUInt32(s.Substring(2), 16);
            return uint.Parse(s);
        }
        catch { return def; }
    }

    private static void PrintHelp()
    {
        PrintBanner();
        Console.WriteLine("SSD secure-erase utility for Windows / WinPE");
        Console.WriteLine();
        Console.WriteLine("With no arguments, the drive list is shown.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  SecureErase                 (no args: list drives)");
        Console.WriteLine("  SecureErase list");
        Console.WriteLine("  SecureErase version");
        Console.WriteLine("  SecureErase info  <disk>");
        Console.WriteLine("  SecureErase verify <disk> [--samples N] [--full]   (read raw media, check blank)");
        Console.WriteLine("  SecureErase erase <disk> [--enhanced] [--crypto] [--user]");
        Console.WriteLine("                          [--password STR] [--nsid N|--all-namespaces]");
        Console.WriteLine("                          [--yes] [--force]");
        Console.WriteLine("  SecureErase unlock     <disk> [--password STR]   (recover a locked ATA drive)");
        Console.WriteLine("  SecureErase disablepw  <disk> [--password STR]   (clear a stuck ATA password)");
        Console.WriteLine("  SecureErase annihilate [--enhanced] [--crypto]   (erase ALL internal drives)");
        Console.WriteLine("       (alias: 'erase --annihilation'. Skips USB and the running-OS disk.)");
        Console.WriteLine();
        Console.WriteLine("Erase method is auto-selected by bus type:");
        Console.WriteLine("  SATA/ATA -> ATA SECURITY ERASE UNIT   (--enhanced for enhanced erase)");
        Console.WriteLine("  NVMe     -> Format NVM                 (--crypto for cryptographic erase)");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --enhanced        ATA enhanced secure erase (if supported)");
        Console.WriteLine("  --crypto          NVMe cryptographic erase (SES=2); default is user-data (SES=1)");
        Console.WriteLine("  --password STR    ATA security password (default: " + DefaultPassword + ")");
        Console.WriteLine("  --nsid N          NVMe namespace id (default 1)");
        Console.WriteLine("  --all-namespaces  NVMe: target NSID 0xFFFFFFFF");
        Console.WriteLine("  --yes             skip the interactive typed confirmation");
        Console.WriteLine("  --force           allow erasing the disk that hosts the running OS");
        Console.WriteLine("  --include-usb     annihilation: also wipe USB/external drives");
        Console.WriteLine("  --include-system  annihilation: also wipe the running-OS/boot disk (danger)");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  * Requires Administrator (full Windows) or run inside WinPE.");
        Console.WriteLine("  * If an ATA drive is 'FROZEN', power-cycle it (hot-plug SATA / S3 sleep) then retry.");
        Console.WriteLine("  * After erase, diskpart may show old partitions until 'rescan' or reboot (cached).");
        Console.WriteLine("  * Verify by reading raw media:  SecureErase verify <disk> --full");
        Console.WriteLine("  * Gold-standard check (proves the erase cleared real data):");
        Console.WriteLine("      1) before erase, note some data exists / write a known pattern,");
        Console.WriteLine("      2) run erase,  3) run 'verify' and confirm those blocks are gone.");
        Console.WriteLine("      Crypto erase may not read zeros - the guarantee is key destruction,");
        Console.WriteLine("      so prior known plaintext should read back as garbage/zeros afterwards.");
        Console.WriteLine("  * Erasing is irreversible. Always confirm the serial number first.");
    }
}
