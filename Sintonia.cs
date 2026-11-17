// Sintonia - controle de som do Windows na bandeja: saidas (fone, caixa, monitor...),
// microfones, volume e mudo por app, saida de cada app, perfis e atalhos.
// Leve: com o painel fechado nao ha nenhum timer rodando; so acorda com clique,
// atalho ou aviso do proprio Windows (dispositivo trocado, app comecou a tocar).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

[assembly: AssemblyTitle("Sintonia")]
[assembly: AssemblyProduct("Sintonia")]
[assembly: AssemblyDescription("Saidas de som, volume por app e perfis")]
[assembly: AssemblyCopyright("Licença MIT")]
[assembly: AssemblyVersion("1.0.2.0")]
[assembly: AssemblyFileVersion("1.0.2.0")]

static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string s);
    public static uint MsgAbrir, MsgConfig, MsgPerfil, MsgPedido;
    public static string ArqPedido { get { return Path.Combine(Path.GetTempPath(), "Sintonia_pedido.txt"); } }
    public static int PaginaInicial = -1;

    [STAThread]
    static void Main(string[] args)
    {
        // Coordenadas e tamanhos certos em cada monitor (100% e 175%)
        try { if (!SetProcessDpiAwarenessContext(new IntPtr(-4) /*PER_MONITOR_AWARE_V2*/)) SetProcessDPIAware(); }
        catch { try { SetProcessDPIAware(); } catch { } }

        if (args.Length == 2 && args[0] == "--ico") { Desenho.SalvarIco(args[1]); return; }
        if (args.Length == 2 && args[0] == "--logo") { using (var b = Desenho.Logo(256)) b.Save(args[1]); return; }
        if (args.Length == 2 && args[0] == "--png")
        {
            using (var b = new Bitmap(160, 64))
            using (var g = Graphics.FromImage(b))
            {
                g.Clear(Color.FromArgb(32, 32, 32));
                using (var l = Desenho.Logo(64)) g.DrawImage(l, 0, 0);
                using (var t = Desenho.Bandeja(32, Color.White, false)) g.DrawImage(t, 76, 16);
                using (var t = Desenho.Bandeja(16, Color.White, false)) g.DrawImage(t, 114, 24); using (var t = Desenho.Bandeja(16, Color.White, true)) g.DrawImage(t, 138, 24);
                b.Save(args[1]);
            }
            return;
        }
        if (args.Length == 2 && args[0] == "--diagnostico") { Diagnostico(args[1]); return; }
        if (args.Length == 3 && args[0] == "--autoteste") { AutoTeste(args[1], args[2]); return; }
        if (args.Length == 5 && args[0] == "--testemover") { TesteMover(args[1], args[2], args[3], args[4]); return; }
        if (args.Length == 2 && args[0] == "--testeopcoes") { App.TesteOpcoes(args[1]); return; }
        MsgAbrir = RegisterWindowMessage("Sintonia_AbrirPainel");
        MsgConfig = RegisterWindowMessage("Sintonia_AbrirConfig");
        MsgPerfil = RegisterWindowMessage("Sintonia_AtivarPerfil");
        MsgPedido = RegisterWindowMessage("Sintonia_Pedido");
        bool pedido = false;
        if (args.Length >= 3 && args[0] == "--perfil-op")
        {
            File.WriteAllLines(ArqPedido, new[] { string.Join("|", args, 1, args.Length - 1) });
            pedido = true;
        }
        // Sintonia.exe --perfil Nome: ativa um perfil no Sintonia que ja esta aberto
        int perfilIdx = -1;
        if (args.Length >= 2 && args[0] == "--perfil")
        {
            try
            {
                string nome = string.Join(" ", args, 1, args.Length - 1);
                int i = 0;
                foreach (var l in File.ReadAllLines(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sintonia.ini")))
                    if (l.StartsWith("perfil="))
                    {
                        if (string.Equals(l.Substring(7).Split('|')[0], nome, StringComparison.CurrentCultureIgnoreCase)) { perfilIdx = i; break; }
                        i++;
                    }
            }
            catch { }
            if (perfilIdx < 0) return;
        }
        // Sintonia.exe --config [pagina]: abre as Configuracoes (0 Geral, 1 Perfis, 2 Atalhos, 3 Dispositivos)
        int paginaConfig = -1;
        if (args.Length >= 1 && args[0] == "--config") { paginaConfig = 0; if (args.Length >= 2) int.TryParse(args[1], out paginaConfig); }
        PaginaInicial = paginaConfig;
        bool novo;
        using (var m = new Mutex(true, "Sintonia_Unico", out novo))
        {
            if (!novo)
            {
                // ja esta rodando: pede para a outra copia abrir o painel
                AllowSetForegroundWindow(-1);
                if (pedido) PostMessage(new IntPtr(0xFFFF), MsgPedido, IntPtr.Zero, IntPtr.Zero);
                else if (perfilIdx >= 0) PostMessage(new IntPtr(0xFFFF), MsgPerfil, new IntPtr(perfilIdx), IntPtr.Zero);
                else if (paginaConfig >= 0) PostMessage(new IntPtr(0xFFFF), MsgConfig, new IntPtr(paginaConfig), IntPtr.Zero);
                else PostMessage(new IntPtr(0xFFFF), MsgAbrir, IntPtr.Zero, IntPtr.Zero);
                return;
            }
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // erro inesperado: anota em %APPDATA%\Som.log em vez de fechar o app
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Log(e.Exception);
            Application.Run(new App());
        }
    }

    public static void Log(Exception ex)
    {
        try
        {
            File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sintonia.log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + "\r\n\r\n");
        }
        catch { }
    }

    // Som.exe --diagnostico arquivo.txt : lista o que o app enxerga (nao muda nada)
    static void Diagnostico(string arq)
    {
        var l = new List<string>();
        l.Add("Saida por app disponivel: " + Politica.Disponivel);
        foreach (var t in Screen.AllScreens)
        {
            int borda;
            var util = App.AreaUtil(t, out borda);
            l.Add("Tela " + t.DeviceName + " " + t.Bounds + " area Windows " + t.WorkingArea + " | area Sintonia " + util +
                  " barra=" + new[] { "embaixo", "em cima", "esquerda", "direita" }[borda] + " escala " + App.EscalaEm(new Point(t.Bounds.X + 10, t.Bounds.Y + 10)));
        }
        l.Add("Cursor " + Cursor.Position);
        foreach (var fluxo in new[] { 0, 1 })
        {
            var ds = Audio.Listar(fluxo, true, true, null);
            l.Add(fluxo == 0 ? "== Saidas ==" : "== Microfones ==");
            foreach (var d in ds)
            {
                l.Add((d.Padrao ? "* " : "  ") + d.Nome + " | " + d.Adaptador + " | forma " + d.Forma + " | vol " +
                      Math.Round(d.Vol * 100) + (d.Mudo ? " mudo" : "") + " | " + d.Id);
                foreach (var a in d.Apps)
                    l.Add("      app: " + a.Nome + " (" + a.Exe + ") pids " + string.Join(",", a.Pids) + " vol " +
                          Math.Round(a.Vol * 100) + (a.Mudo ? " mudo" : "") + (a.Ativo ? " tocando" : "") + " saida=" + (a.Saida ?? "padrao"));
            }
            Audio.Liberar(ds);
        }
        File.WriteAllLines(arq, l);
    }

    // --testemover exe idDispositivo modo arquivo: pede a troca de saida de um app e
    // acompanha por 4 s em qual saida ele esta tocando de verdade
    // modo: direto | nulo-antes | nulo
    static void TesteMover(string exe, string alvo, string modo, string arq)
    {
        var l = new List<string>();
        Func<string> ondeToca = () =>
        {
            var ds = Audio.Listar(0, true, true, null, false);
            var r = new List<string>();
            foreach (var d in ds) foreach (var a in d.Apps) if (a.Exe == exe && a.Ativo) r.Add(d.Nome);
            Audio.Liberar(ds);
            return string.Join(",", r);
        };
        var pids = new List<uint>();
        foreach (var s in Audio.Sessoes()) if (s.Value == exe && !pids.Contains(s.Key)) pids.Add(s.Key);
        l.Add("pids: " + string.Join(",", pids) + " | antes toca em: " + ondeToca());
        foreach (var p in pids)
        {
            if (modo == "nulo-antes" || modo == "nulo") Politica.Definir(p, null);
            if (modo == "nulo-antes") Thread.Sleep(300);
            if (modo != "nulo") Politica.Definir(p, alvo);
        }
        for (int i = 1; i <= 8; i++)
        {
            Thread.Sleep(500);
            l.Add((i * 0.5) + "s: toca em " + ondeToca() + " | escolhida=" + (Politica.Obter(pids.Count > 0 ? pids[0] : 0) ?? "padrao"));
        }
        File.WriteAllLines(arq, l);
    }

    // Som.exe --autoteste exe arquivo.txt : testa as acoes e desfaz cada uma
    static void AutoTeste(string exe, string arq)
    {
        var l = new List<string>();
        var ds = Audio.Listar(0, true, true, null);
        Disp pad = ds.Find(d => d.Padrao), outra = ds.Find(d => !d.Padrao);
        GrupoApp app = null;
        foreach (var d in ds) foreach (var a in d.Apps) if (a.Exe == exe && app == null) app = a;
        if (app == null || pad == null || outra == null) { l.Add("faltou app/saida"); File.WriteAllLines(arq, l); return; }
        uint pid = app.Pids[0];
        l.Add("app " + app.Nome + " pid " + pid + " saida antes=" + (Politica.Obter(pid) ?? "padrao"));
        l.Add("definir -> " + outra.Nome + ": " + Politica.Definir(pid, outra.Id) + " / lido=" + (Politica.Obter(pid) ?? "padrao"));
        l.Add("voltar padrao: " + Politica.Definir(pid, null) + " / lido=" + (Politica.Obter(pid) ?? "padrao"));
        float v0 = app.Vol; bool m0 = app.Mudo;
        foreach (var s in app.Vols) { s.SetMasterVolume(0.37f, ref Audio.Ctx); s.SetMute(true, ref Audio.Ctx); }
        float v1; bool m1; app.Vols[0].GetMasterVolume(out v1); app.Vols[0].GetMute(out m1);
        l.Add("volume app -> 37 mudo: lido " + Math.Round(v1 * 100) + " mudo=" + m1);
        foreach (var s in app.Vols) { s.SetMasterVolume(v0, ref Audio.Ctx); s.SetMute(m0, ref Audio.Ctx); }
        app.Vols[0].GetMasterVolume(out v1); app.Vols[0].GetMute(out m1);
        l.Add("restaurado: " + Math.Round(v1 * 100) + " mudo=" + m1);
        string idPad = pad.Id;
        l.Add("padrao -> " + outra.Nome + ": " + Audio.DefinirPadrao(outra.Id) + " / lido=" + (Audio.PadraoId(0) == outra.Id));
        Thread.Sleep(400);
        l.Add("padrao volta -> " + pad.Nome + ": " + Audio.DefinirPadrao(idPad) + " / lido=" + (Audio.PadraoId(0) == idPad));
        Audio.Liberar(ds);
        File.WriteAllLines(arq, l);
    }
}

// =====================================================================
//  Core Audio (COM) - interfaces oficiais do Windows
// =====================================================================
[StructLayout(LayoutKind.Sequential)]
public struct PROPERTYKEY
{
    public Guid fmtid; public int pid;
    public PROPERTYKEY(Guid g, int p) { fmtid = g; pid = p; }
}

[StructLayout(LayoutKind.Explicit)]
struct PROPVARIANT
{
    [FieldOffset(0)] public ushort vt;
    [FieldOffset(8)] public IntPtr p;
    [FieldOffset(8)] public uint u;
    [FieldOffset(16)] public IntPtr resto;
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCo { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IMMDeviceCollection c);
    [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice d);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice d);
    [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient c);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient c);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out int n);
    [PreserveSig] int Item(int i, out IMMDevice d);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
    [PreserveSig] int OpenPropertyStore(int acesso, out IPropertyStore s);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out int st);
}

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPropertyStore
{
    [PreserveSig] int GetCount(out int n);
    [PreserveSig] int GetAt(int i, out PROPERTYKEY k);
    [PreserveSig] int GetValue(ref PROPERTYKEY k, out PROPVARIANT v);
}

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback c);
    [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback c);
    [PreserveSig] int GetChannelCount(out int n);
    [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid ctx);
    [PreserveSig] int SetMasterVolumeLevelScalar(float v, ref Guid ctx);
    [PreserveSig] int GetMasterVolumeLevel(out float db);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float v);
    [PreserveSig] int SetChannelVolumeLevel(uint c, float db, ref Guid ctx);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint c, float v, ref Guid ctx);
    [PreserveSig] int GetChannelVolumeLevel(uint c, out float db);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint c, out float v);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool m, ref Guid ctx);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool m);
}

[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionManager2
{
    [PreserveSig] int GetAudioSessionControl(IntPtr a, int b, out IntPtr c);
    [PreserveSig] int GetSimpleAudioVolume(IntPtr a, int b, out IntPtr c);
    [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator e);
    [PreserveSig] int RegisterSessionNotification(IAudioSessionNotification n);
    [PreserveSig] int UnregisterSessionNotification(IAudioSessionNotification n);
}

[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionEnumerator
{
    [PreserveSig] int GetCount(out int n);
    [PreserveSig] int GetSession(int i, out IAudioSessionControl2 s);
}

[ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionControl2
{
    [PreserveSig] int GetState(out int st);
    [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string n);
    [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string n, ref Guid ctx);
    [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string p);
    [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string p, ref Guid ctx);
    [PreserveSig] int GetGroupingParam(out Guid g);
    [PreserveSig] int SetGroupingParam(ref Guid g, ref Guid ctx);
    [PreserveSig] int RegisterAudioSessionNotification(IntPtr n);
    [PreserveSig] int UnregisterAudioSessionNotification(IntPtr n);
    [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string s);
    [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string s);
    [PreserveSig] int GetProcessId(out uint pid);
    [PreserveSig] int IsSystemSoundsSession();
    [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool b);
}

// Medidor do Windows: nivel do som que esta tocando agora (0 a 1)
[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioMeterInformation
{
    [PreserveSig] int GetPeakValue(out float pico);
}

[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ISimpleAudioVolume
{
    [PreserveSig] int SetMasterVolume(float v, ref Guid ctx);
    [PreserveSig] int GetMasterVolume(out float v);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool m, ref Guid ctx);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool m);
}

// Avisos que o Windows manda para nos (implementados pela classe Vigia)
[ComVisible(true), Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMNotificationClient
{
    [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, int st);
    [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
    [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
    [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string id);
    [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PROPERTYKEY k);
}

[ComVisible(true), Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioSessionNotification
{
    [PreserveSig] int OnSessionCreated([MarshalAs(UnmanagedType.IUnknown)] object s);
}

[ComVisible(true), Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioEndpointVolumeCallback
{
    [PreserveSig] int OnNotify(IntPtr dados);
}

// Nao documentada (usada pelo proprio Windows e pelo EarTrumpet): trocar o dispositivo padrao
[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat();
    [PreserveSig] int GetDeviceFormat();
    [PreserveSig] int ResetDeviceFormat();
    [PreserveSig] int SetDeviceFormat();
    [PreserveSig] int GetProcessingPeriod();
    [PreserveSig] int SetProcessingPeriod();
    [PreserveSig] int GetShareMode();
    [PreserveSig] int SetShareMode();
    [PreserveSig] int GetPropertyValue();
    [PreserveSig] int SetPropertyValue();
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
}
[ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] class PolicyConfigClient { }

// =====================================================================
//  Modelo
// =====================================================================
class Disp
{
    public string Id, Nome, NomeOriginal, Adaptador;
    public int Forma, Fluxo;      // Fluxo: 0 = saida, 1 = microfone
    public bool Padrao, Mudo;
    public float Vol;
    public IAudioEndpointVolume Ep;
    public IAudioMeterInformation Medidor;
    public List<GrupoApp> Apps = new List<GrupoApp>();
}

class GrupoApp
{
    public string Exe, Caminho, Nome, Saida; // Saida: id do dispositivo escolhido para o app (null = padrao)
    public bool Sistema, Mudo, Ativo;
    public float Vol;
    public List<ISimpleAudioVolume> Vols = new List<ISimpleAudioVolume>();
    public List<uint> Pids = new List<uint>();
    public List<object> Rcws = new List<object>();
    public List<IAudioMeterInformation> Medidores = new List<IAudioMeterInformation>();
}

class Perfil
{
    public string Nome, Saida, Entrada;
    public uint Mod, Vk;
    public List<string> Gatilhos = new List<string>();  // exes que ativam o perfil sozinhos
    public Dictionary<string, string> Regras = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    // null = perfil antigo que nao guardou isso (entao nao mexe)
    public Dictionary<string, float> Volumes, AppVol;      // dispositivo -> volume / exe -> volume
    public Dictionary<string, bool> Mudos, AppMudo, Abertos;
    public List<string> Ocultos, Ordem;
}

// =====================================================================
//  Acesso ao audio (thread da interface)
// =====================================================================
static class Audio
{
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT p);

    static IMMDeviceEnumerator en;
    public static IMMDeviceEnumerator En
    {
        get { if (en == null) en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo(); return en; }
    }
    public static Guid IID_EP = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
    public static Guid IID_SM2 = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
    public static Guid IID_MEDIDOR = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064");
    public static bool ComMedidor;   // so pega os medidores quando a animacao esta ligada
    public static Guid Ctx = Guid.NewGuid();

    static readonly Guid PK_DEV = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
    static readonly Guid PK_IFACE = new Guid("026e516e-b814-414b-83cd-856d6fef4822");
    static readonly Guid PK_FORMA = new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e");

    public static void Soltar(object o) { if (o != null) try { Marshal.ReleaseComObject(o); } catch { } }

    public static string PadraoId(int fluxo)
    {
        IMMDevice d;
        try { if (En.GetDefaultAudioEndpoint(fluxo, 0, out d) != 0 || d == null) return null; }
        catch { return null; }
        string id; d.GetId(out id);
        Soltar(d);
        return id;
    }

    public static bool Existe(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        IMMDevice d;
        try
        {
            if (En.GetDevice(id, out d) != 0 || d == null) return false;
            int st; d.GetState(out st);
            Soltar(d);
            return st == 1;
        }
        catch { return false; }
    }

    static string Texto(IPropertyStore ps, Guid g, int pid)
    {
        var k = new PROPERTYKEY(g, pid);
        PROPVARIANT v;
        if (ps.GetValue(ref k, out v) != 0) return null;
        string s = v.vt == 31 /*VT_LPWSTR*/ ? Marshal.PtrToStringUni(v.p) : null;
        PropVariantClear(ref v);
        return s;
    }

    static int Numero(IPropertyStore ps, Guid g, int pid)
    {
        var k = new PROPERTYKEY(g, pid);
        PROPVARIANT v;
        if (ps.GetValue(ref k, out v) != 0) return -1;
        int n = v.vt == 19 /*VT_UI4*/ ? (int)v.u : -1;
        PropVariantClear(ref v);
        return n;
    }

    public static List<Disp> Listar(int fluxo, bool comApps, bool inativos, Dictionary<string, string> nomes, bool filtrar = true)
    {
        var l = new List<Disp>();
        string pad = PadraoId(fluxo);
        IMMDeviceCollection col;
        try { if (En.EnumAudioEndpoints(fluxo, 1 /*ATIVOS*/, out col) != 0) return l; }
        catch { return l; }
        int n; col.GetCount(out n);
        for (int i = 0; i < n; i++)
        {
            IMMDevice d;
            if (col.Item(i, out d) != 0 || d == null) continue;
            try
            {
                var x = new Disp { Fluxo = fluxo };
                d.GetId(out x.Id);
                IPropertyStore ps;
                if (d.OpenPropertyStore(0, out ps) == 0 && ps != null)
                {
                    x.NomeOriginal = Texto(ps, PK_DEV, 2);
                    x.Adaptador = Texto(ps, PK_IFACE, 2);
                    x.Forma = Numero(ps, PK_FORMA, 0);
                    if (string.IsNullOrEmpty(x.NomeOriginal)) x.NomeOriginal = Texto(ps, PK_DEV, 14);
                    Soltar(ps);
                }
                if (string.IsNullOrEmpty(x.NomeOriginal)) x.NomeOriginal = fluxo == 0 ? "Saída de som" : "Microfone";
                string ren;
                x.Nome = nomes != null && nomes.TryGetValue(x.Id, out ren) ? ren : x.NomeOriginal;
                x.Padrao = string.Equals(x.Id, pad, StringComparison.OrdinalIgnoreCase);
                object o;
                if (d.Activate(ref IID_EP, 23, IntPtr.Zero, out o) == 0 && o != null)
                {
                    x.Ep = (IAudioEndpointVolume)o;
                    x.Ep.GetMasterVolumeLevelScalar(out x.Vol);
                    x.Ep.GetMute(out x.Mudo);
                }
                if (ComMedidor)
                {
                    object om;
                    if (d.Activate(ref IID_MEDIDOR, 23, IntPtr.Zero, out om) == 0 && om != null) x.Medidor = (IAudioMeterInformation)om;
                }
                if (comApps && fluxo == 0) LerApps(d, x, inativos, filtrar);
                l.Add(x);
            }
            catch { }
            finally { Soltar(d); }
        }
        Soltar(col);
        l.Sort((a, b) => string.Compare(a.Nome, b.Nome, StringComparison.CurrentCultureIgnoreCase));
        return l;
    }

    static void LerApps(IMMDevice d, Disp x, bool inativos, bool filtrar)
    {
        object o;
        if (d.Activate(ref IID_SM2, 23, IntPtr.Zero, out o) != 0 || o == null) return;
        var sm = (IAudioSessionManager2)o;
        IAudioSessionEnumerator se;
        if (sm.GetSessionEnumerator(out se) == 0 && se != null)
        {
            int n; se.GetCount(out n);
            var porChave = new Dictionary<string, GrupoApp>();
            for (int i = 0; i < n; i++)
            {
                IAudioSessionControl2 s;
                if (se.GetSession(i, out s) != 0 || s == null) continue;
                int st; s.GetState(out st);
                uint pid; s.GetProcessId(out pid);
                bool sis = s.IsSystemSoundsSession() == 0;
                string caminho = sis ? null : Processos.Caminho(pid);
                if (st == 2 || (!inativos && st == 0) || (!sis && caminho == null)) { Soltar(s); continue; }
                string chave = sis ? "*sistema" : caminho.ToLowerInvariant();
                GrupoApp g;
                bool m; float v;
                var vol = s as ISimpleAudioVolume;
                if (!porChave.TryGetValue(chave, out g))
                {
                    g = new GrupoApp { Sistema = sis, Caminho = caminho, Mudo = true };
                    g.Exe = sis ? "*sistema" : Path.GetFileName(caminho).ToLowerInvariant();
                    if (sis) g.Nome = "Sons do sistema";
                    else
                    {
                        g.Nome = Processos.Nome(caminho);
                        if (g.Nome == null)
                        {
                            string dn; s.GetDisplayName(out dn);
                            g.Nome = !string.IsNullOrEmpty(dn) && !dn.StartsWith("@") ? dn : Processos.NomeArquivo(caminho);
                        }
                    }
                    porChave[chave] = g;
                    x.Apps.Add(g);
                    if (vol != null && vol.GetMasterVolume(out v) == 0) g.Vol = v;
                }
                if (vol != null) { g.Vols.Add(vol); if (vol.GetMute(out m) == 0) g.Mudo = g.Mudo && m; }
                if (ComMedidor) { var med = s as IAudioMeterInformation; if (med != null) g.Medidores.Add(med); }
                if (st == 1) g.Ativo = true;
                if (pid != 0 && !g.Pids.Contains(pid)) g.Pids.Add(pid);
                g.Rcws.Add(s);
            }
            Soltar(se);
        }
        Soltar(sm);
        foreach (var g in x.Apps)
        {
            if (g.Vols.Count == 0) g.Mudo = false;
            if (!g.Sistema && g.Pids.Count > 0) g.Saida = Politica.Obter(g.Pids[0]);
        }
        // Cada app aparece so na saida onde ele toca: a que foi escolhida para ele, ou a
        // padrao se nao tiver escolha (ou onde ele estiver tocando agora mesmo)
        if (filtrar)
            x.Apps.RemoveAll(g =>
            {
                bool fica = g.Sistema ? x.Padrao
                          : g.Ativo || (g.Saida != null ? Politica.Igual(g.Saida, x.Id) : x.Padrao);
                if (fica) return false;
                foreach (var r in g.Rcws) Soltar(r);
                g.Rcws.Clear(); g.Vols.Clear();
                return true;
            });
        x.Apps.Sort((a, b) =>
        {
            if (a.Sistema != b.Sistema) return a.Sistema ? 1 : -1;
            return string.Compare(a.Nome, b.Nome, StringComparison.CurrentCultureIgnoreCase);
        });
    }

    public static void Liberar(List<Disp> l)
    {
        if (l == null) return;
        foreach (var d in l)
        {
            Soltar(d.Ep); d.Ep = null;
            Soltar(d.Medidor); d.Medidor = null;
            foreach (var g in d.Apps) { foreach (var r in g.Rcws) Soltar(r); g.Rcws.Clear(); g.Vols.Clear(); g.Medidores.Clear(); }
        }
        l.Clear();
    }

    // Apps que tem som agora (pid + exe), em todas as saidas
    public static List<KeyValuePair<uint, string>> Sessoes()
    {
        var r = new List<KeyValuePair<uint, string>>();
        var l = Listar(0, true, true, null, false);
        foreach (var d in l)
            foreach (var g in d.Apps)
                if (!g.Sistema) foreach (var p in g.Pids) r.Add(new KeyValuePair<uint, string>(p, g.Exe));
        Liberar(l);
        return r;
    }

    public static void DefinirVolume(string id, float? vol, bool? mudo)
    {
        IMMDevice d;
        try { if (En.GetDevice(id, out d) != 0 || d == null) return; }
        catch { return; }
        try
        {
            object o;
            if (d.Activate(ref IID_EP, 23, IntPtr.Zero, out o) != 0 || o == null) return;
            var ep = (IAudioEndpointVolume)o;
            if (vol.HasValue) ep.SetMasterVolumeLevelScalar(Math.Max(0f, Math.Min(1f, vol.Value)), ref Ctx);
            if (mudo.HasValue) ep.SetMute(mudo.Value, ref Ctx);
            Soltar(ep);
        }
        catch { }
        finally { Soltar(d); }
    }

    public static bool DefinirPadrao(string id)
    {
        try
        {
            var pc = (IPolicyConfig)new PolicyConfigClient();
            bool ok = true;
            for (int role = 0; role < 3; role++) ok &= pc.SetDefaultEndpoint(id, role) == 0;
            Soltar(pc);
            return ok;
        }
        catch { return false; }
    }

    // Volume do dispositivo padrao (para o icone e para a roda do mouse)
    public static bool LerPadrao(int fluxo, out float vol, out bool mudo, out string id)
    {
        vol = 0; mudo = false; id = null;
        IMMDevice d;
        try { if (En.GetDefaultAudioEndpoint(fluxo, 0, out d) != 0 || d == null) return false; }
        catch { return false; }
        try
        {
            d.GetId(out id);
            object o;
            if (d.Activate(ref IID_EP, 23, IntPtr.Zero, out o) != 0 || o == null) return false;
            var ep = (IAudioEndpointVolume)o;
            ep.GetMasterVolumeLevelScalar(out vol);
            ep.GetMute(out mudo);
            Soltar(ep);
            return true;
        }
        catch { return false; }
        finally { Soltar(d); }
    }

    public static void MudarPadrao(int fluxo, float? vol, bool? mudo)
    {
        IMMDevice d;
        try { if (En.GetDefaultAudioEndpoint(fluxo, 0, out d) != 0 || d == null) return; }
        catch { return; }
        try
        {
            object o;
            if (d.Activate(ref IID_EP, 23, IntPtr.Zero, out o) != 0 || o == null) return;
            var ep = (IAudioEndpointVolume)o;
            if (vol.HasValue) ep.SetMasterVolumeLevelScalar(Math.Max(0f, Math.Min(1f, vol.Value)), ref Ctx);
            if (mudo.HasValue) ep.SetMute(mudo.Value, ref Ctx);
            Soltar(ep);
        }
        catch { }
        finally { Soltar(d); }
    }

    public static char Glifo(Disp d)
    {
        if (d.Fluxo == 1) return '\uE720';
        switch (d.Forma)
        {
            case 3: return '\uE7F6';  // fone
            case 5: return '\uE95B';  // headset
            case 9: return '\uE7F4';  // monitor/TV (HDMI, DisplayPort)
            default: return '\uE7F5'; // caixas
        }
    }

    public static char GlifoVolume(float v, bool mudo, int fluxo)
    {
        if (fluxo == 1) return mudo ? '\uEC54' : '\uE720';
        if (mudo) return '\uE74F';
        if (v <= 0.001f) return '\uE992';
        if (v < 0.34f) return '\uE993';
        if (v < 0.67f) return '\uE994';
        return '\uE995';
    }
}

// Saida de cada app: interface interna do Windows (a mesma usada pelo EarTrumpet
// e pela pagina "Volume do app e preferencias de dispositivo" das Configuracoes)
static class Politica
{
    [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr cls, ref Guid iid, out IntPtr f);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] static extern int WindowsCreateString(string s, int n, out IntPtr h);
    [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr h);
    [DllImport("combase.dll")] static extern IntPtr WindowsGetStringRawBuffer(IntPtr h, out int n);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FnDefinir(IntPtr self, uint pid, int flow, int role, IntPtr id);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FnObter(IntPtr self, uint pid, int flow, int role, out IntPtr id);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FnLimpar(IntPtr self);

    static IntPtr fab;
    static bool tentou;
    static FnDefinir definir;
    static FnObter obter;
    static FnLimpar limpar;

    public static bool Disponivel { get { return Iniciar(); } }

    static bool Iniciar()
    {
        if (tentou) return fab != IntPtr.Zero;
        tentou = true;
        try
        {
            const string nome = "Windows.Media.Internal.AudioPolicyConfig";
            IntPtr hs;
            if (WindowsCreateString(nome, nome.Length, out hs) != 0) return false;
            try
            {
                foreach (var iid in new[] { "ab3d4648-e242-459f-b02f-541c70306324", "2a59116d-6c4f-45e0-a74f-707e3fef9258" })
                {
                    Guid g = new Guid(iid);
                    IntPtr f;
                    if (RoGetActivationFactory(hs, ref g, out f) == 0 && f != IntPtr.Zero) { fab = f; break; }
                }
            }
            finally { WindowsDeleteString(hs); }
            if (fab == IntPtr.Zero) return false;
            // IUnknown(3) + IInspectable(3) + 19 metodos -> SetPersisted=25, GetPersisted=26, ClearAll=27
            IntPtr vt = Marshal.ReadIntPtr(fab);
            definir = (FnDefinir)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, 25 * IntPtr.Size), typeof(FnDefinir));
            obter = (FnObter)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, 26 * IntPtr.Size), typeof(FnObter));
            limpar = (FnLimpar)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, 27 * IntPtr.Size), typeof(FnLimpar));
            return true;
        }
        catch { fab = IntPtr.Zero; return false; }
    }

    public static string Curto(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        int i = s.IndexOf("MMDEVAPI#", StringComparison.OrdinalIgnoreCase);
        if (i >= 0) s = s.Substring(i + 9);
        int j = s.LastIndexOf('#');
        if (j > 0) s = s.Substring(0, j);
        return s;
    }

    public static bool Igual(string a, string b)
    {
        return string.Equals(Curto(a), Curto(b), StringComparison.OrdinalIgnoreCase);
    }

    // null = o app segue o padrao do Windows
    public static string Obter(uint pid)
    {
        if (!Iniciar()) return null;
        try
        {
            IntPtr hs;
            if (obter(fab, pid, 0, 1, out hs) != 0 || hs == IntPtr.Zero) return null;
            int n;
            IntPtr p = WindowsGetStringRawBuffer(hs, out n);
            string s = n > 0 ? Marshal.PtrToStringUni(p, n) : null;
            WindowsDeleteString(hs);
            return Curto(s);
        }
        catch { return null; }
    }

    public static bool Definir(uint pid, string id)
    {
        if (!Iniciar()) return false;
        IntPtr hs = IntPtr.Zero;
        try
        {
            if (id != null)
            {
                string longo = @"\\?\SWD#MMDEVAPI#" + Curto(id) + "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
                if (WindowsCreateString(longo, longo.Length, out hs) != 0) return false;
            }
            int a = definir(fab, pid, 0, 0, hs);  // console
            int b = definir(fab, pid, 0, 1, hs);  // multimidia
            return a == 0 && b == 0;
        }
        catch { return false; }
        finally { if (hs != IntPtr.Zero) WindowsDeleteString(hs); }
    }

    public static bool LimparTudo()
    {
        if (!Iniciar()) return false;
        try { return limpar(fab) == 0; } catch { return false; }
    }
}

