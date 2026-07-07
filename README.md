<h1 align="center">🔥 SecureErase</h1>

<p align="center">
  <strong>Native, single-file SSD secure-erase utility for Windows &amp; WinPE.</strong><br>
  Talks straight to the drive — no MSBuild, no NuGet, no dependencies.
</p>

<p align="center">
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows%20%7C%20WinPE-0078D6?logo=windows&logoColor=white">
  <img alt="Language" src="https://img.shields.io/badge/language-C%23%205.0-239120?logo=csharp&logoColor=white">
  <img alt="Framework" src="https://img.shields.io/badge/.NET%20Framework-4.x-512BD4?logo=dotnet&logoColor=white">
  <img alt="Build" src="https://img.shields.io/badge/build-csc.exe-lightgrey">
  <img alt="Version" src="https://img.shields.io/badge/version-1.0.0-blue">
  <img alt="License" src="https://img.shields.io/badge/license-MIT-green">
</p>

---

> ⚠️ **This tool permanently destroys data.** There is no undo, no recycle bin, no recovery.
> Always confirm the drive's serial number first, and prefer running from **WinPE**.

---

## ✨ Features

- 🧭 **Auto-detects the erase method by bus type** — you don't pick the protocol, the drive's interface does.
  - **SATA / ATA SSDs** → `ATA SECURITY ERASE UNIT` (normal or enhanced)
  - **NVMe SSDs** → `Format NVM` (user-data or cryptographic erase)
- 🔍 **Built-in verification** — reads the raw media (bypassing the OS cache) and reports whether it comes back blank.
- 💥 **Annihilation mode** — securely wipe **all internal drives** unattended, each auto-verified.
- 🛟 **Recovery commands** — unlock or clear a stuck ATA password if an erase is interrupted.
- 🧊 **Frozen-state detection** — tells you when the BIOS froze the drive and how to fix it.
- 🪶 **Tiny & self-contained** — one `.cs` file, compiles with the in-box `csc.exe`. Perfect for a WinPE image.
- 🛡️ **Safety rails** — refuses the running-OS disk, requires typed confirmation, shows model/serial/size before acting.

## 📁 What's in the box

| File | Purpose |
|------|---------|
| `SecureErase.cs` | The entire program (single source file). |
| `build.cmd` | Compiles it with the framework `csc.exe`; auto-picks x86/x64. |
| `app.manifest` | Requests Administrator on full Windows (no-op in WinPE). |
| `README.md` | This file. |

## 📑 Table of contents

