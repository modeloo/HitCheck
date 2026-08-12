












using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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
        "vape.gg", "everlack.in", "doomsdayclient.com", "cortexclient.com",
        "nemezida.ru", "dreampoolhack.ru", "akrien.wtf", "takker.ru", "ammit.cc",
        "stubborn.website", "meteorclient.com", "liquidbounce.net", "aristois.net",
        "wurstclient.net", "novoline.ru", "expensive.lol", "moon.vin", "rise.ovh"
    };

    static readonly string[] CheatUrlPatterns = {
        "vk.com/avaloneclient", "vk.com/norender", "vk.com/troxill"
    };





    class Sig { public string Cat, Label, Conf, Mode; public Sig(string c, string l, string k, string m){Cat=c;Label=l;Conf=k;Mode=m;} }
    static readonly Dictionary<string, Sig> SigLookup = new Dictionary<string, Sig>(StringComparer.OrdinalIgnoreCase);
    static Regex SigRegex;

    static void BuildSignatures()
    {

        var defs = new[] {
        
            new[]{"ASM:",                          "SIG","ASM marker (ClownClient/DEADCODE/self-written)","MED","PFX"},
            new[]{"net.minecraftforge.ASMEventHandler","SIG","Forge ASM event handler hook","MED","ANY"},
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
            new[]{"reach:",                        "SIG","reach: [vert client]","MED","PFX"},
            new[]{"hitbox:",                       "SIG","hitbox: [vert client]","MED","PFX"},
            new[]{"az85",                          "SIG","Az85 [AnanaV4]","LOW","EXACT"},
            new[]{"71l",                           "SIG","71L [AnanaV4]","LOW","EXACT"},
            new[]{"#hit",                          "SIG","#Hit [AnanaV2]","LOW","EXACT"},
            new[]{"chs/main",                      "SIG","chs/main [Vertzah]","MED","CTX"},
            new[]{"pastebin",                      "SIG","pastebin [NoRender Lite]","LOW","CTX"},
        
            new[]{"cortexclient.com",              "SITE","cortexclient.com","HIGH","ANY"},
            new[]{"vk.com/avaloneclient",          "SITE","vk.com/avaloneclient","HIGH","ANY"},
            new[]{"vk.com/norender",               "SITE","vk.com/norender","HIGH","ANY"},
            new[]{"vk.com/troxill",                "SITE","vk.com/troxill","HIGH","ANY"},
            new[]{"doomsdayclient.com",            "SITE","doomsdayclient.com","HIGH","ANY"},
            new[]{"ammit.cc",                      "SITE","ammit.cc","HIGH","ANY"},
            new[]{"takker.ru",                     "SITE","takker.ru","HIGH","ANY"},
            new[]{"akrien.wtf",                    "SITE","akrien.wtf","HIGH","ANY"},
            new[]{"dreampoolhack.ru",              "SITE","dreampoolhack.ru","HIGH","ANY"},
            new[]{"everlack.in",                   "SITE","everlack.in","HIGH","ANY"},
            new[]{"vape.gg",                       "SITE","vape.gg","HIGH","ANY"},
            new[]{"nemezida.ru",                   "SITE","nemezida.ru","HIGH","ANY"},
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


            if (patterns[i].Equals("aimassist", StringComparison.OrdinalIgnoreCase))
                esc = "(?<![a-z])" + esc;
            sb.Append(esc);
        }
        SigRegex = new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }


    static readonly Regex FileRegex = new Regex(
        @"file:\/\/\/?(?<u>[A-Za-z]:[\\/][^\s""'<>|\r\n*?]*?\.(?:jar|rar|exe|zip|dll|js|bat|cmd|ps1|msi|vbs))(?![A-Za-z0-9])" +
        @"|(?<p>[A-Za-z]:\\[^\s""'<>|\r\n*?]*?\.(?:jar|rar|exe|zip|dll|js|bat|cmd|ps1|msi|vbs))(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);




    static readonly Regex FileNameKeyword = new Regex(
        @"vapeclient|vapev\d|vape_|nemezida|norender|liquidbounce|wurstclient|" +
        @"aristois|novoline|exhibition|deadcode|clownclient|clowdy|augustus|" +
        @"akrien|dreampool|everlack|troxil|ammit|huzuni|doomsdayclient|" +
        @"killaura|aimbot|aimassist|autoclick|triggerbot|injector|cheatengine|" +
        @"\bcheat|\bhack\b|bypass|nursultan|jigsawclient|\bexpensive\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);


    static readonly string[] HotDirs = {
        @"\downloads\", @"\desktop\", @"\temp\", @"\appdata\local\temp\",
        @".minecraft\mods\", @"\onedrive\downloads\", @"\onedrive\desktop\"
    };

    static readonly string[] HotExts = { ".jar", ".rar", ".zip", ".7z" };


    static readonly string[] DeletedExts = { ".jar", ".rar", ".zip", ".7z", ".exe", ".dll", ".js", ".bat", ".cmd", ".ps1" };


    static readonly string[] SystemDirs = {
        @"\windows\", @"\program files\", @"\program files (x86)\", @"\programdata\microsoft\",
        @"\libraries\", @"\assets\", @"\natives", @"\versions\", @"\meta\",
        @"\.fabric\", @"\processedmods\", @"\.mixin", @"\bin\", @"\patched\",
        @"\.gradle\", @"\gradle\caches\", @"\.m2\", @"\.ivy2\", @"\.nuget\", @"\node_modules\"
    };


    class Finding { public string Cat, Label, Conf; public int Count; public HashSet<string> Procs = new HashSet<string>(); public List<string> Examples = new List<string>(); }
    class FileHit { public string Dir, Name, Reason, BinPath; public int DelMin = -1; public HashSet<string> Procs = new HashSet<string>(); }
    static readonly Dictionary<string, Finding> Sigs = new Dictionary<string, Finding>();
    static readonly Dictionary<string, FileHit> Files = new Dictionary<string, FileHit>(StringComparer.OrdinalIgnoreCase);
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

    static void ScanString(string s, string proc) { ScanString(s, proc, false); }






    static void ScanString(string s, string proc, bool history)
    {
        if (history)
            MatchHistoryUrls(s, proc);
        else if (proc.StartsWith("java", StringComparison.OrdinalIgnoreCase))
            MatchSignatures(s, proc);


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
        if (!SigRegex.IsMatch(s)) return;
        string low = null;
        foreach (Match m in SigRegex.Matches(s))
        {
            Sig sig;
            if (!SigLookup.TryGetValue(m.Value, out sig)) continue;


            if (s.IndexOf("[*.]", StringComparison.Ordinal) >= 0) continue;



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





            if (m.Value.Equals("ASM:", StringComparison.OrdinalIgnoreCase))
            {
                if (low == null) low = s.ToLowerInvariant();
                string mark = null;
                if (low.IndexOf("extension", StringComparison.Ordinal) >= 0) mark = "Extension (FakeTapeMouse-style hitbox)";
                else if (low.IndexOf("killaura", StringComparison.Ordinal) >= 0 || low.IndexOf("aura", StringComparison.Ordinal) >= 0) mark = "aura module";
                else if (low.IndexOf("hitbox", StringComparison.Ordinal) >= 0) mark = "hitbox module";
                else if (low.IndexOf("trigger", StringComparison.Ordinal) >= 0) mark = "trigger module";
                else if (low.IndexOf("reach", StringComparison.Ordinal) >= 0) mark = "reach module";
                if (mark != null) AddSig(new Sig("SIG", "ASM hook w/ " + mark, "HIGH", "ANY"), s, proc);
            }
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

        bool nameHit = FileNameKeyword.IsMatch(path);
        bool inSystem = false; foreach (var d in SystemDirs) if (low.Contains(d)) { inSystem = true; break; }
        bool inHot = false;    foreach (var d in HotDirs)    if (low.Contains(d)) { inHot = true; break; }
        bool hotExt = false;   foreach (var e in HotExts)    if (low.EndsWith(e)) { hotExt = true; break; }

        string reason;
        if (nameHit) reason = "cheat keyword in name";
        else if (inSystem) return;
        else if (inHot && hotExt) reason = "archive/jar in download/desktop/temp";
        else return;




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
                    bool susp = FileNameKeyword.IsMatch(rec.Path);
                    if (!susp) foreach (var e in DeletedExts) if (low.EndsWith(e)) { susp = true; break; }
                    if (!susp) continue;

                    int mins = (int)Math.Max(0, Math.Round((DateTime.Now - rec.Deleted).TotalMinutes));
                    string binR = null;
                    try {
                        string name = Path.GetFileName(meta);
                        if (name.StartsWith("$I")) binR = Path.Combine(sidDir, "$R" + name.Substring(2));
                    } catch { }
                    string why = FileNameKeyword.IsMatch(rec.Path)
                        ? "recently deleted, cheat keyword in name"
                        : "recently deleted archive/executable";
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
        ExtractAndScan(data, data.Length, label, true);
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


        var targets = new List<Process>();
        Process[] running;
        try { running = Process.GetProcesses(); }
        catch (Exception e) { Console.WriteLine("Cannot enumerate processes: " + e.Message); return 2; }

        foreach (var pr in running)
        {
            string name = pr.ProcessName.ToLowerInvariant();
            bool take;
            if (explicitTargets.Count > 0)
            {
                take = false;
                foreach (var t in explicitTargets)
                {
                    int pid;
                    if (int.TryParse(t, out pid)) { if (pr.Id == pid) { take = true; break; } }
                    else if (name == t.ToLowerInvariant().Replace(".exe", "")) { take = true; break; }
                }
            }
            else if (all) take = pr.Id != Process.GetCurrentProcess().Id;
            else take = Array.IndexOf(DefaultTargets, name) >= 0;

            if (take) targets.Add(pr);
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
            Console.WriteLine("  (browser checked via history, not memory; use --deep to also scan browser memory)");

        if (listOnly)
        {
            Console.WriteLine("\nBrowser history files:");
            var dbs = FindHistoryDbs();
            if (dbs.Count == 0) Console.WriteLine("  (none found)");
            foreach (var db in dbs) Console.WriteLine("  - " + db[0] + "/" + db[1] + "  ->  " + db[2]);
            return 0;
        }

        long total = 0;
        var sw = Stopwatch.StartNew();
        if (targets.Count > 0)
        {
            Console.WriteLine("\nScanning memory across " + Environment.ProcessorCount + " CPU cores...");
            foreach (var pr in targets)
            {
                Console.Write("  " + pr.ProcessName + ".exe (pid " + pr.Id + ") ... ");
                long n = 0;
                var psw = Stopwatch.StartNew();
                try { n = ScanProcess(pr); } catch (Exception e) { Console.Write("error: " + e.Message + " "); }
                psw.Stop();
                total += n;
                Console.WriteLine((n / (1024 * 1024)) + " MB in " + psw.Elapsed.TotalSeconds.ToString("0.0") + "s");
            }
        }

        Console.WriteLine("\nScanning browser history (visited sites, on-disk)...");
        try { ScanBrowserHistories(); } catch (Exception e) { Console.WriteLine("  error: " + e.Message); }

        sw.Stop();
        Console.WriteLine("\nDone in " + sw.Elapsed.TotalSeconds.ToString("0.0") + "s (" + (total / (1024 * 1024)) + " MB of process memory).");

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
        Console.WriteLine("  (no args)   memory-scan explorer + javaw/java (parallel), and read");
        Console.WriteLine("              visited cheat sites from the browsers' on-disk history");
        Console.WriteLine("  --deep      ALSO scan live browser process memory (slower, noisier)");
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
        W("[ CHEAT SIGNATURES ] (" + cheats.Count + ")");
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