// Nome, caminho e icone de cada processo (com cache)
static class Processos
{
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint acesso, bool herda, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool QueryFullProcessImageName(IntPtr h, int f, System.Text.StringBuilder s, ref int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint PrivateExtractIcons(string arq, int idx, int cx, int cy, IntPtr[] ic, uint[] ids, uint n, uint f);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

    static readonly Dictionary<string, string> nomes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, Bitmap> icones = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

    public static string Caminho(uint pid)
    {
        if (pid == 0) return null;
        IntPtr h = OpenProcess(0x1000 /*QUERY_LIMITED_INFORMATION*/, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            int n = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    public static string Exe(uint pid)
    {
        string c = Caminho(pid);
        return c == null ? null : Path.GetFileName(c).ToLowerInvariant();
    }

    public static string NomeArquivo(string caminho)
    {
        string n = Path.GetFileNameWithoutExtension(caminho);
        return n.Length > 0 ? char.ToUpper(n[0]) + n.Substring(1) : n;
    }

    // Nome "bonito" (descricao do arquivo); null se nao tiver
    public static string Nome(string caminho)
    {
        string n;
        if (nomes.TryGetValue(caminho, out n)) return n;
        n = null;
        try
        {
            var fv = FileVersionInfo.GetVersionInfo(caminho);
            n = (fv.FileDescription ?? "").Trim();
            if (n.Length == 0 || n.Length > 40) n = (fv.ProductName ?? "").Trim();
            if (n.Length == 0 || n.Length > 40) n = null;
        }
        catch { }
        nomes[caminho] = n;
        return n;
    }

    public static Bitmap Icone(string caminho, int tam)
    {
        string k = caminho + "|" + tam;
        Bitmap b;
        if (icones.TryGetValue(k, out b)) return b;
        b = null;
        try
        {
            var hs = new IntPtr[1]; var ids = new uint[1];
            if (PrivateExtractIcons(caminho, 0, tam, tam, hs, ids, 1, 0) == 1 && hs[0] != IntPtr.Zero)
            {
                using (var ic = Icon.FromHandle(hs[0])) b = ic.ToBitmap();
                DestroyIcon(hs[0]);
            }
            else
            {
                using (var ic = Icon.ExtractAssociatedIcon(caminho)) if (ic != null) b = ic.ToBitmap();
            }
        }
        catch { }
        icones[k] = b;
        return b;
    }

    public static void LimparIcones()
    {
        foreach (var b in icones.Values) if (b != null) b.Dispose();
        icones.Clear();
    }
}

// =====================================================================
//  Vigia: thread propria (MTA) que so recebe avisos do Windows.
//  Fica dormindo; nao gasta CPU enquanto nada acontece.
// =====================================================================
[ComVisible(true)]
public class Vigia : IMMNotificationClient, IAudioSessionNotification, IAudioEndpointVolumeCallback
{
    readonly Action<int> evento;    // 1 = dispositivos mudaram, 2 = padrao mudou, 3 = volume mudou
    readonly Action<uint> sessao;   // app comecou a usar o som (pid)
    readonly AutoResetEvent sinal = new AutoResetEvent(false);
    volatile bool reconstruir = true, sair;
    IMMDeviceEnumerator en;
    readonly List<IAudioSessionManager2> gerentes = new List<IAudioSessionManager2>();
    IAudioEndpointVolume epPadrao;

    public Vigia(Action<int> ev, Action<uint> se)
    {
        evento = ev; sessao = se;
        var t = new Thread(Rodar) { IsBackground = true, Name = "Sintonia.Vigia", Priority = ThreadPriority.BelowNormal };
        t.SetApartmentState(ApartmentState.MTA);
        t.Start();
    }

    void Rodar()
    {
        try
        {
            en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
            en.RegisterEndpointNotificationCallback(this);
        }
        catch { return; }
        while (!sair)
        {
            if (reconstruir) { reconstruir = false; try { Reconstruir(); } catch { } }
            sinal.WaitOne();
        }
        Limpar();
        try { en.UnregisterEndpointNotificationCallback(this); } catch { }
    }

    void Limpar()
    {
        foreach (var g in gerentes) { try { g.UnregisterSessionNotification(this); } catch { } Audio.Soltar(g); }
        gerentes.Clear();
        if (epPadrao != null) { try { epPadrao.UnregisterControlChangeNotify(this); } catch { } Audio.Soltar(epPadrao); epPadrao = null; }
    }

    void Reconstruir()
    {
        Limpar();
        IMMDeviceCollection col;
        if (en.EnumAudioEndpoints(0, 1, out col) == 0 && col != null)
        {
            int n; col.GetCount(out n);
            for (int i = 0; i < n; i++)
            {
                IMMDevice d;
                if (col.Item(i, out d) != 0 || d == null) continue;
                object o;
                Guid g = Audio.IID_SM2;
                if (d.Activate(ref g, 23, IntPtr.Zero, out o) == 0 && o != null)
                {
                    var sm = (IAudioSessionManager2)o;
                    if (sm.RegisterSessionNotification(this) == 0)
                    {
                        IAudioSessionEnumerator e; // necessario para comecar a receber os avisos
                        if (sm.GetSessionEnumerator(out e) == 0) Audio.Soltar(e);
                        gerentes.Add(sm);
                    }
                    else Audio.Soltar(sm);
                }
                Audio.Soltar(d);
            }
            Audio.Soltar(col);
        }
        IMMDevice dp;
        if (en.GetDefaultAudioEndpoint(0, 0, out dp) == 0 && dp != null)
        {
            object o;
            Guid g = Audio.IID_EP;
            if (dp.Activate(ref g, 23, IntPtr.Zero, out o) == 0 && o != null)
            {
                epPadrao = (IAudioEndpointVolume)o;
                epPadrao.RegisterControlChangeNotify(this);
            }
            Audio.Soltar(dp);
        }
    }

    public void Parar() { sair = true; sinal.Set(); }

    void Refazer(int tipo) { reconstruir = true; sinal.Set(); evento(tipo); }

    public int OnDeviceStateChanged(string id, int st) { Refazer(1); return 0; }
    public int OnDeviceAdded(string id) { Refazer(1); return 0; }
    public int OnDeviceRemoved(string id) { Refazer(1); return 0; }
    public int OnDefaultDeviceChanged(int flow, int role, string id)
    {
        if (role == 0) { if (flow == 0) Refazer(2); else evento(2); }
        return 0;
    }
    public int OnPropertyValueChanged(string id, PROPERTYKEY k) { return 0; }

    public int OnSessionCreated(object s)
    {
        try
        {
            var c = s as IAudioSessionControl2;
            uint pid;
            if (c != null && c.GetProcessId(out pid) >= 0 && pid != 0) sessao(pid);
        }
        catch { }
        finally { Audio.Soltar(s); }
        return 0;
    }

    public int OnNotify(IntPtr dados) { evento(3); return 0; }
}

// =====================================================================
//  Desenho: icones com a fonte de simbolos do Windows (mesmos do sistema)
// =====================================================================
// "Clicar de novo fecha": se o clique que fechou um menu caiu no mesmo lugar que o abriu,
// nao abre de novo
static class Alterna
{
    static string chave;
    static DateTime quando;
    public static bool PodeAbrir(string k) { return !(k == chave && (DateTime.Now - quando).TotalMilliseconds < 450); }
    public static void Fechou(string k) { chave = k; quando = DateTime.Now; if (abertaChave == k) { aberto = null; abertaChave = null; } }

    // Menu aberto agora: um clique (ou clique duplo) no mesmo lugar fecha em vez de abrir outro
    static ToolStripDropDown aberto;
    static string abertaChave;
    public static void Abriu(string k, ToolStripDropDown m) { aberto = m; abertaChave = k; }
    public static bool FecharSeAberto(string k)
    {
        if (aberto != null && abertaChave == k && aberto.Visible) { aberto.Close(); return true; }
        return false;
    }
}

static class Desenho
{
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    static string fonte;

    public static string FonteIcones
    {
        get
        {
            if (fonte == null)
            {
                fonte = "Segoe MDL2 Assets";
                try { using (var f = new Font("Segoe Fluent Icons", 10f)) if (f.Name == "Segoe Fluent Icons") fonte = f.Name; } catch { }
            }
            return fonte;
        }
    }

    public static GraphicsPath Arred(RectangleF r, float raio)
    {
        var p = new GraphicsPath();
        float d = Math.Min(raio * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static Bitmap Glifo(char c, int n, Color cor, float escala)
    {
        // A fonte de icones so desenha pelo GDI (TextRenderer): desenha branco no preto
        // com suavizacao em tons de cinza e usa o brilho de cada ponto como transparencia.
        var bmp = new Bitmap(n, n, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        using (var f = new Font(FonteIcones, Math.Max(1, (int)Math.Round(n * escala)), GraphicsUnit.Pixel))
        {
            g.Clear(Color.Black);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            TextRenderer.DrawText(g, c.ToString(), f, new Rectangle(0, 0, n, n), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
        var r = new Rectangle(0, 0, n, n);
        var bd = bmp.LockBits(r, System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var px = new int[n * n];
        Marshal.Copy(bd.Scan0, px, 0, px.Length);
        int rgb = cor.ToArgb() & 0xFFFFFF;
        for (int i = 0; i < px.Length; i++)
        {
            int v = px[i];
            int a = Math.Max((v >> 16) & 0xFF, Math.Max((v >> 8) & 0xFF, v & 0xFF));
            px[i] = (a << 24) | rgb;
        }
        Marshal.Copy(px, 0, bd.Scan0, px.Length);
        bmp.UnlockBits(bd);
        return bmp;
    }

    public static Icon IconeDe(Bitmap bmp)
    {
        IntPtr h = bmp.GetHicon();
        var ic = (Icon)Icon.FromHandle(h).Clone();
        DestroyIcon(h);
        return ic;
    }

    // Logo do Sintonia: quadrado arredondado roxo com um botao giratorio de volume
    // (desenho em grade 64x64)
    public static Bitmap Logo(int n)
    {
        var bmp = new Bitmap(n, n, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float u = n / 64f;
            using (var p = Arred(new RectangleF(2 * u, 2 * u, 60 * u, 60 * u), 15 * u))
            using (var b = new LinearGradientBrush(new PointF(0, 0), new PointF(n, n), Color.FromArgb(139, 124, 246), Color.FromArgb(74, 53, 201)))
                g.FillPath(b, p);
            float cx = 32 * u, cy = 33 * u, r = 19 * u;
            var arco = new RectangleF(cx - r, cy - r, r * 2, r * 2);
            using (var pn = new Pen(Color.FromArgb(115, 255, 255, 255), Math.Max(1f, 4.5f * u)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(pn, arco, 142, 256);   // trilho
            using (var pn = new Pen(Color.White, Math.Max(1f, 4.5f * u)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawArc(pn, arco, 142, 146);   // nivel
                pn.Width = Math.Max(1f, 3f * u);
                double ang = (142 + 146) * Math.PI / 180;
                g.DrawLine(pn, cx, cy, cx + (float)Math.Cos(ang) * (r - 3 * u), cy + (float)Math.Sin(ang) * (r - 3 * u));
            }
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, cx - 8 * u, cy - 8 * u, 16 * u, 16 * u);
        }
        return bmp;
    }

    // Icone da bandeja: so o botao giratorio, de uma cor (branco na barra escura,
    // preto na clara). No mudo ganha um risco atravessado.
    public static Bitmap Bandeja(int n, Color cor, bool mudo)
    {
        var bmp = new Bitmap(n, n, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float u = n / 64f;
            float cx = 32 * u, cy = 35 * u, r = 25 * u;
            using (var pn = new Pen(cor, 7.5f * u) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(pn, new RectangleF(cx - r, cy - r, r * 2, r * 2), 144, 252);
            using (var b = new SolidBrush(cor)) g.FillEllipse(b, cx - 11 * u, cy - 11 * u, 22 * u, 22 * u);
            if (mudo)
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                using (var pn = new Pen(Color.Transparent, 15 * u)) g.DrawLine(pn, 6 * u, 6 * u, 58 * u, 58 * u);
                g.CompositingMode = CompositingMode.SourceOver;
                using (var pn = new Pen(cor, 6.5f * u) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(pn, 8 * u, 8 * u, 56 * u, 56 * u);
            }
        }
        return bmp;
    }

    public static void SalvarIco(string caminho)
    {
        int[] tams = { 16, 20, 24, 32, 48, 64, 256 };
        var pngs = new List<byte[]>();
        foreach (var t in tams)
            using (var bmp = Logo(t))
            using (var ms = new MemoryStream()) { bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png); pngs.Add(ms.ToArray()); }
        using (var w = new BinaryWriter(File.Create(caminho)))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)tams.Length);
            int ofs = 6 + 16 * tams.Length;
            for (int i = 0; i < tams.Length; i++)
            {
                w.Write((byte)(tams[i] >= 256 ? 0 : tams[i])); w.Write((byte)(tams[i] >= 256 ? 0 : tams[i]));
                w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                w.Write(pngs[i].Length); w.Write(ofs); ofs += pngs[i].Length;
            }
            foreach (var p in pngs) w.Write(p);
        }
    }
}

// =====================================================================
//  Visual do menu (igual ao OLED Descanso)
// =====================================================================
class TemaMenu : ToolStripProfessionalRenderer
{
    public readonly Color Fundo, Borda, Destaque, Texto, Fraco, Linha;

    public TemaMenu(bool escuro) : base(new Cores(escuro))
    {
        RoundedEdges = false;
        Fundo = escuro ? Color.FromArgb(43, 43, 43) : Color.FromArgb(249, 249, 249);
        Borda = escuro ? Color.FromArgb(69, 69, 69) : Color.FromArgb(213, 213, 213);
        Destaque = escuro ? Color.FromArgb(61, 61, 61) : Color.FromArgb(229, 229, 229);
        Texto = escuro ? Color.FromArgb(242, 242, 242) : Color.FromArgb(27, 27, 27);
        Fraco = escuro ? Color.FromArgb(154, 154, 154) : Color.FromArgb(112, 112, 112);
        Linha = escuro ? Color.FromArgb(71, 71, 71) : Color.FromArgb(220, 220, 220);
    }

    class Cores : ProfessionalColorTable
    {
        readonly Color f, b, d;
        public Cores(bool e)
        {
            UseSystemColors = false;
            f = e ? Color.FromArgb(43, 43, 43) : Color.FromArgb(249, 249, 249);
            b = e ? Color.FromArgb(69, 69, 69) : Color.FromArgb(213, 213, 213);
            d = e ? Color.FromArgb(61, 61, 61) : Color.FromArgb(229, 229, 229);
        }
        public override Color ToolStripDropDownBackground { get { return f; } }
        public override Color ImageMarginGradientBegin { get { return f; } }
        public override Color ImageMarginGradientMiddle { get { return f; } }
        public override Color ImageMarginGradientEnd { get { return f; } }
        public override Color MenuBorder { get { return b; } }
        public override Color MenuItemBorder { get { return d; } }
        public override Color MenuItemSelected { get { return d; } }
        public override Color MenuItemSelectedGradientBegin { get { return d; } }
        public override Color MenuItemSelectedGradientEnd { get { return d; } }
        public override Color MenuItemPressedGradientBegin { get { return d; } }
        public override Color MenuItemPressedGradientEnd { get { return d; } }
        public override Color CheckBackground { get { return f; } }
        public override Color CheckSelectedBackground { get { return d; } }
        public override Color CheckPressedBackground { get { return d; } }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = (!e.Item.Enabled || (e.Item.Tag as string) == "fraco") ? Fraco : Texto;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Texto;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        TextRenderer.DrawText(e.Graphics, "✓", e.Item.Font, e.ImageRectangle, Texto,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using (var p = new Pen(Linha)) e.Graphics.DrawLine(p, 6, y, e.Item.Width - 6, y);
    }
}

// Base das janelinhas (atalho, nome): paleta do Windows 11 e escala do monitor
class JanelaBase : Form
{
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr hmon, int t, out uint x, out uint y);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point p, uint f);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

    protected readonly float esc;
    protected readonly bool escuro;
    protected readonly Color cFundo, cCartao, cBorda, cTexto, cFraco, cTecla, cTeclaBorda, cAcento, cTextoAcento, cOk, cAviso, cErro;
    protected readonly Font fTitulo, fNormal, fPequena, fTecla;

    protected int E(float v) { return (int)Math.Round(v * esc); }

    public JanelaBase(bool temaEscuro)
    {
        escuro = temaEscuro;
        esc = 1f;
        try { uint dx, dy; if (GetDpiForMonitor(MonitorFromPoint(Cursor.Position, 2), 0, out dx, out dy) == 0) esc = dx / 96f; } catch { }
        cFundo = escuro ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);
        cCartao = escuro ? Color.FromArgb(43, 43, 43) : Color.FromArgb(251, 251, 251);
        cBorda = escuro ? Color.FromArgb(58, 58, 58) : Color.FromArgb(229, 229, 229);
        cTexto = escuro ? Color.FromArgb(242, 242, 242) : Color.FromArgb(27, 27, 27);
        cFraco = escuro ? Color.FromArgb(160, 160, 160) : Color.FromArgb(96, 96, 96);
        cTecla = escuro ? Color.FromArgb(58, 58, 58) : Color.White;
        cTeclaBorda = escuro ? Color.FromArgb(85, 85, 85) : Color.FromArgb(200, 200, 200);
        cAcento = App.CorDeDestaque(escuro);
        cTextoAcento = App.TextoSobre(cAcento);
        cOk = escuro ? Color.FromArgb(108, 203, 95) : Color.FromArgb(15, 123, 15);
        cAviso = escuro ? Color.FromArgb(252, 225, 0) : Color.FromArgb(157, 93, 0);
        cErro = escuro ? Color.FromArgb(255, 153, 164) : Color.FromArgb(196, 43, 28);
        fTitulo = new Font("Segoe UI Semibold", 14f * esc);
        fNormal = new Font("Segoe UI", 10f * esc);
        fPequena = new Font("Segoe UI", 9f * esc);
        fTecla = new Font("Segoe UI Semibold", 13f * esc);

        Text = "Sintonia";
        using (var b = Desenho.Logo(32)) Icon = Desenho.IconeDe(b);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = true;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = cFundo;
        ForeColor = cTexto;
        Font = fNormal;
        KeyPreview = true;
    }

    protected Button Botao(string texto, bool principal)
    {
        var b = new Button
        {
            Text = texto, Font = fNormal, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            Size = new Size(Math.Max(E(96), TextRenderer.MeasureText(texto, fNormal).Width + E(28)), E(34)),
            BackColor = principal ? cAcento : cTecla, ForeColor = principal ? cTextoAcento : cTexto,
            TabStop = false, UseVisualStyleBackColor = false
        };
        b.FlatAppearance.BorderColor = principal ? cAcento : cTeclaBorda;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = principal ? ControlPaint.Light(cAcento, 0.15f) : (escuro ? Color.FromArgb(68, 68, 68) : Color.FromArgb(240, 240, 240));
        b.FlatAppearance.MouseDownBackColor = principal ? ControlPaint.Dark(cAcento, 0.05f) : (escuro ? Color.FromArgb(52, 52, 52) : Color.FromArgb(230, 230, 230));
        return b;
    }

    protected Panel Rodape()
    {
        var rodape = new Panel { Dock = DockStyle.Bottom, Height = E(64), BackColor = cCartao };
        rodape.Paint += (s, e) => { using (var p = new Pen(cBorda)) e.Graphics.DrawLine(p, 0, 0, rodape.Width, 0); };
        Controls.Add(rodape);
        return rodape;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int sim = escuro ? 1 : 0, round = 2;
        try { DwmSetWindowAttribute(Handle, 20 /*DWMWA_USE_IMMERSIVE_DARK_MODE*/, ref sim, 4); } catch { }
        try { DwmSetWindowAttribute(Handle, 33 /*DWMWA_WINDOW_CORNER_PREFERENCE*/, ref round, 4); } catch { }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var a = Screen.FromPoint(Cursor.Position).WorkingArea; // abre no monitor onde esta o mouse
        Location = new Point(a.X + (a.Width - Width) / 2, a.Y + (a.Height - Height) / 2);
        Activate();
    }
}

// Janelinha para digitar um nome (renomear dispositivo, criar perfil)
class JanelaTexto : JanelaBase
{
    readonly TextBox caixa;
    public string Valor { get { return caixa.Text.Trim(); } }

    public JanelaTexto(string titulo, string dica, string valor, bool temaEscuro) : base(temaEscuro)
    {
        ClientSize = new Size(E(420), E(210));
        int x = E(24), w = ClientSize.Width - E(48), y = E(20);
        Controls.Add(new Label { Text = titulo, Font = fTitulo, AutoSize = true, Location = new Point(x, y), ForeColor = cTexto });
        y += E(38);
        Controls.Add(new Label { Text = dica, Font = fPequena, AutoSize = false, Size = new Size(w, E(22)), Location = new Point(x, y), ForeColor = cFraco });
        y += E(26);
        caixa = new TextBox
        {
            Text = valor ?? "", Font = fNormal, Location = new Point(x, y), Width = w,
            BackColor = cTecla, ForeColor = cTexto, BorderStyle = BorderStyle.FixedSingle, MaxLength = 40
        };
        Controls.Add(caixa);

        var rodape = Rodape();
        var bSalvar = Botao("Salvar", true);
        var bCancelar = Botao("Cancelar", false);
        int by = (rodape.Height - E(34)) / 2;
        bSalvar.Location = new Point(rodape.Width - E(24) - bSalvar.Width, by);
        bCancelar.Location = new Point(bSalvar.Left - E(8) - bCancelar.Width, by);
        rodape.Controls.AddRange(new Control[] { bSalvar, bCancelar });
        bSalvar.Click += (s, e) => { if (Valor.Length > 0) DialogResult = DialogResult.OK; };
        bCancelar.Click += (s, e) => DialogResult = DialogResult.Cancel;
        AcceptButton = bSalvar;
        CancelButton = bCancelar;
        Shown += (s, e) => { caixa.Focus(); caixa.SelectAll(); };
    }
}

// Janelinha para escolher um atalho: so le as teclas enquanto ela esta aberta e com foco
class JanelaAtalho : JanelaBase
{
    [DllImport("user32.dll")] static extern short GetKeyState(int k);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

    public uint Mod, Vk;
    uint modAoVivo;
    bool valido, livre;
    readonly Painel teclas;
    readonly Label lblStatus;
    readonly Button bSalvar;

    public JanelaAtalho(string titulo, uint mod, uint vk, bool temaEscuro) : base(temaEscuro)
    {
        Mod = mod; Vk = vk;
        ClientSize = new Size(E(460), E(330));
        int x = E(24), w = ClientSize.Width - E(48), y = E(20);

        Controls.Add(new Label { Text = titulo, Font = fTitulo, AutoSize = true, Location = new Point(x, y), ForeColor = cTexto });
        y += E(36);
        Controls.Add(new Label { Text = "Pressione a combinação de teclas que você quer usar.", Font = fNormal, AutoSize = true, Location = new Point(x, y), ForeColor = cFraco });
        y += E(34);

        teclas = new Painel(this) { Location = new Point(x, y), Size = new Size(w, E(96)) };
        Controls.Add(teclas);
        y += E(96) + E(12);

        lblStatus = new Label { Font = fNormal, AutoSize = false, Location = new Point(x, y), Size = new Size(w, E(24)), TextAlign = ContentAlignment.MiddleLeft };
        Controls.Add(lblStatus);
        y += E(26);
        Controls.Add(new Label
        {
            Font = fPequena, AutoSize = false, Location = new Point(x, y), Size = new Size(w, E(40)), ForeColor = cFraco,
            Text = "Use Ctrl, Alt ou Win com outra tecla. Sozinhas: F1–F24, Insert, Pause e Scroll Lock (deixam de funcionar nos outros programas)."
        });

        var rodape = Rodape();
        bSalvar = Botao("Salvar", true);
        var bCancelar = Botao("Cancelar", false);
        var bRemover = Botao("Remover atalho", false);
        bRemover.FlatAppearance.BorderSize = 0;
        bRemover.BackColor = cCartao;
        bRemover.ForeColor = cFraco;
        int by = (rodape.Height - E(34)) / 2;
        bSalvar.Location = new Point(rodape.Width - E(24) - bSalvar.Width, by);
        bCancelar.Location = new Point(bSalvar.Left - E(8) - bCancelar.Width, by);
        bRemover.Location = new Point(E(16), by);
        rodape.Controls.AddRange(new Control[] { bSalvar, bCancelar, bRemover });
        bSalvar.Click += (s, e) => { if (valido && livre) DialogResult = DialogResult.OK; };
        bCancelar.Click += (s, e) => DialogResult = DialogResult.Cancel;
        bRemover.Click += (s, e) => { Mod = 0; Vk = 0; DialogResult = DialogResult.OK; };

        Validar();
    }

    void Validar()
    {
        valido = Vk != 0;
        livre = false;
        if (valido)
        {
            livre = RegisterHotKey(Handle, 77, Mod | 0x4000, Vk);
            if (livre) UnregisterHotKey(Handle, 77);
        }
        if (!valido) { lblStatus.ForeColor = cFraco; lblStatus.Text = "Nenhum atalho definido"; }
        else if (livre) { lblStatus.ForeColor = cOk; lblStatus.Text = "✓  Disponível"; }
        else { lblStatus.ForeColor = cAviso; lblStatus.Text = "⚠  Já usado pelo Windows ou por outro programa"; }
        bSalvar.Enabled = valido && livre;
        bSalvar.BackColor = bSalvar.Enabled ? cAcento : (escuro ? Color.FromArgb(55, 55, 55) : Color.FromArgb(220, 220, 220));
        bSalvar.ForeColor = bSalvar.Enabled ? cTextoAcento : cFraco;
        teclas.Invalidate();
    }

    List<string> TeclasAtuais(out bool parcial)
    {
        parcial = modAoVivo != 0;
        uint mod = parcial ? modAoVivo : Mod;
        var l = new List<string>();
        if (!parcial && Vk == 0) return l;
        if ((mod & 2) != 0) l.Add("Ctrl");
        if ((mod & 1) != 0) l.Add("Alt");
        if ((mod & 4) != 0) l.Add("Shift");
        if ((mod & 8) != 0) l.Add("Win");
        l.Add(parcial ? "…" : App.TextoAtalho(0, Vk));
        return l;
    }

    static uint ModsAgora(Keys keyData)
    {
        uint m = 0;
        if ((keyData & Keys.Control) != 0 || (GetKeyState(0x11) & 0x8000) != 0) m |= 2;
        if ((keyData & Keys.Alt) != 0 || (GetKeyState(0x12) & 0x8000) != 0) m |= 1;
        if ((keyData & Keys.Shift) != 0 || (GetKeyState(0x10) & 0x8000) != 0) m |= 4;
        if ((GetKeyState(0x5B) & 0x8000) != 0 || (GetKeyState(0x5C) & 0x8000) != 0) m |= 8;
        return m;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var k = keyData & Keys.KeyCode;
        uint m = ModsAgora(keyData);
        if (k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu || k == Keys.LWin || k == Keys.RWin)
        {
            modAoVivo = m; teclas.Invalidate();
            return true;
        }
        if (m == 0 && k == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
        if (m == 0 && k == Keys.Enter) { if (valido && livre) DialogResult = DialogResult.OK; return true; }
        bool sozinhaOk = (k >= Keys.F1 && k <= Keys.F24) || k == Keys.Insert || k == Keys.Pause || k == Keys.Scroll;
        if ((m & (1 | 2 | 8)) == 0 && !sozinhaOk)
        {
            modAoVivo = 0;
            lblStatus.ForeColor = cErro;
            lblStatus.Text = "✕  Combine com Ctrl, Alt ou Win";
            teclas.Invalidate();
            return true;
        }
        Mod = m; Vk = (uint)k; modAoVivo = 0;
        Validar();
        return true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        modAoVivo = ModsAgora(Keys.None) & modAoVivo;
        if (modAoVivo == 0) teclas.Invalidate();
        base.OnKeyUp(e);
    }

    class Painel : Control
    {
        readonly JanelaAtalho j;
        public Painel(JanelaAtalho dono)
        {
            j = dono;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(j.cFundo);
            var card = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var p = Desenho.Arred(card, j.E(8)))
            {
                using (var b = new SolidBrush(j.cCartao)) g.FillPath(b, p);
                using (var pen = new Pen(j.modAoVivo != 0 ? j.cAcento : j.cBorda, j.modAoVivo != 0 ? 2f : 1f)) g.DrawPath(pen, p);
            }
            bool parcial;
            var lista = j.TeclasAtuais(out parcial);
            if (lista.Count == 0)
            {
                TextRenderer.DrawText(g, "Pressione as teclas…", j.fNormal, ClientRectangle, j.cFraco,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            int padX = j.E(16), alt = j.E(44), gap = j.E(10);
            var larg = new List<int>();
            int plus = TextRenderer.MeasureText("+", j.fTecla).Width;
            int total = 0;
            foreach (var t in lista)
            {
                int lw = Math.Max(alt, TextRenderer.MeasureText(t, j.fTecla, Size.Empty, TextFormatFlags.NoPadding).Width + padX * 2);
                larg.Add(lw); total += lw;
            }
            total += (lista.Count - 1) * (plus + gap * 2);
            float x = (Width - total) / 2f, y = (Height - alt) / 2f;
            for (int i = 0; i < lista.Count; i++)
            {
                if (i > 0)
                {
                    var rp = new Rectangle((int)x, (int)y, plus + gap * 2, alt);
                    TextRenderer.DrawText(g, "+", j.fTecla, rp, j.cFraco, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    x += plus + gap * 2;
                }
                bool ret = parcial && i == lista.Count - 1;
                var r = new RectangleF(x, y, larg[i], alt);
                using (var ps = Desenho.Arred(new RectangleF(r.X, r.Y + j.E(3), r.Width, r.Height), j.E(7)))
                using (var bs = new SolidBrush(j.cTeclaBorda)) if (!ret) g.FillPath(bs, ps);
                using (var pk = Desenho.Arred(r, j.E(7)))
                {
                    using (var bk = new SolidBrush(ret ? j.cCartao : j.cTecla)) g.FillPath(bk, pk);
                    using (var pen = new Pen(j.cTeclaBorda) { DashStyle = ret ? DashStyle.Dash : DashStyle.Solid }) g.DrawPath(pen, pk);
                }
                TextRenderer.DrawText(g, lista[i], j.fTecla, Rectangle.Round(r), ret ? j.cFraco : j.cTexto,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                x += larg[i];
            }
        }
    }
}

// =====================================================================
//  Aviso na tela (ao trocar saida, perfil, mudo). Nao rouba o foco.
// =====================================================================
class Aviso : Form
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint f);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int c);

    readonly Timer t = new Timer { Interval = 1600 };
    readonly Timer tFade = new Timer { Interval = 15 };
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint cor, byte alfa, uint f);
    float alfa = 1f, alfaAlvo = 1f;
    public bool Suave = true;
    char glifo; string texto;
    Color cFundo, cTexto, cIcone;
    Font fTexto, fGlifo;
    float esc = 1f;

    public Aviso()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        t.Tick += (s, e) =>
        {
            t.Stop();
            if (Suave) { alfaAlvo = 0f; tFade.Start(); } else Hide();
        };
        tFade.Tick += (s, e) =>
        {
            alfa += (alfaAlvo - alfa) * 0.3f;
            if (Math.Abs(alfaAlvo - alfa) < 0.03f) alfa = alfaAlvo;
            SetLayeredWindowAttributes(Handle, 0, (byte)(alfa * 255), 2);
            if (alfa == alfaAlvo) { tFade.Stop(); if (alfa == 0f) Hide(); }
        };
    }

    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 /*NOACTIVATE*/ | 0x80 /*TOOLWINDOW*/ | 0x8 /*TOPMOST*/ | 0x80000 /*LAYERED*/;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x02E0 /*WM_DPICHANGED*/) { m.Result = IntPtr.Zero; return; }
        if (m.Msg == 0x0021 /*WM_MOUSEACTIVATE*/) { m.Result = new IntPtr(3) /*NOACTIVATE*/; return; }
        base.WndProc(ref m);
    }

    int E(float v) { return (int)Math.Round(v * esc); }

    public void Mostrar(char gl, string txt, bool escuro, Rectangle area, float escala)
    {
        glifo = gl; texto = txt; esc = escala;
        cFundo = escuro ? Color.FromArgb(44, 44, 44) : Color.FromArgb(249, 249, 249);
        cTexto = escuro ? Color.FromArgb(242, 242, 242) : Color.FromArgb(27, 27, 27);
        cIcone = App.CorDeDestaque(escuro);
        if (fTexto != null) { fTexto.Dispose(); fGlifo.Dispose(); }
        fTexto = new Font("Segoe UI Semibold", E(15), GraphicsUnit.Pixel);
        fGlifo = new Font(Desenho.FonteIcones, E(20), GraphicsUnit.Pixel);
        int w = Math.Min(area.Width - E(40), TextRenderer.MeasureText(texto, fTexto).Width + E(76));
        int h = E(52);
        int x = area.X + (area.Width - w) / 2, y = area.Bottom - h - E(64);
        var ignora = Handle;
        int sim = escuro ? 1 : 0, round = 2;
        try { DwmSetWindowAttribute(Handle, 20, ref sim, 4); } catch { }
        try { DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { }
        alfa = Suave && !Visible ? 0f : 1f; alfaAlvo = 1f;
        SetLayeredWindowAttributes(Handle, 0, (byte)(alfa * 255), 2);
        if (Suave) tFade.Start(); else tFade.Stop();
        SetWindowPos(Handle, new IntPtr(-1), x, y, w, h, 0x0010 /*NOACTIVATE*/);
        ShowWindow(Handle, 4 /*SW_SHOWNOACTIVATE*/);
        SetWindowPos(Handle, new IntPtr(-1), x, y, w, h, 0x0010);
        Invalidate();
        t.Stop(); t.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(cFundo);
        if (fTexto == null) return;
        TextRenderer.DrawText(g, glifo.ToString(), fGlifo, new Rectangle(E(14), 0, E(32), Height), cIcone,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, texto, fTexto, new Rectangle(E(54), 0, Width - E(66), Height), cTexto,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }
}

// =====================================================================
//  Painel (abre ao clicar no icone): saidas, apps e microfones
// =====================================================================
class Painel : Form
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint f);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);

    readonly App app;
    List<Disp> saidas = new List<Disp>(), entradas = new List<Disp>();
    float esc = 1f;
    bool escuro;
    Color cFundo, cCartao, cHover, cBorda, cTexto, cFraco, cAcento, cTextoAcento, cTrilho, cPolegar, cPolegarBorda;
    Font fTitulo, fNormal, fNegrito, fPequena, fGlifo, fGlifoP;
    float escFontes = -1;
    int rolagem, alturaConteudo, largura;
    Rectangle area, telaB;
    bool cimaBarra; // barra de tarefas em cima: painel cresce para baixo
    int bordaBarra;  // lado da barra de tarefas: 0 embaixo, 1 em cima, 2 esquerda, 3 direita
    readonly List<Zona> zonas = new List<Zona>();
    Zona sobre, apertada, arrastando;
    readonly Timer tRecarregar = new Timer { Interval = 1500 };
    public DateTime Escondido = DateTime.MinValue;

    // Animacoes: o timer so roda durante uma transicao e para sozinho depois
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint cor, byte alfa, uint f);
    readonly Timer tAnim = new Timer { Interval = 15 };
    float opac = 1f, opacAlvo = 1f, desl;
    bool fechando;
    readonly Dictionary<string, float> abertura = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

    // Arrastar um app para outra saida
    Zona arrastoApp;            // app sendo arrastado (Tipo Z_NOME_A)
    Point arrastoInicio;
    Disp arrastoAlvo;
    GrupoApp menuApp;
    readonly List<KeyValuePair<Rectangle, Disp>> cartoes = new List<KeyValuePair<Rectangle, Disp>>();
    readonly List<KeyValuePair<Rectangle, Perfil>> chips = new List<KeyValuePair<Rectangle, Perfil>>();

    class Zona { public Rectangle R; public int Tipo; public Disp D; public GrupoApp G; public Perfil P; public int F; public string Id; }
    const int Z_RECOLHER = 10, Z_NOME_A = 11, Z_ESCONDIDOS = 12, Z_MOSTRAR = 13, Z_USAR = 14;
    // itens escondidos de cada secao (0 = saidas, 1 = microfones)
    class Esc { public string Id, Nome; public char Glifo; }
    readonly List<Esc>[] escondidos = { new List<Esc>(), new List<Esc>() };
    // 0 = lista normal, 1 = lista dos escondidos (animado entre os dois)
    readonly float[] vista = { 0f, 0f };
    readonly bool[] vistaAlvo = { false, false };
    const int Z_SLIDER_D = 1, Z_MUDO_D = 2, Z_PADRAO = 3, Z_SLIDER_A = 4, Z_MUDO_A = 5, Z_SAIDA_A = 6,
              Z_PERFIL = 7, Z_MENU = 8, Z_NOVO_PERFIL = 9;

    int E(float v) { return (int)Math.Round(v * esc); }

    public Painel(App a)
    {
        app = a;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        TopMost = true;
        KeyPreview = true;
        Text = "Sintonia";
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        tRecarregar.Tick += (s, e) =>
        {
            if (!Visible || arrastando != null || arrastoApp != null || app.MenuAberto || fechando) return;
            // leve: a cada 1,5 s so confere os volumes que ja estao na tela; a lista inteira so a cada 6 s
            // (apps novos e saidas ligadas/desligadas ja chegam na hora pelos avisos do Windows)
            if (++ticks % 4 == 0) Recarregar(); else AtualizarValores();
        };
        tAnim.Tick += (s, e) => PassoAnimacao();
        tSom.Tick += (s, e) => PassoSom();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x20000; // sombra
            cp.ExStyle |= 0x80 | 0x80000; // nao aparece no Alt+Tab; camada (para aparecer/sumir suave)
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetLayeredWindowAttributes(Handle, 0, 255, 2 /*LWA_ALPHA*/);
    }

    void Alfa(float a) { SetLayeredWindowAttributes(Handle, 0, (byte)Math.Round(Math.Max(0, Math.Min(1, a)) * 255), 2); }

    float Abertura(Disp d)
    {
        float f;
        if (!abertura.TryGetValue(d.Id, out f)) { f = app.Aberto(d) ? 1f : 0f; abertura[d.Id] = f; }
        return f;
    }

    void Animar() { if (!tAnim.Enabled) tAnim.Start(); }

    // Um passo de animacao (~60 por segundo, so enquanto algo esta mudando)
    void PassoAnimacao()
    {
        bool mexeu = false, layout = false;
        float alvoO = fechando ? 0f : opacAlvo;
        if (opac != alvoO)
        {
            opac += (alvoO - opac) * (fechando ? 0.4f : 0.3f);
            if (Math.Abs(alvoO - opac) < 0.03f) opac = alvoO;
            Alfa(opac); mexeu = true;
        }
        float alvoD = fechando ? E(10) : 0f;
        if (desl != alvoD)
        {
            desl += (alvoD - desl) * 0.3f;
            if (Math.Abs(alvoD - desl) < 0.5f) desl = alvoD;
            layout = true;
        }
        for (int f = 0; f < 2; f++)
        {
            float alvoV = vistaAlvo[f] ? 1f : 0f;
            if (vista[f] == alvoV) continue;
            vista[f] += (alvoV - vista[f]) * 0.25f;
            if (Math.Abs(alvoV - vista[f]) < 0.02f) vista[f] = alvoV;
            layout = true;
        }
        foreach (var lista in new[] { saidas, entradas })
            foreach (var d in lista)
            {
                float f = Abertura(d), alvo = app.Aberto(d) ? 1f : 0f;
                if (f == alvo) continue;
                f += (alvo - f) * 0.3f;
                if (Math.Abs(alvo - f) < 0.02f) f = alvo;
                abertura[d.Id] = f; layout = true;
            }
        if (layout) { Montar(null); Posicionar(); Invalidate(); mexeu = true; }
        if (!mexeu)
        {
            tAnim.Stop();
            if (fechando) EsconderAgora();
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x02E0 /*WM_DPICHANGED*/) { m.Result = IntPtr.Zero; return; } // tamanho e calculado por nos
        base.WndProc(ref m);
    }

    void PrepararVisual()
    {
        escuro = app.TemaEscuro();
        cFundo = escuro ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);
        cCartao = escuro ? Color.FromArgb(43, 43, 43) : Color.FromArgb(251, 251, 251);
        cHover = escuro ? Color.FromArgb(55, 55, 55) : Color.FromArgb(236, 236, 236);
        cBorda = escuro ? Color.FromArgb(58, 58, 58) : Color.FromArgb(229, 229, 229);
        cTexto = escuro ? Color.FromArgb(242, 242, 242) : Color.FromArgb(27, 27, 27);
        cFraco = escuro ? Color.FromArgb(160, 160, 160) : Color.FromArgb(96, 96, 96);
        cAcento = App.CorDeDestaque(escuro);
        cTextoAcento = App.TextoSobre(cAcento);
        cTrilho = escuro ? Color.FromArgb(110, 110, 110) : Color.FromArgb(150, 150, 150);
        cPolegar = escuro ? Color.FromArgb(69, 69, 69) : Color.White;
        cPolegarBorda = escuro ? Color.FromArgb(90, 90, 90) : Color.FromArgb(210, 210, 210);
        if (escFontes != esc)
        {
            if (fTitulo != null) foreach (var f in new[] { fTitulo, fNormal, fNegrito, fPequena, fGlifo, fGlifoP }) f.Dispose();
            fTitulo = new Font("Segoe UI Semibold", E(16), GraphicsUnit.Pixel);
            fNormal = new Font("Segoe UI", E(14), GraphicsUnit.Pixel);
            fNegrito = new Font("Segoe UI Semibold", E(14), GraphicsUnit.Pixel);
            fPequena = new Font("Segoe UI", E(12), GraphicsUnit.Pixel);
            fGlifo = new Font(Desenho.FonteIcones, E(16), GraphicsUnit.Pixel);
            fGlifoP = new Font(Desenho.FonteIcones, E(10), GraphicsUnit.Pixel);
            escFontes = esc;
        }
        BackColor = cFundo;
    }