- [Quick start](#-quick-start)
- [Build](#-build)
- [Commands](#-commands)
- [Options](#-options)
- [Examples](#-examples)
- [How it works](#-how-it-works)
- [Verifying an erase](#-verifying-an-erase)
- [Annihilation mode](#-annihilation-mode)
- [Safety &amp; caveats](#-safety--caveats)
- [License](#-license)

## 🚀 Quick start

```bat
:: Build (run inside your target WinPE, or pass the arch explicitly)
build.cmd

:: See what's connected (this is also the default when run with no args)
SecureErase.exe

:: Inspect one drive
SecureErase.exe info 1

:: Erase it (you'll be asked to type: ERASE DISK 1)
SecureErase.exe erase 1

:: Prove it worked
SecureErase.exe verify 1 --full
```

## 🔧 Build

No Visual Studio required — just the .NET Framework 4.x `csc.exe` that ships with Windows/WinPE.

```bat
build.cmd            :: auto-detect x86/x64 from the current machine
build.cmd x86        :: force a 32-bit binary  (for 32-bit WinPE)
build.cmd x64        :: force a 64-bit binary  (for 64-bit WinPE)
```

The script locates `csc.exe` under `%WINDIR%\Microsoft.NET\Framework(64)\v4.0.30319`, embeds
`app.manifest`, and emits `SecureErase.exe`.

> 💡 **Match the binary to the WinPE architecture.** Running an x64 exe on a 32-bit WinPE throws
> *"unsupported 16-bit application"*. If unsure, run `build.cmd` **inside** the target WinPE so it
> auto-detects correctly. Check with `echo %PROCESSOR_ARCHITECTURE%` (`x86` vs `AMD64`).

## 🧰 Commands

| Command | Description |
|---------|-------------|
| *(no args)* / `list` | List physical drives with bus type, size, and ATA security status. |
| `info <disk>` | Detailed IDENTIFY / security state for one drive. |
| `verify <disk>` | Read raw media and check whether it's blank. `--full` reads every block. |
| `erase <disk>` | Secure-erase one drive (method auto-selected by bus type). |
| `annihilate` | Secure-erase **all internal drives**, unattended, each auto-verified. |
| `unlock <disk>` | `SECURITY UNLOCK` — recover a drive locked by an interrupted erase. |
| `disablepw <disk>` | `SECURITY DISABLE PASSWORD` — clear a stuck ATA password. |
| `version` | Print version and copyright. |
| `help` | Full usage. |

## ⚙️ Options

| Option | Applies to | Meaning |
|--------|-----------|---------|
| `--enhanced` | ATA erase | Use enhanced secure erase (if supported). |
| `--crypto` | NVMe erase | Cryptographic erase (SES=2). Default is user-data (SES=1). |
| `--user` | NVMe erase | Force user-data erase (overrides `--crypto`). |
| `--password STR` | ATA | Security password (default `SecureErasePwd`). Auto-cleared after erase. |
| `--nsid N` | NVMe | Target namespace ID (default `1`). |
| `--all-namespaces` | NVMe | Target NSID `0xFFFFFFFF`. |
| `--samples N` | verify | Number of blocks to sample (default `512`). |
| `--full` | verify | Read **every** block instead of sampling. |
| `--yes` | erase | Skip the interactive typed confirmation. |
| `--force` | erase | Allow erasing the disk that hosts the running OS. |
| `--include-usb` | annihilate | Also wipe USB/external drives. |
| `--include-system` | annihilate | Also wipe the running-OS/boot disk. **Dangerous.** |

## 💡 Examples

```bat
:: List everything (default action)
SecureErase.exe

:: Enhanced ATA erase, no prompt (scripted)
SecureErase.exe erase 1 --enhanced --yes

:: NVMe cryptographic erase across all namespaces
SecureErase.exe erase 2 --crypto --all-namespaces

:: Exhaustive verification of a single drive
SecureErase.exe verify 2 --full

:: Wipe every internal drive in the machine, then auto-verify each
SecureErase.exe annihilate

:: Recover a drive left locked by an interrupted erase
SecureErase.exe disablepw 1 --password SecureErasePwd
```

## 🛠 How it works

| Drive type | Mechanism | Windows IOCTL |
|-----------|-----------|---------------|
| SATA / ATA | `SECURITY SET PASSWORD` → `ERASE PREPARE` → `ERASE UNIT` | `IOCTL_ATA_PASS_THROUGH` |
| NVMe | `Format NVM` with Secure Erase Settings (SES) | `IOCTL_STORAGE_PROTOCOL_COMMAND` |
| Verify | Aligned raw sector reads, cache-bypassed | `ReadFile` + `FILE_FLAG_NO_BUFFERING` |
| Refresh | Re-read partition table after erase | `IOCTL_DISK_UPDATE_PROPERTIES` |

Everything is issued through P/Invoke to the Win32 storage stack — no third-party binaries.

## 🔍 Verifying an erase

The cached partition table is **not** proof. Verify by reading the raw media:

```bat
SecureErase.exe verify 1            :: fast, samples blocks across the drive
SecureErase.exe verify 1 --full     :: definitive, reads every block
```

- **User-data / ATA erase** → expect **blank** reads (all-zero, or all-`0xFF` on some drives).
  For NVMe, `verify` prints the drive's `DLFEAT` so you know which to expect.
- **Cryptographic erase** → does **not** guarantee zeroed reads. The guarantee is *key destruction*,
  so old data becomes unrecoverable ciphertext that may read as zeros **or** random bytes.

> 🥇 **Gold-standard check:** before erasing, note that real data exists (or write a known pattern);
> erase; then `verify` those same locations and confirm the data is gone. This proves the erase
> actually cleared previously-written content — the read-back verification expected by
> **NIST SP 800-88**.

## 💥 Annihilation mode

```bat
SecureErase.exe annihilate [--enhanced] [--crypto]
:: identical alias:
SecureErase.exe erase --annihilation
```

1. Enumerates `PhysicalDrive0..31` and builds the target list.
2. **Excludes USB/external and the running-OS/boot disk by default.**
3. Prints exactly what will be erased (model / serial / size / bus).
4. Runs a **5-second unattended countdown** (Ctrl+C to abort — it's not a prompt).
5. Erases each drive with the right method, then **auto-runs a basic verification**.
6. Prints `X erased+verified, Y failed/suspect` and returns a non-zero exit code on any failure.

> In WinPE the "running-OS disk" is the RAM/boot media — so your internal Windows disk **is** still
> targeted. The exclusion protects the device you booted from, not the drives you mean to wipe.

## 🛡 Safety & caveats

- **Requires Administrator** (full Windows) or a **WinPE** session.
- **eSATA / Thunderbolt** drives may report as *internal* — annihilation lists every target; physically verify first.
- **Interrupted ATA erase** can leave a drive password-locked → recover with `disablepw`.
- A sampled verify is statistical evidence, not proof of every sector — use `--full` for formal decommissioning.
- Provided **as-is, no warranty**. Test on a scratch drive before trusting it on anything that matters.

## 📄 License

Released under the **MIT License**.

```
Copyright (c) 2026 inteliboy

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

<p align="center"><sub>SecureErase v1.0.0 — Copyright © 2026 inteliboy</sub></p>
