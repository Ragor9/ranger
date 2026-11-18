// Leitura de bateria: Attack Shark X11 (HID passivo), receptores 2.4 GHz Compx (consulta HID)
// e dispositivos Bluetooth (propriedades do Windows).
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Ranger
{
    public enum Kind { Mouse, Keyboard, Headset, Gamepad, Other }

    public class DeviceState
    {
        public string Key, Name, Detail;
        public Kind Kind;
        public int Percent = -1;
        public bool Charging, Full, Stale;

        public string Signature()
        {
            return Key + "|" + Name + "|" + Detail + "|" + Percent + "|" + Charging + "|" + Full + "|" + Stale;
        }
    }

    // ---------------------------------------------------------------- HID
    static class Hid
    {
        [StructLayout(LayoutKind.Sequential)] struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid g; public int Flags; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }
        [StructLayout(LayoutKind.Sequential)] struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices, NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, out HIDP_CAPS c);
        [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int type, IntPtr caps, ref ushort len, IntPtr p);
        [DllImport("hid.dll")] static extern int HidP_GetButtonCaps(int type, IntPtr caps, ref ushort len, IntPtr p);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, int f);
        [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA di);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA di, IntPtr det, int sz, out int req, IntPtr dd);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFile(string n, uint acc, uint share, IntPtr sec, uint disp, uint flags, IntPtr t);
        [DllImport("kernel32.dll")] public static extern bool CancelIoEx(SafeFileHandle h, IntPtr o);

        public class Collection
        {
            public string Path; public ushort Vid, Pid, UsagePage, Usage, InLen, OutLen, FeatLen;
            public List<byte> InputIds = new List<byte>();
        }

        static List<string> Paths()
        {
            var list = new List<string>();
            Guid g; HidD_GetHidGuid(out g);
            IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12); // PRESENT | DEVICEINTERFACE
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return list;
            var di = new SP_DEVICE_INTERFACE_DATA(); di.cbSize = Marshal.SizeOf(di);
            for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref di); i++)
            {
                int req; SetupDiGetDeviceInterfaceDetail(set, ref di, IntPtr.Zero, 0, out req, IntPtr.Zero);
                if (req <= 0) continue;
                IntPtr buf = Marshal.AllocHGlobal(req);
                Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                if (SetupDiGetDeviceInterfaceDetail(set, ref di, buf, req, out req, IntPtr.Zero))
                    list.Add(Marshal.PtrToStringUni(buf + 4));
                Marshal.FreeHGlobal(buf);
            }
            SetupDiDestroyDeviceInfoList(set);
            return list;
        }

        public static SafeFileHandle Open(string path, bool read)
        {
            return CreateFile(path, read ? 0x80000000u : 0u, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        }

        // Leitura com I/O assíncrono (permite esperar com tempo-limite) ou leitura+escrita para feature reports.
        public static SafeFileHandle OpenAsyncRead(string path)
        {
            return CreateFile(path, 0x80000000u, 3, IntPtr.Zero, 3, 0x40000000u, IntPtr.Zero); // FILE_FLAG_OVERLAPPED
        }

        public static SafeFileHandle OpenReadWrite(string path)
        {
            return CreateFile(path, 0x80000000u | 0x40000000u, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        }

        [DllImport("hid.dll")] static extern bool HidD_SetFeature(SafeFileHandle h, byte[] b, int n);
        public static bool SetFeature(SafeFileHandle h, byte[] report) { return HidD_SetFeature(h, report, report.Length); }

        static void InputIds(ushort nValue, ushort nButton, IntPtr pp, List<byte> into)
        {
            // HIDP_VALUE_CAPS e HIDP_BUTTON_CAPS têm 72 bytes; ReportID no offset 2
            for (int kind = 0; kind < 2; kind++)
            {
                ushort n = kind == 0 ? nValue : nButton; if (n == 0) continue;
                IntPtr mem = Marshal.AllocHGlobal(72 * n);
                int r = kind == 0 ? HidP_GetValueCaps(0, mem, ref n, pp) : HidP_GetButtonCaps(0, mem, ref n, pp);
                if (r == 0x110000) for (int i = 0; i < n; i++) { byte id = Marshal.ReadByte(mem, i * 72 + 2); if (!into.Contains(id)) into.Add(id); }
                Marshal.FreeHGlobal(mem);
            }
        }

        public static List<Collection> Enumerate(ushort vidFilter)
        {
            var res = new List<Collection>();
            foreach (var p in Paths())
            {
                if (vidFilter != 0 && p.IndexOf("vid_" + vidFilter.ToString("x4"), StringComparison.OrdinalIgnoreCase) < 0) continue;
                using (var h = Open(p, false))
                {
                    if (h.IsInvalid) continue;
                    var a = new HIDD_ATTRIBUTES(); a.Size = Marshal.SizeOf(a);
                    if (!HidD_GetAttributes(h, ref a)) continue;
                    var c = new Collection { Path = p, Vid = a.VendorID, Pid = a.ProductID };
                    IntPtr pp;
                    if (HidD_GetPreparsedData(h, out pp))
                    {
                        HIDP_CAPS caps; HidP_GetCaps(pp, out caps);
                        c.UsagePage = caps.UsagePage; c.Usage = caps.Usage; c.InLen = caps.InputReportByteLength;
                        c.OutLen = caps.OutputReportByteLength; c.FeatLen = caps.FeatureReportByteLength;
                        InputIds(caps.NumberInputValueCaps, caps.NumberInputButtonCaps, pp, c.InputIds);
                        HidD_FreePreparsedData(pp);
                    }
                    res.Add(c);
                }
            }
            return res;
        }
    }

    // ------------------------------------------------- Attack Shark X11
    // O receptor (1D57:FA60) envia sozinho, a cada ~2 s, um quadro de 5 bytes: 03 xx 40 <estado> <nível>.
    // estado: 01 = na bateria, 02 = carregando, 03 = carregado. Nível = 0–100 %.
    // Só lemos; nada é enviado ao mouse.
    public class X11Provider
    {
        const ushort Vid = 0x1D57;
        const ushort DonglePid = 0xFA60;
        readonly object gate = new object();
        int percent = -1, status;
        bool wired;
        DateTime lastFrame = DateTime.MinValue, lastScan = DateTime.MinValue;
        SafeFileHandle current;
        string currentPath;
        bool cablePresent;

        public void Start()
        {
            var t = new Thread(Loop); t.IsBackground = true; t.Name = "x11"; t.Start();
        }

        static Hid.Collection FindChannel(List<Hid.Collection> cols)
        {
            Hid.Collection best = null;
            foreach (var c in cols)
            {
                if (c.Vid != Vid || c.InLen != 5 || c.OutLen != 0 || c.FeatLen != 0) continue;
                if (c.UsagePage == 0x0001 || c.UsagePage == 0x000C) continue;
                if (!c.InputIds.Contains(0x03)) continue;
                if (best == null || (best.Pid == DonglePid && c.Pid != DonglePid)) best = c; // cabo tem prioridade
            }
            return best;
        }

        void Loop()
        {
            while (true)
            {
                try
                {
                    var cols = Hid.Enumerate(Vid);
                    UpdateCable(cols);
                    var ch = FindChannel(cols);
                    if (ch == null) { Thread.Sleep(3000); continue; }
                    using (var h = Hid.Open(ch.Path, true))
                    {
                        if (h.IsInvalid) { Thread.Sleep(3000); continue; }
                        lock (gate) { current = h; currentPath = ch.Path; wired = ch.Pid != DonglePid; }
                        using (var fs = new FileStream(h, FileAccess.Read, 1, false))
                        {
                            var buf = new byte[5];
                            while (true)
                            {
                                int n = fs.Read(buf, 0, buf.Length);
                                if (n <= 0) break;
                                Parse(buf, n);
                            }
                        }
                    }
                }
                catch (Exception) { }
                lock (gate) { current = null; currentPath = null; }
                Thread.Sleep(1500);
            }
        }

        void UpdateCable(List<Hid.Collection> cols)
        {
            bool cable = false;
            foreach (var c in cols) if (c.Vid == Vid && c.Pid != DonglePid) { cable = true; break; }
            lock (gate) cablePresent = cable;
        }

        void Parse(byte[] b, int n)
        {
            if (n < 5 || b[0] != 0x03 || b[2] != 0x40) return; // outros quadros (ex.: piscar de link) são ignorados
            lock (gate)
            {
                status = b[3];
                percent = Math.Min(100, (int)b[4]);
                lastFrame = DateTime.UtcNow;
            }
        }

        // Chamado pela UI a cada ciclo.
        public DeviceState Snapshot()
        {
            SafeFileHandle toCancel = null;
            DeviceState s;
            lock (gate)
            {
                double silent = (DateTime.UtcNow - lastFrame).TotalSeconds;
                // Receptor quieto? Talvez o cabo tenha sido ligado: reprocura de vez em quando.
                if (silent > 8 && (DateTime.UtcNow - lastScan).TotalSeconds > 6)
                {
                    lastScan = DateTime.UtcNow;
                    toCancel = current;
                }
                if (percent < 0) s = null;
                else
                {
                    s = new DeviceState { Key = "x11", Name = "Attack Shark X11", Kind = Kind.Mouse, Percent = percent };
                    s.Charging = status == 0x02 || (cablePresent && silent > 8);
                    s.Full = status == 0x03;
                    s.Stale = silent > 12 && !s.Charging;
                    if (s.Full) s.Detail = "Carregado";
                    else if (s.Charging) s.Detail = cablePresent && silent > 8 ? "Carregando no cabo" : "Carregando";
                    else if (s.Stale) s.Detail = "Sem sinal · " + Ago(silent);
                    else s.Detail = wired ? "Cabo USB" : "2.4 GHz";
                }
            }
            if (toCancel != null)
            {
                ThreadPool.QueueUserWorkItem(delegate {
                    try
                    {
                        var cols = Hid.Enumerate(Vid);
                        UpdateCable(cols);
                        var ch = FindChannel(cols);
                        string cur; lock (gate) cur = currentPath;
                        if (ch != null && cur != null && !string.Equals(ch.Path, cur, StringComparison.OrdinalIgnoreCase))
                            Hid.CancelIoEx(toCancel, IntPtr.Zero); // força a troca de canal
                    }
                    catch (Exception) { }
                });
            }
            return s;
        }

        public static string Ago(double sec)
        {
            if (sec < 60) return "agora";
            if (sec < 3600) return "há " + (int)(sec / 60) + " min";
            if (sec < 86400) return "há " + (int)(sec / 3600) + " h";
            return "há " + (int)(sec / 86400) + " d";
        }
    }

    // ---------------------------------------------------- receptores Compx
    // Receptores 2.4 GHz com chip Compx (VID 25A7), usados por várias marcas (ex.: Redragon M808-KS).
    // O receptor só responde quando perguntado:
    //   pedido   (feature id 8, 17 bytes, página 0xFF02): 08 04 00 00 00 00 … chk
    //   resposta (input   id 9, 17 bytes, página 0xFF01): 09 04 <estado> 00 00 <n> <nível> <b7> … chk
    // estado 00 = ok (01 = recusado/sem resposta do mouse); nível 0–10 = dezenas de %.
    // chk faz os 17 bytes somarem 0x55. O comando 04 só lê; nada é gravado no mouse.
    // O receptor é reconhecido pelo formato dos canais, não por um PID específico.
    public class CompxProvider
    {
        const ushort Vid = 0x25A7;
        const int Len = 17;
        const double PollSec = 60, RetrySec = 10;
        readonly object gate = new object();
        readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();

        class Entry { public string Name; public int Percent = -1; public DateTime Last = DateTime.MinValue; public bool Present; }

        // Modelos conhecidos (PID do receptor → nome); os demais aparecem com um nome genérico.
        static string ModelName(ushort pid)
        {
            switch (pid)
            {
                case 0xFA07: case 0xFA08: return "Redragon M808-KS";
                default: return "Mouse 2.4 GHz";
            }
        }

        public void Start()
        {
            var t = new Thread(Loop); t.IsBackground = true; t.Name = "compx"; t.Start();
        }

        // Agrupa as coleções do mesmo receptor (os canais de pedido e resposta podem ficar em interfaces diferentes).
        static string DeviceKey(Hid.Collection c) { return "pid_" + c.Pid.ToString("x4"); }

        void Loop()
        {
            while (true)
            {
                bool any = false;
                try
                {
                    var cols = Hid.Enumerate(Vid);
                    var seen = new HashSet<string>();
                    var groups = new Dictionary<string, Hid.Collection[]>(); // [0] pedido, [1] resposta
                    foreach (var c in cols)
                    {
                        if (c.Vid != Vid || c.UsagePage < 0xFF00) continue;
                        string k = DeviceKey(c);
                        Hid.Collection[] g;
                        if (!groups.TryGetValue(k, out g)) groups[k] = g = new Hid.Collection[2];
                        if (c.FeatLen == Len && c.InLen == 0) g[0] = c;
                        else if (c.InLen == Len && c.InputIds.Contains(0x09)) g[1] = c;
                    }
                    foreach (var kv in groups)
                    {
                        var g = kv.Value;
                        if (g[0] == null || g[1] == null) continue;
                        any = true; seen.Add(kv.Key);
                        int level = Query(g[0].Path, g[1].Path);
                        lock (gate)
                        {
                            Entry e;
                            if (!entries.TryGetValue(kv.Key, out e)) entries[kv.Key] = e = new Entry { Name = ModelName(g[0].Pid) };
                            e.Present = true;
                            if (level >= 0) { e.Percent = Math.Min(100, level * 10); e.Last = DateTime.UtcNow; }
                        }
                    }
                    lock (gate) foreach (var kv in entries) if (!seen.Contains(kv.Key)) kv.Value.Present = false;
                }
                catch (Exception) { }
                Thread.Sleep(TimeSpan.FromSeconds(any ? PollSec : RetrySec));
            }
        }

        static byte[] Request()
        {
            var b = new byte[Len];
            b[0] = 0x08; b[1] = 0x04;
            int sum = 0; for (int i = 0; i < Len - 1; i++) sum += b[i];
            b[Len - 1] = (byte)(0x55 - sum);
            return b;
        }

        static bool Valid(byte[] b, int n)
        {
            if (n < Len || b[0] != 0x09 || b[1] != 0x04) return false;
            int sum = 0; for (int i = 0; i < Len; i++) sum += b[i];
            return (sum & 0xFF) == 0x55;
        }

        // Pergunta a bateria e espera a resposta. Devolve o nível (0–10) ou -1.
        static int Query(string reqPath, string inPath)
        {
            using (var ih = Hid.OpenAsyncRead(inPath))
            using (var fh = Hid.OpenReadWrite(reqPath))
            {
                if (ih.IsInvalid || fh.IsInvalid) return -1;
                using (var fs = new FileStream(ih, FileAccess.Read, Len, true))
                {
                    var buf = new byte[Len];
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        // A leitura começa antes do pedido para não perder a resposta.
                        IAsyncResult ar = fs.BeginRead(buf, 0, Len, null, null);
                        if (!Hid.SetFeature(fh, Request())) { Cancel(ih, fs, ar); return -1; }
                        DateTime until = DateTime.UtcNow.AddMilliseconds(800);
                        while (true)
                        {
                            int left = (int)(until - DateTime.UtcNow).TotalMilliseconds;
                            if (left <= 0 || !ar.AsyncWaitHandle.WaitOne(left)) { Cancel(ih, fs, ar); break; }
                            int n = fs.EndRead(ar);
                            if (Valid(buf, n))
                            {
                                if (buf[2] == 0x00 && buf[5] >= 1 && buf[6] <= 10) return buf[6];
                                break; // recusado (ex.: mouse dormindo): tenta de novo
                            }
                            ar = fs.BeginRead(buf, 0, Len, null, null); // outro quadro (ex.: do software do fabricante)
                        }
                        Thread.Sleep(150);
                    }
                }
            }
            return -1;
        }

        static void Cancel(SafeFileHandle h, FileStream fs, IAsyncResult ar)
        {
            Hid.CancelIoEx(h, IntPtr.Zero);
            try { fs.EndRead(ar); } catch (Exception) { }
        }

        public List<DeviceState> Snapshot()
        {
            var list = new List<DeviceState>();
            lock (gate)
            {
                foreach (var kv in entries)
                {
                    var e = kv.Value;
                    if (!e.Present || e.Percent < 0) continue;
                    double age = (DateTime.UtcNow - e.Last).TotalSeconds;
                    var s = new DeviceState { Key = "compx:" + kv.Key, Name = e.Name, Kind = Kind.Mouse, Percent = e.Percent };
                    s.Stale = age > PollSec * 3;
                    s.Detail = s.Stale ? "Sem sinal · " + X11Provider.Ago(age) : "2.4 GHz";
                    list.Add(s);
                }
            }
            return list;
        }
    }

    // ---------------------------------------------------------- Bluetooth
    // O Windows guarda a bateria de Bluetooth (clássico e LE) em DEVPKEY_Bluetooth_Battery.
    // O valor fica salvo mesmo desligado, então só mostramos os que estão conectados agora.
    public static class BluetoothProvider
    {
        [StructLayout(LayoutKind.Sequential)] struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public int DevInst; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] struct DEVPROPKEY { public Guid fmtid; public uint pid; public DEVPROPKEY(string g, uint p) { fmtid = new Guid(g); pid = p; } }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevs(IntPtr g, string enumerator, IntPtr w, int f);
        [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInfo(IntPtr s, int i, ref SP_DEVINFO_DATA d);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInstanceId(IntPtr s, ref SP_DEVINFO_DATA d, StringBuilder sb, int size, out int req);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDevicePropertyW(IntPtr s, ref SP_DEVINFO_DATA d, ref DEVPROPKEY k, out uint type, byte[] buf, int size, out int req, int flags);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);

        static DEVPROPKEY KBattery = new DEVPROPKEY("104EA319-6EE2-4701-BD47-8DDBF425BBE5", 2);
        static DEVPROPKEY KConnected = new DEVPROPKEY("83DA6326-97A6-4088-9453-A1923F573B29", 15);
        static DEVPROPKEY KFriendly = new DEVPROPKEY("a45c254e-df1c-4efd-8020-67d146a850e0", 14);
        static DEVPROPKEY KDesc = new DEVPROPKEY("a45c254e-df1c-4efd-8020-67d146a850e0", 2);
        static DEVPROPKEY KCod = new DEVPROPKEY("2BD67D8B-8BEB-48D5-87E0-6CDA3428040A", 10);

        class Entry { public string Name; public bool Connected; public bool HasDev; public int Battery = -1; public uint Cod; }

        static byte[] Prop(IntPtr set, ref SP_DEVINFO_DATA d, DEVPROPKEY k)
        {
            uint type; int req; var buf = new byte[512];
            if (!SetupDiGetDevicePropertyW(set, ref d, ref k, out type, buf, buf.Length, out req, 0) || req <= 0) return null;
            var r = new byte[Math.Min(req, buf.Length)]; Array.Copy(buf, r, r.Length); return r;
        }

        static string Str(byte[] b) { return b == null ? null : Encoding.Unicode.GetString(b).TrimEnd('\0'); }

        static readonly Regex DevAddr = new Regex(@"DEV_([0-9A-F]{12})", RegexOptions.Compiled);
        static readonly Regex TailAddr = new Regex(@"([0-9A-F]{12})(_C[0-9A-F]+)?$", RegexOptions.Compiled);

        static string Address(string id)
        {
            id = id.ToUpperInvariant();
            var m = DevAddr.Match(id); if (m.Success) return m.Groups[1].Value;
            int amp = id.LastIndexOf('&');
            string tail = amp >= 0 ? id.Substring(amp + 1) : id;
            m = TailAddr.Match(tail); return m.Success ? m.Groups[1].Value : null;
        }

        public static List<DeviceState> Poll()
        {
            var map = new Dictionary<string, Entry>();
            foreach (var en in new[] { "BTHENUM", "BTHLE", "BTHLEDEVICE" })
            {
                IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, en, IntPtr.Zero, 0x2 | 0x4); // PRESENT | ALLCLASSES
                if (set == IntPtr.Zero || set == new IntPtr(-1)) continue;
                var d = new SP_DEVINFO_DATA(); d.cbSize = Marshal.SizeOf(d);
                var sb = new StringBuilder(512);
                for (int i = 0; SetupDiEnumDeviceInfo(set, i, ref d); i++)
                {
                    int req;
                    if (!SetupDiGetDeviceInstanceId(set, ref d, sb, sb.Capacity, out req)) continue;
                    string id = sb.ToString();
                    string addr = Address(id);
                    if (addr == null) continue;
                    Entry e; if (!map.TryGetValue(addr, out e)) { e = new Entry(); map[addr] = e; }

                    var bat = Prop(set, ref d, KBattery);
                    if (bat != null && bat.Length >= 1 && e.Battery < 0) e.Battery = bat[0];

                    bool isDevNode = id.ToUpperInvariant().Contains("\\DEV_");
                    if (isDevNode)
                    {
                        e.HasDev = true;
                        var con = Prop(set, ref d, KConnected);
                        e.Connected = con != null && con.Length >= 1 && con[0] != 0;
                        e.Name = Str(Prop(set, ref d, KFriendly)) ?? Str(Prop(set, ref d, KDesc));
                        var cod = Prop(set, ref d, KCod);
                        if (cod != null && cod.Length >= 4) e.Cod = BitConverter.ToUInt32(cod, 0);
                    }
                }
                SetupDiDestroyDeviceInfoList(set);
            }

            var list = new List<DeviceState>();
            foreach (var kv in map)
            {
                var e = kv.Value;
                if (!e.HasDev || !e.Connected || e.Battery < 0 || string.IsNullOrEmpty(e.Name)) continue;
                list.Add(new DeviceState
                {
                    Key = "bt:" + kv.Key, Name = e.Name, Kind = Guess(e.Name, e.Cod),
                    Percent = Math.Min(100, e.Battery), Detail = "Bluetooth"
                });
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return list;
        }

        static Kind Guess(string name, uint cod)
        {
            uint major = (cod >> 8) & 0x1F, minor = (cod >> 2) & 0x3F;
            if (major == 0x04) return Kind.Headset;
            if (major == 0x05)
            {
                if ((minor & 0x0F) == 0x01 || (minor & 0x0F) == 0x02) return Kind.Gamepad;
                if ((minor & 0x30) == 0x10) return Kind.Keyboard;
                if ((minor & 0x30) == 0x20) return Kind.Mouse;
            }
            string n = name.ToLowerInvariant();
            if (Regex.IsMatch(n, "controller|controle|gamepad|xbox|dualsense|dualshock|joy")) return Kind.Gamepad;
            if (Regex.IsMatch(n, "headset|headphone|fone|buds|airpods|earbud|wh-|wf-|arctis|cloud|blackshark|kraken|hammerhead")) return Kind.Headset;
            if (Regex.IsMatch(n, "keyboard|teclado|keys|kb\\b")) return Kind.Keyboard;
            if (Regex.IsMatch(n, "mouse|mx |\\bm\\d")) return Kind.Mouse;
            return Kind.Other;
        }
    }
}