    public void Alternar()
    {
        if (Visible && !fechando) { Esconder(); return; }
        if ((DateTime.Now - Escondido).TotalMilliseconds < 300) return; // o clique que fechou e o mesmo que abriria
        Mostrar();
    }

    public void Mostrar()
    {
        var pt = Cursor.Position;
        var tela = Screen.FromPoint(pt);
        area = App.AreaUtil(tela, out bordaBarra); telaB = tela.Bounds;
        cimaBarra = bordaBarra == 1;
        esc = App.EscalaEm(pt);
        PrepararVisual();
        largura = E(360);
        rolagem = 0;
        fechando = false;
        abertura.Clear();
        vista[0] = vista[1] = 0f; vistaAlvo[0] = vistaAlvo[1] = false;
        Carregar();
        Montar(null);
        if (app.Animacoes) { opac = 0f; desl = E(18); opacAlvo = 1f; Alfa(0); }
        else { opac = 1f; desl = 0; Alfa(1); }
        int sim = escuro ? 1 : 0, round = 2, borda = ColorTranslator.ToWin32(cBorda);
        try { DwmSetWindowAttribute(Handle, 20, ref sim, 4); } catch { }
        try { DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { }
        try { DwmSetWindowAttribute(Handle, 34 /*BORDER_COLOR*/, ref borda, 4); } catch { }
        Posicionar();
        Show();
        Posicionar(); // de novo, caso o Windows tenha ajustado ao trocar de monitor
        SetForegroundWindow(Handle);
        Activate();
        tRecarregar.Start();
        if (app.Animacoes) Animar();
        if (app.MostrarSom) tSom.Start();
    }

    void Posicionar()
    {
        int h = Math.Min(alturaConteudo, (int)(area.Height * app.AlturaMax / 100.0));
        int x = area.Right - largura - E(12);
        int y = cimaBarra ? area.Top + E(12) - (int)desl : area.Bottom - h - E(12) + (int)desl;
        if (bordaBarra == 2) x = area.Left + E(12);
        int maxRol = Math.Max(0, alturaConteudo - h);
        if (rolagem > maxRol) rolagem = maxRol;
        SetWindowPos(Handle, new IntPtr(-1), x, y, largura, h, 0x0010 /*NOACTIVATE*/);
    }

    public void Esconder()
    {
        if (!Visible) return;
        Escondido = DateTime.Now;
        if (app.Animacoes && !fechando)
        {
            fechando = true; arrastoApp = null; arrastando = null;
            Animar();
            return;
        }
        if (!fechando || !app.Animacoes) EsconderAgora();
    }

    // ---- barrinhas animadas do som que esta tocando (so com o painel aberto e a opcao ligada) ----
    class Med { public Rectangle R; public Color Fundo; public string Chave; public IAudioMeterInformation M; public List<IAudioMeterInformation> Ms; }
    readonly List<Med> meds = new List<Med>();
    readonly Dictionary<string, float[]> historico = new Dictionary<string, float[]>();
    readonly Timer tSom = new Timer { Interval = 40 };   // 25 quadros por segundo
    const int BARRAS = 5;

    int LarguraMed() { return BARRAS * E(3) + (BARRAS - 1) * E(2); }

    // registra (e desenha) as barrinhas de uma linha
    void Medidor(Graphics g, int x, int yCentro, Color fundo, string chave, IAudioMeterInformation m, List<IAudioMeterInformation> ms)
    {
        if (!app.MostrarSom || g == null || (m == null && (ms == null || ms.Count == 0))) return;
        var md = new Med { R = new Rectangle(x, yCentro - E(8), LarguraMed(), E(16)), Fundo = fundo, Chave = chave, M = m, Ms = ms };
        meds.Add(md);
        DesenharMed(g, md);
    }

    void DesenharMed(Graphics g, Med md)
    {
        float[] h;
        if (!historico.TryGetValue(md.Chave, out h)) h = new float[BARRAS];
        using (var b = new SolidBrush(md.Fundo)) g.FillRectangle(b, md.R);
        bool silencio = true;
        foreach (var v in h) if (v > 0.02f) silencio = false;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(silencio ? Color.FromArgb(120, cFraco) : cAcento))
            for (int i = 0; i < BARRAS; i++)
            {
                // escala em decibeis (como medidores de audio): -50 dB = vazio, 0 dB = cheio
                float nivel = h[i] <= 0.0003f ? 0f : Math.Max(0f, Math.Min(1f, 1f + 20f * (float)Math.Log10(h[i]) / 50f));
                float alt = Math.Max(E(2), nivel * md.R.Height);
                var r = new RectangleF(md.R.X + i * (E(3) + E(2)), md.R.Y + (md.R.Height - alt) / 2f, E(3), alt);
                using (var pth = Desenho.Arred(r, E(1.5f))) g.FillPath(b, pth);
            }
        g.SmoothingMode = SmoothingMode.None;
    }

    // um quadro: le o nivel de cada linha e redesenha so as barrinhas
    void PassoSom()
    {
        if (!Visible || fechando || !app.MostrarSom) { tSom.Stop(); return; }
        if (arrastoApp != null || arrastando != null || meds.Count == 0) return;
        Graphics g = null;
        try
        {
            foreach (var md in meds)
            {
                float pico = 0, v;
                try
                {
                    if (md.M != null && md.M.GetPeakValue(out v) == 0) pico = v;
                    if (md.Ms != null) foreach (var m in md.Ms) if (m.GetPeakValue(out v) == 0 && v > pico) pico = v;
                }
                catch { }
                float[] h;
                if (!historico.TryGetValue(md.Chave, out h)) { h = new float[BARRAS]; historico[md.Chave] = h; }
                bool tinha = false; foreach (var x in h) if (x > 0.001f) tinha = true;
                if (!tinha && pico < 0.001f) continue;              // continua em silencio: nao redesenha
                // cada barra mostra um instante (da mais nova a mais antiga), do centro para as pontas
                Array.Copy(h, 0, h, 1, BARRAS - 1);
                h[0] = Math.Max(pico, h[1] * 0.6f);
                if (!ClientRectangle.Contains(md.R)) continue;
                if (g == null) g = CreateGraphics();
                using (var bmp = new Bitmap(md.R.Width, md.R.Height))
                {
                    using (var gb = Graphics.FromImage(bmp))
                    {
                        gb.TranslateTransform(-md.R.X, -md.R.Y);
                        // ordem das barras: centro = agora, lados = instantes anteriores (visual de "onda")
                        var orig = (float[])h.Clone();
                        h[2] = orig[0]; h[1] = orig[1]; h[3] = orig[2]; h[0] = orig[3]; h[4] = orig[4];
                        DesenharMed(gb, md);
                        Array.Copy(orig, h, BARRAS);
                    }
                    g.DrawImageUnscaled(bmp, md.R.Location);
                }
            }
        }
        finally { if (g != null) g.Dispose(); }
    }

    public void SomMudou()
    {
        Audio.ComMedidor = app.MostrarSom;
        if (!Visible) return;
        Recarregar();
        if (app.MostrarSom) tSom.Start(); else { tSom.Stop(); historico.Clear(); Invalidate(); }
    }

    void EsconderAgora()
    {
        if (!Visible) return;
        tSom.Stop();
        historico.Clear();
        fechando = false;
        tAnim.Stop();
        Hide();
        tRecarregar.Stop();
        Escondido = DateTime.Now;
        sobre = apertada = arrastando = null;
        Audio.Liberar(saidas);
        Audio.Liberar(entradas);
        zonas.Clear();
        app.Aparar();
    }

