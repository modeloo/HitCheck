# HitCheck

Comprehensive system scanning tool for detecting suspicious applications and malicious artifacts on Windows. Analyzes process memory, file system, browser history, and recycle bin to identify unauthorized software.

- **Windows Services Audit**: Audits critical monitoring services (`PcaSvc`, `DPS`, `SysMain`, `bam`, `EventLog`, `DiagTrack`) to detect tampering or disabled logging
- **Services Memory Scanning**: Examines memory of key Windows services (`BFE`, `DPS`, `DcomLaunch`, `SearchIndexer`) for launched jar cheats, DPS execution traces, and cleaner tools (e.g. `cleanerdps.exe`)
- **Process Memory Scanning**: Examines memory of system processes and Java applications (`javaw.exe`, `explorer.exe`) for embedded detection signatures
- **DLL Module & Unloaded Modules Analysis**: Inspects loaded and unloaded DLLs in `javaw.exe` via NT APIs (`RtlQueryProcessDebugInformation`) for suspicious hitbox weights (1.42MB, 1.43MB, 1.56MB, 1.89MB) and empty file descriptions
- **Open/Save Dialog MRU Inspection**: Analyzes `ComDlg32\OpenSavePidlMRU` registry artifacts for recently injected/opened cheat DLLs and executables
- **Browser History Analysis**: Reads browser history from Chrome, Edge, Firefox, Safari, Opera, Brave and other Chromium-based browsers to identify visited suspicious sites
- **Suspicious File Detection**: Identifies recently deleted cheat files and suspicious executables/archives based on targeted signature keywords
- **Recycle Bin Monitoring**: Detects cheat files deleted within the last 30 minutes before scan execution
- **Parallel Processing**: Multi-threaded memory scanning across all CPU cores for fast analysis
- **Low False Positives**: Context-aware matching to eliminate false positives on legitimate mods and game archives

## Usage

```
HitCheck.exe            scan default targets (system processes, services + browser)
HitCheck.exe --deep     include all browser child processes (slower)
HitCheck.exe --all      scan all accessible processes
HitCheck.exe --list     list targets and service status without performing scan
HitCheck.exe --help     show command help
```

### Examples

```
HitCheck.exe                           # Quick scan (recommended: run as Administrator)
HitCheck.exe --deep                    # Thorough scan with browser process memory
HitCheck.exe chrome.exe javaw.exe      # Scan specific processes
HitCheck.exe --all > report.txt        # Comprehensive scan with output
```

## Output

Generates a detailed report including:

- **Windows Services Audit**: Real-time status of critical telemetry and forensic services
- **Suspicious Files & Modules**: Detected cheat executables/archives and injected/unloaded DLLs
- **Cheat Websites Visited**: Sites detected in browser history with example URLs
- **Threat Signatures & DPS Records**: Known cheat signatures, ASM hooks, and DPS execution history
- **Verdict**: Risk assessment (CLEAN / INCONCLUSIVE / SUSPICIOUS)

Each finding includes:
- Severity rating (HIGH / MEDIUM / LOW confidence)
- Relevant process names
- Example strings or URLs for verification

## Requirements

- Windows 10 / Windows 11
- Administrator rights recommended for full system coverage
- .NET Framework 4.0+ (included with Windows)

## Building

```
build.bat
```

Or compile directly:
```
csc /platform:x64 /optimize+ /out:HitCheck.exe src\HitCheck.cs
```

## Technical Details

- **Read-Only**: Tool only reads memory, never modifies processes or system state
- **Parallel Scanning**: Uses all CPU cores for fast memory analysis
- **History Database**: Safely reads browser history files through memory-mapped access
- **Context Filtering**: Distinguishes between legitimate brand names and actual threats
- **No Dependencies**: Single executable, no runtimes or external libraries required

## Notes

- A "CLEAN" verdict does not prove absence of unauthorized software (memory can be cleared, dormant processes, etc.)
- Tool assists human reviewers - findings should be manually verified
- Browser history detection identifies visited sites, not necessarily installed applications
- Compatible with common Windows browsers (Chrome, Edge, Firefox, Opera, Brave, Vivaldi, Yandex, Safari)

## License

Usage at your own risk. Intended for authorized system administration and security analysis only.
