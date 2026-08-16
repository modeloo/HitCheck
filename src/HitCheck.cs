












using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

static class HitCheck
{
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, IntPtr size, out IntPtr read);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr VirtualQueryEx(IntPtr h, IntPtr addr, out MEMORY_BASIC_INFORMATION mbi, IntPtr len);
    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr proc, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool LookupPrivilegeValue(string sys, string name, out LUID luid);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES nw, uint len, IntPtr prev, IntPtr ret);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    static extern IntPtr OpenSCManager(string machineName, string databaseName, uint desiredAccess);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    static extern IntPtr OpenService(IntPtr scm, string serviceName, uint desiredAccess);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool QueryServiceStatusEx(IntPtr hService, int infoLevel, IntPtr buffer, uint bufSize, out uint bytesNeeded);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool CloseServiceHandle(IntPtr h);

    [DllImport("ntdll.dll")]
    static extern IntPtr RtlCreateQueryDebugBuffer(uint size, bool eventPair);
    [DllImport("ntdll.dll")]
    static extern int RtlQueryProcessDebugInformation(int pid, uint debugInfoClassMask, IntPtr debugBuffer);
    [DllImport("ntdll.dll")]
    static extern int RtlDestroyQueryDebugBuffer(IntPtr debugBuffer);

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORY_BASIC_INFORMATION
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public uint __align1;
        public IntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
        public uint __align2;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct LUID { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)]
    struct TOKEN_PRIVILEGES { public uint Count; public LUID Luid; public uint Attributes; }

    [StructLayout(LayoutKind.Sequential)]
    struct SERVICE_STATUS_PROCESS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
        public uint dwProcessId;
        public uint dwServiceFlags;
    }

    const uint PROCESS_QUERY_INFORMATION = 0x0400;
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const uint PROCESS_VM_READ = 0x0010;
    const uint MEM_COMMIT = 0x1000;
    const uint MEM_IMAGE = 0x1000000;
    const uint MEM_MAPPED = 0x40000;
    const uint MEM_PRIVATE = 0x20000;
    const uint PAGE_NOACCESS = 0x01;
    const uint PAGE_GUARD = 0x100;
    const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    const uint TOKEN_QUERY = 0x0008;
    const uint SE_PRIVILEGE_ENABLED = 0x0002;

    const int MIN_LEN = 4;
    const int MAX_STR = 4096;
    const int CHUNK = 8 * 1024 * 1024;
    const int OVL = 8192;
    const long MAX_PER_PROC = 6L * 1024 * 1024 * 1024;
    const int MAX_EXAMPLES = 8;

    static readonly string[] DefaultTargets = {
        "explorer", "javaw", "java",
        "chrome", "msedge", "firefox", "opera", "opera_gx", "browser",
        "brave", "vivaldi", "yandex", "iexplore"
    };

    static readonly string[] BrowserNames = {
        "chrome", "msedge", "firefox", "opera", "opera_gx",
        "brave", "vivaldi", "yandex", "browser", "iexplore"
    };

    static readonly object ResultLock = new object();

    static readonly string[] CheatHostDomains = {
        "vape.gg", "everlack.in", "neverlack.in", "doomsdayclient.com", "cortexclient.com",
        "nemezida.ru", "nemezida.cc", "dreampoolhack.ru", "akrien.wtf", "takker.ru", "ammit.cc",
        "stubborn.website", "meteorclient.com", "liquidbounce.net", "aristois.net",
        "wurstclient.net", "novoline.ru", "expensive.lol", "moon.vin", "rise.ovh"
    };

    static readonly string[] CheatUrlPatterns = {
        "vk.com/avaloneclient", "vk.com/norender", "vk.com/troxill", "vk.com/ammitclient",
        "cortexclient.com/account", "doomsdayclient.com/loader"
    };

    class Sig { public string Cat, Label, Conf, Mode; public Sig(string c, string l, string k, string m){Cat=c;Label=l;Conf=k;Mode=m;} }
    static readonly Dictionary<string, Sig> SigLookup = new Dictionary<string, Sig>(StringComparer.OrdinalIgnoreCase);
    static Regex SigRegex;

    static void BuildSignatures()
    {
        var defs = new[] {
            new[]{"(Ljava/lang/Class<*>;Ljava/lang/String;Ljava/lang/Object;)V","SIG","Self-written cheat signature","HIGH","ANY"},

            new[]{"killaura",                      "SIG","KillAura module","HIGH","ANY"},
            new[]{"invisiblehitbox",               "SIG","InvisibleHitbox module","HIGH","ANY"},
            new[]{"triggerbot",                    "SIG","TriggerBot module","HIGH","ANY"},
            new[]{"aimassist",                     "SIG","AimAssist module","HIGH","ANY"},
            new[]{"autoclicker",                   "SIG","AutoClicker module","MED","ANY"},

            new[]{"vape4dll",                      "SIG","VapeClient V4","HIGH","ANY"},
            new[]{"faketapemouse",                 "SIG","FakeTapeMouse (hitbox+trigger)","HIGH","ANY"},
            new[]{".tapemouse",                    "SIG","FakeTapeMouse namespace","HIGH","ANY"},
            new[]{"clownclient",                   "SIG","ClownClient","HIGH","ANY"},
            new[]{"deadcode",                      "SIG","DEADCODE client","HIGH","CTX"},
            new[]{"doomsday",                      "SIG","DoomsDay client","HIGH","CTX"},
            new[]{"nemezida",                      "SIG","Nemezida","HIGH","ANY"},
            new[]{"cortex",                        "SIG","Cortex client","HIGH","CTX"},
            new[]{"avalone",                       "SIG","Avalon(e) client","HIGH","CTX"},
            new[]{"liquidbounce",                  "SIG","LiquidBounce","HIGH","ANY"},
            new[]{"wurst",                         "SIG","Wurst client","HIGH","CTX"},
            new[]{"meteorclient",                  "SIG","Meteor client","HIGH","ANY"},
            new[]{"aristois",                      "SIG","Aristois","HIGH","ANY"},
            new[]{"novoline",                      "SIG","Novoline","HIGH","ANY"},
            new[]{"exhibition",                    "SIG","Exhibition","HIGH","CTX"},
            new[]{"celestial",                     "SIG","Celestial","HIGH","CTX"},
            new[]{"tenacity",                      "SIG","Tenacity","HIGH","CTX"},
            new[]{"huzuni",                        "SIG","Huzuni","HIGH","ANY"},

            new[]{"bushroot",                      "SIG","bushroot [hitbox]","HIGH","CTX"},
            new[]{"clowdy",                        "SIG","ClowdyClient","HIGH","CTX"},
            new[]{"derick1337",                    "SIG","Derick1337 [hitbox]","HIGH","ANY"},
            new[]{"allatori",                      "SIG","allatori [obfuscation mod]","MED","CTX"},
            new[]{"stubborn.website",              "SIG","stubborn.website [cortex]","HIGH","ANY"},
            new[]{"baobab",                        "SIG","baobab [hitbox]","MED","CTX"},
            new[]{"okuma:",                        "SIG","okuma [Hitbox]","HIGH","PFX"},
            new[]{"walvbt#",                       "SIG","Walvbt# [AnanaV2]","HIGH","ANY"},
            new[]{"swqxnv",                        "SIG","SWqxNv [doomsday]","HIGH","ANY"},
            new[]{"onikoasp",                      "SIG","oNIkoasP [doomsday]","HIGH","ANY"},
            new[]{"yiqdgferojr",                   "SIG","yIQDgFEROJr [doomsday]","HIGH","ANY"},
            new[]{"reach:",                        "SIG","reach: [vert client]","MED","PFX"},
            new[]{"hitbox:",                       "SIG","hitbox: [vert client]","MED","PFX"},
            new[]{"az85",                          "SIG","Az85 [AnanaV4]","LOW","EXACT"},
            new[]{"71l",                           "SIG","71L [AnanaV4]","LOW","EXACT"},
            new[]{"#hit",                          "SIG","#Hit [AnanaV2]","LOW","EXACT"},
            new[]{"chs/main",                      "SIG","chs/main [Vertzah]","MED","CTX"},
            new[]{"pastebin",                      "SIG","pastebin [NoRender Lite]","LOW","CTX"},

            // Cortex Lite strings from manual
            new[]{"xEnzy",                         "SIG","xEnzy [Cortex Lite]","HIGH","ANY"},
            new[]{"(O9XD",                         "SIG","(O9XD [Cortex Lite]","HIGH","ANY"},
            new[]{"~WIr",                          "SIG","~WIr [Cortex Lite]","HIGH","ANY"},
            new[]{"{7K[c",                         "SIG","{7K[c [Cortex Lite]","HIGH","ANY"},
            new[]{"]A[XAY",                        "SIG","]A[XAY [Cortex Lite]","HIGH","ANY"},

            // Sites from manual
            new[]{"cortexclient.com",              "SITE","cortexclient.com","HIGH","ANY"},
            new[]{"vk.com/avaloneclient",          "SITE","vk.com/avaloneclient","HIGH","ANY"},
            new[]{"vk.com/norender",               "SITE","vk.com/norender","HIGH","ANY"},
            new[]{"vk.com/troxill",                "SITE","vk.com/troxill","HIGH","ANY"},
            new[]{"vk.com/ammitclient",            "SITE","vk.com/ammitclient","HIGH","ANY"},
            new[]{"doomsdayclient.com",            "SITE","doomsdayclient.com","HIGH","ANY"},
            new[]{"ammit.cc",                      "SITE","ammit.cc","HIGH","ANY"},
            new[]{"takker.ru",                     "SITE","takker.ru","HIGH","ANY"},
            new[]{"akrien.wtf",                    "SITE","akrien.wtf","HIGH","ANY"},
            new[]{"dreampoolhack.ru",              "SITE","dreampoolhack.ru","HIGH","ANY"},
            new[]{"everlack.in",                   "SITE","everlack.in","HIGH","ANY"},
            new[]{"neverlack.in",                  "SITE","neverlack.in","HIGH","ANY"},
            new[]{"meteorclient.com",              "SITE","meteorclient.com","HIGH","ANY"},
            new[]{"vape.gg",                       "SITE","vape.gg","HIGH","ANY"},
            new[]{"nemezida.ru",                   "SITE","nemezida.ru","HIGH","ANY"},
            new[]{"nemezida.cc",                   "SITE","nemezida.cc","HIGH","ANY"},
        };

        var patterns = new List<string>();
        foreach (var d in defs)
        {
            string tok = d[0];
            if (!SigLookup.ContainsKey(tok)) SigLookup[tok] = new Sig(d[1], d[2], d[3], d[4]);
            patterns.Add(tok);
        }

        patterns.Sort((a, b) => b.Length.CompareTo(a.Length));
        var sb = new StringBuilder();
        for (int i = 0; i < patterns.Count; i++)
        {
            if (i > 0) sb.Append('|');
            string esc = Regex.Escape(patterns[i]);

            if (patterns[i].Equals("aimassist", StringComparison.OrdinalIgnoreCase) ||
                patterns[i].Equals("triggerbot", StringComparison.OrdinalIgnoreCase) ||
                patterns[i].Equals("killaura", StringComparison.OrdinalIgnoreCase) ||
                patterns[i].Equals("autoclicker", StringComparison.OrdinalIgnoreCase) ||
                patterns[i].Equals("invisiblehitbox", StringComparison.OrdinalIgnoreCase))
            {
                esc = @"(?<![a-zA-Z0-9_])" + esc + @"(?![a-zA-Z0-9_])";
            }
            sb.Append(esc);
        }
        SigRegex = new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    static readonly Regex FileRegex = new Regex(
        @"file:\/\/\/?(?<u>[A-Za-z]:[\\/][^\s""'<>|\r\n*?]*?\.(?:jar|rar|exe|zip|dll|js|bat|cmd|ps1|msi|vbs))(?![A-Za-z0-9])" +
        @"|(?<p>[A-Za-z]:\\[^\s""'<>|\r\n*?]*?\.(?:jar|rar|exe|zip|dll|js|bat|cmd|ps1|msi|vbs))(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex FileNameKeyword = new Regex(
        @"(?:vapeclient|vapev\d|vape_|nemezida|norender|liquidbounce|wurstclient|" +
        @"aristois|novoline|exhibition|deadcode|clownclient|clowdy|augustus|" +
        @"akrien|dreampool|everlack|neverlack|troxill?|ammit|huzuni|doomsday|" +
        @"killaura|aimbot|aimassist|autoclick|triggerbot|faketapemouse|injector|cheatengine|" +
        @"dauntiblyat|renamemeplease|123\.dll|vec\.dll|osuautorender|editme\.dll|lb3\.dll|" +
        @"cleanerdps|bushroot|nursultan|jigsawclient|\bexpensive\b|\bfluegel\b|" +
        @"(?<!anti[-_]?)(?:cheat|hack)(?!er\b))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex DisplayTextRegex = new Regex(
        @"\{""displayText"":""(?<name>[^""]+?\.(?:exe|jar|dll))""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex DpsRecordRegex = new Regex(
        @"!(?:!(?<name>[^! \r\n\t]+?\.(?:exe|jar|dll))!(?<date>\d{4}/\d{2}/\d{2}:\d{2}:\d{2}:\d{2})!0!|(?<name>[^! \r\n\t]+?\.(?:exe|jar|dll))!\d{4}/\d{2}/\d{2}:\d{2}:\d{2}:\d{2}!)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly string[] SystemDirs = {
        @"\windows\", @"\program files\", @"\program files (x86)\", @"\programdata\microsoft\",
        @"\libraries\", @"\assets\", @"\natives", @"\versions\", @"\meta\",
        @"\.fabric\", @"\processedmods\", @"\.mixin", @"\bin\", @"\patched\",
        @"\.gradle\", @"\gradle\caches\", @"\.m2\", @"\.ivy2\", @"\.nuget\", @"\node_modules\"
    };

    class Finding { public string Cat, Label, Conf; public int Count; public HashSet<string> Procs = new HashSet<string>(); public List<string> Examples = new List<string>(); }
    class FileHit { public string Dir, Name, Reason, BinPath; public int DelMin = -1; public HashSet<string> Procs = new HashSet<string>(); }
    class ServiceAudit { public string Name, DisplayName, Status; public int Pid; public bool Suspicious; }

    static readonly Dictionary<string, Finding> Sigs = new Dictionary<string, Finding>();
    static readonly Dictionary<string, FileHit> Files = new Dictionary<string, FileHit>(StringComparer.OrdinalIgnoreCase);
    static readonly List<ServiceAudit> ServicesReport = new List<ServiceAudit>();
    static DateTime Started;

    static void AddSig(Sig s, string context, string proc)
    {
        lock (ResultLock)
        {
            Finding f;
            if (!Sigs.TryGetValue(s.Label, out f)) { f = new Finding{Cat=s.Cat, Label=s.Label, Conf=s.Conf}; Sigs[s.Label] = f; }
            f.Count++;
            f.Procs.Add(proc);
            if (f.Examples.Count < MAX_EXAMPLES)
            {
                string ex = Clean(context);
                if (!f.Examples.Contains(ex)) f.Examples.Add(ex);
            }
        }
    }

    static void AddFile(string full, string reason, string proc)
    {
        lock (ResultLock)
        {
            string key = full.ToLowerInvariant();
            FileHit h;
            if (!Files.TryGetValue(key, out h))
            {
                int i = full.LastIndexOf('\\');
                string dir = i > 0 ? full.Substring(0, i) : "(unknown)";
                string name = i >= 0 ? full.Substring(i + 1) : full;
                h = new FileHit{Dir=dir, Name=name, Reason=reason};
                Files[key] = h;
            }
            if (proc != null) h.Procs.Add(proc);
        }
    }

    static void AddDeleted(string full, string reason, int delMin, string binPath)
    {
        lock (ResultLock)
        {
            string key = "del::" + full.ToLowerInvariant();
            if (Files.ContainsKey(key)) return;
            int i = full.LastIndexOf('\\');
            string dir = i > 0 ? full.Substring(0, i) : "(unknown)";
            string name = i >= 0 ? full.Substring(i + 1) : full;
            Files[key] = new FileHit{Dir=dir, Name=name, Reason=reason, DelMin=delMin, BinPath=binPath};
        }
    }

    static string Clean(string s)
    {
        if (s.Length > 200) s = s.Substring(0, 200) + "...";
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) sb.Append(c < 0x20 ? ' ' : c);
        return sb.ToString().Trim();
    }

    static bool HasCheatContext(string low)
    {
        return low.IndexOf(".jar", StringComparison.Ordinal) >= 0
            || low.IndexOf("\\mods\\", StringComparison.Ordinal) >= 0
            || low.IndexOf("/mods/", StringComparison.Ordinal) >= 0
            || low.IndexOf(".minecraft", StringComparison.Ordinal) >= 0
            || low.IndexOf("net.minecraft", StringComparison.Ordinal) >= 0
            || low.IndexOf("minecraftforge", StringComparison.Ordinal) >= 0
            || low.IndexOf("labymod", StringComparison.Ordinal) >= 0;
    }

    static bool LooksLikeFilePath(string s)
    {
        return s.IndexOf(":\\", StringComparison.Ordinal) >= 0
            || s.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsAnticheatOrTool(string low)
    {
        return low.Contains("anticheat") || low.Contains("anti-cheat") || low.Contains("grimac") ||
               low.Contains("vulcan") || low.Contains("processhacker") || low.Contains("systeminformer");
    }

    static bool IsAnticheatFalsePositive(string s)
    {
        string low = s.ToLowerInvariant();
        if (Regex.IsMatch(s, @"\b7\s+(?:KillAura|Velocity|HitBox|TriggerBot|Reach|AimAssist)\b", RegexOptions.IgnoreCase))
            return true;
        if (Regex.IsMatch(s, @"\bAG\s*\((?:KillAura|Velocity|HitBox|TriggerBot|Reach|AimAssist)\)", RegexOptions.IgnoreCase))
            return true;
        if (low.Contains("anticheat") || low.Contains("anti-cheat") || low.Contains("grimac") ||
            low.Contains("vulcan") || low.Contains("matrix anticheat") || low.Contains("karhu"))
            return true;
        return false;
    }

    static void ScanString(string s, string proc) { ScanString(s, proc, false); }

    static void ScanString(string s, string proc, bool history)
    {
        if (history)
            MatchHistoryUrls(s, proc);
        else if (proc.StartsWith("java", StringComparison.OrdinalIgnoreCase))
            MatchSignatures(s, proc);

        // Check explorer.exe displayText launch artifacts (Theme 8.1)
        if (proc.IndexOf("explorer", StringComparison.OrdinalIgnoreCase) >= 0 && s.IndexOf("displayText", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            foreach (Match m in DisplayTextRegex.Matches(s))
            {
                string fn = m.Groups["name"].Value;
                if (FileNameKeyword.IsMatch(fn) && !IsAnticheatOrTool(fn.ToLowerInvariant()))
                {
                    AddSig(new Sig("EXPLORER", "Suspicious execution entry in explorer: " + fn, "HIGH", "ANY"), fn, proc);
                }
            }
        }

        // Check DPS records in svchost / services (Theme 9.4)
        if (s.IndexOf("!0!", StringComparison.Ordinal) >= 0 || s.IndexOf("cleanerdps", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            foreach (Match m in DpsRecordRegex.Matches(s))
            {
                string fn = m.Groups["name"].Value;
                string dt = m.Groups["date"].Value;
                if (!string.IsNullOrEmpty(fn))
                {
                    string low = fn.ToLowerInvariant();
                    if (!IsAnticheatOrTool(low))
                    {
                        if (low.Contains("cleanerdps"))
                            AddSig(new Sig("DPS", "DPS cleaner utility trace detected: " + fn, "HIGH", "ANY"), fn + " (" + dt + ")", proc);
                        else if (FileNameKeyword.IsMatch(fn))
                            AddSig(new Sig("DPS", "Cheat execution recorded in DPS: " + fn, "HIGH", "ANY"), fn + " (" + dt + ")", proc);
                    }
                }
            }
        }

        if (s.IndexOf("file:", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf(":\\", StringComparison.Ordinal) >= 0)
        {
            foreach (Match m in FileRegex.Matches(s))
            {
                string raw = m.Groups["u"].Success ? m.Groups["u"].Value : m.Groups["p"].Value;
                HandleFile(raw, proc);
            }
        }
    }

    static void MatchSignatures(string s, string proc)
    {
        if (IsAnticheatFalsePositive(s)) return;
        if (s.IndexOf("[*.]", StringComparison.Ordinal) >= 0) return;

        string low = null;

        // Specialized ASM check for ClownClient / DEADCODE / TapeMouse / Cheats (Themes 8.3, 8.4, 8.6)
        if (s.StartsWith("ASM:", StringComparison.OrdinalIgnoreCase))
        {
            string after = s.Substring(4).Trim();
            if (after.Length == 0)
            {
                if (s.Length >= 100)
                    AddSig(new Sig("SIG", "ClownClient (empty ASM result > 100)", "HIGH", "ANY"), s, proc);
                else if (s.Length == 10)
                    AddSig(new Sig("SIG", "DEADCODE (empty ASM result = 10)", "HIGH", "ANY"), s, proc);
            }
            else
            {
                if (low == null) low = s.ToLowerInvariant();
                if (low.Contains("extension") && low.Contains("tapemouse"))
                    AddSig(new Sig("SIG", "FakeTapeMouse (ASM Extension hook)", "HIGH", "ANY"), s, proc);
                else if (low.Contains("killaura") || low.Contains("aura"))
                    AddSig(new Sig("SIG", "ASM hook w/ KillAura", "HIGH", "ANY"), s, proc);
                else if (low.Contains("hitbox"))
                    AddSig(new Sig("SIG", "ASM hook w/ HitBox", "HIGH", "ANY"), s, proc);
                else if (low.Contains("trigger"))
                    AddSig(new Sig("SIG", "ASM hook w/ TriggerBot", "HIGH", "ANY"), s, proc);
                else if (low.Contains("reach"))
                    AddSig(new Sig("SIG", "ASM hook w/ Reach", "HIGH", "ANY"), s, proc);
                else if (low.Contains("axisalignedbb") || low.Contains("setplayer"))
                    AddSig(new Sig("SIG", "ASM hook modifying player bounding box", "HIGH", "ANY"), s, proc);
            }
        }

        if (!SigRegex.IsMatch(s)) return;

        foreach (Match m in SigRegex.Matches(s))
        {
            Sig sig;
            if (!SigLookup.TryGetValue(m.Value, out sig)) continue;

            if (sig.Cat == "SIG" && LooksLikeFilePath(s)) continue;
            if (sig.Mode == "PFX")
            {
                if (!s.TrimStart().StartsWith(m.Value, StringComparison.OrdinalIgnoreCase)) continue;
            }
            else if (sig.Mode == "EXACT")
            {
                if (!s.Trim().Equals(m.Value, StringComparison.OrdinalIgnoreCase)) continue;
            }
            else if (sig.Mode == "CTX")
            {
                if (low == null) low = s.ToLowerInvariant();
                if (!HasCheatContext(low)) continue;
            }
            AddSig(sig, s, proc);
        }
    }

    static int IndexOfHttp(string s, int from, int to)
    {
        for (int i = from; i + 4 <= to; i++)
            if ((s[i] == 'h' || s[i] == 'H') && (s[i + 1] == 't' || s[i + 1] == 'T') &&
                (s[i + 2] == 't' || s[i + 2] == 'T') && (s[i + 3] == 'p' || s[i + 3] == 'P'))
                return i;
        return -1;
    }

    static void MatchHistoryUrls(string s, string proc)
    {
        int idx = 0;
        while (true)
        {
            int p = s.IndexOf("://", idx, StringComparison.Ordinal);
            if (p < 0) break;
            idx = p + 3;

            int schemeStart;
            if (p >= 5 && string.Compare(s, p - 5, "https", 0, 5, true, System.Globalization.CultureInfo.InvariantCulture) == 0) schemeStart = p - 5;
            else if (p >= 4 && string.Compare(s, p - 4, "http", 0, 4, true, System.Globalization.CultureInfo.InvariantCulture) == 0) schemeStart = p - 4;
            else continue;

            int hostStart = p + 3, hostEnd = hostStart;
            while (hostEnd < s.Length)
            {
                char c = s[hostEnd];
                if (c == '/' || c == '?' || c == '#' || c == ':' || c == ' ' || c == '"' ||
                    c == '\\' || c == '\'' || c == ',' || c == '<' || c == '>' || c < 0x20) break;
                hostEnd++;
            }
            if (hostEnd <= hostStart) continue;

            string host = s.Substring(hostStart, hostEnd - hostStart).ToLowerInvariant();
            if (host.StartsWith("www.")) host = host.Substring(4);
            foreach (var d in CheatHostDomains)
            {
                if (host == d || host.EndsWith("." + d, StringComparison.Ordinal))
                {
                    int urlEnd = hostEnd;
                    while (urlEnd < s.Length && s[urlEnd] > 0x20 && s[urlEnd] != '"' && s[urlEnd] != '\'' &&
                           s[urlEnd] != '<' && s[urlEnd] != '>' && s[urlEnd] != '?') urlEnd++;
                    int next = IndexOfHttp(s, hostEnd, urlEnd);
                    if (next > hostEnd) urlEnd = next;
                    if (urlEnd - schemeStart > 120) urlEnd = schemeStart + 120;
                    AddSig(new Sig("SITE", d, "HIGH", "ANY"), s.Substring(schemeStart, urlEnd - schemeStart), proc);
                    break;
                }
            }
        }
        foreach (var pat in CheatUrlPatterns)
            if (s.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0)
                AddSig(new Sig("SITE", pat, "HIGH", "ANY"), s, proc);
    }

    static void HandleFile(string raw, string proc)
    {
        string path;
        try { path = Uri.UnescapeDataString(raw); } catch { path = raw; }
        path = path.Replace('/', '\\');
        while (path.Contains("\\\\")) path = path.Replace("\\\\", "\\");
        string low = path.ToLowerInvariant();

        foreach (var d in SystemDirs) if (low.Contains(d)) return;

        if (IsAnticheatOrTool(low)) return;

        // Only flag if file name contains a known cheat keyword or DLL name
        bool nameHit = FileNameKeyword.IsMatch(path);
        if (!nameHit) return;

        string reason = "cheat keyword in file name";

        bool exists;
        try { exists = File.Exists(path); } catch { exists = false; }
        if (!exists) return;

        AddFile(path, reason, proc);
    }

    static void ExtractAndScan(byte[] buf, int len, string proc) { ExtractAndScan(buf, len, proc, false); }

    static void ExtractAndScan(byte[] buf, int len, string proc, bool history)
    {
        int start = -1;
        for (int i = 0; i < len; i++)
        {
            byte b = buf[i];
            bool p = (b >= 0x20 && b <= 0x7E) || b == 0x09;
            if (p) { if (start < 0) start = i; }
            else { if (start >= 0) { int n = i - start; if (n >= MIN_LEN) ScanString(Encoding.ASCII.GetString(buf, start, Math.Min(n, MAX_STR)), proc, history); start = -1; } }
        }
        if (start >= 0) { int n = len - start; if (n >= MIN_LEN) ScanString(Encoding.ASCII.GetString(buf, start, Math.Min(n, MAX_STR)), proc, history); }

        start = -1;
        for (int i = 0; i + 1 < len; i += 2)
        {
            byte b = buf[i], hi = buf[i + 1];
            bool p = (hi == 0) && (((b >= 0x20 && b <= 0x7E) || b == 0x09));
            if (p) { if (start < 0) start = i; }
            else { if (start >= 0) { int nb = i - start; if (nb / 2 >= MIN_LEN) ScanString(Encoding.Unicode.GetString(buf, start, Math.Min(nb, MAX_STR * 2)), proc, history); start = -1; } }
        }
        if (start >= 0) { int nb = (len & ~1) - start; if (nb >= 2 && nb / 2 >= MIN_LEN) ScanString(Encoding.Unicode.GetString(buf, start, Math.Min(nb, MAX_STR * 2)), proc, history); }
    }

    static long ScanProcess(Process p)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, p.Id);
        if (h == IntPtr.Zero) h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, false, p.Id);
        if (h == IntPtr.Zero)
        {
            Console.WriteLine("  [skip] " + p.ProcessName + " (pid " + p.Id + "): cannot open, err=" + Marshal.GetLastWin32Error() + " (try running as Administrator)");
            return 0;
        }

        string pname = p.ProcessName;
        long scanned = 0;
        try
        {
            var work = new List<long[]>();
            long planned = 0;
            int mbiSize = Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION));
            IntPtr addr = IntPtr.Zero;
            while (true)
            {
                MEMORY_BASIC_INFORMATION mbi;
                if (VirtualQueryEx(h, addr, out mbi, (IntPtr)mbiSize) == IntPtr.Zero) break;
                long regionSize = (long)mbi.RegionSize;
                long baseAddr = (long)mbi.BaseAddress;
                if (regionSize <= 0) break;

                bool commit = mbi.State == MEM_COMMIT;
                bool readable = mbi.Protect != 0 && (mbi.Protect & PAGE_NOACCESS) == 0 && (mbi.Protect & PAGE_GUARD) == 0;
                bool typeOk = mbi.Type == MEM_IMAGE || mbi.Type == MEM_MAPPED || mbi.Type == MEM_PRIVATE;
                if (commit && readable && typeOk)
                {
                    long off = 0;
                    while (off < regionSize)
                    {
                        long len = Math.Min((long)CHUNK, regionSize - off);
                        work.Add(new long[] { baseAddr + off, len });
                        planned += len;
                        if (off + len >= regionSize) break;
                        off += (CHUNK - OVL);
                    }
                }
                long next = baseAddr + regionSize;
                if (next <= baseAddr) break;
                addr = (IntPtr)next;
                if (planned > MAX_PER_PROC) break;
            }

            var po = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount) };
            Parallel.ForEach(work, po,
                () => new byte[CHUNK],
                (item, state, buf) =>
                {
                    IntPtr got;
                    int len = (int)item[1];
                    if (ReadProcessMemory(h, (IntPtr)item[0], buf, (IntPtr)len, out got) && (int)got > 0)
                    {
                        ExtractAndScan(buf, (int)got, pname);
                        Interlocked.Add(ref scanned, (int)got);
                    }
                    return buf;
                },
                buf => { });
        }
        finally { CloseHandle(h); }
        return scanned;
    }

    // Theme 8.2: Module analysis in javaw.exe
    static void ScanProcessModules(Process p)
    {
        try
        {
            var modules = p.Modules;
            foreach (ProcessModule mod in modules)
            {
                string name = mod.ModuleName;
                string path = "";
                try { path = mod.FileName; } catch { }
                string low = path.ToLowerInvariant();
                long size = mod.ModuleMemorySize;

                // Suspicious hitbox sizes: 1.42 MB, 1.43 MB, 1.56 MB, 1.89 MB
                bool suspiciousWeight = (size >= 1480000 && size <= 1515000) ||
                                       (size >= 1625000 && size <= 1655000) ||
                                       (size >= 1970000 && size <= 2000000);

                string desc = "";
                try { desc = mod.FileVersionInfo.FileDescription ?? ""; } catch { }
                bool emptyDesc = string.IsNullOrEmpty(desc.Trim());

                bool knownCheatDll = FileNameKeyword.IsMatch(name);
                bool suspLocation = low.Contains(@"\downloads\") || low.Contains(@"\temp\") ||
                                    low.Contains(@"\desktop\") || low.Contains(@"\appdata\local\temp\");

                if (knownCheatDll)
                {
                    AddSig(new Sig("DLL", "Known cheat DLL loaded: " + name, "HIGH", "ANY"), path + " (" + (size / 1024) + " KB)", p.ProcessName);
                }
                else if (suspiciousWeight && emptyDesc)
                {
                    AddSig(new Sig("DLL", "Suspicious hitbox DLL weight (" + (size / (1024.0 * 1024.0)).ToString("0.00") + " MB, empty desc): " + name, "HIGH", "ANY"), path, p.ProcessName);
                }
                else if (suspiciousWeight && suspLocation)
                {
                    AddSig(new Sig("DLL", "Suspicious DLL weight in temp/downloads: " + name + " (" + (size / (1024.0 * 1024.0)).ToString("0.00") + " MB)", "HIGH", "ANY"), path, p.ProcessName);
                }
            }
        }
        catch { }
    }

    // Theme 8.2: Unloaded modules inspection in javaw.exe
    static void ScanUnloadedModules(int pid, string procName)
    {
        try
        {
            IntPtr dbg = RtlCreateQueryDebugBuffer(0, false);
            if (dbg == IntPtr.Zero) return;
            try
            {
                int st = RtlQueryProcessDebugInformation(pid, 0x04 /* RTL_QUERY_PROCESS_UNLOADED_MODULES */, dbg);
                if (st >= 0)
                {
                    IntPtr unl = Marshal.ReadIntPtr(dbg, 112);
                    if (unl != IntPtr.Zero)
                    {
                        uint count = (uint)Marshal.ReadInt32(unl, 0);
                        for (int i = 0; i < count && i < 1000; i++)
                        {
                            IntPtr entry = new IntPtr(unl.ToInt64() + 8 + i * 32);
                            long modSize = Marshal.ReadInt64(entry, 8);
                            short strLen = Marshal.ReadInt16(entry, 24);
                            IntPtr strBuf = Marshal.ReadIntPtr(entry, 32);
                            string modName = "";
                            if (strBuf != IntPtr.Zero && strLen > 0)
                            {
                                byte[] nameBytes = new byte[strLen];
                                Marshal.Copy(strBuf, nameBytes, 0, strLen);
                                modName = Encoding.Unicode.GetString(nameBytes);
                            }

                            bool suspWeight = (modSize >= 1480000 && modSize <= 1515000) ||
                                             (modSize >= 1625000 && modSize <= 1655000) ||
                                             (modSize >= 1970000 && modSize <= 2000000);
                            bool cheatName = !string.IsNullOrEmpty(modName) && FileNameKeyword.IsMatch(modName);

                            if (cheatName)
                            {
                                AddSig(new Sig("DLL", "Unloaded cheat module detected: " + modName, "HIGH", "ANY"), "Size: " + (modSize / 1024) + " KB", procName);
                            }
                            else if (suspWeight)
                            {
                                AddSig(new Sig("DLL", "Unloaded module with hitbox weight (" + (modSize / (1024.0 * 1024.0)).ToString("0.00") + " MB): " + (string.IsNullOrEmpty(modName) ? "unknown" : modName), "HIGH", "ANY"), "Unloaded module", procName);
                            }
                        }
                    }
                }
            }
            finally { RtlDestroyQueryDebugBuffer(dbg); }
        }
        catch { }
    }

    // Theme 8: Check OpenSavePidlMRU for recent injector file dialogs
    static void ScanComDlgMRU()
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\OpenSavePidlMRU"))
            {
                if (key == null) return;
                foreach (var sub in key.GetSubKeyNames())
                {
                    using (var subKey = key.OpenSubKey(sub))
                    {
                        if (subKey == null) continue;
                        foreach (var valName in subKey.GetValueNames())
                        {
                            if (valName.Equals("MRUListEx", StringComparison.OrdinalIgnoreCase)) continue;
                            byte[] raw = subKey.GetValue(valName) as byte[];
                            if (raw == null || raw.Length < 4) continue;
                            string str = Encoding.Unicode.GetString(raw);
                            int nullIdx = str.IndexOf('\0');
                            if (nullIdx > 0) str = str.Substring(0, nullIdx);

                            foreach (Match m in Regex.Matches(str, @"[A-Za-z0-9_\-\. ]+\.(?:dll|jar|exe|zip|rar)", RegexOptions.IgnoreCase))
                            {
                                string fn = m.Value;
                                if (FileNameKeyword.IsMatch(fn) && !IsAnticheatOrTool(fn.ToLowerInvariant()))
                                {
                                    AddSig(new Sig("MRU", "Cheat file in Open/Save dialog MRU: " + fn, "HIGH", "ANY"), fn + @" (ComDlg32\" + sub + ")", "Registry");
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { }
    }

    // Theme 9: Windows Services Audit
    static int GetServiceProcessId(string serviceName)
    {
        IntPtr scm = OpenSCManager(null, null, 0x0001);
        if (scm == IntPtr.Zero) return 0;
        try
        {
            IntPtr svc = OpenService(scm, serviceName, 0x0004);
            if (svc == IntPtr.Zero) return 0;
            try
            {
                int size = Marshal.SizeOf(typeof(SERVICE_STATUS_PROCESS));
                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    uint needed;
                    if (QueryServiceStatusEx(svc, 0, buf, (uint)size, out needed))
                    {
                        var ssp = (SERVICE_STATUS_PROCESS)Marshal.PtrToStructure(buf, typeof(SERVICE_STATUS_PROCESS));
                        return (int)ssp.dwProcessId;
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            finally { CloseServiceHandle(svc); }
        }
        finally { CloseServiceHandle(scm); }
        return 0;
    }

    static string GetServiceStatus(string serviceName)
    {
        IntPtr scm = OpenSCManager(null, null, 0x0001);
        if (scm == IntPtr.Zero) return "ACCESS_DENIED";
        try
        {
            IntPtr svc = OpenService(scm, serviceName, 0x0004);
            if (svc == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                return err == 1060 ? "NOT_INSTALLED" : "ERROR_" + err;
            }
            try
            {
                int size = Marshal.SizeOf(typeof(SERVICE_STATUS_PROCESS));
                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    uint needed;
                    if (QueryServiceStatusEx(svc, 0, buf, (uint)size, out needed))
                    {
                        var ssp = (SERVICE_STATUS_PROCESS)Marshal.PtrToStructure(buf, typeof(SERVICE_STATUS_PROCESS));
                        switch (ssp.dwCurrentState)
                        {
                            case 4: return "RUNNING (PID " + ssp.dwProcessId + ")";
                            case 1: return "STOPPED";
                            case 2: return "START_PENDING";
                            case 3: return "STOP_PENDING";
                            case 7: return "PAUSED";
                            default: return "STATE_" + ssp.dwCurrentState;
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            finally { CloseServiceHandle(svc); }
        }
        finally { CloseServiceHandle(scm); }
        return "UNKNOWN";
    }

    static void AuditServices()
    {
        var monitored = new[] {
            new[] { "PcaSvc", "Program Compatibility Assistant" },
            new[] { "DPS", "Diagnostic Policy Service" },
            new[] { "SysMain", "Superfetch / SysMain" },
            new[] { "bam", "Background Activity Moderator" },
            new[] { "EventLog", "Windows Event Log" },
            new[] { "DiagTrack", "Connected User Experiences and Telemetry" },
            new[] { "BFE", "Base Filtering Engine" },
            new[] { "DcomLaunch", "DCOM Server Process Launcher" }
        };

        foreach (var svc in monitored)
        {
            string name = svc[0];
            string display = svc[1];
            int pid = GetServiceProcessId(name);
            string status = GetServiceStatus(name);

            bool isStopped = status.StartsWith("STOPPED") || status.StartsWith("NOT_INSTALLED");
            if (isStopped && name != "bam")
            {
                AddSig(new Sig("SERVICE", "Critical monitoring service disabled/stopped: " + name + " (" + display + ")", "MED", "ANY"), "Status: " + status, "ServiceManager");
            }

            ServicesReport.Add(new ServiceAudit { Name = name, DisplayName = display, Status = status, Pid = pid, Suspicious = isStopped });
        }
    }

    static void EnableDebugPrivilege()
    {
        IntPtr token;
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out token)) return;
        try
        {
            LUID luid;
            if (!LookupPrivilegeValue(null, "SeDebugPrivilege", out luid)) return;
            var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
            AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally { CloseHandle(token); }
    }

    class RbRec { public string Path; public DateTime Deleted; }

    static RbRec ParseRecycleMeta(string metaPath)
    {
        byte[] b;
        try { b = File.ReadAllBytes(metaPath); } catch { return null; }
        if (b.Length < 24) return null;
        long ver = BitConverter.ToInt64(b, 0);
        long ft = BitConverter.ToInt64(b, 16);
        DateTime del;
        try { del = DateTime.FromFileTime(ft); } catch { return null; }
        string path;
        if (ver == 2 && b.Length >= 28)
        {
            int chars = BitConverter.ToInt32(b, 24);
            int byteLen = chars * 2;
            int avail = b.Length - 28;
            if (byteLen > avail || byteLen < 0) byteLen = avail;
            path = Encoding.Unicode.GetString(b, 28, byteLen);
        }
        else
        {
            int avail = b.Length - 24;
            int byteLen = Math.Min(520, avail);
            path = Encoding.Unicode.GetString(b, 24, byteLen);
        }
        int nul = path.IndexOf('\0');
        if (nul >= 0) path = path.Substring(0, nul);
        return string.IsNullOrEmpty(path) ? null : new RbRec { Path = path, Deleted = del };
    }

    static int ScanRecycleBin(int windowMinutes)
    {
        DateTime lo = Started.AddMinutes(-windowMinutes);
        DateTime hi = DateTime.Now.AddMinutes(1);
        int found = 0;
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); } catch { return 0; }
        foreach (var drive in drives)
        {
            string rb;
            try { if (!drive.IsReady) continue; rb = drive.Name + "$Recycle.Bin"; } catch { continue; }
            if (!Directory.Exists(rb)) continue;
            string[] sidDirs;
            try { sidDirs = Directory.GetDirectories(rb); } catch { continue; }
            foreach (var sidDir in sidDirs)
            {
                string[] metas;
                try { metas = Directory.GetFiles(sidDir, "$I*"); } catch { continue; }
                foreach (var meta in metas)
                {
                    RbRec rec = ParseRecycleMeta(meta);
                    if (rec == null) continue;
                    if (rec.Deleted < lo || rec.Deleted > hi) continue;

                    string low = rec.Path.ToLowerInvariant();
                    if (IsAnticheatOrTool(low)) continue;
                    if (!FileNameKeyword.IsMatch(rec.Path)) continue;

                    int mins = (int)Math.Max(0, Math.Round((DateTime.Now - rec.Deleted).TotalMinutes));
                    string binR = null;
                    try {
                        string name = Path.GetFileName(meta);
                        if (name.StartsWith("$I")) binR = Path.Combine(sidDir, "$R" + name.Substring(2));
                    } catch { }
                    string why = "recently deleted cheat file";
                    AddDeleted(rec.Path, why, mins, binR);
                    found++;
                }
            }
        }
        return found;
    }

    static bool IsBrowser(string procName)
    {
        return Array.IndexOf(BrowserNames, procName.ToLowerInvariant()) >= 0;
    }

    static List<string[]> FindHistoryDbs()
    {
        var res = new List<string[]>();
        string local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        string roaming = Environment.GetEnvironmentVariable("APPDATA");

        var chromium = new List<string[]>();
        if (local != null)
        {
            chromium.Add(new[] { "Chrome",        local + @"\Google\Chrome\User Data" });
            chromium.Add(new[] { "Chrome Beta",   local + @"\Google\Chrome Beta\User Data" });
            chromium.Add(new[] { "Chrome Canary", local + @"\Google\Chrome SxS\User Data" });
            chromium.Add(new[] { "Edge",          local + @"\Microsoft\Edge\User Data" });
            chromium.Add(new[] { "Brave",         local + @"\BraveSoftware\Brave-Browser\User Data" });
            chromium.Add(new[] { "Vivaldi",       local + @"\Vivaldi\User Data" });
            chromium.Add(new[] { "Yandex",        local + @"\Yandex\YandexBrowser\User Data" });
            chromium.Add(new[] { "Chromium",      local + @"\Chromium\User Data" });
        }
        if (roaming != null)
        {
            chromium.Add(new[] { "Opera",    roaming + @"\Opera Software\Opera Stable" });
            chromium.Add(new[] { "Opera GX", roaming + @"\Opera Software\Opera GX Stable" });
        }
        foreach (var c in chromium)
        {
            if (!Directory.Exists(c[1])) continue;
            var dirs = new List<string>(); dirs.Add(c[1]);
            try { dirs.AddRange(Directory.GetDirectories(c[1])); } catch { }
            foreach (var d in dirs)
            {
                string hp = Path.Combine(d, "History");
                if (File.Exists(hp)) res.Add(new[] { c[0], Path.GetFileName(d), hp });
            }
        }
        if (roaming != null)
        {
            string ff = roaming + @"\Mozilla\Firefox\Profiles";
            if (Directory.Exists(ff))
            {
                try
                {
                    foreach (var d in Directory.GetDirectories(ff))
                    {
                        string pp = Path.Combine(d, "places.sqlite");
                        if (File.Exists(pp)) res.Add(new[] { "Firefox", Path.GetFileName(d), pp });
                    }
                }
                catch { }
            }
        }
        return res;
    }

    // ── SQLite helpers ──────────────────────────────────────────────────
    static long SqliteVarInt(byte[] d, ref int p)
    {
        long v = 0;
        for (int i = 0; i < 9; i++)
        {
            if (p >= d.Length) break;
            byte b = d[p++];
            if (i == 8) { v = (v << 8) | b; break; }
            v = (v << 7) | (long)(b & 0x7F);
            if ((b & 0x80) == 0) break;
        }
        return v;
    }

    static int SqliteSerialLen(long st)
    {
        if (st <= 0) return 0;
        if (st == 1) return 1;
        if (st == 2) return 2;
        if (st == 3) return 3;
        if (st == 4) return 4;
        if (st == 5) return 6;
        if (st == 6 || st == 7) return 8;
        if (st == 8 || st == 9) return 0;
        if (st >= 12) return (int)((st - (st % 2 == 0 ? 12 : 13)) / 2);
        return 0;
    }

    /// <summary>
    /// Parse SQLite leaf table B-tree pages and extract URLs from actual cell records.
    /// This skips freed/deleted pages, avoiding false positives from residual data.
    /// </summary>
    static List<string> ExtractSqliteUrls(byte[] data)
    {
        var urls = new List<string>();
        if (data.Length < 100) return urls;

        // Verify SQLite magic
        if (data[0] != 0x53 || data[1] != 0x51 || data[2] != 0x4C) return urls;

        // Read page size from header (bytes 16-17, big-endian)
        int pageSize = (data[16] << 8) | data[17];
        if (pageSize == 1) pageSize = 65536;
        if (pageSize < 512 || pageSize > 65536) return urls;

        int totalPages = data.Length / pageSize;

        for (int pg = 0; pg < totalPages; pg++)
        {
            int pageOff = pg * pageSize;
            // Page 1 (pg==0) has the 100-byte file header before the page header
            int hdrOff = (pg == 0) ? 100 : pageOff;
            if (hdrOff + 8 > data.Length) break;

            byte pageType = data[hdrOff];
            if (pageType != 0x0D) continue; // only leaf table b-tree pages

            int cellCount = (data[hdrOff + 3] << 8) | data[hdrOff + 4];
            int ptrBase = hdrOff + 8;

            for (int c = 0; c < cellCount; c++)
            {
                int pp = ptrBase + c * 2;
                if (pp + 2 > data.Length) break;
                int cellOff = pageOff + ((data[pp] << 8) | data[pp + 1]);
                if (cellOff < pageOff || cellOff >= pageOff + pageSize) continue;
                if (cellOff >= data.Length) continue;

                try
                {
                    int pos = cellOff;
                    long payloadLen = SqliteVarInt(data, ref pos);
                    if (payloadLen <= 0 || payloadLen > pageSize) continue;
                    long rowId = SqliteVarInt(data, ref pos);

                    int recHdrStart = pos;
                    long hdrLen = SqliteVarInt(data, ref pos);
                    if (hdrLen <= 0 || hdrLen > payloadLen) continue;
                    int recHdrEnd = recHdrStart + (int)hdrLen;
                    if (recHdrEnd > data.Length) continue;

                    // Collect serial types
                    var stypes = new List<long>();
                    while (pos < recHdrEnd && pos < data.Length)
                        stypes.Add(SqliteVarInt(data, ref pos));

                    // Walk values; extract text fields that look like URLs
                    int vpos = recHdrEnd;
                    foreach (long st in stypes)
                    {
                        int len = SqliteSerialLen(st);
                        if (st >= 13 && (st % 2 == 1) && len > 10) // text field
                        {
                            if (vpos + len <= data.Length)
                            {
                                // Quick prefix check before allocating a string
                                bool looksLikeUrl = false;
                                if (vpos + 8 <= data.Length)
                                {
                                    char c0 = (char)data[vpos];
                                    if ((c0 == 'h' || c0 == 'H') &&
                                        (data[vpos + 1] == (byte)'t' || data[vpos + 1] == (byte)'T') &&
                                        (data[vpos + 2] == (byte)'t' || data[vpos + 2] == (byte)'T') &&
                                        (data[vpos + 3] == (byte)'p' || data[vpos + 3] == (byte)'P'))
                                        looksLikeUrl = true;
                                }
                                if (looksLikeUrl)
                                {
                                    string text = Encoding.UTF8.GetString(data, vpos, len);
                                    urls.Add(text);
                                }
                            }
                        }
                        vpos += len;
                    }
                }
                catch { /* corrupted cell — skip */ }
            }
        }
        return urls;
    }

    static long ScanHistoryFile(string label, string path)
    {
        byte[] data;
        try
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var ms = new MemoryStream())
            {
                fs.CopyTo(ms);
                data = ms.ToArray();
            }
        }
        catch
        {
            try
            {
                string tmp = Path.Combine(Path.GetTempPath(), "hc_" + Guid.NewGuid().ToString("N") + ".db");
                File.Copy(path, tmp, true);
                data = File.ReadAllBytes(tmp);
                try { File.Delete(tmp); } catch { }
            }
            catch { return -1; }
        }

        // Parse SQLite structure — read only active leaf table cells
        var historyUrls = ExtractSqliteUrls(data);
        foreach (var url in historyUrls)
            ScanString(url, label, true);

        return data.Length;
    }

    static void ScanBrowserHistories()
    {
        var dbs = FindHistoryDbs();
        if (dbs.Count == 0) { Console.WriteLine("  (no browser history files found)"); return; }
        foreach (var db in dbs)
        {
            string label = db[0] + "/" + db[1];
            Console.Write("  " + label + " ... ");
            long n = ScanHistoryFile(label, db[2]);
            if (n < 0) Console.WriteLine("locked - close the browser and re-run");
            else       Console.WriteLine((n / (1024 * 1024)) + " MB");
        }
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Started = DateTime.Now;
        bool all = false, listOnly = false, deep = false;
        var explicitTargets = new List<string>();
        foreach (var a in args)
        {
            if (a == "--all") all = true;
            else if (a == "--list") listOnly = true;
            else if (a == "--deep") deep = true;
            else if (a == "-h" || a == "--help") { PrintHelp(); return 0; }
            else explicitTargets.Add(a);
        }

        Console.WriteLine("========================================================");
        Console.WriteLine("  HitCheck  -  system threat detection tool");
        Console.WriteLine("  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "   (read-only memory scan)");
        Console.WriteLine("========================================================");

        BuildSignatures();
        EnableDebugPrivilege();

        // 1. Audit Windows Services (Theme 9)
        Console.WriteLine("\nAuditing Windows Services (PcaSvc, DPS, SysMain, bam, EventLog, DiagTrack)...");
        try { AuditServices(); } catch (Exception e) { Console.WriteLine("  Services audit error: " + e.Message); }
        foreach (var s in ServicesReport)
        {
            string flag = s.Suspicious ? "[!] " : "    ";
            Console.WriteLine("  " + flag + s.Name.PadRight(12) + " (" + s.DisplayName + "): " + s.Status);
        }

        // 2. Check OpenSave MRU Dialogs in Registry (Theme 8)
        try { ScanComDlgMRU(); } catch { }

        // 3. Enumerate processes
        var targets = new List<Process>();
        Process[] running;
        try { running = Process.GetProcesses(); }
        catch (Exception e) { Console.WriteLine("Cannot enumerate processes: " + e.Message); return 2; }

        var targetPids = new HashSet<int>();

        foreach (var pr in running)
        {
            string name = pr.ProcessName.ToLowerInvariant();
            bool take = false;
            if (explicitTargets.Count > 0)
            {
                foreach (var t in explicitTargets)
                {
                    int pid;
                    if (int.TryParse(t, out pid)) { if (pr.Id == pid) { take = true; break; } }
                    else if (name == t.ToLowerInvariant().Replace(".exe", "")) { take = true; break; }
                }
            }
            else if (all) take = pr.Id != Process.GetCurrentProcess().Id;
            else take = Array.IndexOf(DefaultTargets, name) >= 0;

            if (take && targetPids.Add(pr.Id)) targets.Add(pr);
        }

        // Add service processes (BFE, DPS, DcomLaunch, SearchIndexer) to scan targets
        if (explicitTargets.Count == 0 && !listOnly)
        {
            foreach (var svc in ServicesReport)
            {
                if (svc.Pid > 0 && targetPids.Add(svc.Pid))
                {
                    try { targets.Add(Process.GetProcessById(svc.Pid)); } catch { }
                }
            }
            foreach (var pr in running)
            {
                if (pr.ProcessName.Equals("SearchIndexer", StringComparison.OrdinalIgnoreCase) && targetPids.Add(pr.Id))
                    targets.Add(pr);
            }
        }

        int droppedBrowsers = 0;
        if (explicitTargets.Count == 0 && !all && !deep)
        {
            var filtered = new List<Process>();
            foreach (var pr in targets) { if (IsBrowser(pr.ProcessName)) droppedBrowsers++; else filtered.Add(pr); }
            targets = filtered;
        }

        if (targets.Count == 0 && explicitTargets.Count > 0)
        { Console.WriteLine("\nNo matching processes found."); return 0; }

        Console.WriteLine("\nProcesses selected (memory scan):");
        if (targets.Count == 0) Console.WriteLine("  (none)");
        foreach (var pr in targets)
            Console.WriteLine("  - " + pr.ProcessName + ".exe  (pid " + pr.Id + ")");
        if (droppedBrowsers > 0)
            Console.WriteLine("  (browser memory excluded; use --deep to include live browser processes)");

        if (listOnly)
        {
            Console.WriteLine("\nBrowser history files:");
            var dbs = FindHistoryDbs();
            if (dbs.Count == 0) Console.WriteLine("  (none found)");
            foreach (var db in dbs) Console.WriteLine("  - " + db[0] + "/" + db[1] + "  ->  " + db[2]);
            return 0;
        }

        // 4. Module & Memory Scan
        long total = 0;
        var sw = Stopwatch.StartNew();
        if (targets.Count > 0)
        {
            Console.WriteLine("\nAnalyzing modules & scanning memory across " + Environment.ProcessorCount + " CPU cores...");
            foreach (var pr in targets)
            {
                // Module analysis for Java processes (Theme 8.2)
                if (pr.ProcessName.StartsWith("java", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Write("  " + pr.ProcessName + ".exe (pid " + pr.Id + ") modules... ");
                    ScanProcessModules(pr);
                    ScanUnloadedModules(pr.Id, pr.ProcessName);
                    Console.WriteLine("ok");
                }

                Console.Write("  " + pr.ProcessName + ".exe (pid " + pr.Id + ") memory... ");
                long n = 0;
                var psw = Stopwatch.StartNew();
                try { n = ScanProcess(pr); } catch (Exception e) { Console.Write("error: " + e.Message + " "); }
                psw.Stop();
                total += n;
                Console.WriteLine((n / (1024 * 1024)) + " MB in " + psw.Elapsed.TotalSeconds.ToString("0.0") + "s");
            }
        }

        // 5. Browser History (on-disk)
        Console.WriteLine("\nScanning browser history (visited sites, on-disk)...");
        try { ScanBrowserHistories(); } catch (Exception e) { Console.WriteLine("  error: " + e.Message); }

        sw.Stop();
        Console.WriteLine("\nDone in " + sw.Elapsed.TotalSeconds.ToString("0.0") + "s (" + (total / (1024 * 1024)) + " MB of process memory).");

        // 6. Recycle Bin
        Console.Write("Checking Recycle Bin for files deleted in the last 30 min... ");
        int del = 0;
        try { del = ScanRecycleBin(30); } catch (Exception e) { Console.Write("error: " + e.Message); }
        Console.WriteLine(del + " suspicious");

        Report();
        return (Files.Count > 0 || HighConfidenceSig()) ? 1 : 0;
    }

    static bool HighConfidenceSig()
    {
        foreach (var f in Sigs.Values) if (f.Conf == "HIGH") return true;
        return false;
    }

    static void PrintHelp()
    {
        Console.WriteLine("HitCheck - Automated threat detection and forensics tool");
        Console.WriteLine();
        Console.WriteLine("Usage: HitCheck.exe [options] [name.exe | pid ...]");
        Console.WriteLine("  (no args)   memory-scan explorer, javaw/java, target services, and on-disk browser history");
        Console.WriteLine("  --deep      ALSO scan live browser process memory (slower)");
        Console.WriteLine("  --all       scan every process the tool can open");
        Console.WriteLine("  --list      list target processes + history files, do not read memory");
        Console.WriteLine("  --help      show this help");
        Console.WriteLine();
        Console.WriteLine("Run as Administrator for best coverage. Output is written to console");
        Console.WriteLine("and to hitcheck_report_<timestamp>.txt next to the exe.");
    }

    static void Report()
    {
        var outp = new StringBuilder();
        Action<string> W = (line) => { Console.WriteLine(line); outp.AppendLine(line); };

        W("");
        W("========================================================");
        W("  RESULTS");
        W("========================================================");

        // Windows Services section
        W("");
        W("[ WINDOWS SERVICES AUDIT ] (" + ServicesReport.Count + ")");
        foreach (var s in ServicesReport)
        {
            string flag = s.Suspicious ? "  !! " : "     ";
            W(flag + s.Name.PadRight(12) + " (" + s.DisplayName + "): " + s.Status);
        }

        // Suspicious Files section
        W("");
        W("[ SUSPICIOUS FILES ] (" + Files.Count + ")");
        if (Files.Count == 0) W("  none found");
        else
        {
            var list = new List<FileHit>(Files.Values);
            list.Sort((a, b) =>
            {
                bool da = a.DelMin >= 0, db = b.DelMin >= 0;
                if (da != db) return da ? -1 : 1;
                if (da && db) return a.DelMin - b.DelMin;
                return string.Compare(a.Dir + a.Name, b.Dir + b.Name, StringComparison.OrdinalIgnoreCase);
            });
            foreach (var f in list)
            {
                if (f.DelMin >= 0) W("  !! " + f.Name + "   [DELETED ~" + f.DelMin + " min ago]");
                else               W("  !! " + f.Name);
                W("       directory : " + f.Dir);
                W("       reason    : " + f.Reason);
                if (f.BinPath != null) W("       recycle bin: " + f.BinPath);
                if (f.Procs.Count > 0)
                    W("       seen in   : " + string.Join(", ", new List<string>(f.Procs).ToArray()));

                string src = f.Procs.Count > 0 ? new List<string>(f.Procs)[0] : "explorer";
                W("       PH2 lookup: " + src + ".exe > right-click Properties > Memory > Strings...");
                W("                   (Min length 4, tick Image + Mapped) > Filter >");
                W("                   Contains: file:  then Filter > Contains: " + f.Name);
            }
        }

        var sites = new List<Finding>();
        var cheats = new List<Finding>();
        foreach (var f in Sigs.Values) { if (f.Cat == "SITE") sites.Add(f); else cheats.Add(f); }

        W("");
        W("[ CHEAT WEBSITES VISITED ] (" + sites.Count + ")");
        if (sites.Count == 0) W("  none found");
        else foreach (var f in Sort(sites))
        {
            W("  !! " + f.Label + "   (in: " + string.Join(", ", new List<string>(f.Procs).ToArray()) + ")");
            foreach (var ex in f.Examples) W("        > " + ex);
        }

        W("");
        W("[ CHEAT SIGNATURES & TRACES ] (" + cheats.Count + ")");
        if (cheats.Count == 0) W("  none found");
        else foreach (var f in Sort(cheats))
        {
            W("  [" + f.Conf + "] " + f.Label + "   (x" + f.Count + ", in: " + string.Join(", ", new List<string>(f.Procs).ToArray()) + ")");
            foreach (var ex in f.Examples) W("        > " + ex);
        }

        W("");
        W("========================================================");
        bool bad = Files.Count > 0 || HighConfidenceSig();
        if (bad)
        {
            W("  VERDICT: SUSPICIOUS - manual review required.");
            W("  Cheat traces and/or suspicious files were detected.");
        }
        else if (cheats.Count > 0 || sites.Count > 0)
            W("  VERDICT: INCONCLUSIVE - low/medium hits only, review examples.");
        else
            W("  VERDICT: CLEAN - no known cheat traces found in scanned memory.");
        W("  Note: a clean result is not proof of innocence (memory can be");
        W("  wiped, or the cheat was not running). This tool assists, not decides.");
        W("========================================================");

        try
        {
            string file = "hitcheck_report_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
            File.WriteAllText(file, outp.ToString(), new UTF8Encoding(false));
            Console.WriteLine("\nReport saved to: " + Path.GetFullPath(file));
        }
        catch (Exception e) { Console.WriteLine("\nCould not write report file: " + e.Message); }
    }

    static List<Finding> Sort(List<Finding> l)
    {
        l.Sort((a, b) =>
        {
            int ra = Rank(a.Conf), rb = Rank(b.Conf);
            if (ra != rb) return ra - rb;
            return b.Count.CompareTo(a.Count);
        });
        return l;
    }
    static int Rank(string c) { return c == "HIGH" ? 0 : c == "MED" ? 1 : 2; }
}