    void Carregar()
    {
        var antS = saidas; var antE = entradas;
        saidas = Audio.Listar(0, true, app.Inativos, app.Nomes);
        escondidos[0].Clear(); escondidos[1].Clear();
        saidas.RemoveAll(d => !d.Padrao && app.Ocultos.Contains(d.Id) && Guardar(d) && Soltar(d));
        entradas = app.Mics ? Audio.Listar(1, false, false, app.Nomes) : new List<Disp>();
        entradas.RemoveAll(d => !d.Padrao && app.Ocultos.Contains(d.Id) && Guardar(d) && Soltar(d));
        var tocando = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in saidas) foreach (var g in d.Apps) if (g.Ativo && !g.Sistema) tocando.Add(g.Exe);
        foreach (var d in saidas)
            d.Apps.RemoveAll(g =>
            {
                if (g.Ativo || g.Sistema || !tocando.Contains(g.Exe)) return false;
                foreach (var r in g.Rcws) Audio.Soltar(r);
                g.Rcws.Clear(); g.Vols.Clear();
                return true;
            });
        app.Ordenar(saidas);
        app.Ordenar(entradas);
        for (int f = 0; f < 2; f++) if (escondidos[f].Count == 0) vistaAlvo[f] = false; // nada mais escondido: volta
        Audio.Liberar(antS);
        Audio.Liberar(antE);
    }

    static bool Soltar(Disp d) { Audio.Liberar(new List<Disp> { d }); return true; }

    int ticks;
    // Relê so volume e mudo do que ja esta na tela (sem procurar dispositivos/apps de novo)
    void AtualizarValores()
    {
        bool mudou = false;
        foreach (var lista in new[] { saidas, entradas })
            foreach (var d in lista)
            {
                try
                {
                    float v; bool m;
                    if (d.Ep != null && d.Ep.GetMasterVolumeLevelScalar(out v) == 0 && d.Ep.GetMute(out m) == 0)
                    {
                        if (Math.Abs(v - d.Vol) > 0.004f || m != d.Mudo) { d.Vol = v; d.Mudo = m; mudou = true; }
                    }
                    foreach (var a in d.Apps)
                        if (a.Vols.Count > 0 && a.Vols[0].GetMasterVolume(out v) == 0 && a.Vols[0].GetMute(out m) == 0)
                            if (Math.Abs(v - a.Vol) > 0.004f || m != a.Mudo) { a.Vol = v; a.Mudo = m; mudou = true; }
                }
                catch { }
            }
        if (mudou) Invalidate();
    }

    bool Guardar(Disp d) { escondidos[d.Fluxo].Add(new Esc { Id = d.Id, Nome = d.Nome, Glifo = Audio.Glifo(d) }); return true; }

    public void Recarregar()
    {
        if (!Visible || arrastando != null || arrastoApp != null || fechando) return;
        Carregar();
        int antes = alturaConteudo;
        Montar(null);
        if (alturaConteudo != antes) Posicionar();
        sobre = null;
        AtualizarSobre();
        Invalidate();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (!app.MenuAberto) app.ConferirPainel();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys k)
    {
        if (k == Keys.Escape)
        {
            if (arrastoApp != null) { arrastoApp = null; arrastoAlvo = null; Capture = false; Cursor = Cursors.Default; Invalidate(); }
            else Esconder();
            return true;
        }
        return base.ProcessCmdKey(ref msg, k);
    }

    // ---------------- desenho + mapa de cliques (mesma rotina) ----------------
    const TextFormatFlags TF = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis |
                               TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding |
                               TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform;
    const TextFormatFlags TC = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                               TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                               TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform;

    Zona Add(Rectangle r, int tipo, Disp d, GrupoApp g, Perfil p)
    {
        var z = new Zona { R = r, Tipo = tipo, D = d, G = g, P = p };
        zonas.Add(z);
        return z;
    }

    bool Em(Zona z) { return z != null && (sobre == z || apertada == z || arrastando == z); }
    bool Igual(Zona a, Zona b)
    {
        return a != null && b != null && a.Tipo == b.Tipo && a.D == b.D && a.G == b.G && a.P == b.P && a.F == b.F && a.Id == b.Id;
    }
    bool Sobre(int tipo, Disp d, GrupoApp g, Perfil p)
    {
        var s = arrastando ?? sobre;
        return s != null && s.Tipo == tipo && s.D == d && s.G == g && s.P == p;
    }

    void Fundo(Graphics g, Rectangle r, Color c, int raio)
    {
        using (var p = Desenho.Arred(r, raio))
        using (var b = new SolidBrush(c)) g.FillPath(b, p);
    }

    void Montar(Graphics g)
    {
        zonas.Clear();
        cartoes.Clear();
        chips.Clear();
        meds.Clear();
        int W = largura, pad = E(12);
        int y = pad - rolagem;

        // Cabecalho
        var rTit = new Rectangle(pad + E(4), y, W - pad * 2 - E(40), E(32));
        var rMenu = new Rectangle(W - pad - E(32), y, E(32), E(32));
        Add(rMenu, Z_MENU, null, null, null);
        if (g != null)
        {
            string sub = app.UsarPerfis && app.PerfilAtivo != null ? "  ·  " + app.PerfilAtivo : "";
            if (app.MostrarLogo)
            {
                int lg = E(22);
                using (var logo = Desenho.Logo(lg)) g.DrawImage(logo, rTit.X, rTit.Y + (rTit.Height - lg) / 2);
                rTit = new Rectangle(rTit.X + lg + E(10), rTit.Y, rTit.Width - lg - E(10), rTit.Height);
            }
            int tw = TextRenderer.MeasureText(g, "Sintonia", fTitulo, Size.Empty, TextFormatFlags.NoPadding).Width;
            TextRenderer.DrawText(g, "Sintonia", fTitulo, rTit, cTexto, TF);
            if (sub.Length > 0)
                TextRenderer.DrawText(g, sub, fNormal, new Rectangle(rTit.X + tw, rTit.Y, rTit.Width - tw, rTit.Height), cFraco, TF);
            if (Sobre(Z_MENU, null, null, null)) Fundo(g, rMenu, cHover, E(4));
            TextRenderer.DrawText(g, "\uE713", fGlifo, rMenu, cTexto, TC);
        }
        y += E(40);
        // divisao sutil entre o titulo e o conteudo
        if (g != null) using (var pn = new Pen(cBorda)) g.DrawLine(pn, pad + E(4), y, W - pad - E(4), y);
        y += E(10);

        // Perfis (botoes)
        if (app.UsarPerfis)
        {
            int x = pad, hC = E(30), gap = E(6);
            var lista = new List<Perfil>(app.Perfis);
            for (int i = 0; i <= lista.Count; i++)
            {
                bool novo = i == lista.Count;
                if (novo && lista.Count >= App.MaxPerfis) break;   // limite de perfis: sem o "+"
                string txt = novo ? (lista.Count == 0 ? "Salvar como perfil" : "") : lista[i].Nome;
                int tw = txt.Length == 0 ? 0 : TextRenderer.MeasureText(txt, fNormal, Size.Empty, TextFormatFlags.NoPadding).Width;
                int cw = novo ? (tw == 0 ? hC : tw + E(46)) : Math.Min(W - pad * 2, tw + E(28));
                if (x + cw > W - pad && x > pad) { x = pad; y += hC + gap; }
                var r = new Rectangle(x, y, cw, hC);
                Perfil p = novo ? null : lista[i];
                Add(r, novo ? Z_NOVO_PERFIL : Z_PERFIL, null, null, p);
                if (g != null)
                {
                    bool ativo = p != null && p.Nome == app.PerfilAtivo;
                    bool hov = Sobre(novo ? Z_NOVO_PERFIL : Z_PERFIL, null, null, p);
                    Color fundo = ativo ? (hov ? ControlPaint.Light(cAcento, 0.1f) : cAcento) : (hov ? cHover : cCartao);
                    using (var pth = Desenho.Arred(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), E(4)))
                    {
                        using (var b = new SolidBrush(fundo)) g.FillPath(b, pth);
                        if (!ativo) using (var pn = new Pen(cBorda)) g.DrawPath(pn, pth);
                    }
                    if (novo)
                    {
                        var rg = tw == 0 ? r : new Rectangle(r.X + E(8), r.Y, E(20), r.Height);
                        TextRenderer.DrawText(g, "\uE710", fGlifoP, rg, cTexto, TC);
                        if (tw > 0) TextRenderer.DrawText(g, txt, fNormal, new Rectangle(r.X + E(32), r.Y, r.Width - E(38), r.Height), cTexto, TF);
                    }
                    else TextRenderer.DrawText(g, txt, fNormal, new Rectangle(r.X + E(14), r.Y, r.Width - E(28), r.Height),
                        ativo ? cTextoAcento : cTexto, TF);
                }
                if (p != null)
                {
                    chips.Add(new KeyValuePair<Rectangle, Perfil>(r, p));
                    if (g != null && arrastoApp != null && arrastoApp.P == p)
                        Fundo(g, r, Color.FromArgb(150, cFundo), E(4));   // o que esta sendo arrastado fica apagadinho
                }
                x += cw + gap;
            }
            y += hC + E(14);
        }

        // Saidas
        y = Secao(g, "Saída", y, pad, W, 0);
        y = Corpo(g, 0, y, pad, W);

        // Microfones
        if (app.Mics)
        {
            y += E(6);
            y = Secao(g, "Entrada", y, pad, W, 1);
            y = Corpo(g, 1, y, pad, W);
        }
        y += pad - E(8);
        alturaConteudo = y + rolagem;
    }

    int Secao(Graphics g, string t, int y, int pad, int W, int fluxo)
    {
        if (g != null) TextRenderer.DrawText(g, t, fNegrito, new Rectangle(pad + E(4), y, W - pad * 2, E(22)), cTexto, TF);
        // botao "olhinho" (so aparece quando ha algo escondido nesta secao)
        if (escondidos[fluxo].Count > 0 || vistaAlvo[fluxo])
        {
            var r = new Rectangle(W - pad - E(30), y - E(3), E(28), E(26));
            zonas.Add(new Zona { R = r, Tipo = Z_ESCONDIDOS, F = fluxo });
            if (g != null)
            {
                bool hov = sobre != null && sobre.Tipo == Z_ESCONDIDOS && sobre.F == fluxo;
                if (hov) Fundo(g, r, cHover, E(4));
                if (vistaAlvo[fluxo]) Fundo(g, r, Color.FromArgb(escuro ? 60 : 40, cAcento), E(4));
                TextRenderer.DrawText(g, (vistaAlvo[fluxo] ? '\uED1A' : '\uE7B3').ToString(), fGlifo, r,
                    vistaAlvo[fluxo] ? cTexto : hov ? cTexto : cFraco, TC);
            }
        }
        return y + E(28);
    }

    // Corpo da secao: lista normal ou lista dos escondidos, deslizando de um para o outro
    int Corpo(Graphics g, int f, int y, int pad, int W)
    {
        float v = vista[f];
        if (v <= 0f) return Normal(g, f, y, pad, W);
        if (v >= 1f) return ListaEscondidos(g, f, y, pad, W);
        int nz = zonas.Count, nc = cartoes.Count;
        int hN = Normal(null, f, y, pad, W) - y;
        int hE = ListaEscondidos(null, f, y, pad, W) - y;
        int h = hN + (int)Math.Round((hE - hN) * v);
        if (g != null)
        {
            var clip = g.Clip;
            g.SetClip(new Rectangle(0, y - E(4), W, h + E(4)), CombineMode.Intersect);
            var st = g.Save(); g.TranslateTransform(-v * W, 0); Normal(g, f, y, pad, W); g.Restore(st);
            st = g.Save(); g.TranslateTransform((1 - v) * W, 0); ListaEscondidos(g, f, y, pad, W); g.Restore(st);
            g.Clip = clip; clip.Dispose();
        }
        zonas.RemoveRange(nz, zonas.Count - nz);       // sem cliques durante a troca
        cartoes.RemoveRange(nc, cartoes.Count - nc);
        return y + h;
    }

    int Normal(Graphics g, int f, int y, int pad, int W)
    {
        var lista = f == 0 ? saidas : entradas;
        if (lista.Count == 0) y = Vazio(g, f == 0 ? "Nenhuma saída de som ativa" : "Nenhum microfone ativo", y, pad, W);
        foreach (var d in lista) y = Cartao(g, d, y, pad, W);
        return y;
    }

    int ListaEscondidos(Graphics g, int f, int y, int pad, int W)
    {
        var lista = escondidos[f];
        int x0 = pad, cw = W - pad * 2;
        string dica = lista.Count == 0 ? "Nada escondido" : lista.Count == 1 ? "1 item escondido" : lista.Count + " itens escondidos";
        if (g != null) TextRenderer.DrawText(g, dica, fPequena, new Rectangle(x0 + E(4), y, cw, E(26)), cFraco, TF);
        if (lista.Count > 1)
        {
            string t = "Mostrar todos";
            int tw = TextRenderer.MeasureText(t, fPequena, Size.Empty, TextFormatFlags.NoPadding).Width;
            var r = new Rectangle(x0 + cw - tw - E(18), y, tw + E(16), E(26));
            zonas.Add(new Zona { R = r, Tipo = Z_MOSTRAR, F = f, Id = null });
            if (g != null)
            {
                if (sobre != null && sobre.Tipo == Z_MOSTRAR && sobre.Id == null && sobre.F == f) Fundo(g, r, cHover, E(4));
                TextRenderer.DrawText(g, t, fPequena, r, cAcento, TC);
            }
        }
        y += E(32);
        foreach (var e in lista)
        {
            var rc = new Rectangle(x0, y, cw, E(48));
            var rb = new Rectangle(x0 + cw - E(96), y + E(9), E(86), E(30));
            zonas.Add(new Zona { R = rb, Tipo = Z_MOSTRAR, F = f, Id = e.Id });
            if (g != null)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = Desenho.Arred(new RectangleF(rc.X + 0.5f, rc.Y + 0.5f, rc.Width - 1, rc.Height - 1), E(8)))
                {
                    using (var b = new SolidBrush(cCartao)) g.FillPath(b, p);
                    using (var pn = new Pen(cBorda) { DashStyle = DashStyle.Dash }) g.DrawPath(pn, p);
                }
                g.SmoothingMode = SmoothingMode.None;
                TextRenderer.DrawText(g, e.Glifo.ToString(), fGlifo, new Rectangle(x0 + E(10), y, E(32), rc.Height), cFraco, TC);
                TextRenderer.DrawText(g, e.Nome, fNormal, new Rectangle(x0 + E(50), y, rb.Left - x0 - E(58), rc.Height), cFraco, TF);
                bool hov = sobre != null && sobre.Tipo == Z_MOSTRAR && sobre.Id == e.Id;
                using (var p = Desenho.Arred(new RectangleF(rb.X + 0.5f, rb.Y + 0.5f, rb.Width - 1, rb.Height - 1), E(4)))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var b = new SolidBrush(hov ? cHover : cFundo)) g.FillPath(b, p);
                    using (var pn = new Pen(cBorda)) g.DrawPath(pn, p);
                    g.SmoothingMode = SmoothingMode.None;
                }
                TextRenderer.DrawText(g, "\uE7B3", fGlifoP, new Rectangle(rb.X + E(8), rb.Y, E(16), rb.Height), cTexto, TC);
                TextRenderer.DrawText(g, "Mostrar", fPequena, new Rectangle(rb.X + E(28), rb.Y, rb.Width - E(32), rb.Height), cTexto, TF);
            }
            y += E(56);
        }
        return y;
    }

    int Vazio(Graphics g, string t, int y, int pad, int W)
    {
        if (g != null) TextRenderer.DrawText(g, t, fNormal, new Rectangle(pad + E(4), y, W - pad * 2, E(24)), cFraco, TF);
        return y + E(30);
    }

    int Cartao(Graphics g, Disp d, int y, int pad, int W)
    {
        int x0 = pad, cw = W - pad * 2;
        int hLinha1 = E(40), hLinha2 = E(36), hApp = E(50);
        bool aberto = app.Aberto(d);
        float f = Abertura(d);
        int hFechado = hLinha1 + E(4);
        int hAberto = hLinha1 + hLinha2 + E(4) + d.Apps.Count * hApp + (d.Apps.Count > 0 ? E(6) : 0);
        int h = hFechado + (int)Math.Round((hAberto - hFechado) * f);
        var rc = new Rectangle(x0, y, cw, h);
        cartoes.Add(new KeyValuePair<Rectangle, Disp>(rc, d));
        bool alvo = arrastoApp != null && arrastoApp.G != null && arrastoAlvo == d && arrastoApp.D != d;
        bool movendo = arrastoApp != null && arrastoApp.G == null && arrastoApp.P == null && arrastoApp.D == d;
        if (g != null)
            using (var p = Desenho.Arred(new RectangleF(rc.X + 0.5f, rc.Y + 0.5f, rc.Width - 1, rc.Height - 1), E(8)))
            {
                using (var b = new SolidBrush(alvo ? cHover : cCartao)) g.FillPath(b, p);
                Color cb = alvo ? cAcento : cBorda;
                using (var pn = new Pen(cb, alvo ? Math.Max(2, E(2)) : 1)) g.DrawPath(pn, p);
            }
        int colIcone = x0 + E(26);   // centro da coluna dos icones
        int xTexto = x0 + E(50);

        // Saida/microfone padrao: barrinha fina na cor de destaque na borda esquerda (como a selecao do menu)
        if (d.Padrao && g != null)
            Fundo(g, new Rectangle(x0, y + E(4) + (hLinha1 - E(4)) / 2 - E(9), Math.Max(2, E(3)), E(18)), cAcento, E(1));

        // Linha 1: nome. Arrastar = mudar a ordem; a setinha abre/fecha.
        // "Usar como padrao" e um botao proprio (aparece ao passar o mouse), para nao trocar sem querer.
        var rCh = new Rectangle(x0 + cw - E(40), y + E(4), E(36), hLinha1 - E(4));
        var r1 = new Rectangle(x0 + E(4), y + E(4), cw - E(48), hLinha1 - E(4));
        Add(r1, Z_PADRAO, d, null, null);
        Add(rCh, Z_RECOLHER, d, null, null);
        string tUsar = d.Fluxo == 0 ? "Usar como padrão" : "Usar como padrão";
        int bw = TextRenderer.MeasureText(tUsar, fPequena, Size.Empty, TextFormatFlags.NoPadding).Width + E(22);
        var rUsar = new Rectangle(rCh.Left - bw - E(4), y + E(9), bw, E(26));
        if (!d.Padrao) Add(rUsar, Z_USAR, d, null, null);
        if (g != null)
        {
            bool hovCab = (Sobre(Z_PADRAO, d, null, null) || Sobre(Z_USAR, d, null, null) || Sobre(Z_RECOLHER, d, null, null)) && arrastoApp == null;
            if (hovCab) Fundo(g, new Rectangle(r1.X, r1.Y, rCh.Right - r1.X, r1.Height), cHover, E(5));
            if (Sobre(Z_RECOLHER, d, null, null)) Fundo(g, rCh, ControlPaint.Light(cHover, 0.1f), E(5));
            TextRenderer.DrawText(g, (aberto ? '' : '').ToString(), fGlifoP, rCh, cFraco, TC);
            TextRenderer.DrawText(g, Audio.Glifo(d).ToString(), fGlifo, new Rectangle(colIcone - E(16), r1.Y, E(32), r1.Height),
                d.Padrao ? cAcento : cTexto, TC);

            // lado direito: selo "Padrao", botao "Usar como padrao" (no hover) ou resumo quando fechado
            int direita = rCh.Left - E(6);
            if (alvo)
            {
                string t = "Soltar aqui";
                int tw = TextRenderer.MeasureText(t, fPequena, Size.Empty, TextFormatFlags.NoPadding).Width;
                TextRenderer.DrawText(g, t, fPequena, new Rectangle(direita - tw, r1.Y, tw + E(2), r1.Height), cAcento, TF);
                direita -= tw + E(8);
            }
            else if (d.Padrao) { } // a padrao e indicada pela barrinha lateral
            else if (hovCab)
            {
                bool hb = Sobre(Z_USAR, d, null, null);
                using (var pth = Desenho.Arred(new RectangleF(rUsar.X + 0.5f, rUsar.Y + 0.5f, rUsar.Width - 1, rUsar.Height - 1), E(4)))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var b = new SolidBrush(hb ? cAcento : cCartao)) g.FillPath(b, pth);
                    using (var pn = new Pen(hb ? cAcento : cBorda)) g.DrawPath(pn, pth);
                    g.SmoothingMode = SmoothingMode.None;
                }
                TextRenderer.DrawText(g, tUsar, fPequena, rUsar, hb ? cTextoAcento : cTexto, TC);
                direita = rUsar.Left - E(8);
            }
            if (!aberto && !hovCab && !alvo)
            {
                string resumo = (d.Apps.Count > 0 ? d.Apps.Count + (d.Apps.Count == 1 ? " app · " : " apps · ") : "") +
                                (d.Mudo ? "mudo" : (int)Math.Round(d.Vol * 100) + "%");
                int tw = TextRenderer.MeasureText(resumo, fPequena, Size.Empty, TextFormatFlags.NoPadding).Width;
                TextRenderer.DrawText(g, resumo, fPequena, new Rectangle(direita - tw, r1.Y, tw + E(2), r1.Height), cFraco, TF);
                direita -= tw + E(8);
            }
            int nomeW = Math.Max(E(20), direita - xTexto);
            int nw = Math.Min(nomeW, TextRenderer.MeasureText(g, d.Nome, fNegrito, Size.Empty, TextFormatFlags.NoPadding).Width);
            TextRenderer.DrawText(g, d.Nome, d.Padrao ? fNegrito : fNormal, new Rectangle(xTexto, r1.Y, nomeW, r1.Height), cTexto, TF);
            int xm = xTexto + nw + E(10);
            if (app.MostrarSom && d.Medidor != null && nomeW - nw > LarguraMed() + E(12))
            {
                Medidor(g, xm, r1.Y + r1.Height / 2, hovCab ? cHover : cCartao, "d|" + d.Id, d.Medidor, null);
                xm += LarguraMed() + E(4);
                nw += LarguraMed() + E(14);
            }
            if (app.MostrarAdaptador && !string.IsNullOrEmpty(d.Adaptador) && nomeW - nw > E(40))
                TextRenderer.DrawText(g, d.Adaptador, fPequena, new Rectangle(xTexto + nw + E(8), r1.Y + E(1), nomeW - nw - E(8), r1.Height), cFraco, TF);
        }

        if (f > 0.001f)
        {
            Region clipAnt = null;
            if (g != null && f < 1f) { clipAnt = g.Clip; g.SetClip(rc, CombineMode.Intersect); }
            try
            {
                // Linha 2: volume da SAIDA (barra mais grossa, numero em destaque)
                int y2 = y + hLinha1;
                var rMudo = new Rectangle(colIcone - E(17), y2 + E(1), E(34), E(34));
                var rSl = new Rectangle(xTexto - E(10), y2, cw - (xTexto - x0) - E(44), hLinha2);
                if (f >= 1f) { Add(rMudo, Z_MUDO_D, d, null, null); Add(rSl, Z_SLIDER_D, d, null, null); }
                if (g != null)
                {
                    if (Sobre(Z_MUDO_D, d, null, null)) Fundo(g, rMudo, cHover, E(5));
                    TextRenderer.DrawText(g, Audio.GlifoVolume(d.Vol, d.Mudo, d.Fluxo).ToString(), fGlifo, rMudo,
                        d.Mudo ? cFraco : cTexto, TC);
                    Slider(g, rSl, d.Vol, d.Mudo, Sobre(Z_SLIDER_D, d, null, null), true);
                    TextRenderer.DrawText(g, ((int)Math.Round(d.Vol * 100)).ToString(), fNegrito,
                        new Rectangle(rSl.Right, y2, E(40), hLinha2), d.Mudo ? cFraco : cTexto, TC);
                }

                // Apps desta saida
                int ya = y2 + hLinha2 + E(4);
                if (d.Apps.Count > 0 && g != null)
                    using (var pn = new Pen(cBorda)) g.DrawLine(pn, x0 + E(12), ya - E(2), x0 + cw - E(12), ya - E(2));
                int nz = zonas.Count;
                foreach (var a in d.Apps)
                {
                    AppLinha(g, d, a, ya, x0, cw, colIcone, xTexto, hApp);
                    ya += hApp;
                }
                if (f < 1f) zonas.RemoveRange(nz, zonas.Count - nz); // durante a animacao nao da para clicar
            }
            finally { if (clipAnt != null) { g.Clip = clipAnt; clipAnt.Dispose(); } }
        }

        // cartao sendo arrastado para mudar a ordem: fica apagadinho no lugar
        if (movendo && g != null)
            using (var p = Desenho.Arred(new RectangleF(rc.X, rc.Y, rc.Width, rc.Height), E(8)))
            using (var b = new SolidBrush(Color.FromArgb(150, cFundo))) g.FillPath(b, p);
        return y + h + E(8);
    }

    void AppLinha(Graphics g, Disp d, GrupoApp a, int y, int x0, int cw, int colIcone, int xTexto, int h)
    {
        bool movel = !a.Sistema && Politica.Disponivel && a.Pids.Count > 0;
        var rLinha = new Rectangle(x0 + E(4), y, cw - E(8), h);
        // A linha inteira pode ser arrastada para outra saida (fica por baixo das outras zonas)
        if (movel) Add(rLinha, Z_NOME_A, d, a, null);
        int ti = E(24);
        var rIc = new Rectangle(colIcone - E(17), y + (h - E(34)) / 2, E(34), E(34));
        Add(rIc, Z_MUDO_A, d, a, null);           // clique = mudo; arrastar = mover
        var rSl = new Rectangle(xTexto - E(10), y + E(24), cw - (xTexto - x0) - E(44), E(26));
        Add(rSl, Z_SLIDER_A, d, a, null);
        var rMais = new Rectangle(x0 + cw - E(40), y + E(2), E(32), E(24));
        if (movel) Add(rMais, Z_SAIDA_A, d, a, null);   // "..." = escolher a saida pela lista
        if (g == null) return;

        bool linhaSobre = sobre != null && sobre.G == a && arrastoApp == null;
        bool arrastado = arrastoApp != null && arrastoApp.G == a;
        if (arrastado) Fundo(g, rLinha, Color.FromArgb(45, cAcento), E(6));
        else if (linhaSobre && movel) Fundo(g, rLinha, cHover, E(6));

        // pontinhos de "pegar" a linha (aparecem ao passar o mouse)
        if (linhaSobre && movel)
            using (var b = new SolidBrush(cFraco))
                for (int i = 0; i < 3; i++)
                    for (int j = 0; j < 2; j++)
                        g.FillRectangle(b, x0 + E(7) + j * E(4), y + h / 2 - E(5) + i * E(4), Math.Max(1, E(2)), Math.Max(1, E(2)));

        if (Sobre(Z_MUDO_A, d, a, null)) Fundo(g, rIc, ControlPaint.Light(cHover, 0.1f), E(5));
        var rImg = new Rectangle(colIcone - ti / 2, rIc.Y + (rIc.Height - ti) / 2, ti, ti);
        Bitmap bmp = a.Caminho != null ? Processos.Icone(a.Caminho, ti) : null;
        if (bmp != null)
        {
            if (a.Mudo)
            {
                using (var ia = new System.Drawing.Imaging.ImageAttributes())
                {
                    var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.35f };
                    ia.SetColorMatrix(cm);
                    g.DrawImage(bmp, rImg, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, ia);
                }
            }
            else g.DrawImage(bmp, rImg);
        }
        else TextRenderer.DrawText(g, (a.Sistema ? '' : '').ToString(), fGlifo, rImg, a.Mudo ? cFraco : cTexto, TC);
        if (a.Mudo)
        {
            var rb = new Rectangle(rImg.Right - E(11), rImg.Bottom - E(11), E(14), E(14));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(cCartao)) g.FillEllipse(b, rb);
            g.SmoothingMode = SmoothingMode.None;
            TextRenderer.DrawText(g, "", fGlifoP, rb, cTexto, TC);
        }

        // nome + alfinete quando o app tem uma saida so dele
        int nomeMax = rMais.Left - xTexto - E(22);
        int nw = Math.Min(nomeMax, TextRenderer.MeasureText(g, a.Nome, fNormal, Size.Empty, TextFormatFlags.NoPadding).Width);
        TextRenderer.DrawText(g, a.Nome, fNormal, new Rectangle(xTexto, y + E(3), nomeMax, E(24)), a.Ativo ? cTexto : cFraco, TF);
        if (a.Saida != null)
            TextRenderer.DrawText(g, "", fGlifoP, new Rectangle(xTexto + nw + E(6), y + E(3), E(14), E(24)), cAcento, TC);
        if (app.MostrarSom && a.Medidores.Count > 0 && !arrastado)
            Medidor(g, xTexto + nw + (a.Saida != null ? E(26) : E(10)), y + E(3) + E(12), linhaSobre && movel ? cHover : cCartao,
                "a|" + d.Id + "|" + a.Exe, null, a.Medidores);

        // botao "..." discreto, so aparece com o mouse na linha
        if (movel && (linhaSobre || (app.MenuAberto && menuApp == a)))
        {
            if (Sobre(Z_SAIDA_A, d, a, null) || menuApp == a) Fundo(g, rMais, ControlPaint.Light(cHover, 0.1f), E(4));
            TextRenderer.DrawText(g, "", fGlifo, rMais, cTexto, TC);
        }

        Slider(g, rSl, a.Vol, a.Mudo, Sobre(Z_SLIDER_A, d, a, null), false);
        TextRenderer.DrawText(g, ((int)Math.Round(a.Vol * 100)).ToString(), fPequena,
            new Rectangle(rSl.Right, rSl.Y, E(40), rSl.Height), a.Mudo ? cFraco : cTexto, TC);
    }

    string NomeSaida(string id)
    {
        if (id == null) return "Padrão";
        foreach (var d in saidas) if (Politica.Igual(d.Id, id)) return d.Nome;
        return "Outra saída";
    }

    void Slider(Graphics g, Rectangle r, float v, bool mudo, bool ativo, bool grosso)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float raio = E(10), x0 = r.Left + raio, x1 = r.Right - raio, cy = r.Top + r.Height / 2f,
              th = grosso ? Math.Max(3, E(5)) : Math.Max(2, E(4));
        float xv = x0 + (x1 - x0) * Math.Max(0, Math.Min(1, v));
        using (var b = new SolidBrush(cTrilho))
        using (var p = Desenho.Arred(new RectangleF(x0, cy - th / 2, x1 - x0, th), th / 2)) g.FillPath(b, p);
        if (xv - x0 >= 1)
            using (var b = new SolidBrush(mudo ? cFraco : cAcento))
            using (var p = Desenho.Arred(new RectangleF(x0, cy - th / 2, xv - x0, th), th / 2)) g.FillPath(b, p);
        using (var b = new SolidBrush(cPolegar)) g.FillEllipse(b, xv - raio, cy - raio, raio * 2, raio * 2);
        using (var p = new Pen(cPolegarBorda)) g.DrawEllipse(p, xv - raio, cy - raio, raio * 2, raio * 2);
        float ri = ativo ? E(7) : E(5);
        using (var b = new SolidBrush(mudo ? cFraco : cAcento)) g.FillEllipse(b, xv - ri, cy - ri, ri * 2, ri * 2);
        g.SmoothingMode = SmoothingMode.None;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(cFundo);
        if (fTitulo == null) return;
        Montar(g);
        DesenharArrasto(g);
        if (alturaConteudo > ClientSize.Height) // indicador de rolagem
        {
            float frac = ClientSize.Height / (float)alturaConteudo;
            float hb = Math.Max(E(24), ClientSize.Height * frac);
            float yb = (ClientSize.Height - hb) * rolagem / (float)(alturaConteudo - ClientSize.Height);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(cTrilho))
            using (var p = Desenho.Arred(new RectangleF(ClientSize.Width - E(5), yb + E(2), E(3), hb - E(4)), E(1.5f))) g.FillPath(b, p);
        }
    }

    // ---------------- mouse ----------------
    Zona ZonaEm(Point p)
    {
        for (int i = zonas.Count - 1; i >= 0; i--) if (zonas[i].R.Contains(p)) return zonas[i];
        return null;
    }

    void AtualizarSobre()
    {
        var z = ZonaEm(PointToClient(Cursor.Position));
        bool mudou = (z == null) != (sobre == null) || (z != null && !Igual(z, sobre));
        sobre = z;
        if (mudou)
        {
            Cursor = z == null ? Cursors.Default : (z.Tipo == Z_NOME_A || z.Tipo == Z_PADRAO) ? Cursors.SizeAll : Cursors.Hand;
            Invalidate();
        }
    }

    // Para onde o item arrastado vai: outra saida (app) ou nova posicao (saida/microfone)
    int arrastoIndice = -1;
    void AtualizarArrasto(Point p)
    {
        arrastoAlvo = null; arrastoIndice = -1;
        if (arrastoApp.P != null)
        {
            // posicao entre os botoes de perfil (na ordem de leitura)
            arrastoIndice = 0;
            foreach (var kv in chips)
            {
                var c = kv.Key;
                bool antes = c.Bottom + E(3) < p.Y || (p.Y >= c.Top - E(3) && p.Y <= c.Bottom + E(3) && c.Left + c.Width / 2 < p.X);
                if (antes) arrastoIndice++;
            }
        }
        else if (arrastoApp.G != null)
        {
            foreach (var kv in cartoes)
                if (kv.Value.Fluxo == 0 && kv.Key.Contains(p)) arrastoAlvo = kv.Value;
        }
        else
        {
            int i = 0;
            foreach (var kv in cartoes)
            {
                if (kv.Value.Fluxo != arrastoApp.D.Fluxo) continue;
                if (p.Y > kv.Key.Top + kv.Key.Height / 2) arrastoIndice = i + 1;
                else if (arrastoIndice < 0) arrastoIndice = i;
                i++;
            }
            if (arrastoIndice < 0) arrastoIndice = i;
        }
        // rola sozinho perto das bordas
        int max = Math.Max(0, alturaConteudo - ClientSize.Height);
        if (max > 0 && (p.Y < E(30) || p.Y > ClientSize.Height - E(30)))
        {
            rolagem = Math.Max(0, Math.Min(max, rolagem + (p.Y < E(30) ? -E(12) : E(12))));
            Montar(null);
        }
        Invalidate();
    }

    void TerminarArrasto()
    {
        var z = arrastoApp;
        arrastoApp = null;
        Capture = false;
        Cursor = Cursors.Default;
        if (z.P != null)
        {
            int de = app.Perfis.IndexOf(z.P), para = arrastoIndice;
            if (de >= 0 && para >= 0)
            {
                if (para > de) para--;
                if (para != de) app.MoverPerfil(z.P, para);
                Montar(null);
                Posicionar();
            }
        }
        else if (z.G != null)
        {
            var alvo = arrastoAlvo;
            if (alvo != null && alvo != z.D)
            {
                app.DefinirSaidaApp(z.G.Exe, z.G.Pids, alvo.Id);
                app.Abertos[alvo.Id] = true; abertura[alvo.Id] = 1f;
                app.SalvarConfig();
                Depois();
            }
        }
        else if (arrastoIndice >= 0)
        {
            var lista = z.D.Fluxo == 0 ? saidas : entradas;
            int de = lista.IndexOf(z.D), para = arrastoIndice;
            if (de >= 0)
            {
                if (para > de) para--;
                lista.RemoveAt(de);
                lista.Insert(Math.Max(0, Math.Min(lista.Count, para)), z.D);
                app.GuardarOrdem(lista);
                Montar(null);
                Posicionar();
            }
        }
        arrastoAlvo = null; arrastoIndice = -1;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (arrastando != null) { DefinirValor(arrastando, e.X); return; }
        if (arrastoApp != null) { AtualizarArrasto(e.Location); return; }
        if (apertada != null && (apertada.Tipo == Z_NOME_A || apertada.Tipo == Z_PADRAO || (apertada.Tipo == Z_PERFIL && apertada.P != null) || (apertada.Tipo == Z_MUDO_A && apertada.G != null && !apertada.G.Sistema)) && e.Button == MouseButtons.Left &&
            Math.Abs(e.X - arrastoInicio.X) + Math.Abs(e.Y - arrastoInicio.Y) > E(6))
        {
            arrastoApp = apertada; apertada = null;
            Capture = true;
            Cursor = Cursors.SizeAll;
            AtualizarArrasto(e.Location);
            return;
        }
        AtualizarSobre();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (sobre != null && arrastando == null && arrastoApp == null) { sobre = null; Invalidate(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        var z = ZonaEm(e.Location);
        if (z == null) return;
        if (z.Tipo == Z_SLIDER_D || z.Tipo == Z_SLIDER_A) { arrastando = z; Capture = true; DefinirValor(z, e.X); return; }
        apertada = z;
        arrastoInicio = e.Location;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (arrastando != null) { arrastando = null; Capture = false; Invalidate(); return; }
        if (arrastoApp != null) { TerminarArrasto(); return; }
        var z = ZonaEm(e.Location);
        var a = apertada; apertada = null;
        if (e.Button == MouseButtons.Right)
        {
            if (z != null && z.G != null && !z.G.Sistema && z.G.Pids.Count > 0 && Politica.Disponivel) MenuSaida(z);
            else if (z != null && z.Tipo == Z_PERFIL && z.P != null) MenuPerfil(z);
            else if (z != null && z.G == null && z.D != null) MenuDisp(z);
            else app.MostrarMenu(this, PointToClient(Cursor.Position));
            return;
        }
        if (z == null || !Igual(z, a)) return;
        Clique(z);
    }

    // Desenha o item que esta sendo arrastado junto com o mouse
    void DesenharArrasto(Graphics g)
    {
        if (arrastoApp == null) return;
        var p = PointToClient(Cursor.Position);
        if (arrastoApp.P != null && arrastoIndice >= 0 && chips.Count > 0)
        {
            // barrinha vertical mostrando onde o perfil vai ficar
            Rectangle c = arrastoIndice < chips.Count ? chips[arrastoIndice].Key : chips[chips.Count - 1].Key;
            int xl = arrastoIndice < chips.Count ? c.Left - E(5) : c.Right + E(2);
            Fundo(g, new Rectangle(xl, c.Top, Math.Max(2, E(3)), c.Height), cAcento, E(2));
        }
        else if (arrastoApp.G == null && arrastoApp.D != null && arrastoIndice >= 0)
        {
            // linha mostrando onde a saida vai ficar
            int i = 0, yl = -1;
            Rectangle ult = Rectangle.Empty;
            foreach (var kv in cartoes)
            {
                if (kv.Value.Fluxo != arrastoApp.D.Fluxo) continue;
                if (i == arrastoIndice) yl = kv.Key.Top - E(5);
                ult = kv.Key; i++;
            }
            if (yl < 0 && ult != Rectangle.Empty) yl = ult.Bottom + E(3);
            if (yl >= 0) Fundo(g, new Rectangle(ult.Left + E(6), yl, ult.Width - E(12), Math.Max(2, E(3))), cAcento, E(2));
        }
        string txt = arrastoApp.G != null ? arrastoApp.G.Nome : arrastoApp.P != null ? arrastoApp.P.Nome : arrastoApp.D.Nome;
        int tw = TextRenderer.MeasureText(txt, fNormal, Size.Empty, TextFormatFlags.NoPadding).Width;
        var r = new Rectangle(p.X + E(10), p.Y - E(16), Math.Min(largura - E(20), tw + E(50)), E(32));
        if (r.Right > ClientSize.Width - E(4)) r.X = ClientSize.Width - E(4) - r.Width;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pth = Desenho.Arred(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), E(6)))
        {
            using (var b = new SolidBrush(Color.FromArgb(235, cHover))) g.FillPath(b, pth);
            using (var pn = new Pen(cAcento, Math.Max(1, E(1.5f)))) g.DrawPath(pn, pth);
        }
        g.SmoothingMode = SmoothingMode.None;
        var ri = new Rectangle(r.X + E(8), r.Y + (r.Height - E(20)) / 2, E(20), E(20));
        Bitmap bmp = arrastoApp.G != null && arrastoApp.G.Caminho != null ? Processos.Icone(arrastoApp.G.Caminho, E(24)) : null;
        if (bmp != null) g.DrawImage(bmp, ri);
        else TextRenderer.DrawText(g, (arrastoApp.G != null ? '\uE8D6' : arrastoApp.P != null ? '\uE734' : Audio.Glifo(arrastoApp.D)).ToString(), fGlifo, ri, cTexto, TC);
        TextRenderer.DrawText(g, txt, fNormal, new Rectangle(r.X + E(36), r.Y, r.Width - E(44), r.Height), cTexto, TF);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var z = ZonaEm(e.Location);
        // A roda so rola a lista; mudar volume pela roda no painel e opcional (desligado por padrao)
        if (app.RodaPainel && z != null && (z.Tipo == Z_SLIDER_D || z.Tipo == Z_SLIDER_A))
        {
            float passo = app.Passo / 100f * (e.Delta / 120f);
            if (z.G != null) AplicarApp(z.G, z.G.Vol + passo);
            else AplicarDisp(z.D, z.D.Vol + passo);
            Invalidate();
            return;
        }
        int max = Math.Max(0, alturaConteudo - ClientSize.Height);
        if (max == 0) return;
        rolagem = Math.Max(0, Math.Min(max, rolagem - e.Delta * E(40) / 120));
        Montar(null);
        AtualizarSobre();
        Invalidate();
    }

    void DefinirValor(Zona z, int mx)
    {
        float raio = E(10);
        float v = (mx - z.R.Left - raio) / (z.R.Width - raio * 2);
        if (z.G != null) AplicarApp(z.G, v); else AplicarDisp(z.D, v);
        Invalidate();
    }

    void AplicarDisp(Disp d, float v)
    {
        v = Math.Max(0f, Math.Min(1f, v));
        if (d.Ep == null) return;
        try
        {
            d.Ep.SetMasterVolumeLevelScalar(v, ref Audio.Ctx);
            d.Vol = v;
            if (d.Mudo && v > 0) { d.Ep.SetMute(false, ref Audio.Ctx); d.Mudo = false; }
        }
        catch { }
    }

    void AplicarApp(GrupoApp a, float v)
    {
        v = Math.Max(0f, Math.Min(1f, v));
        foreach (var s in a.Vols)
            try
            {
                s.SetMasterVolume(v, ref Audio.Ctx);
                if (a.Mudo && v > 0) s.SetMute(false, ref Audio.Ctx);
            }
            catch { }
        a.Vol = v;
        if (v > 0) a.Mudo = false;
    }

    void Clique(Zona z)
    {
        switch (z.Tipo)
        {
            case Z_MUDO_D:
                if (z.D.Ep != null) try { z.D.Ep.SetMute(!z.D.Mudo, ref Audio.Ctx); z.D.Mudo = !z.D.Mudo; } catch { }
                Invalidate();
                break;
            case Z_MUDO_A:
                bool m = !z.G.Mudo;
                foreach (var s in z.G.Vols) try { s.SetMute(m, ref Audio.Ctx); } catch { }
                z.G.Mudo = m;
                Invalidate();
                break;
            case Z_USAR:
                if (!z.D.Padrao) { Audio.DefinirPadrao(z.D.Id); Recarregar(); }
                break;
            case Z_SAIDA_A:
                MenuSaida(z);
                break;
            case Z_PERFIL:
                app.AplicarPerfil(z.P, false);
                abertura.Clear();
                vistaAlvo[0] = vistaAlvo[1] = false; vista[0] = vista[1] = 0f;
                Recarregar();
                Depois();            // o Windows leva um instante para mover os apps
                Depois(1200);
                break;
            case Z_NOVO_PERFIL:
                Esconder();
                app.NovoPerfil();
                Mostrar();   // volta o painel depois de digitar o nome (ja com o perfil novo)
                break;
            case Z_MENU:
                app.AbrirConfiguracoes();
                app.AfastarConfig(Bounds);
                break;
            case Z_ESCONDIDOS:
                vistaAlvo[z.F] = !vistaAlvo[z.F];
                if (app.Animacoes) { Animar(); break; }
                vista[z.F] = vistaAlvo[z.F] ? 1f : 0f;
                Montar(null); Posicionar(); AtualizarSobre(); Invalidate();
                break;
            case Z_MOSTRAR:
                if (z.Id == null) foreach (var e in escondidos[z.F]) app.Ocultos.Remove(e.Id);
                else app.Ocultos.Remove(z.Id);
                app.SalvarConfig();
                Recarregar();
                if (!vistaAlvo[z.F] && vista[z.F] > 0f)
                {
                    if (app.Animacoes) Animar();
                    else { vista[z.F] = 0f; Montar(null); Posicionar(); Invalidate(); }
                }
                break;
            case Z_RECOLHER:
                app.AlternarAberto(z.D);
                if (app.Animacoes) { Animar(); break; }
                abertura[z.D.Id] = app.Aberto(z.D) ? 1f : 0f;
                Montar(null);
                Posicionar();
                AtualizarSobre();
                Invalidate();
                break;
        }
    }

    // Abre o menu sem sair do monitor onde esta o painel (o Windows as vezes joga
    // para o monitor de baixo/do lado quando falta espaco)
    void MostrarDentro(ToolStripDropDown m, Point ptCliente, bool preferirEsquerda)
    {
        var scr = PointToScreen(ptCliente);
        var sz = m.GetPreferredSize(Size.Empty);
        var a = Screen.FromPoint(scr).WorkingArea;
        bool cima = scr.Y + sz.Height > a.Bottom && scr.Y - sz.Height >= a.Top;
        bool esq = preferirEsquerda ? scr.X - sz.Width >= a.Left : scr.X + sz.Width > a.Right;
        var dir = cima ? (esq ? ToolStripDropDownDirection.AboveLeft : ToolStripDropDownDirection.AboveRight)
                       : (esq ? ToolStripDropDownDirection.BelowLeft : ToolStripDropDownDirection.BelowRight);
        m.Show(this, ptCliente, dir);
    }

    // Clique direito num perfil
    void MenuPerfil(Zona z)
    {
        var p = z.P;
        string chave = "perfil|" + p.Nome;
        if (Alterna.FecharSeAberto(chave) || !Alterna.PodeAbrir(chave)) return;
        var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };
        menu.Items.Add(new ToolStripLabel("Perfil " + p.Nome) { Tag = "fraco" });
        if (p.Nome != app.PerfilAtivo)
        {
            var at = new ToolStripMenuItem("Ativar");
            at.Click += (s, e) => Clique(new Zona { Tipo = Z_PERFIL, P = p, R = z.R });
            menu.Items.Add(at);
        }
        var guardar = new ToolStripMenuItem("Guardar a configuração de agora neste perfil");
        guardar.Click += (s, e) => { app.AtualizarPerfil(p); Invalidate(); };
        menu.Items.Add(guardar);
        menu.Items.Add(new ToolStripSeparator());

        var ren = new ToolStripMenuItem("Renomear…");
        ren.Click += (s, e) => BeginInvoke(new Action(() => { Esconder(); app.RenomearPerfil(p); Mostrar(); }));
        menu.Items.Add(ren);
        var atalho = new ToolStripMenuItem("Atalho: " + App.TextoAtalho(p.Mod, p.Vk) + "…");
        atalho.Click += (s, e) => BeginInvoke(new Action(() => { Esconder(); app.AtalhoPerfil(p); Mostrar(); }));
        menu.Items.Add(atalho);
        var auto = new ToolStripMenuItem("Ativar sozinho quando um app tocar");
        var cands = app.AppsComSom(p);
        if (cands.Count == 0) auto.DropDownItems.Add(new ToolStripLabel("Abra o app (jogo, Discord…) e volte aqui") { Tag = "fraco" });
        foreach (var c in cands)
        {
            string exe = c;
            var it = new ToolStripMenuItem(exe) { Checked = p.Gatilhos.Contains(exe) };
            it.Click += (s, e) => { if (!p.Gatilhos.Remove(exe)) p.Gatilhos.Add(exe); app.SalvarConfig(); };
            auto.DropDownItems.Add(it);
        }
        menu.Items.Add(auto);
        menu.Items.Add(new ToolStripSeparator());
        var exc = new ToolStripMenuItem("Excluir perfil");
        var sim = new ToolStripMenuItem("Sim, excluir \"" + p.Nome + "\"");
        sim.Click += (s, e) => BeginInvoke(new Action(() => { app.ExcluirPerfil(p, false); Recarregar(); }));
        exc.DropDownItems.Add(sim);
        exc.DropDownItems.Add(new ToolStripMenuItem("Cancelar"));
        menu.Items.Add(exc);

        app.AplicarTema(menu);
        app.MenuAberto = true;
        menu.Closed += (s, e) => { Alterna.Fechou(chave); app.MenuAberto = false; app.ConferirPainel(); };
        Alterna.Abriu(chave, menu);
        MostrarDentro(menu, PointToClient(Cursor.Position), false);
    }

    // Clique direito numa saida/microfone: opcoes daquele item
    void MenuDisp(Zona z)
    {
        var d = z.D;
        string chave = "disp|" + d.Id;
        if (Alterna.FecharSeAberto(chave) || !Alterna.PodeAbrir(chave)) return;
        var lista = d.Fluxo == 0 ? saidas : entradas;
        int pos = lista.IndexOf(d);
        string id = d.Id, orig = d.NomeOriginal, atual = d.Nome;
        bool mudo = d.Mudo;
        var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };
        menu.Items.Add(new ToolStripLabel(d.Nome) { Tag = "fraco" });

        if (!d.Padrao)
        {
            var pad = new ToolStripMenuItem(d.Fluxo == 0 ? "Usar como saída padrão" : "Usar como microfone padrão");
            pad.Click += (s, e) => { Audio.DefinirPadrao(id); Depois(); };
            menu.Items.Add(pad);
        }
        var mu = new ToolStripMenuItem(mudo ? "Ativar som" : "Mudo");
        mu.Click += (s, e) =>
        {
            if (d.Ep != null) try { d.Ep.SetMute(!mudo, ref Audio.Ctx); d.Mudo = !mudo; } catch { }
            Invalidate();
        };
        menu.Items.Add(mu);
        var rec = new ToolStripMenuItem(app.Aberto(d) ? "Recolher" : "Expandir");
        rec.Click += (s, e) => Clique(new Zona { Tipo = Z_RECOLHER, D = d, R = z.R });
        menu.Items.Add(rec);
        menu.Items.Add(new ToolStripSeparator());

        var ren = new ToolStripMenuItem("Renomear…");
        ren.Click += (s, e) => BeginInvoke(new Action(() => { Esconder(); app.RenomearDispositivo(id, orig, atual); }));
        menu.Items.Add(ren);
        if (atual != orig)
        {
            var volta = new ToolStripMenuItem("Voltar ao nome original (" + orig + ")");
            volta.Click += (s, e) => { app.NomeOriginal(id); Depois(); };
            menu.Items.Add(volta);
        }
        var esc = new ToolStripMenuItem(d.Padrao ? "Esconder do painel (a padrão sempre aparece)" : "Esconder do painel") { Enabled = !d.Padrao };
        esc.Click += (s, e) => { app.Ocultos.Add(id); app.SalvarConfig(); Depois(); };
        menu.Items.Add(esc);

        app.AplicarTema(menu);
        app.MenuAberto = true;
        menu.Closed += (s, e) => { Alterna.Fechou(chave); app.MenuAberto = false; app.ConferirPainel(); };
        Alterna.Abriu(chave, menu);
        MostrarDentro(menu, PointToClient(Cursor.Position), false);
    }

    void Mover(List<Disp> lista, Disp d, int passo)
    {
        int i = lista.IndexOf(d), j = i + passo;
        if (i < 0 || j < 0 || j >= lista.Count) return;
        lista.RemoveAt(i);
        lista.Insert(j, d);
        app.GuardarOrdem(lista);
        Montar(null);
        Posicionar();
        Invalidate();
    }

    void MenuSaida(Zona z)
    {
        var a = z.G;
        string chave = "saida|" + a.Exe;
        if (Alterna.FecharSeAberto(chave) || !Alterna.PodeAbrir(chave)) return; // o mesmo clique que fechou o menu
        var menu = new ContextMenuStrip { ShowImageMargin = true, ShowCheckMargin = true };
        menu.Items.Add(new ToolStripLabel("Saída de " + a.Nome) { Tag = "fraco" });
        var pad = new ToolStripMenuItem("Padrão do Windows") { Checked = a.Saida == null };
        pad.Click += (s, e) => { app.DefinirSaidaApp(a.Exe, a.Pids, null); Depois(); };
        menu.Items.Add(pad);
        menu.Items.Add(new ToolStripSeparator());
        int ti = Math.Max(16, E(16));
        foreach (var d in saidas)
        {
            string id = d.Id;
            var it = new ToolStripMenuItem(d.Nome) { Checked = a.Saida != null && Politica.Igual(a.Saida, id) };
            it.Image = Desenho.Glifo(Audio.Glifo(d), ti, cTexto, 1f);
            it.Click += (s, e) => { app.DefinirSaidaApp(a.Exe, a.Pids, id); Depois(); };
            menu.Items.Add(it);
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripLabel("Dica: arraste o app até outra saída") { Tag = "fraco" });
        app.AplicarTema(menu);
        app.MenuAberto = true;
        menuApp = a;
        menu.Closed += (s, e) => { Alterna.Fechou(chave); menuApp = null; app.MenuAberto = false; Invalidate(); app.ConferirPainel(); };
        var onde = z.Tipo == Z_SAIDA_A ? new Point(z.R.Right, z.R.Bottom + E(2)) : PointToClient(Cursor.Position);
        Alterna.Abriu(chave, menu);
        MostrarDentro(menu, onde, z.Tipo == Z_SAIDA_A);
        Invalidate();
    }

    void Depois(int ms)
    {
        var t = new Timer { Interval = ms };
        t.Tick += (s, e) => { t.Stop(); t.Dispose(); Recarregar(); };
        t.Start();
    }

    void Depois()
    {
        // o Windows leva um instante para mover o som do app
        var t = new Timer { Interval = 350 };
        t.Tick += (s, e) => { t.Stop(); t.Dispose(); Recarregar(); };
        t.Start();
    }
}

// =====================================================================
//  Janela invisivel: atalhos globais, roda do mouse no icone, tema
// =====================================================================
class Mensageiro : NativeWindow
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] d, uint n, uint tam);
    [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr h, uint cmd, IntPtr dados, ref uint tam, uint tamCab);

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTDEVICE { public ushort Pagina, Uso; public uint Flags; public IntPtr Janela; }

    public Action<int> Atalho;
    public Action<int> Roda;
    public Action Tema, Abrir;
    public Action<int> Config, Perfil;
    public Action Pedido;
    public bool Capturando { get; private set; }
    readonly IntPtr buf = Marshal.AllocHGlobal(256);

    public Mensageiro() { CreateHandle(new CreateParams()); }

    public bool Registrar(int id, uint mod, uint vk)
    {
        UnregisterHotKey(Handle, id);
        if (vk == 0) return true;
        return RegisterHotKey(Handle, id, mod | 0x4000 /*NOREPEAT*/, vk);
    }
    public void Remover(int id) { UnregisterHotKey(Handle, id); }

    // So "escuta" o mouse enquanto o ponteiro esta em cima do icone da bandeja
    public void CapturarRoda(bool ligar)
    {
        if (ligar == Capturando) return;
        var r = new RAWINPUTDEVICE { Pagina = 1, Uso = 2, Flags = ligar ? 0x100u /*INPUTSINK*/ : 0x1u /*REMOVE*/, Janela = ligar ? Handle : IntPtr.Zero };
        if (RegisterRawInputDevices(new[] { r }, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)))) Capturando = ligar;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 /*WM_HOTKEY*/ && Atalho != null) Atalho(m.WParam.ToInt32());
        else if (m.Msg == 0x00FF /*WM_INPUT*/ && Capturando)
        {
            uint cab = (uint)(8 + IntPtr.Size * 2), tam = 256;
            if (GetRawInputData(m.LParam, 0x10000003 /*RID_INPUT*/, buf, ref tam, cab) != unchecked((uint)-1) &&
                Marshal.ReadInt32(buf, 0) == 0 /*MOUSE*/)
            {
                ushort flags = (ushort)Marshal.ReadInt16(buf, (int)cab + 4);
                if ((flags & 0x0400 /*WHEEL*/) != 0 && Roda != null) Roda(Marshal.ReadInt16(buf, (int)cab + 6));
            }
        }
        else if (m.Msg == 0x001A /*WM_SETTINGCHANGE*/ && m.LParam != IntPtr.Zero && Tema != null)
        {
            string s = Marshal.PtrToStringUni(m.LParam);
            if (s == "ImmersiveColorSet") Tema();
        }
        else if (m.Msg == (int)Program.MsgAbrir && Abrir != null) Abrir();
        else if (m.Msg == (int)Program.MsgConfig && Config != null) Config(m.WParam.ToInt32());
        else if (m.Msg == (int)Program.MsgPerfil && Perfil != null) Perfil(m.WParam.ToInt32());
        else if (m.Msg == (int)Program.MsgPedido && Pedido != null) Pedido();
        base.WndProc(ref m);
    }
}

// =====================================================================
//  App
// =====================================================================
class App : ApplicationContext
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr hmon, int t, out uint x, out uint y);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point p, uint f);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint f);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string c, string n);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] static extern int GetSystemMetricsForDpi(int i, uint dpi);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("shell32.dll")] static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER id, out RECT r);
    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr h, IntPtr min, IntPtr max);

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct NOTIFYICONIDENTIFIER { public uint cbSize; public IntPtr hWnd; public uint uID; public Guid guid; }

    // ---- configuracao ----
    public bool Lembrar = true, PerfisAuto = true, VoltarPerfil = true, RodaIcone = true, Mics = true,
                Inativos = true, UsarPerfis = false, AvisoTela = true, RodaPainel = false,
                Animacoes = true, FixarBandeja = true, MenuLateralAberto = true,
                MostrarAdaptador = false, MostrarLogo = true, MostrarSom = false;
    public readonly List<string> Ordem = new List<string>();
    public readonly Dictionary<string, bool> Abertos = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    public int Passo = 2;
    public int AlturaMax = 90;   // % da altura livre da tela; passando disso o painel rola
    string tema = "auto";
    public readonly uint[] AtMod = { 0x8 | 0x1, 0x8 | 0x1, 0x8 | 0x1 };            // Win+Alt
    public readonly uint[] AtVk = { (uint)Keys.V, (uint)Keys.O, (uint)Keys.S };   // V painel, O proxima saida, S silenciar (Win+Alt+M ja e usado por outro programa)
    public static readonly string[] AtNome = { "Abrir o painel", "Próxima saída", "Mudo geral" };
    readonly bool[] atOk = { true, true, true };
    public readonly Dictionary<string, string> Regras = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string> Nomes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public readonly HashSet<string> Ocultos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public readonly List<Perfil> Perfis = new List<Perfil>();
    public string PerfilAtivo;
    readonly string arqConfig = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sintonia.ini");

    // ---- estado ----
    readonly NotifyIcon icone;
    readonly Mensageiro msg;
    readonly Painel painel;
    readonly Aviso aviso = new Aviso();
    readonly Control invocador = new Control();
    readonly Vigia vigia;
    readonly ContextMenuStrip menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };
    readonly Timer tAtualizar = new Timer { Interval = 150 };
    readonly Timer tRoda = new Timer { Interval = 300 };
    public bool MenuAberto;
    bool recarregarPainel;
    string chaveIcone, ultimoId, ultimoNome;
    Point ultimoSobreIcone;

    // perfis automaticos
    Perfil perfilAnterior; bool temAnterior;
    readonly Dictionary<uint, Process> vigiados = new Dictionary<uint, Process>();
    readonly Dictionary<uint, DateTime> recentes = new Dictionary<uint, DateTime>();

    public App()
    {
        // O app se chamava "Som": aproveita as configuracoes antigas
        string antigo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Som.ini");
        if (!File.Exists(arqConfig) && File.Exists(antigo)) try { File.Move(antigo, arqConfig); } catch { }
        bool primeiraVez = !File.Exists(arqConfig);
        LerConfig();
        Audio.ComMedidor = MostrarSom;
        invocador.CreateControl();
        var h = invocador.Handle;

        painel = new Painel(this);
        msg = new Mensageiro();
        msg.Atalho = AoAtalho;
        msg.Tema = () => { chaveIcone = null; AtualizarIcone(); };
        msg.Abrir = () => { if (!painel.Visible) painel.Mostrar(); };
        msg.Config = pg => AbrirConfiguracoes(pg);
        msg.Perfil = i => { if (i >= 0 && i < Perfis.Count) { AplicarPerfil(Perfis[i], true); PainelMudou(); } };
        msg.Pedido = () =>
        {
            try
            {
                var a = File.ReadAllText(Program.ArqPedido).Trim().Split('|');
                File.Delete(Program.ArqPedido);
                var p = Perfis.Find(x => x.Nome == a[1]);
                if (a[0] == "novo") CriarPerfil(a[1]);
                else if (a[0] == "guardar" && p != null) AtualizarPerfil(p);
                else if (a[0] == "renomear" && p != null && a.Length > 2) RenomearPerfilPara(p, a[2]);
                else if (a[0] == "excluir" && p != null) ExcluirPerfil(p, false);
                PainelMudou();
            }
            catch (Exception ex) { Program.Log(ex); }
        };
        if (Program.PaginaInicial >= 0) { int pg0 = Program.PaginaInicial; Postar(() => AbrirConfiguracoes(pg0)); }

        icone = new NotifyIcon { Visible = true, Text = "Sintonia" };
        icone.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) painel.Alternar(); };
        icone.ContextMenuStrip = menu;
        menu.Opening += (s, e) => { MenuAberto = true; MontarMenu(); };
        menu.Closed += (s, e) => { MenuAberto = false; ConferirPainel(); if (!painel.Visible) Aparar(); };
        menu.Closing += (s, e) =>
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked && segurarMenu) e.Cancel = true;
            segurarMenu = false;
        };

        tAtualizar.Tick += (s, e) =>
        {
            tAtualizar.Stop();
            AtualizarIcone();
            if (recarregarPainel && painel.Visible) painel.Recarregar();
            recarregarPainel = false;
        };
        tRoda.Tick += (s, e) => { if (!CursorNoIcone()) { msg.CapturarRoda(false); tRoda.Stop(); } };

        AtualizarIcone();
        RegistrarAtalhos();

        vigia = new Vigia(
            tipo => Postar(() =>
            {
                if (tipo != 3) { recarregarPainel = true; ultimoId = null; }
                if (!tAtualizar.Enabled) tAtualizar.Start();
            }),
            pid => Postar(() =>
            {
                SessaoNova(pid);
                if (painel.Visible) { recarregarPainel = true; if (!tAtualizar.Enabled) tAtualizar.Start(); }
            }));

        // Ao iniciar: aplica as saidas lembradas aos apps que ja estao tocando
        var tIni = new Timer { Interval = 2500 };
        tIni.Tick += (s, e) =>
        {
            tIni.Stop(); tIni.Dispose();
            foreach (var kv in Audio.Sessoes()) SessaoNova(kv.Key);
            if (!atOk[0] || !atOk[1] || !atOk[2]) RegistrarAtalhos(); // tenta de novo (ex.: copia antiga ainda fechando)
            AplicarFixar(FixarBandeja);
            Aparar();
        };
        tIni.Start();

        // Iniciar com o Windows: na primeira vez ja liga; depois so confere o caminho
        new Thread(() =>
        {
            if (primeiraVez) { if (CriarTarefa()) inicioCache = true; }
            else ConferirInicio();
        }) { IsBackground = true, Priority = ThreadPriority.Lowest }.Start();
        if (primeiraVez) SalvarConfig();
    }

    void Postar(Action a)
    {
        try { if (invocador.IsHandleCreated) invocador.BeginInvoke(a); } catch { }
    }

    // Diminui a memoria usada depois de fechar o painel/menu
    public void Aparar()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        try { SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, new IntPtr(-1), new IntPtr(-1)); } catch { }
    }

    // ---------------- icone da bandeja ----------------
    bool BarraClara()
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                object v = k == null ? null : k.GetValue("SystemUsesLightTheme");
                if (v is int) return (int)v != 0;
            }
        }
        catch { }
        return false;
    }

    void AtualizarIcone()
    {
        float v; bool m; string id;
        bool ok = Audio.LerPadrao(0, out v, out m, out id);
        bool mudoIcone = !ok || m;
        uint dpi = 96;
        try { IntPtr bar = FindWindow("Shell_TrayWnd", null); if (bar != IntPtr.Zero) dpi = GetDpiForWindow(bar); } catch { }
        if (dpi == 0) dpi = 96;
        int n = 16;
        try { n = GetSystemMetricsForDpi(49 /*SM_CXSMICON*/, dpi); } catch { n = (int)(16 * dpi / 96); }
        bool claro = BarraClara();
        string chave = mudoIcone + "|" + n + "|" + claro;
        if (chave != chaveIcone)
        {
            chaveIcone = chave;
            using (var bmp = Desenho.Bandeja(n, claro ? Color.FromArgb(28, 28, 28) : Color.White, mudoIcone))
            {
                var antigo = icone.Icon;
                icone.Icon = Desenho.IconeDe(bmp);
                if (antigo != null) antigo.Dispose();
            }
        }
        string nome = "Sem saída de som";
        if (ok)
        {
            nome = NomeDe(id);
            nome += m ? " · mudo" : " · " + (int)Math.Round(v * 100) + "%";
        }
        if (UsarPerfis && PerfilAtivo != null) nome += "\nPerfil: " + PerfilAtivo;
        icone.Text = nome.Length > 63 ? nome.Substring(0, 63) : nome;
    }

    // Se o menu fechou e o foco foi para outro lugar, fecha o painel tambem
    public void ConferirPainel()
    {
        Postar(() =>
        {
            var af = Form.ActiveForm;
            bool configAtiva = config != null && !config.IsDisposed && af == config;
            if (painel.Visible && af != painel && !configAtiva) painel.Esconder();
        });
    }

    string NomeDe(string id)
    {
        if (id == null) return "?";
        string n;
        if (Nomes.TryGetValue(id, out n)) return n;
        if (id == ultimoId) return ultimoNome;
        var l = Audio.Listar(0, false, false, null);
        l.AddRange(Audio.Listar(1, false, false, null));
        n = "?";
        foreach (var d in l) if (string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)) n = d.Nome;
        Audio.Liberar(l);
        ultimoId = id; ultimoNome = n;
        return n;
    }

    // ---------------- roda do mouse sobre o icone ----------------
    void SobreIcone()
    {
        ultimoSobreIcone = Cursor.Position;
        if (!RodaIcone) return;
        msg.CapturarRoda(true);
        if (!tRoda.Enabled) tRoda.Start();
    }

    bool CursorNoIcone()
    {
        var p = Cursor.Position;
        try
        {
            var fw = typeof(NotifyIcon).GetField("window", BindingFlags.NonPublic | BindingFlags.Instance);
            var fi = typeof(NotifyIcon).GetField("id", BindingFlags.NonPublic | BindingFlags.Instance);
            var janela = fw == null ? null : fw.GetValue(icone) as NativeWindow;
            if (janela != null && fi != null)
            {
                var nid = new NOTIFYICONIDENTIFIER { hWnd = janela.Handle, uID = (uint)(int)fi.GetValue(icone) };
                nid.cbSize = (uint)Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER));
                RECT r;
                if (Shell_NotifyIconGetRect(ref nid, out r) == 0)
                    return p.X >= r.L && p.X < r.R && p.Y >= r.T && p.Y < r.B;
            }
        }
        catch { }
        int tol = (int)(20 * EscalaEm(p));
        return Math.Abs(p.X - ultimoSobreIcone.X) <= tol && Math.Abs(p.Y - ultimoSobreIcone.Y) <= tol;
    }

    void AoRodar(int delta)
    {
        if (!RodaIcone || !CursorNoIcone()) return;
        float v; bool m; string id;
        if (!Audio.LerPadrao(0, out v, out m, out id)) return;
        float nv = Math.Max(0f, Math.Min(1f, (float)Math.Round((v + Passo / 100f * delta / 120f) * 100) / 100f));
        Audio.MudarPadrao(0, nv, m && nv > 0 ? (bool?)false : null);
    }

    // ---------------- atalhos ----------------
    void RegistrarAtalhos()
    {
        for (int i = 0; i < 3; i++) atOk[i] = msg.Registrar(i + 1, AtMod[i], AtVk[i]);
        for (int i = 0; i < 50; i++) msg.Remover(100 + i);
        if (UsarPerfis) for (int i = 0; i < Perfis.Count && i < 50; i++) msg.Registrar(100 + i, Perfis[i].Mod, Perfis[i].Vk);
    }

    void AoAtalho(int id)
    {
        if (id == 1) painel.Alternar();
        else if (id == 2) ProximaSaida();
        else if (id == 3) MudoGeral();
        else if (UsarPerfis && id >= 100 && id - 100 < Perfis.Count) AplicarPerfil(Perfis[id - 100], true);
    }

    public static string TextoAtalho(uint mod, uint vk)
    {
        if (vk == 0) return "nenhum";
        var p = new List<string>();
        if ((mod & 2) != 0) p.Add("Ctrl");
        if ((mod & 1) != 0) p.Add("Alt");
        if ((mod & 4) != 0) p.Add("Shift");
        if ((mod & 8) != 0) p.Add("Win");
        var k = (Keys)vk;
        string nome = k >= Keys.D0 && k <= Keys.D9 ? ((int)k - (int)Keys.D0).ToString()
                    : k >= Keys.NumPad0 && k <= Keys.NumPad9 ? "Num " + ((int)k - (int)Keys.NumPad0)
                    : k == Keys.Scroll ? "Scroll Lock"
                    : k.ToString();
        p.Add(nome);
        return string.Join("+", p);
    }

    bool EscolherAtalho(string titulo, ref uint mod, ref uint vk, int idAtual)
    {
        if (idAtual > 0) msg.Remover(idAtual); // nao dispara enquanto escolhe
        bool ok = false;
        using (var f = new JanelaAtalho(titulo, mod, vk, TemaEscuro()))
            if (f.ShowDialog() == DialogResult.OK) { mod = f.Mod; vk = f.Vk; ok = true; }
        return ok;
    }

    // ---------------- acoes ----------------
    void ProximaSaida()
    {
        var l = Audio.Listar(0, false, false, Nomes);
        l.RemoveAll(d => !d.Padrao && Ocultos.Contains(d.Id) && Soltar(d));
        if (l.Count > 0)
        {
            int i = l.FindIndex(d => d.Padrao);
            var prox = l[(i + 1) % l.Count];
            if (l.Count > 1) Audio.DefinirPadrao(prox.Id);
            Avisar(Audio.Glifo(prox), prox.Nome);
        }
        Audio.Liberar(l);
    }

    static bool Soltar(Disp d) { Audio.Liberar(new List<Disp> { d }); return true; }

    void MudoGeral()
    {
        float v; bool m; string id;
        if (!Audio.LerPadrao(0, out v, out m, out id)) return;
        Audio.MudarPadrao(0, null, !m);
        Avisar(m ? Audio.GlifoVolume(v, false, 0) : '\uE74F', m ? "Som ligado · " + (int)Math.Round(v * 100) + "%" : "Mudo");
    }

    void Avisar(char gl, string txt)
    {
        if (!AvisoTela || painel.Visible || TelaCheiaAtiva()) return;
        var p = Cursor.Position;
        aviso.Suave = Animacoes;
        int borda;
        aviso.Mostrar(gl, txt, TemaEscuro(), AreaUtil(Screen.FromPoint(p), out borda), EscalaEm(p));
    }

    // Um jogo ou video em tela cheia esta na frente? (entao nao mostra aviso)
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);

    static bool TelaCheiaAtiva()
    {
        IntPtr h = GetForegroundWindow();
        if (h == IntPtr.Zero) return false;
        var sb = new System.Text.StringBuilder(64);
        GetClassName(h, sb, 64);
        string c = sb.ToString();
        if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd") return false;
        // janela maximizada ou com barra de titulo e programa normal, nao jogo/video em tela cheia
        if (IsZoomed(h) || (GetWindowLong(h, -16 /*GWL_STYLE*/) & 0x00C00000 /*WS_CAPTION*/) == 0x00C00000) return false;
        RECT r;
        if (!GetWindowRect(h, out r)) return false;
        var tela = Screen.FromHandle(h).Bounds;
        return r.L <= tela.Left && r.T <= tela.Top && r.R >= tela.Right && r.B >= tela.Bottom;
    }

    public void DefinirSaidaApp(string exe, List<uint> pids, string id)
    {
        foreach (var p in pids) Politica.Definir(p, id);
        if (Lembrar && exe != null)
        {
            if (id == null) Regras.Remove(exe); else Regras[exe] = id;
            SalvarConfig();
        }
    }

    // Um app comecou a usar o som: aplica a saida lembrada e os perfis automaticos
    void SessaoNova(uint pid)
    {
        DateTime t;
        if (recentes.TryGetValue(pid, out t) && (DateTime.Now - t).TotalSeconds < 3) return;
        recentes[pid] = DateTime.Now;
        if (recentes.Count > 200) recentes.Clear();
        string exe = Processos.Exe(pid);
        if (exe == null) return;

        string id;
        if (Lembrar && Regras.TryGetValue(exe, out id) && Audio.Existe(id))
        {
            string alvo = id;
            GarantirSaidas(x => alvo, exe);
            GarantirDepois(x => alvo, exe);
        }

        if (!PerfisAuto || !UsarPerfis) return;
        foreach (var p in Perfis)
        {
            if (!p.Gatilhos.Contains(exe)) continue;
            if (!vigiados.ContainsKey(pid))
            {
                try
                {
                    var pr = Process.GetProcessById((int)pid);
                    pr.EnableRaisingEvents = true;
                    uint pp = pid;
                    pr.Exited += (s, e) => Postar(() => GatilhoFechou(pp));
                    vigiados[pid] = pr;
                }
                catch { }
            }
            if (PerfilAtivo != p.Nome)
            {
                if (!temAnterior)
                {
                    perfilAnterior = Perfis.Find(x => x.Nome == PerfilAtivo) ?? Instantaneo(null);
                    temAnterior = true;
                }
                AplicarPerfil(p, true);
            }
            break;
        }
    }

    void GatilhoFechou(uint pid)
    {
        Process pr;
        if (vigiados.TryGetValue(pid, out pr)) { vigiados.Remove(pid); try { pr.Dispose(); } catch { } }
        if (vigiados.Count > 0 || !temAnterior) return;
        var ant = perfilAnterior;
        temAnterior = false; perfilAnterior = null;
        if (VoltarPerfil && ant != null) AplicarPerfil(ant, true);
    }

    // ---------------- perfis ----------------
    // Foto do estado atual: saida padrao, microfone padrao e a saida de cada app
    Perfil Instantaneo(string nome)
    {
        var p = new Perfil { Nome = nome, Saida = Audio.PadraoId(0), Entrada = Audio.PadraoId(1) };
        foreach (var kv in Regras) p.Regras[kv.Key] = kv.Value;
        foreach (var s in Audio.Sessoes())
        {
            string id = Politica.Obter(s.Key);
            if (id == null) p.Regras.Remove(s.Value); else p.Regras[s.Value] = id;
        }
        // volumes e mudo das saidas, microfones e apps
        p.Volumes = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        p.Mudos = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        p.AppVol = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        p.AppMudo = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        for (int f = 0; f < 2; f++)
        {
            var ds = Audio.Listar(f, f == 0, true, null, false);
            foreach (var d in ds)
            {
                p.Volumes[d.Id] = d.Vol; p.Mudos[d.Id] = d.Mudo;
                foreach (var g in d.Apps)
                    if (g.Vols.Count > 0 && !p.AppVol.ContainsKey(g.Exe)) { p.AppVol[g.Exe] = g.Vol; p.AppMudo[g.Exe] = g.Mudo; }
            }
            Audio.Liberar(ds);
        }
        // como o painel esta: escondidos, ordem, abertos/fechados
        p.Ocultos = new List<string>(Ocultos);
        p.Ordem = new List<string>(Ordem);
        p.Abertos = new Dictionary<string, bool>(Abertos, StringComparer.OrdinalIgnoreCase);
        return p;
    }

    // Garante que cada app toque na saida certa. Nao basta o Windows "ter marcado" a saida:
    // se o som continua tocando em outro lugar, pede a troca de novo (isso faz o app mudar).
    // alvoDe: exe -> id da saida (null = seguir a saida padrao). soExe: limita a um app.
    void GarantirSaidas(Func<string, string> alvoDe, string soExe)
    {
        string pad = Audio.PadraoId(0);
        var ds = Audio.Listar(0, true, true, null, false);
        var feitos = new HashSet<uint>();
        foreach (var d in ds)
            foreach (var g in d.Apps)
            {
                if (g.Sistema || (soExe != null && g.Exe != soExe)) continue;
                string alvo = alvoDe(g.Exe);
                if (alvo != null && !Audio.Existe(alvo)) alvo = null;
                string efetivo = alvo ?? pad;
                foreach (var pid in g.Pids)
                {
                    bool marcadoErrado = !Politica.Igual(Politica.Obter(pid), alvo);
                    bool tocandoErrado = g.Ativo && efetivo != null && !Politica.Igual(d.Id, efetivo);
                    if ((marcadoErrado || tocandoErrado) && feitos.Add(pid)) Politica.Definir(pid, alvo);
                }
            }
        Audio.Liberar(ds);
    }

    // Confere de novo um pouco depois: as vezes o Windows so move o som no segundo pedido
    void GarantirDepois(Func<string, string> alvoDe, string soExe)
    {
        foreach (var ms in new[] { 400, 1200 })
        {
            var t = new Timer { Interval = ms };
            t.Tick += (s, e) =>
            {
                t.Stop(); t.Dispose();
                GarantirSaidas(alvoDe, soExe);
                if (painel.Visible) painel.Recarregar();
            };
            t.Start();
        }
    }

    public void AplicarPerfil(Perfil p, bool avisar)
    {
        if (p.Saida != null && Audio.Existe(p.Saida)) Audio.DefinirPadrao(p.Saida);
        if (p.Entrada != null && Audio.Existe(p.Entrada)) Audio.DefinirPadrao(p.Entrada);
        Regras.Clear();
        foreach (var kv in p.Regras) Regras[kv.Key] = kv.Value;
        Func<string, string> alvoPerfil = exe => { string q; return p.Regras.TryGetValue(exe, out q) ? q : null; };
        GarantirSaidas(alvoPerfil, null);
        GarantirDepois(alvoPerfil, null);
        if (p.Volumes != null)
            foreach (var kv in p.Volumes)
            {
                bool m = false;
                bool? mudo = null;
                if (p.Mudos != null && p.Mudos.TryGetValue(kv.Key, out m)) mudo = m;
                Audio.DefinirVolume(kv.Key, kv.Value, mudo);
            }
        if (p.AppVol != null)
        {
            var ds = Audio.Listar(0, true, true, null, false);
            foreach (var d in ds)
                foreach (var g in d.Apps)
                {
                    float v; bool m = false;
                    bool temV = p.AppVol.TryGetValue(g.Exe, out v);
                    bool temM = p.AppMudo != null && p.AppMudo.TryGetValue(g.Exe, out m);
                    foreach (var sv in g.Vols)
                        try
                        {
                            if (temV) sv.SetMasterVolume(v, ref Audio.Ctx);
                            if (temM) sv.SetMute(m, ref Audio.Ctx);
                        }
                        catch { }
                }
            Audio.Liberar(ds);
        }
        if (p.Ocultos != null) { Ocultos.Clear(); foreach (var o in p.Ocultos) Ocultos.Add(o); }
        if (p.Ordem != null) { Ordem.Clear(); Ordem.AddRange(p.Ordem); }
        if (p.Abertos != null) { Abertos.Clear(); foreach (var kv in p.Abertos) Abertos[kv.Key] = kv.Value; }
        PerfilAtivo = Perfis.Contains(p) ? p.Nome : null;
        SalvarConfig();
        AtualizarIcone();
        if (avisar) Avisar('\uE734', PerfilAtivo != null ? "Perfil: " + PerfilAtivo : "Configuração anterior");
    }

    public const int MaxPerfis = 10;

    public void NovoPerfil()
    {
        if (Perfis.Count >= MaxPerfis)
        {
            MessageBox.Show("Você já tem " + MaxPerfis + " perfis, o limite do Sintonia.\nExclua um perfil para criar outro.",
                "Sintonia", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string nome = PedirTexto("Novo perfil", "Guarda a saída, o microfone e a saída de cada app de agora.",
            "Perfil " + (Perfis.Count + 1));
        if (nome == null) return;
        CriarPerfil(nome);
    }

    public void CriarPerfil(string nome)
    {
        var antigo = Perfis.Find(x => x.Nome == nome);
        if (antigo == null && Perfis.Count >= MaxPerfis) return;
        var p = Instantaneo(nome);
        if (antigo != null) { p.Mod = antigo.Mod; p.Vk = antigo.Vk; p.Gatilhos = antigo.Gatilhos; Perfis[Perfis.IndexOf(antigo)] = p; }
        else Perfis.Add(p);
        PerfilAtivo = nome;
        SalvarConfig();
        RegistrarAtalhos();
        AtualizarIcone();
    }

    string PedirTexto(string titulo, string dica, string valor)
    {
        using (var f = new JanelaTexto(titulo, dica, valor, TemaEscuro()))
        {
            if (f.ShowDialog() != DialogResult.OK) return null;
            string v = f.Valor.Replace("|", "").Replace(">", "").Replace(";", "").Replace("=", "").Replace("\r", "").Replace("\n", "");
            return v.Length == 0 ? null : v;
        }
    }

    // ---------------- menu ----------------
    static bool segurarMenu;
    static void ManterAberto(ToolStripMenuItem m)
    {
        m.DropDown.ItemClicked += (s, e) =>
        {
            if (!(e.ClickedItem is ToolStripMenuItem) || !((ToolStripMenuItem)e.ClickedItem).HasDropDownItems) segurarMenu = true;
        };
        m.DropDown.Closing += (s, e) => { if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true; };
    }

    public void MostrarMenu(Point p)
    {
        menu.Show(p, ToolStripDropDownDirection.BelowLeft);
    }

    // Abre o menu do icone a partir do painel, sem sair do monitor do painel
    public void MostrarMenu(Control dono, Point ptCliente)
    {
        MontarMenu();
        var scr = dono.PointToScreen(ptCliente);
        var sz = menu.GetPreferredSize(Size.Empty);
        var a = Screen.FromPoint(scr).WorkingArea;
        bool cima = scr.Y + sz.Height > a.Bottom && scr.Y - sz.Height >= a.Top;
        bool esq = scr.X + sz.Width > a.Right;
        menu.Show(dono, ptCliente, cima ? (esq ? ToolStripDropDownDirection.AboveLeft : ToolStripDropDownDirection.AboveRight)
                                        : (esq ? ToolStripDropDownDirection.BelowLeft : ToolStripDropDownDirection.BelowRight));
    }

    // Menu curto do clique direito: so o essencial. O resto fica na janela de Configuracoes.
    void MontarMenu()
    {
        menu.Items.Clear();
        var saidas = Audio.Listar(0, false, false, Nomes);
        var entradas = Audio.Listar(1, false, false, Nomes);

        menu.Items.Add(new ToolStripLabel("Sintonia") { Tag = "titulo" });
        var pad = saidas.Find(d => d.Padrao);
        menu.Items.Add(new ToolStripLabel(pad == null ? "Nenhuma saída ativa" :
            pad.Nome + (pad.Mudo ? " · mudo" : " · " + (int)Math.Round(pad.Vol * 100) + "%")) { Tag = "fraco" });
        menu.Items.Add(new ToolStripSeparator());

        var mAbrir = new ToolStripMenuItem("Abrir painel") { ShortcutKeyDisplayString = AtVk[0] == 0 ? "" : TextoAtalho(AtMod[0], AtVk[0]) };
        mAbrir.Click += (s, e) => { if (!painel.Visible) BeginPainel(); };
        menu.Items.Add(mAbrir);

        var mSaida = new ToolStripMenuItem("Saída de som");
        foreach (var d in saidas)
        {
            string id = d.Id;
            var it = new ToolStripMenuItem(d.Nome) { Checked = d.Padrao };
            it.Click += (s, e) => { Audio.DefinirPadrao(id); tAtualizar.Start(); };
            mSaida.DropDownItems.Add(it);
        }
        if (saidas.Count == 0) mSaida.DropDownItems.Add(new ToolStripLabel("Nenhuma saída ativa") { Tag = "fraco" });
        menu.Items.Add(mSaida);

        var mEntrada = new ToolStripMenuItem("Microfone");
        foreach (var d in entradas)
        {
            string id = d.Id;
            var it = new ToolStripMenuItem(d.Nome) { Checked = d.Padrao };
            it.Click += (s, e) => { Audio.DefinirPadrao(id); tAtualizar.Start(); };
            mEntrada.DropDownItems.Add(it);
        }
        if (entradas.Count == 0) mEntrada.DropDownItems.Add(new ToolStripLabel("Nenhum microfone ativo") { Tag = "fraco" });
        menu.Items.Add(mEntrada);

        var mMudo = new ToolStripMenuItem("Mudo") { Checked = pad != null && pad.Mudo, ShortcutKeyDisplayString = AtVk[2] == 0 ? "" : TextoAtalho(AtMod[2], AtVk[2]) };
        mMudo.Click += (s, e) => MudoGeral();
        menu.Items.Add(mMudo);

        var mPerfis = new ToolStripMenuItem("Perfis");
        foreach (var p in Perfis)
        {
            var pp = p;
            var it = new ToolStripMenuItem(p.Nome) { Checked = p.Nome == PerfilAtivo };
            if (p.Vk != 0) it.ShortcutKeyDisplayString = TextoAtalho(p.Mod, p.Vk);
            it.Click += (s, e) => AplicarPerfil(pp, false);
            mPerfis.DropDownItems.Add(it);
        }
        if (Perfis.Count > 0) mPerfis.DropDownItems.Add(new ToolStripSeparator());
        bool cheio = Perfis.Count >= MaxPerfis;
        var novo = new ToolStripMenuItem(cheio ? "Limite de " + MaxPerfis + " perfis atingido" : "Novo perfil com a configuração de agora…") { Enabled = !cheio };
        novo.Click += (s, e) => NovoPerfil();
        mPerfis.DropDownItems.Add(novo);
        if (UsarPerfis) menu.Items.Add(mPerfis);

        menu.Items.Add(new ToolStripSeparator());
        var mConf = new ToolStripMenuItem("Configurações…");
        mConf.Click += (s, e) => Postar(AbrirConfiguracoes);
        menu.Items.Add(mConf);
        var mSair = new ToolStripMenuItem("Sair");
        mSair.Click += (s, e) => Sair();
        menu.Items.Add(mSair);

        Audio.Liberar(saidas);
        Audio.Liberar(entradas);
        AplicarTema(menu);
    }

    // ---------------- janela de Configuracoes e as acoes que ela usa ----------------
    Configuracoes config;
    public void AbrirConfiguracoes() { AbrirConfiguracoes(-1); }

    public void AbrirConfiguracoes(int pagina)
    {
        if (pagina >= 0 && config != null && !config.IsDisposed) config.IrPara(pagina);
        if (config != null && !config.IsDisposed) { config.Activate(); return; }
        config = new Configuracoes(this);
        if (pagina >= 0) config.IrPara(pagina);
        config.FormClosed += (s, e) => { config = null; Aparar(); };
        config.Show();
        config.Activate();
    }

    public void PainelMudou() { if (painel.Visible) painel.Recarregar(); }

    public void DefinirSom(bool v) { MostrarSom = v; SalvarConfig(); painel.SomMudou(); }

    // Coloca a janela de Configuracoes ao lado do painel (sem ficar por baixo dele)
    public void AfastarConfig(Rectangle r)
    {
        if (config == null || config.IsDisposed || !config.Bounds.IntersectsWith(r)) return;
        var a = Screen.FromRectangle(r).WorkingArea;
        int m = (int)(12 * EscalaEm(r.Location));
        int x = r.Left - config.Width - m;
        if (x < a.Left + m) x = Math.Max(a.Left + m, r.Left - config.Width);
        config.Left = x;
    }

    public string Tema { get { return tema; } set { tema = value; SalvarConfig(); } }

    // Liga/desliga tudo de perfil (painel, menu do icone, atalhos, troca automatica)
    public void DefinirPerfis(bool v)
    {
        UsarPerfis = v;
        if (!v) { temAnterior = false; perfilAnterior = null; }
        SalvarConfig();
        RegistrarAtalhos();
        AtualizarIcone();
        PainelMudou();
    }

    public void DefinirRodaIcone(bool v) { RodaIcone = v; if (!v) msg.CapturarRoda(false); }

    public bool Aberto(Disp d)
    {
        bool v;
        if (Abertos.TryGetValue(d.Id, out v)) return v;
        return d.Padrao || d.Apps.Exists(a => a.Ativo);
    }

    public void AlternarAberto(Disp d) { Abertos[d.Id] = !Aberto(d); SalvarConfig(); }

    // Ordem das saidas/microfones escolhida arrastando no painel
    public void Ordenar(List<Disp> l)
    {
        var pos = new Dictionary<Disp, int>();
        foreach (var d in l)
        {
            int i = Ordem.FindIndex(x => string.Equals(x, d.Id, StringComparison.OrdinalIgnoreCase));
            pos[d] = i < 0 ? int.MaxValue : i;
        }
        l.Sort((a, b) =>
        {
            int c = pos[a].CompareTo(pos[b]);
            return c != 0 ? c : string.Compare(a.Nome, b.Nome, StringComparison.CurrentCultureIgnoreCase);
        });
    }

    public void GuardarOrdem(List<Disp> l)
    {
        var novos = new List<string>();
        foreach (var d in l) novos.Add(d.Id);
        foreach (var o in Ordem) if (!novos.Exists(x => string.Equals(x, o, StringComparison.OrdinalIgnoreCase))) novos.Add(o);
        Ordem.Clear(); Ordem.AddRange(novos);
        SalvarConfig();
    }

    // Fixar o icone na barra (fora do "^"): mesma preferencia de Configuracoes > Barra de tarefas
    public static void FixarIcone(bool sim)
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings", true))
            {
                if (k == null) return;
                string exe = "\\" + Path.GetFileName(Application.ExecutablePath);
                foreach (var n in k.GetSubKeyNames())
                    using (var s = k.OpenSubKey(n, true))
                    {
                        var p = s == null ? null : s.GetValue("ExecutablePath") as string;
                        if (p != null && p.EndsWith(exe, StringComparison.OrdinalIgnoreCase))
                            s.SetValue("IsPromoted", sim ? 1 : 0, RegistryValueKind.DWord);
                    }
            }
        }
        catch { }
    }

    // O Windows so le essa preferencia quando o icone entra na bandeja: tira e poe de novo
    public void AplicarFixar(bool sim)
    {
        FixarBandeja = sim;
        FixarIcone(sim);
        try
        {
            icone.Visible = false;
            var t = new Timer { Interval = 1500 };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); icone.Visible = true; };
            t.Start();
        }
        catch { }
        SalvarConfig();
    }

    public string TextoAtalhoGeral(int k) { return TextoAtalho(AtMod[k], AtVk[k]) + (atOk[k] || AtVk[k] == 0 ? "" : " (em uso)"); }

    public void AtalhoGeral(int k)
    {
        uint m = AtMod[k], v = AtVk[k];
        if (EscolherAtalho("Atalho: " + AtNome[k].ToLowerInvariant(), ref m, ref v, k + 1)) { AtMod[k] = m; AtVk[k] = v; SalvarConfig(); }
        RegistrarAtalhos();
    }

    public void AtalhoPerfil(Perfil p)
    {
        uint m = p.Mod, v = p.Vk;
        if (EscolherAtalho("Atalho do perfil " + p.Nome, ref m, ref v, 100 + Perfis.IndexOf(p))) { p.Mod = m; p.Vk = v; SalvarConfig(); }
        RegistrarAtalhos();
    }

    public void AtualizarPerfil(Perfil p)
    {
        var n = Instantaneo(p.Nome);
        p.Saida = n.Saida; p.Entrada = n.Entrada; p.Regras = n.Regras;
        p.Volumes = n.Volumes; p.Mudos = n.Mudos; p.AppVol = n.AppVol; p.AppMudo = n.AppMudo;
        p.Ocultos = n.Ocultos; p.Ordem = n.Ordem; p.Abertos = n.Abertos;
        PerfilAtivo = p.Nome; SalvarConfig(); AtualizarIcone();
    }

    public void RenomearPerfil(Perfil p)
    {
        string n = PedirTexto("Renomear perfil", "Nome que aparece no painel.", p.Nome);
        if (n == null) return;
        RenomearPerfilPara(p, n);
    }

    public void RenomearPerfilPara(Perfil p, string n)
    {
        if (PerfilAtivo == p.Nome) PerfilAtivo = n;
        p.Nome = n; SalvarConfig(); AtualizarIcone();
    }

    public void MoverPerfil(Perfil p, int para)
    {
        if (!Perfis.Remove(p)) return;
        Perfis.Insert(Math.Max(0, Math.Min(Perfis.Count, para)), p);
        SalvarConfig();
        RegistrarAtalhos(); // os atalhos seguem cada perfil
    }

    public void ExcluirPerfil(Perfil p) { ExcluirPerfil(p, true); }

    public void ExcluirPerfil(Perfil p, bool perguntar)
    {
        if (perguntar && MessageBox.Show("Excluir o perfil \"" + p.Nome + "\"?", "Sintonia", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Perfis.Remove(p);
        if (PerfilAtivo == p.Nome) PerfilAtivo = null;
        SalvarConfig(); RegistrarAtalhos(); AtualizarIcone();
    }

    public List<string> AppsComSom(Perfil p)
    {
        var l = new List<string>(p.Gatilhos);
        foreach (var se in Audio.Sessoes()) if (!l.Contains(se.Value)) l.Add(se.Value);
        l.Sort();
        return l;
    }

    public void RenomearDispositivo(string id, string orig, string atual)
    {
        string n = PedirTexto("Renomear dispositivo", "Original: " + orig, atual);
        if (n == null) return;
        if (n == orig) Nomes.Remove(id); else Nomes[id] = n;
        ultimoId = null;
        SalvarConfig(); AtualizarIcone();
    }

    public void NomeOriginal(string id) { Nomes.Remove(id); ultimoId = null; SalvarConfig(); AtualizarIcone(); }

    public void LimparSaidas()
    {
        if (MessageBox.Show("Todos os apps voltam a usar a saída padrão do Windows e as saídas lembradas são esquecidas.\n\nContinuar?",
            "Sintonia", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Politica.LimparTudo();
        Regras.Clear();
        SalvarConfig();
    }

    void BeginPainel()
    {
        // espera o menu fechar para o painel pegar o foco
        Postar(() => painel.Mostrar());
    }

    void Sair()
    {
        SalvarConfig();
        vigia.Parar();
        icone.Visible = false;
        Application.Exit();
    }

    // ---------------- tema ----------------
    public bool TemaEscuro()
    {
        if (tema == "claro") return false;
        if (tema == "escuro") return true;
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                if (v is int) return (int)v == 0;
            }
        }
        catch { }
        return false;
    }

    public static Color CorDeDestaque(bool escuro)
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
            {
                object v = k == null ? null : k.GetValue("AccentColor"); // ABGR
                if (v is int)
                {
                    int c = (int)v;
                    var cor = Color.FromArgb(c & 0xFF, (c >> 8) & 0xFF, (c >> 16) & 0xFF);
                    if (escuro)
                        cor = Color.FromArgb(Math.Min(255, cor.R + (255 - cor.R) / 2), Math.Min(255, cor.G + (255 - cor.G) / 2), Math.Min(255, cor.B + (255 - cor.B) / 2));
                    return cor;
                }
            }
        }
        catch { }
        return escuro ? Color.FromArgb(76, 194, 255) : Color.FromArgb(0, 103, 192);
    }

    public static Color TextoSobre(Color c)
    {
        return (c.R * 0.299 + c.G * 0.587 + c.B * 0.114) > 150 ? Color.Black : Color.White;
    }

    // Area livre do monitor, sem a barra de tarefas. Quando a barra se esconde sozinha o
    // Windows diz que a tela toda esta livre; aqui o espaco dela e reservado do mesmo jeito.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr pai, IntPtr depois, string classe, string nome);
    [DllImport("shell32.dll")] static extern uint SHAppBarMessage(uint msg, ref APPBARDATA d);
    [StructLayout(LayoutKind.Sequential)]
    struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }

    public static Rectangle AreaUtil(Screen tela, out int borda)
    {
        var b = tela.Bounds;
        var area = tela.WorkingArea;
        borda = 0;
        // acha a barra de tarefas que esta neste monitor (principal ou das outras telas)
        Rectangle barra = Rectangle.Empty;
        long melhor = 0;
        foreach (var cls in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
        {
            IntPtr h = IntPtr.Zero;
            while ((h = FindWindowEx(IntPtr.Zero, h, cls, null)) != IntPtr.Zero)
            {
                RECT r;
                if (!GetWindowRect(h, out r)) continue;
                var rr = Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
                var inter = Rectangle.Intersect(rr, b);
                long ar = (long)inter.Width * inter.Height;
                // barra escondida deixa so 2 px dentro da tela; vale se ocupa a largura/altura da tela
                bool encosta = inter.Width > 0 && inter.Height > 0 &&
                               ((rr.Width >= b.Width * 0.9 && Math.Abs(rr.Left - b.Left) < 4) || (rr.Height >= b.Height * 0.9 && Math.Abs(rr.Top - b.Top) < 4));
                if (encosta && ar > melhor) { melhor = ar; barra = rr; }
            }
        }
        if (barra == Rectangle.Empty)
        {
            // sem barra neste monitor: usa o que o Windows informa
            if (area.Top > b.Top) borda = 1; else if (area.Left > b.Left) borda = 2; else if (area.Right < b.Right) borda = 3;
            return area;
        }
        bool horizontal = barra.Width >= barra.Height;
        if (horizontal) borda = barra.Top + barra.Height / 2 > b.Top + b.Height / 2 ? 0 : 1;
        else borda = barra.Left + barra.Width / 2 > b.Left + b.Width / 2 ? 3 : 2;
        int esp = Math.Min(horizontal ? barra.Height : barra.Width, (horizontal ? b.Height : b.Width) / 3);

        var abd = new APPBARDATA { cbSize = Marshal.SizeOf(typeof(APPBARDATA)) };
        bool autoEsconde = false;
        try { autoEsconde = (SHAppBarMessage(4 /*ABM_GETSTATE*/, ref abd) & 1 /*ABS_AUTOHIDE*/) != 0; } catch { }
        // barra fixa: o Windows ja desconta; barra que se esconde: desconta aqui
        if (autoEsconde || area == b)
        {
            if (borda == 0) area = Rectangle.FromLTRB(area.Left, area.Top, area.Right, Math.Min(area.Bottom, b.Bottom - esp));
            else if (borda == 1) area = Rectangle.FromLTRB(area.Left, Math.Max(area.Top, b.Top + esp), area.Right, area.Bottom);
            else if (borda == 2) area = Rectangle.FromLTRB(Math.Max(area.Left, b.Left + esp), area.Top, area.Right, area.Bottom);
            else area = Rectangle.FromLTRB(area.Left, area.Top, Math.Min(area.Right, b.Right - esp), area.Bottom);
        }
        return area;
    }

    public static float EscalaEm(Point p)
    {
        try
        {
            uint dx, dy;
            if (GetDpiForMonitor(MonitorFromPoint(p, 2), 0, out dx, out dy) == 0) return dx / 96f;
        }
        catch { }
        return 1f;
    }

    public void AplicarTema(ContextMenuStrip m)
    {
        var r = new TemaMenu(TemaEscuro());
        ToolStripManager.Renderer = r;
        m.Renderer = r;
        float esc = EscalaEm(Cursor.Position);
        var fonte = new Font("Segoe UI", 9.5f * esc);
        m.Font = fonte;
        m.ImageScalingSize = new Size((int)(16 * esc), (int)(16 * esc));
        Arredondar(m);
        AplicarFonte(m.Items, fonte, esc, r);
    }

    void AplicarFonte(ToolStripItemCollection itens, Font fonte, float esc, TemaMenu r)
    {
        foreach (ToolStripItem it in itens)
        {
            it.Font = (it.Tag as string) == "titulo" ? new Font("Segoe UI Semibold", 10.5f * esc) : fonte;
            if (!(it is ToolStripSeparator)) it.Padding = new Padding(0, (int)(2 * esc), 0, (int)(2 * esc));
            var d = it as ToolStripMenuItem;
            if (d != null && d.HasDropDownItems)
            {
                d.DropDown.Renderer = r;
                d.DropDown.Font = fonte;
                d.DropDown.ImageScalingSize = new Size((int)(16 * esc), (int)(16 * esc));
                var dd = d.DropDown as ToolStripDropDownMenu;
                if (dd != null) { dd.ShowImageMargin = false; dd.ShowCheckMargin = true; }
                Arredondar(d.DropDown);
                AplicarFonte(d.DropDownItems, fonte, esc, r);
            }
        }
    }

    static void Arredondar(ToolStripDropDown dd)
    {
        if ((dd.Tag as string) == "arredondado") return;
        dd.Tag = "arredondado";
        dd.Opened += (s, e) =>
        {
            int pref = 2;
            try { DwmSetWindowAttribute(dd.Handle, 33, ref pref, 4); } catch { }
        };
    }

    // ---------------- configuracao ----------------
    void LerConfig()
    {
        try
        {
            if (!File.Exists(arqConfig)) return;
            foreach (var l in File.ReadAllLines(arqConfig))
            {
                var p = l.Split(new[] { '=' }, 2);
                if (p.Length != 2) continue;
                string k = p[0], v = p[1];
                switch (k)
                {
                    case "lembrar": break; // sempre ligado: o app sempre garante a saida escolhida
                    case "perfis_auto": PerfisAuto = v == "1"; break;
                    case "voltar_perfil": VoltarPerfil = v == "1"; break;
                    case "roda": RodaIcone = v == "1"; break;
                    case "mics": Mics = v == "1"; break;
                    case "inativos": Inativos = v == "1"; break;
                    case "perfis": UsarPerfis = v == "1"; break;
                    case "aviso": AvisoTela = v == "1"; break;
                    case "roda_painel": RodaPainel = v == "1"; break;
                    case "animacoes": Animacoes = v == "1"; break;
                    case "menu_lateral": MenuLateralAberto = v == "1"; break;
                    case "adaptador": MostrarAdaptador = v == "1"; break;
                    case "logo": MostrarLogo = v == "1"; break;
                    case "som": MostrarSom = v == "1"; break;
                    case "fixar": FixarBandeja = v == "1"; break;
                    case "ordem": Ordem.Clear(); foreach (var o in v.Split(';')) if (o.Length > 0) Ordem.Add(o); break;
                    case "aberto": { var a = v.Split('>'); if (a.Length == 2) Abertos[a[0]] = a[1] == "1"; } break;
                    case "passo": int.TryParse(v, out Passo); if (Passo < 1 || Passo > 25) Passo = 2; break;
                    case "altura_max": int.TryParse(v, out AlturaMax); if (AlturaMax < 30 || AlturaMax > 100) AlturaMax = 90; break;
                    case "tema": if (v == "auto" || v == "claro" || v == "escuro") tema = v; break;
                    case "perfil_ativo": PerfilAtivo = v.Length == 0 ? null : v; break;
                    case "regra": { var a = v.Split('>'); if (a.Length == 2) Regras[a[0]] = a[1]; } break;
                    case "nome": { var a = v.Split('>'); if (a.Length == 2) Nomes[a[0]] = a[1]; } break;
                    case "oculto": Ocultos.Add(v); break;
                    case "perfil": { var pf = LerPerfil(v); if (pf != null) Perfis.Add(pf); } break;
                    default:
                        if (k.StartsWith("atalho") && k.Length == 7)
                        {
                            int i = k[6] - '0';
                            var a = v.Split(',');
                            uint m, vk;
                            if (i >= 0 && i < 3 && a.Length == 2 && uint.TryParse(a[0], out m) && uint.TryParse(a[1], out vk)) { AtMod[i] = m; AtVk[i] = vk; }
                        }
                        break;
                }
            }
            if (PerfilAtivo != null && Perfis.Find(x => x.Nome == PerfilAtivo) == null) PerfilAtivo = null;
            // padrao antigo do mudo (Win+Alt+M) e ocupado por outro programa: passa para Win+Alt+S
            if (AtMod[2] == (0x8 | 0x1) && AtVk[2] == (uint)Keys.M) AtVk[2] = (uint)Keys.S;
        }
        catch { }
    }

    static Perfil LerPerfil(string v)
    {
        var a = v.Split('|');
        if (a.Length < 6 || a[0].Length == 0) return null;
        var p = new Perfil { Nome = a[0], Saida = a[1].Length == 0 ? null : a[1], Entrada = a[2].Length == 0 ? null : a[2] };
        var at = a[3].Split(',');
        uint m, vk;
        if (at.Length == 2 && uint.TryParse(at[0], out m) && uint.TryParse(at[1], out vk)) { p.Mod = m; p.Vk = vk; }
        foreach (var g in a[4].Split(';')) if (g.Length > 0) p.Gatilhos.Add(g);
        foreach (var r in a[5].Split(';'))
        {
            var x = r.Split('>');
            if (x.Length == 2) p.Regras[x[0]] = x[1];
        }
        // campos novos ("~" = nao guardado)
        if (a.Length > 6 && a[6] != "~") LerVolumes(a[6], out p.Volumes, out p.Mudos);
        if (a.Length > 7 && a[7] != "~") LerVolumes(a[7], out p.AppVol, out p.AppMudo);
        if (a.Length > 8 && a[8] != "~") { p.Ocultos = new List<string>(); foreach (var o in a[8].Split(';')) if (o.Length > 0) p.Ocultos.Add(o); }
        if (a.Length > 9 && a[9] != "~") { p.Ordem = new List<string>(); foreach (var o in a[9].Split(';')) if (o.Length > 0) p.Ordem.Add(o); }
        if (a.Length > 10 && a[10] != "~")
        {
            p.Abertos = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in a[10].Split(';')) { var x = r.Split('>'); if (x.Length == 2) p.Abertos[x[0]] = x[1] == "1"; }
        }
        return p;
    }

    static List<string> AbertosTexto(Dictionary<string, bool> d)
    {
        var l = new List<string>();
        foreach (var kv in d) l.Add(kv.Key + ">" + (kv.Value ? 1 : 0));
        return l;
    }

    static void LerVolumes(string v, out Dictionary<string, float> vols, out Dictionary<string, bool> mudos)
    {
        vols = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        mudos = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in v.Split(';'))
        {
            var x = r.Split('>');
            int n;
            if (x.Length == 3 && int.TryParse(x[1], out n)) { vols[x[0]] = n / 100f; mudos[x[0]] = x[2] == "1"; }
        }
    }

    static string GravarVolumes(Dictionary<string, float> vols, Dictionary<string, bool> mudos)
    {
        if (vols == null) return "~";
        var l = new List<string>();
        foreach (var kv in vols)
        {
            bool m; mudos.TryGetValue(kv.Key, out m);
            l.Add(kv.Key + ">" + (int)Math.Round(kv.Value * 100) + ">" + (m ? 1 : 0));
        }
        return string.Join(";", l);
    }

    public void SalvarConfig()
    {
        try
        {
            var l = new List<string> {
                "lembrar=" + (Lembrar ? 1 : 0), "perfis_auto=" + (PerfisAuto ? 1 : 0), "voltar_perfil=" + (VoltarPerfil ? 1 : 0),
                "roda=" + (RodaIcone ? 1 : 0), "mics=" + (Mics ? 1 : 0), "inativos=" + (Inativos ? 1 : 0),
                "perfis=" + (UsarPerfis ? 1 : 0), "aviso=" + (AvisoTela ? 1 : 0), "roda_painel=" + (RodaPainel ? 1 : 0),
                "animacoes=" + (Animacoes ? 1 : 0), "menu_lateral=" + (MenuLateralAberto ? 1 : 0), "adaptador=" + (MostrarAdaptador ? 1 : 0), "logo=" + (MostrarLogo ? 1 : 0), "som=" + (MostrarSom ? 1 : 0), "fixar=" + (FixarBandeja ? 1 : 0), "ordem=" + string.Join(";", Ordem),
                "passo=" + Passo, "altura_max=" + AlturaMax, "tema=" + tema, "perfil_ativo=" + (PerfilAtivo ?? "") };
            for (int i = 0; i < 3; i++) l.Add("atalho" + i + "=" + AtMod[i] + "," + AtVk[i]);
            foreach (var kv in Regras) l.Add("regra=" + kv.Key + ">" + kv.Value);
            foreach (var kv in Nomes) l.Add("nome=" + kv.Key + ">" + kv.Value);
            foreach (var o in Ocultos) l.Add("oculto=" + o);
            foreach (var kv in Abertos) l.Add("aberto=" + kv.Key + ">" + (kv.Value ? 1 : 0));
            foreach (var p in Perfis)
            {
                var regras = new List<string>();
                foreach (var kv in p.Regras) regras.Add(kv.Key + ">" + kv.Value);
                l.Add("perfil=" + p.Nome + "|" + (p.Saida ?? "") + "|" + (p.Entrada ?? "") + "|" + p.Mod + "," + p.Vk + "|" +
                      string.Join(";", p.Gatilhos) + "|" + string.Join(";", regras) + "|" +
                      GravarVolumes(p.Volumes, p.Mudos) + "|" + GravarVolumes(p.AppVol, p.AppMudo) + "|" +
                      (p.Ocultos == null ? "~" : string.Join(";", p.Ocultos)) + "|" +
                      (p.Ordem == null ? "~" : string.Join(";", p.Ordem)) + "|" +
                      (p.Abertos == null ? "~" : string.Join(";", AbertosTexto(p.Abertos))));
            }
            File.WriteAllLines(arqConfig, l);
        }
        catch { }
    }

    // ---------------- atalho no menu Iniciar ----------------
    static string CaminhoAtalhoIniciar()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Sintonia.lnk");
    }
    public static bool TemAtalhoIniciar() { return File.Exists(CaminhoAtalhoIniciar()); }
    public static void AlternarAtalhoIniciar()
    {
        string lnk = CaminhoAtalhoIniciar();
        try
        {
            if (File.Exists(lnk)) { File.Delete(lnk); return; }
            var tipo = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(tipo);
            object at = tipo.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            var ta = at.GetType();
            string exe = Application.ExecutablePath;
            ta.InvokeMember("TargetPath", BindingFlags.SetProperty, null, at, new object[] { exe });
            ta.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, at, new object[] { Path.GetDirectoryName(exe) });
            ta.InvokeMember("Description", BindingFlags.SetProperty, null, at, new object[] { "Saídas de som, volume por app e perfis" });
            ta.InvokeMember("IconLocation", BindingFlags.SetProperty, null, at, new object[] { exe + ",0" });
            ta.InvokeMember("Save", BindingFlags.InvokeMethod, null, at, null);
            Marshal.FinalReleaseComObject(at);
            Marshal.FinalReleaseComObject(shell);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Não consegui criar o atalho no menu Iniciar:\n" + ex.Message, "Sintonia", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ---------------- iniciar com o Windows (Agendador de Tarefas) ----------------
    const string NomeTarefa = "Sintonia";
    bool? inicioCache;

    // Sintonia.exe --testeopcoes arq: roda de verdade as acoes que so existem por clique
    // (iniciar com o Windows, atalho no menu Iniciar, fixar icone, voltar apps ao padrao),
    // confere o resultado e desfaz cada uma
    public static void TesteOpcoes(string arq)
    {
        var l = new List<string>();
        // iniciar com o Windows
        bool tarefa0 = TarefaExiste();
        Schtasks("/delete /tn \"" + NomeTarefa + "\" /f");
        bool semTarefa = !TarefaExiste();
        bool criou = CriarTarefa() && TarefaExiste();
        if (!tarefa0) Schtasks("/delete /tn \"" + NomeTarefa + "\" /f");
        l.Add("Iniciar com o Windows: desligar=" + (semTarefa ? "ok" : "FALHOU") + " | ligar=" + (criou ? "ok" : "FALHOU") +
              " | no fim: " + (TarefaExiste() ? "ligado" : "desligado") + " (antes: " + (tarefa0 ? "ligado" : "desligado") + ")");

        // atalho no menu Iniciar
        bool lnk0 = TemAtalhoIniciar();
        AlternarAtalhoIniciar(); bool lnk1 = TemAtalhoIniciar();
        AlternarAtalhoIniciar(); bool lnk2 = TemAtalhoIniciar();
        l.Add("Atalho no menu Iniciar: " + lnk0 + " -> " + lnk1 + " -> " + lnk2 + (lnk1 != lnk0 && lnk2 == lnk0 ? " (ok)" : " (FALHOU)"));

        // fixar o icone
        Func<string> promovido = () =>
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings"))
                    foreach (var n in k.GetSubKeyNames())
                        using (var sk = k.OpenSubKey(n))
                        {
                            var pth = sk.GetValue("ExecutablePath") as string;
                            if (pth != null && pth.EndsWith("\\Sintonia.exe", StringComparison.OrdinalIgnoreCase)) return Convert.ToString(sk.GetValue("IsPromoted"));
                        }
            }
            catch { }
            return "?";
        };
        string f0 = promovido();
        FixarIcone(false); string f1 = promovido();
        FixarIcone(true); string f2 = promovido();
        if (f0 != "1") FixarIcone(false);
        l.Add("Fixar o ícone (IsPromoted): " + f0 + " -> desafixar " + f1 + " -> fixar " + f2 + ((f1 == "0" && f2 == "1") ? " (ok)" : " (FALHOU)"));

        // voltar todos os apps para a saida padrao (e restaurar)
        var antes = new List<KeyValuePair<uint, string>>();
        foreach (var se in Audio.Sessoes())
        {
            string id = Politica.Obter(se.Key);
            if (id != null && !antes.Exists(x => x.Key == se.Key)) antes.Add(new KeyValuePair<uint, string>(se.Key, id));
        }
        bool limpou = Politica.LimparTudo();
        int ainda = 0;
        foreach (var kv in antes) if (Politica.Obter(kv.Key) != null) ainda++;
        foreach (var kv in antes) Politica.Definir(kv.Key, kv.Value);
        int voltou = 0;
        foreach (var kv in antes) if (Politica.Igual(Politica.Obter(kv.Key), kv.Value)) voltou++;
        l.Add("Voltar todos os apps ao padrão: apps com saída escolhida=" + antes.Count + " | limpou=" + limpou +
              " | ainda com escolha=" + ainda + (ainda == 0 ? " (ok)" : " (FALHOU)") + " | restaurados=" + voltou + "/" + antes.Count);
        File.WriteAllLines(arq, l);
    }

    static int Schtasks(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
                return p.ExitCode;
            }
        }
        catch { return -1; }
    }

    static bool TarefaExiste() { return Schtasks("/query /tn \"" + NomeTarefa + "\"") == 0; }

    static bool CriarTarefa()
    {
        string usuario = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
        string exe = Application.ExecutablePath;
        Func<string, string> X = System.Security.SecurityElement.Escape;
        string xml =
            "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
            "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
            "  <RegistrationInfo><Description>Abre o Sintonia (controle de som) ao entrar no Windows.</Description></RegistrationInfo>\r\n" +
            "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + X(usuario) + "</UserId><Delay>PT3S</Delay></LogonTrigger></Triggers>\r\n" +
            "  <Principals><Principal id=\"Author\"><UserId>" + X(usuario) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>\r\n" +
            "  <Settings>\r\n" +
            "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
            "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
            "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
            "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
            "    <Priority>7</Priority>\r\n" +
            "  </Settings>\r\n" +
            "  <Actions Context=\"Author\"><Exec><Command>" + X(exe) + "</Command><WorkingDirectory>" + X(Path.GetDirectoryName(exe)) + "</WorkingDirectory></Exec></Actions>\r\n" +
            "</Task>\r\n";
        string tmp = Path.Combine(Path.GetTempPath(), "Sintonia_tarefa.xml");
        try
        {
            File.WriteAllText(tmp, xml, System.Text.Encoding.Unicode);
            return Schtasks("/create /tn \"" + NomeTarefa + "\" /xml \"" + tmp + "\" /f") == 0;
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    void ConferirInicio()
    {
        // tarefa do nome antigo ("Som (bandeja)"): troca pela nova
        if (Schtasks("/query /tn \"Som (bandeja)\"") == 0)
        {
            Schtasks("/delete /tn \"Som (bandeja)\" /f");
            inicioCache = CriarTarefa();
            return;
        }
        bool existe = TarefaExiste();
        if (existe) CriarTarefa(); // recria com o caminho atual do .exe
        inicioCache = existe;
    }

    public bool IniciaComWindows()
    {
        if (inicioCache == null) inicioCache = TarefaExiste();
        return inicioCache.Value;
    }

    public void AlternarInicio()
    {
        if (IniciaComWindows()) Schtasks("/delete /tn \"" + NomeTarefa + "\" /f");
        else if (!CriarTarefa())
            MessageBox.Show("Não consegui criar a tarefa de inicialização no Agendador de Tarefas.", "Sintonia", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        inicioCache = null;
    }
}

// =====================================================================
//  Janela de Configuracoes (estilo Configuracoes do Windows 11)
// =====================================================================
class Configuracoes : Form
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint f);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    readonly App app;
    float esc = 1f;
    bool escuro;
    Color cFundo, cCartao, cHover, cBorda, cTexto, cFraco, cAcento, cTextoAcento;
    Font fTitulo, fNormal, fPequena, fCab, fGlifo, fGlifoP;
    int pagina, rolagem, alturaConteudo;
    float navAtual = -1f;                       // 0 = barra lateral fechada (so icones), 1 = aberta
    readonly Timer tNav = new Timer { Interval = 15 };
    static readonly string[] Paginas = { "Geral", "Perfis", "Atalhos", "Dispositivos" };
    static readonly char[] GlifosPag = { '', '', '', '' };
    readonly List<Linha> linhas = new List<Linha>();
    readonly List<Zona> zonas = new List<Zona>();
    Zona sobre;

    class Linha
    {
        public int Tipo;
        public string Titulo, Desc;
        public char Glifo;
        public Func<bool> Ler;
        public Action<bool> Gravar;
        public Func<string> Valor;
        public Action<Rectangle> Clicar;
    }
    class Zona { public Rectangle R; public int Pagina = -1; public Linha L; }
    const int L_CAB = 0, L_CHAVE = 1, L_BOTAO = 2, L_NOTA = 3, L_GRUPO = 4;
    // perfis abertos na pagina Perfis (comeca so com o ativo aberto)
    readonly HashSet<Perfil> perfisAbertos = new HashSet<Perfil>();
    bool perfisIniciados;

    int E(float v) { return (int)Math.Round(v * esc); }

    public void IrPara(int p)
    {
        if (p < 0 || p >= Paginas.Length) return;
        pagina = p; rolagem = 0; Montar(); Invalidate();
    }

    public Configuracoes(App a)
    {
        app = a;
        Text = "Configurações do Sintonia";
        using (var b = Desenho.Logo(32)) Icon = Desenho.IconeDe(b);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        var p = Cursor.Position;
        esc = App.EscalaEm(p);
        Preparar();
        var area = Screen.FromPoint(p).WorkingArea;
        int w = Math.Min(area.Width - E(40), E(880)), h = Math.Min(area.Height - E(40), E(660));
        Bounds = new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
        MinimumSize = new Size(E(640), E(420));
        Montar();
    }

    void Preparar()
    {
        escuro = app.TemaEscuro();
        cFundo = escuro ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);
        cCartao = escuro ? Color.FromArgb(43, 43, 43) : Color.FromArgb(251, 251, 251);
        cHover = escuro ? Color.FromArgb(50, 50, 50) : Color.FromArgb(246, 246, 246);
        cBorda = escuro ? Color.FromArgb(29, 29, 29) : Color.FromArgb(229, 229, 229);
        cTexto = escuro ? Color.FromArgb(242, 242, 242) : Color.FromArgb(27, 27, 27);
        cFraco = escuro ? Color.FromArgb(160, 160, 160) : Color.FromArgb(96, 96, 96);
        cAcento = App.CorDeDestaque(escuro);
        cTextoAcento = App.TextoSobre(cAcento);
        if (fTitulo != null) foreach (var f in new[] { fTitulo, fNormal, fPequena, fCab, fGlifo, fGlifoP }) f.Dispose();
        fTitulo = new Font("Segoe UI Semibold", E(28), GraphicsUnit.Pixel);
        fNormal = new Font("Segoe UI", E(14), GraphicsUnit.Pixel);
        fPequena = new Font("Segoe UI", E(12), GraphicsUnit.Pixel);
        fCab = new Font("Segoe UI Semibold", E(14), GraphicsUnit.Pixel);
        fGlifo = new Font(Desenho.FonteIcones, E(16), GraphicsUnit.Pixel);
        fGlifoP = new Font(Desenho.FonteIcones, E(12), GraphicsUnit.Pixel);
        BackColor = cFundo;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int sim = escuro ? 1 : 0, round = 2;
        try { DwmSetWindowAttribute(Handle, 20, ref sim, 4); } catch { }
        try { DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x02E0 /*WM_DPICHANGED*/)
        {
            esc = (m.WParam.ToInt32() & 0xFFFF) / 96f;
            var r = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
            Preparar();
            SetWindowPos(Handle, IntPtr.Zero, r.L, r.T, r.R - r.L, r.B - r.T, 0x0014);
            Invalidate();
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys k)
    {
        if (k == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, k);
    }

    // ---------------- conteudo de cada pagina ----------------
    void Cab(string t) { linhas.Add(new Linha { Tipo = L_CAB, Titulo = t }); }
    // titulo de grupo que abre/fecha (seta a direita)
    void Grupo(string t, bool aberto, Action alternar)
    {
        linhas.Add(new Linha { Tipo = L_GRUPO, Titulo = t, Ler = () => aberto, Clicar = r => alternar() });
    }
    void Nota(string t) { linhas.Add(new Linha { Tipo = L_NOTA, Titulo = t }); }
    void Chave(string t, string d, Func<bool> ler, Action<bool> gravar)
    {
        linhas.Add(new Linha { Tipo = L_CHAVE, Titulo = t, Desc = d, Ler = ler, Gravar = gravar });
    }
    void Botao(string t, string d, Func<string> valor, Action<Rectangle> clicar, char glifo = '\0')
    {
        linhas.Add(new Linha { Tipo = L_BOTAO, Titulo = t, Desc = d, Valor = valor, Clicar = clicar, Glifo = glifo });
    }

    ContextMenuStrip NovoMenu()
    {
        return new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };
    }

    void MostrarMenu(ContextMenuStrip m, Rectangle r)
    {
        string chave = "cfg|" + pagina + "|" + (r.Y + rolagem);
        if (Alterna.FecharSeAberto(chave) || !Alterna.PodeAbrir(chave)) { m.Dispose(); return; } // o mesmo clique que fechou
        app.AplicarTema(m);
        m.Closed += (s, e) => { Alterna.Fechou(chave); Montar(); Invalidate(); };
        Alterna.Abriu(chave, m);
        m.Show(this, new Point(r.Right - E(12), r.Bottom - E(4)), ToolStripDropDownDirection.BelowLeft);
    }

    void Montar()
    {
        linhas.Clear();
        const char EXT = '';
        if (pagina == 0)
        {
            Cab("Painel");
            Chave("Mostrar microfones", "Mostra os microfones no painel, abaixo das saídas", () => app.Mics, v => app.Mics = v);
            Chave("Mostrar apps em silêncio", "Mostra também os apps abertos que não estão tocando som agora", () => app.Inativos, v => app.Inativos = v);
            Chave("Mostrar o nome do adaptador", "Ex.: \"AMD High Definition Audio Device\" ao lado do nome de cada saída", () => app.MostrarAdaptador, v => app.MostrarAdaptador = v);
            Chave("Mostrar o logo no topo do painel", "O ícone roxo do Sintonia ao lado do nome, no cabeçalho do painel", () => app.MostrarLogo, v => app.MostrarLogo = v);
            Chave("Mostrar o som tocando", "Barrinhas animadas ao lado de cada saída e app, conforme o som que está tocando. Só funciona com o painel aberto",
                () => app.MostrarSom, v => app.DefinirSom(v));
            Botao("Altura máxima do painel", "Até onde o painel cresce. Passando disso, a lista rola com a roda do mouse",
                () => app.AlturaMax == 100 ? "Tela toda" : app.AlturaMax + "% da tela", r =>
            {
                var m = NovoMenu();
                foreach (var n in new[] { 50, 60, 70, 80, 90, 100 })
                {
                    int v = n;
                    var it = new ToolStripMenuItem(v == 100 ? "Tela toda" : v + "% da tela") { Checked = app.AlturaMax == v };
                    it.Click += (s, e) => { app.AlturaMax = v; app.SalvarConfig(); };
                    m.Items.Add(it);
                }
                MostrarMenu(m, r);
            });
            Chave("Animações suaves", "O painel desliza ao abrir e fechar, e as saídas abrem e fecham devagar", () => app.Animacoes, v => app.Animacoes = v);
            Chave("Roda do mouse nas barras de volume", "Com o mouse sobre uma barra de volume, a roda aumenta ou diminui. Desligado, a roda só rola a lista", () => app.RodaPainel, v => app.RodaPainel = v);

            Botao("Passo do volume", "Quanto o volume muda a cada giro da roda do mouse", () => app.Passo + "%", r =>
            {
                var m = NovoMenu();
                foreach (var n in new[] { 1, 2, 5, 10 })
                {
                    int v = n;
                    var it = new ToolStripMenuItem(v + "%") { Checked = app.Passo == v };
                    it.Click += (s, e) => { app.Passo = v; app.SalvarConfig(); };
                    m.Items.Add(it);
                }
                MostrarMenu(m, r);
            });
            Cab("Ícone na bandeja");
            Chave("Fixar o ícone na barra de tarefas", "Deixa o ícone sempre visível ao lado do relógio, em vez de escondido no ^", () => app.FixarBandeja, v => app.AplicarFixar(v));
            Botao("Ícones da bandeja no Windows", "Abre a tela do Windows onde dá para fixar o ícone manualmente", () => "",
                r => Abrir("ms-settings:taskbar"), EXT);

            Cab("Avisos");
            Chave("Aviso na tela ao usar os atalhos", "Mostra por um instante qual saída, perfil ou mudo foi escolhido. Não aparece por cima de jogos em tela cheia",
                () => app.AvisoTela, v => app.AvisoTela = v);

            Cab("Aparência e início");
            Botao("Tema", "Claro, escuro ou igual ao Windows, no painel, nos menus e nesta janela", () => app.Tema == "claro" ? "Claro" : app.Tema == "escuro" ? "Escuro" : "Igual ao Windows", r =>
            {
                var m = NovoMenu();
                foreach (var op in new[] { "auto", "claro", "escuro" })
                {
                    string v = op;
                    var it = new ToolStripMenuItem(v == "auto" ? "Igual ao Windows" : v == "claro" ? "Claro" : "Escuro") { Checked = app.Tema == v };
                    it.Click += (s, e) => { app.Tema = v; Preparar(); OnHandleCreated(EventArgs.Empty); };
                    m.Items.Add(it);
                }
                MostrarMenu(m, r);
            });
            Chave("Iniciar com o Windows", "Abre o Sintonia sozinho quando você entra no Windows", () => app.IniciaComWindows(), v => app.AlternarInicio());
            Chave("Atalho no menu Iniciar", "Cria um atalho para achar o Sintonia pesquisando no menu Iniciar", () => App.TemAtalhoIniciar(), v => App.AlternarAtalhoIniciar());
        }
        else if (pagina == 1)
        {
            Chave("Usar perfis", "Botões no painel para trocar, de uma vez, as saídas, os volumes e o que aparece", () => app.UsarPerfis, v => app.DefinirPerfis(v));
            if (!app.UsarPerfis)
            {
                Nota("Com os perfis desligados, nada de perfil aparece no painel, no menu do ícone nem nos atalhos." +
                     (app.Perfis.Count > 0 ? " Os perfis que você já criou continuam guardados." : ""));
                return;
            }
            Nota("Um perfil guarda a saída padrão, o microfone, a saída e o volume de cada app, e como o painel está (escondidos, ordem, abertos). Troque pelos botões do painel, pelo menu do ícone ou por um atalho.");
            Botao("Novo perfil com a configuração de agora", "Até " + App.MaxPerfis + " perfis", () => app.Perfis.Count + " de " + App.MaxPerfis,
                r => app.NovoPerfil(), '\uE710');

            Cab("Troca automática");
            Chave("Ativar um perfil quando um app começar a tocar", "Ex.: ao abrir um jogo, troca para o perfil \"Jogo\". Escolha os apps em cada perfil abaixo", () => app.PerfisAuto, v => app.PerfisAuto = v);
            Chave("Voltar ao perfil anterior quando o app fechar", "Quando o app que trocou o perfil fecha, volta o perfil que estava antes", () => app.VoltarPerfil, v => app.VoltarPerfil = v);

            foreach (var pf in app.Perfis)
            {
                var p = pf;
                if (!perfisIniciados && p.Nome == app.PerfilAtivo) perfisAbertos.Add(p);
                bool ab = perfisAbertos.Contains(p);
                Grupo(p.Nome + (p.Nome == app.PerfilAtivo ? "  ·  ativo" : ""), ab,
                    () => { if (!perfisAbertos.Remove(p)) perfisAbertos.Add(p); });
                if (!ab) continue;
                Botao("Ativar agora", null, () => "", r => app.AplicarPerfil(p, false));
                Botao("Guardar a configuração de agora neste perfil", "Substitui o que este perfil guardou pelo que está no painel agora", () => "", r => app.AtualizarPerfil(p));
                if (app.PerfisAuto)
                    Botao("Ativar sozinho quando estes apps tocarem", "Apps que ativam este perfil ao começar a tocar som",
                        () => p.Gatilhos.Count == 0 ? "Nenhum" : string.Join(", ", p.Gatilhos), r =>
                    {
                        var m = NovoMenu();
                        var lista = app.AppsComSom(p);
                        if (lista.Count == 0) m.Items.Add(new ToolStripLabel("Abra o app (jogo, Discord…) e volte aqui") { Tag = "fraco" });
                        foreach (var c in lista)
                        {
                            string exe = c;
                            var it = new ToolStripMenuItem(exe) { Checked = p.Gatilhos.Contains(exe), CheckOnClick = false };
                            it.Click += (s, e) => { if (!p.Gatilhos.Remove(exe)) p.Gatilhos.Add(exe); it.Checked = p.Gatilhos.Contains(exe); app.SalvarConfig(); };
                            m.Items.Add(it);
                        }
                        m.Closing += (s, e) => { if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true; };
                        MostrarMenu(m, r);
                    });
                Botao("Atalho", "Atalho de teclado que ativa este perfil", () => App.TextoAtalho(p.Mod, p.Vk), r => app.AtalhoPerfil(p));
                Botao("Renomear", null, () => p.Nome, r => app.RenomearPerfil(p));
                Botao("Excluir perfil", "Apaga este perfil (pede confirmação antes)", () => "", r => app.ExcluirPerfil(p));
            }
            perfisIniciados = true;
        }
        else if (pagina == 2)
        {
            Nota("Os atalhos funcionam em qualquer lugar, até com um jogo aberto. Eles usam o recurso oficial do Windows para atalhos e não leem o que você digita.");
            Cab("Gerais");
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                Botao(App.AtNome[k], new[] { "Abre e fecha o painel do Sintonia", "Passa a saída padrão para a próxima da lista", "Liga e desliga o som da saída padrão" }[k], () => app.TextoAtalhoGeral(k), r => app.AtalhoGeral(k));
            }
            if (app.UsarPerfis && app.Perfis.Count > 0)
            {
                Cab("Perfis");
                foreach (var pf in app.Perfis)
                {
                    var p = pf;
                    Botao(p.Nome, null, () => App.TextoAtalho(p.Mod, p.Vk), r => app.AtalhoPerfil(p));
                }
            }
        }
        else
        {
            Cab("Saída de cada app");
            Botao("Voltar todos os apps para a saída padrão", "Todos os apps passam a tocar na saída padrão e as escolhas são esquecidas", () => "", r => { app.LimparSaidas(); app.PainelMudou(); });

            foreach (var fluxo in new[] { 0, 1 })
            {
                Cab(fluxo == 0 ? "Saídas" : "Microfones");
                if (fluxo == 0) Nota("Clique numa saída para dar um nome fácil (ex.: \"Fone\", \"Caixa\", \"Monitor\") ou escondê-la do painel.");
                var ds = Audio.Listar(fluxo, false, false, app.Nomes);
                foreach (var d in ds)
                {
                    string id = d.Id, orig = d.NomeOriginal, atual = d.Nome;
                    string desc = (atual != orig ? "Original: " + orig + " · " : "") + (d.Adaptador ?? "") + (d.Padrao ? " · padrão" : "");
                    Botao(atual, desc, () => app.Ocultos.Contains(id) ? "Escondido" : "", r =>
                    {
                        var m = NovoMenu();
                        var ren = new ToolStripMenuItem("Renomear…");
                        ren.Click += (s, e) => BeginInvoke(new Action(() => { app.RenomearDispositivo(id, orig, atual); Montar(); Invalidate(); }));
                        m.Items.Add(ren);
                        if (atual != orig)
                        {
                            var volta = new ToolStripMenuItem("Voltar ao nome original");
                            volta.Click += (s, e) => app.NomeOriginal(id);
                            m.Items.Add(volta);
                        }
                        var oc = new ToolStripMenuItem("Mostrar no painel e na troca rápida") { Checked = !app.Ocultos.Contains(id) };
                        oc.Click += (s, e) => { if (!app.Ocultos.Remove(id)) app.Ocultos.Add(id); app.SalvarConfig(); app.PainelMudou(); };
                        m.Items.Add(oc);
                        MostrarMenu(m, r);
                    }, Audio.Glifo(d));
                }
                if (ds.Count == 0) Nota(fluxo == 0 ? "Nenhuma saída ativa." : "Nenhum microfone ativo.");
                Audio.Liberar(ds);
            }

            Cab("Windows");
            Botao("Configurações de som do Windows", "Saídas, entradas e volume nas Configurações do Windows", () => "", r => Abrir("ms-settings:sound"), EXT);
            Botao("Mixer de volume do Windows", "Volume e saída de cada app na tela do Windows", () => "", r => Abrir("ms-settings:apps-volume"), EXT);
            Botao("Painel de som clássico", "A janela antiga de som do Windows (formatos, melhorias, etc.)", () => "", r => Abrir("mmsys.cpl"), EXT);
        }
    }

    static void Abrir(string cmd) { try { Process.Start(cmd); } catch { } }

    // ---------------- desenho ----------------
    const TextFormatFlags TF = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis |
                               TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
    const TextFormatFlags TC = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

    void Fundo(Graphics g, Rectangle r, Color c, int raio, Color? borda)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var p = Desenho.Arred(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), raio))
        {
            using (var b = new SolidBrush(c)) g.FillPath(b, p);
            if (borda.HasValue) using (var pn = new Pen(borda.Value)) g.DrawPath(pn, p);
        }
        g.SmoothingMode = SmoothingMode.None;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(cFundo);
        zonas.Clear();
        if (navAtual < 0) navAtual = app.MenuLateralAberto ? 1f : 0f;
        int navW = E(56) + (int)Math.Round((E(240) - E(56)) * navAtual);
        bool comNomes = navAtual > 0.5f;

        // Navegacao (esquerda): botao de abrir/fechar + paginas
        int y = E(12);
        var rMenu = new Rectangle(E(8), y, E(40), E(36));
        zonas.Add(new Zona { R = rMenu, Pagina = -2 });
        if (sobre != null && sobre.Pagina == -2) Fundo(g, rMenu, cHover, E(5), null);
        TextRenderer.DrawText(g, "\uE700", fGlifo, rMenu, cTexto, TC);
        if (comNomes)
        {
            using (var logo = Desenho.Logo(E(20))) g.DrawImage(logo, E(58), y + E(8));
            TextRenderer.DrawText(g, "Sintonia", fCab, new Rectangle(E(86), y, navW - E(90), E(36)), cTexto, TF);
        }
        y += E(52);
        for (int i = 0; i < Paginas.Length; i++)
        {
            var r = new Rectangle(E(8), y, navW - E(16), E(40));
            zonas.Add(new Zona { R = r, Pagina = i });
            bool sel = i == pagina, hov = sobre != null && sobre.Pagina == i;
            if (sel || hov) Fundo(g, r, sel ? (escuro ? Color.FromArgb(45, 45, 45) : Color.FromArgb(234, 234, 234)) : cHover, E(5), null);
            if (sel) Fundo(g, new Rectangle(r.X, r.Y + E(12), E(3), E(16)), cAcento, E(1), null);
            TextRenderer.DrawText(g, GlifosPag[i].ToString(), fGlifo, new Rectangle(r.X + E(8), r.Y, E(24), r.Height), cTexto, TC);
            if (comNomes && r.Width - E(56) > E(30))
                TextRenderer.DrawText(g, Paginas[i], fNormal, new Rectangle(r.X + E(44), r.Y, r.Width - E(52), r.Height), cTexto, TF);
            y += E(44);
        }

        // Conteudo (direita)
        int x0 = navW + E(16), w = ClientSize.Width - x0 - E(28);
        var reg = g.Clip;
        g.SetClip(new Rectangle(navW, 0, ClientSize.Width - navW, ClientSize.Height));
        y = E(20) - rolagem;
        TextRenderer.DrawText(g, Paginas[pagina], fTitulo, new Rectangle(x0, y, w, E(44)), cTexto, TF);
        y += E(60);
        foreach (var l in linhas)
        {
            if (l.Tipo == L_GRUPO)
            {
                y += E(10);
                var rg = new Rectangle(x0 - E(6), y, w + E(12), E(36));
                zonas.Add(new Zona { R = rg, L = l });
                if (sobre != null && sobre.L == l) Fundo(g, rg, cHover, E(5), null);
                TextRenderer.DrawText(g, l.Titulo, fCab, new Rectangle(x0 + E(2), y, w - E(40), rg.Height), cTexto, TF);
                TextRenderer.DrawText(g, (l.Ler() ? '\uE70E' : '\uE70D').ToString(), fGlifoP,
                    new Rectangle(rg.Right - E(36), y, E(28), rg.Height), cFraco, TC);
                y += E(40);
                continue;
            }
            if (l.Tipo == L_CAB)
            {
                y += E(14);
                TextRenderer.DrawText(g, l.Titulo, fCab, new Rectangle(x0 + E(2), y, w, E(26)), cTexto, TF);
                y += E(32);
                continue;
            }
            if (l.Tipo == L_NOTA)
            {
                var tam = TextRenderer.MeasureText(g, l.Titulo, fNormal, new Size(w, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, l.Titulo, fNormal, new Rectangle(x0 + E(2), y, w, tam.Height + E(4)), cFraco,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                y += tam.Height + E(16);
                continue;
            }
            // a explicacao quebra em mais linhas quando precisa (o cartao cresce junto)
            int txPre = l.Glifo != '\0' ? x0 + E(54) : x0 + E(20);
            int descW = Math.Max(E(80), w - (txPre - x0) - E(200));
            int descH = l.Desc == null ? 0 : TextRenderer.MeasureText(g, l.Desc, fPequena, new Size(descW, 0),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Height;
            int h = l.Desc != null ? E(50) + descH : E(54);
            var r = new Rectangle(x0, y, w, h);
            zonas.Add(new Zona { R = r, L = l });
            bool hov = sobre != null && sobre.L == l;
            Fundo(g, r, hov ? cHover : cCartao, E(6), cBorda);
            int tx = x0 + E(20);
            if (l.Glifo != '\0')
            {
                TextRenderer.DrawText(g, l.Glifo.ToString(), fGlifo, new Rectangle(x0 + E(14), y, E(28), h), cTexto, TC);
                tx = x0 + E(54);
            }
            int dirW = E(200);
            if (l.Desc != null)
            {
                TextRenderer.DrawText(g, l.Titulo, fNormal, new Rectangle(tx, y + E(13), w - (tx - x0) - dirW, E(22)), cTexto, TF);
                TextRenderer.DrawText(g, l.Desc, fPequena, new Rectangle(tx, y + E(36), descW, descH + E(2)), cFraco,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top);
            }
            else TextRenderer.DrawText(g, l.Titulo, fNormal, new Rectangle(tx, y, w - (tx - x0) - dirW, h), cTexto, TF);

            if (l.Tipo == L_CHAVE)
            {
                bool on = l.Ler();
                var rs = new Rectangle(r.Right - E(60), y + (h - E(20)) / 2, E(40), E(20));
                Interruptor(g, rs, on, hov);
                TextRenderer.DrawText(g, on ? "Ligado" : "Desligado", fNormal, new Rectangle(rs.Left - E(110), y, E(98), h), cTexto,
                    TF & ~TextFormatFlags.Left | TextFormatFlags.Right);
            }
            else
            {
                char seta = l.Glifo == '' ? '' : '';
                if (l.Glifo != '') TextRenderer.DrawText(g, seta.ToString(), fGlifoP, new Rectangle(r.Right - E(40), y, E(24), h), cFraco, TC);
                string v = l.Valor();
                if (v.Length > 0)
                    TextRenderer.DrawText(g, v, fNormal, new Rectangle(r.Right - E(44) - dirW, y, dirW, h), cFraco,
                        TF & ~TextFormatFlags.Left | TextFormatFlags.Right);
            }
            y += h + E(4);
        }
        alturaConteudo = y + rolagem + E(24);
        g.Clip = reg;

        if (alturaConteudo > ClientSize.Height)
        {
            float hb = Math.Max(E(30), ClientSize.Height * ClientSize.Height / (float)alturaConteudo);
            float yb = (ClientSize.Height - hb) * rolagem / (float)(alturaConteudo - ClientSize.Height);
            Fundo(g, new Rectangle(ClientSize.Width - E(8), (int)yb + E(4), E(4), (int)hb - E(8)), cFraco, E(2), null);
        }
    }

    void Interruptor(Graphics g, Rectangle r, bool on, bool hov)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
        using (var p = Desenho.Arred(rf, rf.Height / 2))
        {
            if (on) using (var b = new SolidBrush(cAcento)) g.FillPath(b, p);
            else using (var pn = new Pen(cFraco)) g.DrawPath(pn, p);
        }
        float d = r.Height - E(hov ? 8 : 10);
        float cx = on ? r.Right - r.Height / 2f : r.X + r.Height / 2f, cy = r.Y + r.Height / 2f;
        using (var b = new SolidBrush(on ? cTextoAcento : cFraco)) g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);
        g.SmoothingMode = SmoothingMode.None;
    }

    // ---------------- mouse ----------------
    Zona ZonaEm(Point p)
    {
        foreach (var z in zonas) if (z.R.Contains(p)) return z;
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var z = ZonaEm(e.Location);
        bool mudou = (z == null) != (sobre == null) || (z != null && (z.L != sobre.L || z.Pagina != sobre.Pagina));
        sobre = z;
        if (mudou) { Cursor = z != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (sobre != null) { sobre = null; Invalidate(); }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        int max = Math.Max(0, alturaConteudo - ClientSize.Height);
        rolagem = Math.Max(0, Math.Min(max, rolagem - e.Delta * E(48) / 120));
        sobre = null;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        rolagem = Math.Max(0, Math.Min(rolagem, Math.Max(0, alturaConteudo - ClientSize.Height)));
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        var z = ZonaEm(e.Location);
        if (z == null) return;
        if (z.Pagina == -2)
        {
            app.MenuLateralAberto = !app.MenuLateralAberto;
            app.SalvarConfig();
            if (!app.Animacoes) { navAtual = app.MenuLateralAberto ? 1f : 0f; Invalidate(); return; }
            if (!tNav.Enabled)
            {
                tNav.Tick -= PassoNav; tNav.Tick += PassoNav;
                tNav.Start();
            }
            return;
        }
        if (z.Pagina >= 0) { pagina = z.Pagina; rolagem = 0; Montar(); Invalidate(); return; }
        var l = z.L;
        if (l.Tipo == L_CHAVE)
        {
            l.Gravar(!l.Ler());
            app.SalvarConfig();
            app.PainelMudou();
            Montar();      // refaz a lista na hora (opcoes que dependem desta aparecem/somem)
            sobre = null;
            Invalidate();
            return;
        }
        if ((l.Tipo == L_BOTAO || l.Tipo == L_GRUPO) && l.Clicar != null)
        {
            l.Clicar(z.R);
            if (!IsDisposed) { Montar(); Invalidate(); }
        }
    }

    void PassoNav(object s, EventArgs e)
    {
        float alvo = app.MenuLateralAberto ? 1f : 0f;
        navAtual += (alvo - navAtual) * 0.3f;
        if (Math.Abs(alvo - navAtual) < 0.02f) { navAtual = alvo; tNav.Stop(); }
        Invalidate();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        app.ConferirPainel();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        Montar(); // volta de uma janelinha (atalho, nome): mostra os valores novos
        Invalidate();
    }
}
