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
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
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
[assembly: AssemblyVersion("1.7.4.0")]
[assembly: AssemblyFileVersion("1.7.4.0")]

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
    public static bool Atualizado;   // aberto pelo atualizador (a copia antiga esta fechando)

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
        if (args.Length == 2 && args[0] == "--testerede") { TesteRede(args[1]); return; }
        if (args.Length == 2 && args[0] == "--testeestado") { var l = Audio.Listar(0, true, true, null); File.WriteAllText(args[1], App.EstadoTexto(l)); Audio.Liberar(l); return; }
        if (args.Length == 1 && args[0] == "--apagar-identidade") { Rede.ApagarIdentidade(); return; }
        // so para quem publica: assina o .exe de uma versao (chave que fica guardada neste PC)
        if (args.Length == 2 && args[0] == "--assinar") { File.WriteAllText(args[1] + ".pub.txt", Atualizador.Assinar(args[1])); return; }
        if (args.Length == 2 && args[0] == "--testeatualizar") { File.WriteAllLines(args[1], Atualizador.Teste()); return; }
        if (args.Length >= 1 && args[0] == "--atualizado") { Atualizado = true; args = new string[0]; }
        if (args.Length == 2 && args[0] == "--testecontrole") { TesteControle(args[1]); return; }
        if (args.Length == 1 && args[0] == "--desinstalar") { Application.EnableVisualStyles(); Instalador.Desinstalar(); return; }
        // aberto de fora da pasta de programas (ex.: Downloads): oferece instalar
        if (args.Length == 0 && !Atualizado && !Instalador.EstaInstalado)
        {
            Application.EnableVisualStyles();
            if (!Instalador.Oferecer(Instalador.TemaEscuroSistema())) return;
        }
        if (args.Length >= 1 && args[args.Length - 1] == "--so-local") { Rede.SoLocal = true; Array.Resize(ref args, args.Length - 1); }
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
            if (!novo && Atualizado)
            {
                // a versao antiga ainda esta fechando: espera ela soltar
                try { novo = m.WaitOne(20000); } catch (AbandonedMutexException) { novo = true; }
            }
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

    // ---- teste do protocolo (tudo no proprio PC, porta aleatoria so local) ----
    static void TesteRede(string arq)
    {
        var o = new List<string>();
        Action<string, bool> ok = (n, b) => o.Add((b ? "OK    " : "FALHA ") + n);
        var tl = new TcpListener(IPAddress.Loopback, 0); tl.Start();
        int porta = ((IPEndPoint)tl.LocalEndpoint).Port;
        string idServ = "servidor-teste-0000", idCli = "cliente-teste-0000";
        string pubMinha = Convert.ToBase64String(Rede.MinhaChavePublica());
        string codigo = null; int erros = 0; ParPC pareadoServ = null;
        var pares = new Dictionary<string, ParPC>();
        Func<Canal, ParPC> atender = c => c.AtenderServidor(idServ, "Servidor", id => { ParPC x; return pares.TryGetValue(id, out x) ? x : null; }, () => true,
            () => codigo != null && erros < 3 ? codigo : null, () => { erros++; if (erros >= 3) codigo = null; }, pp => { pareadoServ = pp; codigo = null; });
        Func<Func<Canal, string>, Func<Canal, string>, string[]> rodar = (cli, srv) =>
        {
            string rs = null;
            var t = new Thread(() => { try { using (var c = new Canal(tl.AcceptTcpClient())) rs = srv(c); } catch (Exception ex) { rs = "erro: " + ex.Message; } });
            t.Start();
            string rc;
            try { using (var c = Canal.Conectar("127.0.0.1", 2000, porta)) rc = cli(c); } catch (Exception ex) { rc = "erro: " + ex.Message; }
            t.Join(8000);
            return new[] { rc, rs };
        };
        try
        {
            // 1) pareamento sem codigo gerado: recusado
            var r = rodar(c => { c.ParearCliente(idCli, "Cliente", "ABCD-EFGH"); return "pareou"; }, c => { atender(c); return "ok"; });
            ok("parear sem código gerado é recusado (" + r[0] + ")", r[0].StartsWith("erro"));
            // 2) codigo errado 3x -> codigo invalida
            codigo = Rede.NovoCodigo(); erros = 0;
            string certo = codigo;
            for (int i = 0; i < 3; i++)
            {
                r = rodar(c => { c.ParearCliente(idCli, "Cliente", "2222-2222"); return "pareou"; }, c => { atender(c); return "ok"; });
                ok("código errado " + (i + 1) + " é recusado (" + r[0] + ")", r[0].StartsWith("erro") && pareadoServ == null);
            }
            r = rodar(c => { c.ParearCliente(idCli, "Cliente", certo); return "pareou"; }, c => { atender(c); return "ok"; });
            ok("depois de 3 erros, nem o código certo vale (" + r[0] + ")", r[0].StartsWith("erro") && pareadoServ == null);
            // 3) codigo certo
            codigo = Rede.NovoCodigo(); erros = 0; certo = codigo;
            ParPC parCli = null;
            r = rodar(c => { parCli = c.ParearCliente(idCli, "Cliente", certo.ToLowerInvariant().Replace("-", " ")); return "pareou"; }, c => { atender(c); return "ok"; });
            ok("código certo pareia (" + r[0] + ")", r[0] == "pareou" && pareadoServ != null && parCli != null && parCli.Id == idServ && pareadoServ.Id == idCli);
            ok("chaves trocadas conferem", parCli != null && parCli.ChavePub == pubMinha && pareadoServ != null && pareadoServ.ChavePub == pubMinha);
            ok("código só vale uma vez", codigo == null);
            // 4) sessao com par conhecido + mensagens cifradas
            pares[idCli] = pareadoServ;
            r = rodar(c => { c.SessaoCliente(idCli, parCli); c.Enviar("olá ção"); return c.Receber(); },
                      c => { var pp = atender(c); if (pp == null) return "sem sessão"; var m = c.Receber(); c.Enviar("eco:" + m); return "ok"; });
            ok("sessão cifrada ida e volta (" + r[0] + ")", r[0] == "eco:olá ção" && r[1] == "ok");
            // 5) PC nao pareado: recusado
            r = rodar(c => { c.SessaoCliente("intruso", parCli); return "abriu"; }, c => { var pp = atender(c); return pp == null ? "recusou" : "aceitou"; });
            ok("PC não pareado é recusado (" + r[0] + " / " + r[1] + ")", r[0].StartsWith("erro") && r[1] == "recusou");
            // 6) identidade falsa (outra chave publica esperada): cliente recusa
            var falso = new ParPC { Id = idServ, Nome = "x", ChavePub = Convert.ToBase64String(FalsaChave()) };
            r = rodar(c => { c.SessaoCliente(idCli, falso); return "abriu"; }, c => { var pp = atender(c); return pp == null ? "recusou" : "aceitou"; });
            ok("servidor com chave diferente é recusado (" + r[0] + ")", r[0].StartsWith("erro"));
            // 7) servidor espera outra chave do cliente: recusa a prova
            pares[idCli] = new ParPC { Id = idCli, ChavePub = Convert.ToBase64String(FalsaChave()), MeControla = true };
            r = rodar(c => { c.SessaoCliente(idCli, parCli); return "abriu"; }, c => { var pp = atender(c); return pp == null ? "recusou" : "aceitou"; });
            ok("cliente com chave diferente é recusado (" + r[0] + " / " + r[1] + ")", r[1] == "recusou" && r[0] != "abriu");
            pares[idCli] = pareadoServ;
            // 8) mensagem alterada no caminho e mensagem repetida
            r = rodar(c =>
            {
                c.SessaoCliente(idCli, parCli);
                var x = c.Selar("volume 100");
                x[x.Length - 40] ^= 1;
                c.EnviarBruto(x);
                return "enviou";
            }, c => { atender(c); try { c.Receber(); return "aceitou"; } catch (Exception ex) { return "recusou: " + ex.Message; } });
            ok("mensagem alterada é recusada (" + r[1] + ")", r[1].StartsWith("recusou"));
            r = rodar(c =>
            {
                c.SessaoCliente(idCli, parCli);
                var x = c.Selar("mudo");
                c.EnviarBruto(x); c.EnviarBruto(x);
                return "enviou";
            }, c => { atender(c); c.Receber(); try { c.Receber(); return "aceitou repetida"; } catch (Exception ex) { return "recusou: " + ex.Message; } });
            ok("mensagem repetida é recusada (" + r[1] + ")", r[1].StartsWith("recusou"));
            // 9) o que vai pela rede nao tem o texto
            r = rodar(c => { c.SessaoCliente(idCli, parCli); var x = c.Selar("segredo-segredo-segredo"); return Rede.S(x).Contains("segredo") ? "visível" : "cifrado"; },
                      c => { atender(c); return "ok"; });
            ok("conteúdo vai cifrado (" + r[0] + ")", r[0] == "cifrado");
            r = rodar(c => { c.EnviarBruto(Rede.Juntar(Rede.B("QUEM"))); var q = Rede.Separar(c.ReceberBruto()); return Rede.S(q[0]) + "|" + Rede.S(q[1]) + "|" + Rede.S(q[2]); },
                      c => { atender(c); return "ok"; });
            ok("procura por TCP identifica o PC (" + r[0] + ")", r[0] == "EU|" + idServ + "|Servidor");
            var swp = Stopwatch.StartNew(); var achados = Rede.ProcurarTcp(1500);
            o.Add("INFO  procura TCP na rede: " + swp.ElapsedMilliseconds + " ms, " + achados.Count + " PC(s) com Sintonia 1.1.1+");
            ok("endereço da internet é recusado", !Rede.EhRedeLocal(IPAddress.Parse("8.8.8.8")) && Rede.EhRedeLocal(IPAddress.Loopback));
            ok("código tem 8 caracteres sem 0/O/1/I/L", Rede.NormalizarCodigo(Rede.NovoCodigo()).Length == 8 && Rede.NovoCodigo().IndexOfAny("01OIL".ToCharArray()) < 0);
        }
        catch (Exception ex) { o.Add("ERRO " + ex); }
        tl.Stop();
        File.WriteAllLines(arq, o);
    }
    static byte[] FalsaChave()
    {
        using (var k = CngKey.Create(CngAlgorithm.ECDsaP256)) return k.Export(CngKeyBlobFormat.EccPublicBlob);
    }

    // ---- teste de controle: este processo controla o Sintonia aberto (pareado consigo mesmo) ----
    static void TesteControle(string arq)
    {
        var o = new List<string>();
        try
        {
            string id = null;
            foreach (var l in File.ReadAllLines(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sintonia.ini")))
                if (l.StartsWith("id_pc=")) id = l.Substring(6);
            var par = new ParPC { Id = id, Nome = "eu", ChavePub = Convert.ToBase64String(Rede.MinhaChavePublica()), UltimoIP = "127.0.0.1" };
            var sw = Stopwatch.StartNew();
            using (var c = Canal.Conectar("127.0.0.1", 2000))
            {
                c.SessaoCliente(id, par);
                o.Add("sessão aberta em " + sw.ElapsedMilliseconds + " ms");
                sw.Restart();
                string est = c.Enviar2("estado");
                o.Add("estado em " + sw.ElapsedMilliseconds + " ms, " + est.Length + " bytes:");
                foreach (var l in est.Split('\n')) if (l.Length > 0) o.Add("   " + l);
                string padrao = null, vol = null, mudo = null, exe = null, dev = null, volA = null;
                foreach (var l in est.Split('\n'))
                {
                    var x = l.Split('\t');
                    if (x[0] == "D" && x[1] == "0" && x[5] == "1") { padrao = x[2]; vol = x[6]; mudo = x[7]; }
                    if (x[0] == "A" && x[8] == "0" && exe == null) { dev = x[1]; exe = x[2]; volA = x[4]; }
                }
                o.Add("vold (mesmo valor) -> " + c.Enviar2("vold\t" + padrao + "\t" + vol));
                o.Add("mudod 1 -> " + c.Enviar2("mudod\t" + padrao + "\t1"));
                o.Add("mudod volta -> " + c.Enviar2("mudod\t" + padrao + "\t" + mudo));
                o.Add("padrao (mesmo) -> " + c.Enviar2("padrao\t" + padrao));
                o.Add("padrao inexistente -> " + c.Enviar2("padrao\t{xyz}"));
                o.Add("comando desconhecido -> " + c.Enviar2("apagar\ttudo"));
                if (exe != null)
                {
                    o.Add("vola " + exe + " (mesmo) -> " + c.Enviar2("vola\t" + dev + "\t" + exe + "\t" + volA));
                    string ic = c.Enviar2("icone\t" + exe);
                    o.Add("icone " + exe + " -> " + ic.Length + " caracteres base64");
                }
                o.Add("icone de arquivo qualquer -> '" + c.Enviar2("icone\tC:\\Windows\\notepad.exe") + "'");
                sw.Restart();
                for (int i = 0; i < 20; i++) c.Enviar2("vold\t" + padrao + "\t" + vol);
                o.Add("20 mudanças de volume em " + sw.ElapsedMilliseconds + " ms");
            }
        }
        catch (Exception ex) { o.Add("ERRO " + ex); }
        File.WriteAllLines(arq, o);
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
    public bool Remoto;          // app do outro PC (controle pela rede)
    public List<ParteApp> Partes = new List<ParteApp>();   // cada janela/processo do app com som (2 VLC abertos, etc.)
}

class ParteApp
{
    public uint Pid;
    public string Nome;
    public float Vol;
    public bool Mudo = true, Ativo;
    public List<ISimpleAudioVolume> Vols = new List<ISimpleAudioVolume>();
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
                if (!sis && vol != null)
                {
                    var pt = g.Partes.Find(q => q.Pid == pid);
                    if (pt == null)
                    {
                        pt = new ParteApp { Pid = pid };
                        float pv; if (vol.GetMasterVolume(out pv) == 0) pt.Vol = pv;
                        string dn; if (s.GetDisplayName(out dn) == 0 && !string.IsNullOrEmpty(dn) && !dn.StartsWith("@")) pt.Nome = dn;
                        g.Partes.Add(pt);
                    }
                    pt.Vols.Add(vol);
                    bool pm; if (vol.GetMute(out pm) == 0) pt.Mudo = pt.Mudo && pm;
                    if (st == 1) pt.Ativo = true;
                }
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
            if (g.Partes.Count > 1) NomearPartes(g);
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

    // Nome de cada janela: o titulo da janela do processo; senao o nome da sessao; senao "App 1", "App 2"
    static void NomearPartes(GrupoApp g)
    {
        g.Partes.Sort((a, b) => a.Pid.CompareTo(b.Pid));
        for (int i = 0; i < g.Partes.Count; i++)
        {
            var pt = g.Partes[i];
            string titulo = null;
            try { using (var pr = Process.GetProcessById((int)pt.Pid)) titulo = pr.MainWindowTitle; } catch { }
            if (!string.IsNullOrEmpty(titulo)) pt.Nome = titulo;
            if (string.IsNullOrEmpty(pt.Nome)) pt.Nome = g.Nome + " " + (i + 1);
            if (pt.Vols.Count == 0) pt.Mudo = false;
        }
    }

    public static void Liberar(List<Disp> l)
    {
        if (l == null) return;
        foreach (var d in l)
        {
            Soltar(d.Ep); d.Ep = null;
            Soltar(d.Medidor); d.Medidor = null;
            foreach (var g in d.Apps) { foreach (var r in g.Rcws) Soltar(r); g.Rcws.Clear(); g.Vols.Clear(); g.Medidores.Clear(); foreach (var pt in g.Partes) pt.Vols.Clear(); }
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

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern int SHLoadIndirectString(string fonte, System.Text.StringBuilder saida, int n, IntPtr r);

    // App da Microsoft Store: o .exe nao tem o icone/nome certos; eles ficam no AppxManifest.xml do pacote
    class Pacote { public string Nome, Logo; }
    static readonly Dictionary<string, Pacote> pacotes = new Dictionary<string, Pacote>(StringComparer.OrdinalIgnoreCase);

    static Pacote PacoteDe(string exe)
    {
        if (exe == null || exe.IndexOf("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) < 0) return null;
        Pacote pk;
        if (pacotes.TryGetValue(exe, out pk)) return pk;
        pk = null;
        try
        {
            string dir = Path.GetDirectoryName(exe), raiz = null;
            for (int i = 0; i < 5 && dir != null; i++, dir = Path.GetDirectoryName(dir))
                if (File.Exists(Path.Combine(dir, "AppxManifest.xml"))) { raiz = dir; break; }
            if (raiz != null)
            {
                var doc = new System.Xml.XmlDocument();
                doc.Load(Path.Combine(raiz, "AppxManifest.xml"));
                string ident = null, nomeProp = null, logoProp = null, nomeApp = null, logoApp = null;
                string rel = exe.Substring(raiz.Length).TrimStart('\\');
                foreach (System.Xml.XmlNode n in doc.GetElementsByTagName("*"))
                {
                    var el = n as System.Xml.XmlElement;
                    if (el == null) continue;
                    if (el.LocalName == "Identity" && ident == null) ident = el.GetAttribute("Name");
                    else if (el.LocalName == "DisplayName" && el.ParentNode != null && el.ParentNode.LocalName == "Properties") nomeProp = el.InnerText.Trim();
                    else if (el.LocalName == "Logo" && el.ParentNode != null && el.ParentNode.LocalName == "Properties") logoProp = el.InnerText.Trim();
                    else if (el.LocalName == "Application")
                    {
                        bool este = string.Equals(el.GetAttribute("Executable").Replace('/', '\\'), rel, StringComparison.OrdinalIgnoreCase);
                        if (!este && nomeApp != null) continue;
                        foreach (System.Xml.XmlNode v in el.GetElementsByTagName("*"))
                        {
                            var ve = v as System.Xml.XmlElement;
                            if (ve == null || ve.LocalName != "VisualElements") continue;
                            nomeApp = ve.GetAttribute("DisplayName"); logoApp = ve.GetAttribute("Square44x44Logo");
                            if (logoApp.Length == 0) logoApp = ve.GetAttribute("Square150x150Logo");
                            break;
                        }
                    }
                }
                pk = new Pacote();
                pk.Nome = TextoDoPacote(Path.GetFileName(raiz), ident, string.IsNullOrEmpty(nomeApp) ? nomeProp : nomeApp);
                if (pk.Nome == null && nomeProp != null && nomeApp != nomeProp) pk.Nome = TextoDoPacote(Path.GetFileName(raiz), ident, nomeProp);
                string logo = string.IsNullOrEmpty(logoApp) ? logoProp : logoApp;
                if (!string.IsNullOrEmpty(logo)) pk.Logo = Path.Combine(raiz, logo.Replace('/', '\\'));
            }
        }
        catch { pk = null; }
        pacotes[exe] = pk;
        return pk;
    }

    // "ms-resource:AppName" -> texto de verdade (no idioma do Windows)
    static string TextoDoPacote(string nomeCompleto, string ident, string v)
    {
        if (string.IsNullOrEmpty(v)) return null;
        if (!v.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) return v.Length > 40 ? null : v;
        string chave = v.Substring(12);
        var tentativas = new List<string>();
        if (chave.StartsWith("//")) tentativas.Add(v);
        else
        {
            tentativas.Add("ms-resource://" + ident + "/resources/" + chave.TrimStart('/'));
            tentativas.Add("ms-resource://" + ident + "/" + chave.TrimStart('/'));
            tentativas.Add(v);
        }
        foreach (var t in tentativas)
        {
            var sb = new System.Text.StringBuilder(256);
            if (SHLoadIndirectString("@{" + nomeCompleto + "? " + t + "}", sb, sb.Capacity, IntPtr.Zero) == 0)
            {
                string r = sb.ToString().Trim();
                if (r.Length > 0 && r.Length <= 40 && !r.StartsWith("ms-resource")) return r;
            }
        }
        return null;
    }

    static string Digitos(string s, int i) { int j = i; while (j < s.Length && char.IsDigit(s[j])) j++; return s.Substring(i, j - i); }

    // o logo do pacote tem varias versoes (tamanhos/escalas): pega a mais adequada
    static Bitmap LogoDoPacote(string logo, int tam)
    {
        string dir = Path.GetDirectoryName(logo), nome = Path.GetFileNameWithoutExtension(logo), ext = Path.GetExtension(logo);
        if (!Directory.Exists(dir)) return null;
        string melhor = null; int nota = int.MinValue;
        foreach (var f in Directory.GetFiles(dir, nome + "*" + ext))
        {
            string meio = Path.GetFileNameWithoutExtension(f).Substring(nome.Length).ToLowerInvariant();
            if (meio.Contains("contrast") || meio.Contains("lightunplated")) continue;
            int n = 0, t;
            int i = meio.IndexOf("targetsize-");
            if (i >= 0 && int.TryParse(Digitos(meio, i + 11), out t))
                n = 1000 - Math.Abs(t - tam) * 4 - (t < tam ? 200 : 0);
            else if ((i = meio.IndexOf("scale-")) >= 0 && int.TryParse(Digitos(meio, i + 6), out t))
                n = 500 + Math.Min(t, 200);
            else n = 300;
            if (meio.Contains("altform-unplated")) n += 50;     // sem o quadrado colorido de fundo
            if (n > nota) { nota = n; melhor = f; }
        }
        if (melhor == null) return null;
        using (var ms = new MemoryStream(File.ReadAllBytes(melhor)))
        using (var img = Image.FromStream(ms))
        {
            var b = new Bitmap(tam, tam);
            using (var g = Graphics.FromImage(b))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(img, new Rectangle(0, 0, tam, tam));
            }
            return b;
        }
    }

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
        var pk = PacoteDe(caminho);
        if (pk != null && pk.Nome != null) { nomes[caminho] = pk.Nome; return pk.Nome; }
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
        var pk = PacoteDe(caminho);
        if (pk != null && pk.Logo != null) try { b = LogoDoPacote(pk.Logo, tam); } catch { b = null; }
        if (b != null) { icones[k] = b; return b; }
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
        // anel roxo de volume (como um botao de volume) com um alto-falante no centro
        var bmp = new Bitmap(n, n, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            DesenharAnel(g, n, Color.FromArgb(150, 110, 255), Color.FromArgb(60, 140, 130, 170), Color.FromArgb(150, 110, 255), false);
        }
        return bmp;
    }

    // desenho comum do logo e do icone da bandeja (grade 64x64)
    static void DesenharAnel(Graphics g, int n, Color arco, Color trilho, Color falante, bool mudo)
    {
        float u = n / 64f, esp = Math.Max(1.6f, 7 * u);
        float raio = 28 * u - esp / 2;
        var r = new RectangleF(32 * u - raio, 32 * u - raio, 2 * raio, 2 * raio);
        using (var pn = new Pen(trilho, esp)) g.DrawEllipse(pn, r);
        using (var pn = new Pen(arco, esp) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(pn, r, 135, 230);
        var pts = new[] { new PointF(-14, -6), new PointF(-6, -6), new PointF(6, -16), new PointF(6, 16), new PointF(-6, 6), new PointF(-14, 6) };
        for (int i = 0; i < pts.Length; i++) pts[i] = new PointF((32 + pts[i].X * 0.75f) * u, (32 + pts[i].Y * 0.75f) * u);
        using (var b = new SolidBrush(falante)) g.FillPolygon(b, pts);
        if (mudo)
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            using (var pn = new Pen(Color.Transparent, 15 * u)) g.DrawLine(pn, 6 * u, 6 * u, 58 * u, 58 * u);
            g.CompositingMode = CompositingMode.SourceOver;
            using (var pn = new Pen(falante, 6.5f * u) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(pn, 8 * u, 8 * u, 56 * u, 56 * u);
        }
    }

    // Icone da bandeja: so o botao giratorio, de uma cor (branco na barra escura,
    // preto na clara). No mudo ganha um risco atravessado.
    public static Bitmap Bandeja(int n, Color cor, bool mudo)
    {
        // icone do relogio: o mesmo anel do logo, de uma cor so (branco na barra escura, preto na clara)
        var bmp = new Bitmap(n, n, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            DesenharAnel(g, n, cor, Color.FromArgb(90, cor), cor, mudo);
        }
        return bmp;
    }

    // bolinha de "conectado" no canto do icone da bandeja (outro PC controlando este)
    public static void Conectado(Bitmap bmp, Color acento, Color contorno)
    {
        int n = bmp.Width;
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float d = Math.Max(5f, n * 0.42f), x = n - d, y = n - d;
            g.CompositingMode = CompositingMode.SourceCopy;
            using (var b = new SolidBrush(Color.Transparent)) g.FillEllipse(b, x - n * 0.07f, y - n * 0.07f, d + n * 0.14f, d + n * 0.14f);
            g.CompositingMode = CompositingMode.SourceOver;
            using (var b = new SolidBrush(acento)) g.FillEllipse(b, x, y, d, d);
        }
    }

    // icone da bandeja enquanto o outro PC muda algo: o item (app ou saida) + seta / mudo
    public static Bitmap Atividade(int n, Bitmap app, char glifo, Color cor, int tipo, Color acento)
    {
        var bmp = new Bitmap(n, n, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float t = n * 0.8f;
            if (app != null) g.DrawImage(app, new RectangleF(0, 0, t, t));
            else using (var gl = Glifo(glifo, (int)Math.Round(t), cor, 0.85f)) g.DrawImage(gl, 0, 0);
            // selo no canto: seta para cima/baixo, ou X (mudo), ou alto-falante (som de volta)
            float d = n * 0.56f, x = n - d, y = n - d;
            g.CompositingMode = CompositingMode.SourceCopy;
            using (var b = new SolidBrush(Color.Transparent)) g.FillEllipse(b, x - n * 0.06f, y - n * 0.06f, d + n * 0.12f, d + n * 0.12f);
            g.CompositingMode = CompositingMode.SourceOver;
            Color fundo = tipo == 2 ? Color.FromArgb(196, 43, 28)        // mudo: vermelho
                        : tipo == -1 ? Color.FromArgb(247, 185, 20)      // desceu: amarelo
                        : Color.FromArgb(16, 137, 62);                   // subiu / som de volta: verde
            using (var b = new SolidBrush(fundo)) g.FillEllipse(b, x, y, d, d);
            float cx = x + d / 2, cy = y + d / 2, r = d * 0.26f;
            using (var pn = new Pen(tipo == -1 ? Color.FromArgb(40, 30, 0) : Color.White, Math.Max(1.2f, n / 14f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (tipo == 1 || tipo == -1)
                {
                    float s = tipo == 1 ? -1 : 1;
                    g.DrawLine(pn, cx, cy - s * r, cx, cy + s * r * 1.05f);
                    g.DrawLines(pn, new[] { new PointF(cx - r, cy + s * r * 0.15f), new PointF(cx, cy + s * r * 1.05f), new PointF(cx + r, cy + s * r * 0.15f) });
                }
                else if (tipo == 2)
                {
                    g.DrawLine(pn, cx - r * 0.8f, cy - r * 0.8f, cx + r * 0.8f, cy + r * 0.8f);
                    g.DrawLine(pn, cx + r * 0.8f, cy - r * 0.8f, cx - r * 0.8f, cy + r * 0.8f);
                }
                else
                {
                    g.DrawLines(pn, new[] { new PointF(cx - r, cy + r * 0.05f), new PointF(cx - r * 0.25f, cy + r * 0.8f), new PointF(cx + r, cy - r * 0.7f) });
                }
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

    public JanelaTexto(string titulo, string dica, string valor, bool temaEscuro) : this(titulo, dica, valor, temaEscuro, "Salvar", 40) { }

    public JanelaTexto(string titulo, string dica, string valor, bool temaEscuro, string textoOk, int max) : base(temaEscuro)
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
            BackColor = cTecla, ForeColor = cTexto, BorderStyle = BorderStyle.FixedSingle, MaxLength = max
        };
        Controls.Add(caixa);

        var rodape = Rodape();
        var bSalvar = Botao(textoOk, true);
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
    Color cFundo, cCartao, cHover, cBorda, cTexto, cFraco, cAcento, cTextoAcento, cTrilho, cPolegar, cPolegarBorda, cAlto;
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
    const int Z_RECOLHER = 10, Z_NOME_A = 11, Z_ESCONDIDOS = 12, Z_MOSTRAR = 13, Z_USAR = 14, Z_PC = 15, Z_TENTAR = 16, Z_LISTA_PERFIS = 17,
              Z_PARTES = 18, Z_SLIDER_P = 19, Z_MUDO_P = 20, Z_TESTE = 21, Z_PRIORIDADE = 22;
    string testando;   // saida tocando o som de teste agora   // janelas de um app: setinha, barra e mudo de cada uma
    // apps com as janelas abertas no painel (saida|exe); nao e guardado: o painel abre sempre fechado
    readonly HashSet<string> partesAbertas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, float> partesAnim = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);   // 0..1 enquanto abre/fecha
    float FracPartes(Disp d, GrupoApp a)
    {
        if (!TemPartes(a)) return 0f;
        string k = d.Id + "|" + a.Exe;
        float f;
        if (partesAnim.TryGetValue(k, out f)) return f;
        return partesAbertas.Contains(k) ? 1f : 0f;
    }
    static bool TemPartes(GrupoApp a) { return a != null && !a.Sistema && a.Partes.Count > 1; }
    bool PartesAbertas(Disp d, GrupoApp a) { return TemPartes(a) && partesAbertas.Contains(d.Id + "|" + a.Exe); }
    bool listaPerfis; float listaAb;     // lista suspensa dos perfis (abre dentro do painel)

    // troca de PC: destaque que desliza, conteudo que entra de lado e altura que acompanha
    float pcPos = -1f, troca;
    int trocaDir, yConteudo;
    float hAnim; bool hAnimando;
    int alturaFixa;   // depois de trocar de PC o painel fica com a maior altura vista (colado embaixo e sem pular)

    // ---- outro PC (controle pela rede) ----
    public ParPC Alvo;                 // null = este PC
    Remoto remoto;
    string estadoRemoto;
    bool remotoFalhou, pedindoEstado;
    readonly Dictionary<string, Bitmap> iconesRem = new Dictionary<string, Bitmap>();
    readonly HashSet<string> iconesPedidos = new HashSet<string>();
    readonly Dictionary<string, string> pendentes = new Dictionary<string, string>();
    readonly HashSet<string> enviando = new HashSet<string>();
    static string Txt(string x) { return (x ?? "").Replace("\t", " ").Replace("\n", " ").Replace("\r", " "); }
    static string Num(float v) { return v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture); }

    void GarantirRemoto()
    {
        if (Alvo == null) return;
        if (remoto == null || remoto.Par != Alvo) { if (remoto != null) remoto.Fechar(); remoto = new Remoto(app, Alvo, this); }
    }

    void PedirEstado()
    {
        if (Alvo == null || pedindoEstado) return;
        GarantirRemoto();
        pedindoEstado = true;
        var alvo = Alvo;
        remoto.Pedir("estado", r =>
        {
            pedindoEstado = false;
            if (alvo != Alvo) return;
            bool primeiro = estadoRemoto == null;
            if (r == null) { remotoFalhou = true; } else { remotoFalhou = false; estadoRemoto = r; }
            if (primeiro) Transicao(0);
            Recarregar();
        });
    }

    // envia um comando; varios iguais seguidos (ex.: arrastando a barra) viram so o ultimo
    void EnviarRemoto(string chave, string comando, bool recarregarDepois)
    {
        GarantirRemoto();
        pendentes[chave] = comando;
        if (enviando.Contains(chave)) return;
        Action proximo = null;
        proximo = () =>
        {
            string c;
            if (!pendentes.TryGetValue(chave, out c)) { enviando.Remove(chave); return; }
            pendentes.Remove(chave);
            enviando.Add(chave);
            remoto.Pedir(c, r =>
            {
                enviando.Remove(chave);
                if (pendentes.ContainsKey(chave)) proximo();
                else if (recarregarDepois) { var t = new Timer { Interval = 400 }; t.Tick += (s2, e2) => { t.Stop(); t.Dispose(); PedirEstado(); }; t.Start(); }
            });
        };
        proximo();
    }

    // monta saidas/entradas a partir do estado recebido do outro PC
    void LerEstadoRemoto(out List<Disp> sa, out List<Disp> en)
    {
        sa = new List<Disp>(); en = new List<Disp>();
        if (estadoRemoto == null) return;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var porId = new Dictionary<string, Disp>();
        foreach (var l in estadoRemoto.Split('\n'))
        {
            var c = l.Split('\t');
            try
            {
                if (c[0] == "D" && c.Length >= 8)
                {
                    var d = new Disp { Fluxo = int.Parse(c[1]), Id = c[2], NomeOriginal = c[3], Forma = int.Parse(c[4]), Padrao = c[5] == "1",
                                       Vol = float.Parse(c[6], inv), Mudo = c[7] == "1" };
                    string ren; d.Nome = app.Nomes.TryGetValue(d.Id, out ren) ? ren : d.NomeOriginal;
                    if (c.Length > 8) d.Adaptador = c[8];
                    porId[d.Id] = d;
                    if (d.Fluxo == 0) sa.Add(d); else if (app.Mics) en.Add(d);
                }
                else if (c[0] == "A" && c.Length >= 9)
                {
                    Disp d;
                    if (!porId.TryGetValue(c[1], out d)) continue;
                    var a = new GrupoApp { Remoto = true, Exe = c[2], Nome = c[3], Vol = float.Parse(c[4], inv), Mudo = c[5] == "1", Ativo = c[6] == "1",
                                           Saida = c[7].Length == 0 ? null : c[7], Sistema = c[8] == "1" };
                    d.Apps.Add(a);
                }
                else if (c[0] == "P" && c.Length >= 7)
                {
                    Disp d;
                    if (!porId.TryGetValue(c[1], out d)) continue;
                    var a = d.Apps.Find(x => x.Exe == c[2]);
                    if (a == null) continue;
                    a.Partes.Add(new ParteApp { Pid = uint.Parse(c[3]), Nome = c[4], Vol = float.Parse(c[5], inv), Mudo = c[6] == "1" });
                }
            }
            catch { }
        }
    }

    Bitmap IconeRemoto(GrupoApp a)
    {
        if (Alvo == null || a.Sistema) return null;
        string k = Alvo.Id + "|" + a.Exe;
        Bitmap b;
        if (iconesRem.TryGetValue(k, out b)) return b;
        if (iconesPedidos.Add(k))
        {
            GarantirRemoto();
            remoto.Pedir("icone\t" + a.Exe, r =>
            {
                try { if (!string.IsNullOrEmpty(r) && r != "ok") using (var ms = new MemoryStream(Convert.FromBase64String(r))) iconesRem[k] = new Bitmap(ms); }
                catch { }
                Invalidate();
            });
        }
        return null;
    }
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
            if (Alvo != null) { if (++ticks % 2 == 0) PedirEstado(); return; }   // outro PC: a cada 3 s
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
        foreach (var k in new List<string>(partesAnim.Keys))
        {
            float fpa = partesAnim[k], alvoP = partesAbertas.Contains(k) ? 1f : 0f;
            if (fpa == alvoP) { partesAnim.Remove(k); continue; }
            fpa += (alvoP - fpa) * 0.3f;
            if (Math.Abs(alvoP - fpa) < 0.02f) fpa = alvoP;
            partesAnim[k] = fpa; layout = true;
        }
        float alvoL = listaPerfis ? 1f : 0f;
        if (listaAb != alvoL)
        {
            listaAb += (alvoL - listaAb) * 0.3f;
            if (Math.Abs(alvoL - listaAb) < 0.02f) listaAb = alvoL;
            layout = true;
        }
        int alvoPc = IndiceAlvo();
        if (pcPos >= 0 && pcPos != alvoPc)
        {
            pcPos += (alvoPc - pcPos) * 0.28f;
            if (Math.Abs(alvoPc - pcPos) < 0.01f) pcPos = alvoPc;
            mexeu = true; Invalidate();
        }
        if (troca > 0f)
        {
            troca *= 0.78f;
            if (troca < 0.01f) troca = 0f;
            mexeu = true; Invalidate();
        }
        if (hAnimando)
        {
            float alvoH = AlturaAlvo();
            hAnim += (alvoH - hAnim) * 0.3f;
            if (Math.Abs(alvoH - hAnim) < 1f) { hAnim = alvoH; hAnimando = false; }
            layout = true;
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
        cAlto = escuro ? Color.FromArgb(255, 99, 99) : Color.FromArgb(196, 43, 28);   // vermelho de alerta do Windows 11
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
        partesAbertas.Clear(); partesAnim.Clear();   // as janelas de cada app sempre comecam recolhidas
        var pt = Cursor.Position;
        var tela = Screen.FromPoint(pt);
        area = App.AreaUtil(tela, out bordaBarra); telaB = tela.Bounds;
        cimaBarra = bordaBarra == 1;
        esc = App.EscalaEm(pt);
        PrepararVisual();
        largura = E(360);
        rolagem = 0;
        fechando = false;
        listaPerfis = false; listaAb = 0f; alturaFixa = 0;
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
        if (Alvo != null) { estadoRemoto = null; remotoFalhou = false; PedirEstado(); }
        if (app.Animacoes) Animar();
        if (app.MostrarSom) tSom.Start();
    }

    int AlturaAlvo()
    {
        if (alturaFixa > 0) alturaFixa = Math.Max(alturaFixa, alturaConteudo);
        return Math.Min(Math.Max(alturaConteudo, alturaFixa), (int)(area.Height * app.AlturaMax / 100.0));
    }

    int IndiceAlvo()
    {
        if (Alvo == null) return 0;
        var l = app.Controlados();
        int i = l.FindIndex(x => x.Id == Alvo.Id);
        return i < 0 ? 0 : i + 1;
    }

    // comeca a transicao de troca de PC (dir: -1 veio da direita, 1 da esquerda, 0 so aparece)
    void Transicao(int dir)
    {
        if (!app.Animacoes) { troca = 0f; return; }
        trocaDir = dir; troca = 1f;
        alturaFixa = Math.Max(alturaFixa, ClientSize.Height);
        hAnim = ClientSize.Height; hAnimando = true;
        Animar();
    }

    void Posicionar()
    {
        int h = hAnimando ? (int)Math.Round(hAnim) : AlturaAlvo();
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
    // outro PC: niveis pedidos pela rede so enquanto o painel mostra ele (e a opcao esta ligada)
    readonly Dictionary<string, float> niveisRemotos = new Dictionary<string, float>();
    bool pedindoNiveis, semNiveis; DateTime ultimoNiveis;   // semNiveis: o outro PC e de uma versao que nao manda os niveis
    const int BARRAS = 5;

    int LarguraMed() { return BARRAS * E(3) + (BARRAS - 1) * E(2); }

    // registra (e desenha) as barrinhas de uma linha
    void Medidor(Graphics g, int x, int yCentro, Color fundo, string chave, IAudioMeterInformation m, List<IAudioMeterInformation> ms)
    {
        if (!app.MostrarSom || g == null || (m == null && (ms == null || ms.Count == 0) && (Alvo == null || semNiveis))) return;
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
        if (Alvo != null) PedirNiveis();
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
                    if (Alvo != null && md.M == null && (md.Ms == null || md.Ms.Count == 0) && niveisRemotos.TryGetValue(md.Chave, out v)) pico = v;
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

    // ~8 vezes por segundo, um pedido pequeno (so os niveis); um por vez
    void PedirNiveis()
    {
        if (pedindoNiveis || semNiveis || estadoRemoto == null || remotoFalhou || (DateTime.Now - ultimoNiveis).TotalMilliseconds < 120) return;
        GarantirRemoto();
        pedindoNiveis = true; ultimoNiveis = DateTime.Now;
        var alvo = Alvo;
        remoto.Pedir("niveis", r =>
        {
            pedindoNiveis = false;
            if (alvo != Alvo || r == null) return;
            if (r == "nao") { semNiveis = true; Invalidate(); return; }   // versao antiga do outro lado: esconde as barrinhas
            niveisRemotos.Clear();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var l in r.Split('\n'))
            {
                int t = l.LastIndexOf('\t');
                float v;
                if (t > 0 && float.TryParse(l.Substring(t + 1), System.Globalization.NumberStyles.Float, inv, out v)) niveisRemotos[l.Substring(0, t)] = v;
            }
        });
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
        niveisRemotos.Clear(); pedindoNiveis = false; semNiveis = false;
        if (remoto != null) { remoto.Fechar(); remoto = null; }   // fechou o painel: desconecta do outro PC
        pedindoEstado = false; pendentes.Clear(); enviando.Clear();
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
        List<Disp> remS = null, remE = null;
        if (Alvo != null) LerEstadoRemoto(out remS, out remE);
        saidas = Alvo != null ? remS : Audio.Listar(0, true, app.Inativos, app.Nomes);
        escondidos[0].Clear(); escondidos[1].Clear();
        saidas.RemoveAll(d => !d.Padrao && app.Ocultos.Contains(d.Id) && Guardar(d) && Soltar(d));
        entradas = Alvo != null ? remE : app.Mics ? Audio.Listar(1, false, false, app.Nomes) : new List<Disp>();
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
                    {
                        if (a.Vols.Count == 0) continue;
                        if (a.Partes.Count > 1)
                        {
                            // varias janelas: cada uma tem o seu volume/mudo; o app so fica mudo se todas estiverem
                            bool todas = true;
                            foreach (var pt in a.Partes)
                            {
                                float pv; bool pmTodos = true, leu = false;
                                foreach (var sv in pt.Vols) { bool mm; if (sv.GetMute(out mm) == 0) { pmTodos = pmTodos && mm; leu = true; } }
                                if (leu && pmTodos != pt.Mudo) { pt.Mudo = pmTodos; mudou = true; }
                                if (pt.Vols.Count > 0 && pt.Vols[0].GetMasterVolume(out pv) == 0 && Math.Abs(pv - pt.Vol) > 0.004f) { pt.Vol = pv; mudou = true; }
                                todas = todas && pt.Mudo;
                            }
                            if (todas != a.Mudo) { a.Mudo = todas; mudou = true; }
                            if (a.Vols[0].GetMasterVolume(out v) == 0 && Math.Abs(v - a.Vol) > 0.004f) { a.Vol = v; mudou = true; }
                            continue;
                        }
                        if (a.Vols[0].GetMasterVolume(out v) == 0 && a.Vols[0].GetMute(out m) == 0)
                            if (Math.Abs(v - a.Vol) > 0.004f || m != a.Mudo) { a.Vol = v; a.Mudo = m; mudou = true; }
                    }
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
        if ((k == Keys.Left || k == Keys.Right) && arrastando == null)
        {
            // com o mouse em cima de uma barra: setas mudam de 1 em 1
            var zb = ZonaEm(PointToClient(Cursor.Position));
            if (zb != null && (zb.Tipo == Z_SLIDER_D || zb.Tipo == Z_SLIDER_A || (zb.Tipo == Z_SLIDER_P && zb.F >= 0 && zb.F < zb.G.Partes.Count)))
            {
                int dir = k == Keys.Right ? 1 : -1;
                float atual = zb.Tipo == Z_SLIDER_P ? zb.G.Partes[zb.F].Vol : zb.G != null ? zb.G.Vol : zb.D.Vol;
                float novo = Math.Max(0, Math.Min(100, (int)Math.Round(atual * 100) + dir)) / 100f;
                if (zb.Tipo == Z_SLIDER_P) AplicarParte(zb.G, zb.F, novo);
                else if (zb.G != null) AplicarApp(zb.G, novo); else AplicarDisp(zb.D, novo);
                Invalidate();
                return true;
            }
        }
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
    bool SobreParte(int tipo, GrupoApp g, int i)
    {
        var s = arrastando ?? sobre;
        return s != null && s.Tipo == tipo && s.G == g && s.F == i;
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
        var rTit = new Rectangle(pad + E(4), y, W - pad * 2 - E(76), E(32));
        var rMenu = new Rectangle(W - pad - E(32), y, E(32), E(32));
        var rPrio = new Rectangle(rMenu.X - E(36), y, E(32), E(32));
        Add(rMenu, Z_MENU, null, null, null);
        Add(rPrio, Z_PRIORIDADE, null, null, null);
        if (g != null)
        {
            string sub = Alvo != null ? "  ·  " + Alvo.Nome : app.UsarPerfis && app.PerfilAtivo != null ? "  ·  " + app.PerfilAtivo : "";
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
            // prioridade das saidas: em destaque quando a troca automatica esta ligada
            if (Sobre(Z_PRIORIDADE, null, null, null)) Fundo(g, rPrio, cHover, E(4));
            TextRenderer.DrawText(g, "\uE8CB", fGlifo, rPrio, app.PrioridadeAuto ? cAcento : cTexto, TC);
        }
        y += E(40);
        // divisao sutil entre o titulo e o conteudo
        if (g != null) using (var pn = new Pen(cBorda)) g.DrawLine(pn, pad + E(4), y, W - pad - E(4), y);
        y += E(10);

        // Este PC esta sendo controlado por outro: aviso discreto no topo
        string quem = app.ControladoPor();
        if (quem != null)
        {
            var rA = new Rectangle(pad, y, W - pad * 2, E(34));
            if (g != null)
            {
                Fundo(g, rA, cCartao, E(6));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(cAcento)) g.FillEllipse(b, rA.X + E(13), rA.Y + rA.Height / 2 - E(4), E(8), E(8));
                g.SmoothingMode = SmoothingMode.None;
                TextRenderer.DrawText(g, quem + " está controlando este PC", fNormal, new Rectangle(rA.X + E(30), rA.Y, rA.Width - E(40), rA.Height), cTexto, TF);
            }
            y += rA.Height + E(10);
        }

        // PCs: barra deslizante (Este PC + ate 2 outros), so com "Controlar outros PCs" ligado
        if (Alvo != null && (!app.Controlador || app.ParPorId(Alvo.Id) == null)) { Alvo = null; if (remoto != null) { remoto.Fechar(); remoto = null; } }
        var controlados = app.Controlados();
        if (controlados.Count > App.MaxPCs) controlados.RemoveRange(App.MaxPCs, controlados.Count - App.MaxPCs);
        bool temBarra = app.Controlador && controlados.Count > 0;
        if (temBarra)
        {
            var abas = new List<ParPC> { null }; abas.AddRange(controlados);
            var rB = new Rectangle(pad, y, W - pad * 2, E(36));
            int n = abas.Count, ins = E(3);
            float segW = (rB.Width - ins * 2) / (float)n;
            if (pcPos < 0 || pcPos > n - 1) pcPos = IndiceAlvo();
            if (g != null)
            {
                Fundo(g, rB, cCartao, E(6));
                using (var pn = new Pen(cBorda)) using (var pth = Desenho.Arred(new RectangleF(rB.X + 0.5f, rB.Y + 0.5f, rB.Width - 1, rB.Height - 1), E(6))) g.DrawPath(pn, pth);
                // destaque que desliza
                var rD = new RectangleF(rB.X + ins + pcPos * segW, rB.Y + ins, segW, rB.Height - ins * 2);
                Color cDest = app.TemaEscuro() ? Color.FromArgb(62, 62, 62) : Color.White;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pth = Desenho.Arred(rD, E(4))) using (var bd = new SolidBrush(cDest)) g.FillPath(bd, pth);
                using (var pth = Desenho.Arred(new RectangleF(rD.X + rD.Width / 2 - E(8), rD.Bottom - E(3), E(16), E(3)), E(1.5f))) using (var ba = new SolidBrush(cAcento)) g.FillPath(ba, pth);
                g.SmoothingMode = SmoothingMode.None;
            }
            for (int i = 0; i < n; i++)
            {
                var pc = abas[i];
                var r = new Rectangle((int)(rB.X + ins + i * segW), rB.Y, (int)segW, rB.Height);
                zonas.Add(new Zona { R = r, Tipo = Z_PC, Id = pc == null ? "" : pc.Id });
                if (g == null) continue;
                bool ativo = Math.Abs(pcPos - i) < 0.5f;
                bool hov = sobre != null && sobre.Tipo == Z_PC && sobre.Id == (pc == null ? "" : pc.Id);
                if (hov && !ativo) Fundo(g, new Rectangle(r.X + E(2), r.Y + ins + E(2), r.Width - E(4), r.Height - ins * 2 - E(4)), cHover, E(4));
                string txt = pc == null ? "Este PC" : pc.Nome;
                var fT = ativo ? fNegrito : fNormal;
                int tw = Math.Min(r.Width - E(40), TextRenderer.MeasureText(g, txt, fT, Size.Empty, TextFormatFlags.NoPadding).Width);
                int x0 = r.X + (r.Width - (tw + E(24))) / 2;
                TextRenderer.DrawText(g, (pc == null ? '\uE7F4' : '\uE7F8').ToString(), fGlifoP, new Rectangle(x0, r.Y, E(16), r.Height), ativo ? cTexto : cFraco, TC);
                TextRenderer.DrawText(g, txt, fT, new Rectangle(x0 + E(24), r.Y, tw + E(2), r.Height), ativo ? cTexto : cFraco, TF);
            }
            y += rB.Height + E(12);
        }
        else pcPos = -1f;

        // daqui para baixo e o conteudo do PC escolhido (entra deslizando ao trocar)
        yConteudo = Math.Max(0, y);
        if (g != null && troca > 0f)
        {
            g.SetClip(new Rectangle(0, Math.Max(0, y), W, 100000));
            g.TranslateTransform(trocaDir * troca * E(48), 0);
        }

        // Perfis - sao deste PC: um botao com o perfil ativo que abre a lista aqui mesmo no painel
        if (app.UsarPerfis && Alvo == null)
        {
            if (g != null) TextRenderer.DrawText(g, "Perfil", fPequena, new Rectangle(pad + E(4), y, W - pad * 2, E(18)), cFraco, TF);
            y += E(22);
            var rDD = new Rectangle(pad, y, W - pad * 2, E(36));
            Add(rDD, Z_LISTA_PERFIS, null, null, null);
            var ativo = app.Perfis.Find(pf => pf.Nome == app.PerfilAtivo);
            if (g != null)
            {
                bool hov = Sobre(Z_LISTA_PERFIS, null, null, null);
                Fundo(g, rDD, hov || listaPerfis ? cHover : cCartao, E(6));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pn = new Pen(cBorda)) using (var pth = Desenho.Arred(new RectangleF(rDD.X + 0.5f, rDD.Y + 0.5f, rDD.Width - 1, rDD.Height - 1), E(6))) g.DrawPath(pn, pth);
                g.SmoothingMode = SmoothingMode.None;
                string txt = ativo != null ? ativo.Nome : app.Perfis.Count == 0 ? "Salvar a configuração de agora como perfil" : "Nenhum perfil ativo";
                TextRenderer.DrawText(g, txt, ativo != null ? fNegrito : fNormal, new Rectangle(rDD.X + E(14), rDD.Y, rDD.Width - E(52), rDD.Height), ativo != null ? cTexto : cFraco, TF);
                // setinha que gira conforme a lista abre
                var rSeta = new Rectangle(rDD.Right - E(34), rDD.Y, E(24), rDD.Height);
                var est = g.Save();
                g.TranslateTransform(rSeta.X + rSeta.Width / 2f, rSeta.Y + rSeta.Height / 2f);
                g.RotateTransform(180f * listaAb);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pn = new Pen(cTexto, Math.Max(1f, E(1.4f))) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pn, new[] { new PointF(-E(5), -E(2.5f)), new PointF(0, E(2.5f)), new PointF(E(5), -E(2.5f)) });
                g.SmoothingMode = SmoothingMode.None;
                g.Restore(est);
            }
            y += rDD.Height;
            if (listaAb > 0f)
            {
                var itens = new List<Perfil>(app.Perfis);
                bool comNovo = itens.Count < App.MaxPerfis;
                int hL = E(34), n = itens.Count + (comNovo ? 1 : 0);
                int total = n * hL + E(8) + (comNovo && itens.Count > 0 ? E(9) : 0);
                int hVis = (int)Math.Round(total * listaAb);
                var rL = new Rectangle(pad, y + E(4), W - pad * 2, total);
                GraphicsState est = null;
                if (g != null)
                {
                    est = g.Save();
                    g.SetClip(new Rectangle(rL.X - 1, rL.Y - 1, rL.Width + 2, hVis + 2), CombineMode.Intersect);
                    Fundo(g, rL, cCartao, E(6));
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var pn = new Pen(cBorda)) using (var pth = Desenho.Arred(new RectangleF(rL.X + 0.5f, rL.Y + 0.5f, rL.Width - 1, rL.Height - 1), E(6))) g.DrawPath(pn, pth);
                    g.SmoothingMode = SmoothingMode.None;
                }
                int yi = rL.Y + E(4);
                for (int i = 0; i < n; i++)
                {
                    bool novo = i == itens.Count;
                    if (novo && itens.Count > 0)
                    {
                        if (g != null) using (var pn = new Pen(cBorda)) g.DrawLine(pn, rL.X + E(12), yi + E(4), rL.Right - E(12), yi + E(4));
                        yi += E(9);
                    }
                    Perfil pp = novo ? null : itens[i];
                    var r = new Rectangle(rL.X + E(4), yi, rL.Width - E(8), hL);
                    int tipo = novo ? Z_NOVO_PERFIL : Z_PERFIL;
                    if (listaAb >= 0.99f) Add(r, tipo, null, null, pp);
                    if (g != null)
                    {
                        bool at = pp != null && pp.Nome == app.PerfilAtivo;
                        if (Sobre(tipo, null, null, pp) && listaAb >= 0.99f) Fundo(g, r, cHover, E(4));
                        if (at) Fundo(g, new Rectangle(r.X, r.Y + E(9), E(3), r.Height - E(18)), cAcento, E(1.5f));
                        string gl = novo ? "\uE710" : at ? "\uE73E" : "";
                        if (gl.Length > 0) TextRenderer.DrawText(g, gl, fGlifoP, new Rectangle(r.X + E(10), r.Y, E(20), r.Height), novo ? cFraco : cTexto, TC);
                        TextRenderer.DrawText(g, novo ? "Novo perfil com a configuração de agora" : pp.Nome, at ? fNegrito : fNormal,
                            new Rectangle(r.X + E(38), r.Y, r.Width - E(46), r.Height), novo ? cFraco : cTexto, TF);
                        if (!novo && arrastoApp != null && arrastoApp.P == pp) Fundo(g, r, Color.FromArgb(150, cCartao), E(4));
                    }
                    if (pp != null && listaAb >= 0.99f) chips.Add(new KeyValuePair<Rectangle, Perfil>(r, pp));
                    yi += hL;
                }
                if (g != null) g.Restore(est);
                y += E(4) + hVis;
            }
            y += E(14);
        }

        // Saidas
        y = Secao(g, "Saída", y, pad, W, 0);
        y = Corpo(g, 0, y, pad, W);

        // Microfones
        if (app.Mics && (Alvo == null || estadoRemoto != null))
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
        if (lista.Count == 0 && Alvo != null && f == 0)
        {
            if (remotoFalhou)
            {
                y = Vazio(g, "Não foi possível falar com " + Alvo.Nome + ".", y, pad, W);
                if (g != null) TextRenderer.DrawText(g, "Ele está ligado e com \"Pode ser controlado\"?", fPequena,
                    new Rectangle(pad + E(4), y - E(6), W - pad * 2, E(20)), cFraco, TF);
                y += E(18);
                var rt = new Rectangle(pad + E(4), y, E(130), E(30));
                zonas.Add(new Zona { R = rt, Tipo = Z_TENTAR });
                if (g != null)
                {
                    Fundo(g, rt, sobre != null && sobre.Tipo == Z_TENTAR ? cHover : cCartao, E(4));
                    TextRenderer.DrawText(g, "Tentar de novo", fNormal, rt, cTexto, TC);
                }
                return y + E(40);
            }
            if (estadoRemoto == null) return Vazio(g, "Conectando a " + Alvo.Nome + "…", y, pad, W);
        }
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
        int hLinha1 = E(40), hLinha2 = E(36), hApp = E(50), hParte = E(46);
        bool aberto = app.Aberto(d);
        float f = Abertura(d);
        int hFechado = hLinha1 + E(4);
        int hPartes = 0;
        foreach (var a in d.Apps) hPartes += (int)Math.Round((a.Partes.Count * hParte + E(4)) * FracPartes(d, a));
        int hAberto = hLinha1 + hLinha2 + E(4) + d.Apps.Count * hApp + hPartes + (d.Apps.Count > 0 ? E(6) : 0);
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

        // Volume alto: faixa vermelha na lateral do cartao enquanto passar do limite
        if (g != null && app.AlertaAtivo(d) && rc.Height > E(24))
            Fundo(g, new Rectangle(x0, rc.Y + E(10), Math.Max(2, E(3)), rc.Height - E(20)), cAlto, E(1.5f));

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
            bool comSom = app.MostrarSom && (d.Medidor != null || (Alvo != null && !semNiveis));
            int nomeVis = Math.Max(E(20), nomeW - (comSom ? LarguraMed() + E(14) : 0));
            int nw = Math.Min(nomeVis, TextRenderer.MeasureText(g, d.Nome, fNegrito, Size.Empty, TextFormatFlags.NoPadding).Width);
            TextRenderer.DrawText(g, d.Nome, d.Padrao ? fNegrito : fNormal, new Rectangle(xTexto, r1.Y, nomeVis, r1.Height), cTexto, TF);
            int xm = xTexto + nw + E(10);
            if (comSom)
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
                bool comTeste = d.Fluxo == 0;   // saidas: botao de som de teste logo abaixo da setinha
                var rSl = new Rectangle(xTexto - E(10), y2, cw - (xTexto - x0) - E(44) - (comTeste ? E(34) : 0), hLinha2);
                var rTeste = new Rectangle(rCh.X, y2 + E(1), rCh.Width, E(34));
                if (f >= 1f) { Add(rMudo, Z_MUDO_D, d, null, null); Add(rSl, Z_SLIDER_D, d, null, null); if (comTeste) Add(rTeste, Z_TESTE, d, null, null); }
                if (g != null && comTeste)
                {
                    bool tocandoEste = testando == d.Id;
                    if (Sobre(Z_TESTE, d, null, null)) Fundo(g, rTeste, cHover, E(5));
                    TextRenderer.DrawText(g, tocandoEste ? "\uE767" : "\uE768", fGlifoP, rTeste, tocandoEste ? cAcento : cFraco, TC);
                }
                if (g != null)
                {
                    if (Sobre(Z_MUDO_D, d, null, null)) Fundo(g, rMudo, cHover, E(5));
                    TextRenderer.DrawText(g, Audio.GlifoVolume(d.Vol, d.Mudo, d.Fluxo).ToString(), fGlifo, rMudo,
                        d.Mudo ? cFraco : cTexto, TC);
                    bool alto = app.AlertaAtivo(d);
                    Slider(g, rSl, d.Vol, d.Mudo, Sobre(Z_SLIDER_D, d, null, null), true, alto);
                    TextRenderer.DrawText(g, ((int)Math.Round(d.Vol * 100)).ToString(), fNegrito,
                        new Rectangle(rSl.Right, y2, E(40), hLinha2), d.Mudo ? cFraco : alto ? cAlto : cTexto, TC);
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
                    float fp = FracPartes(d, a);
                    if (fp > 0.001f)
                    {
                        int hVis = (int)Math.Round((a.Partes.Count * hParte + E(4)) * fp);
                        int yPai = ya - hApp, yIni = ya, nzP = zonas.Count;
                        Region clipP = null;
                        if (g != null && fp < 1f) { clipP = g.Clip; g.SetClip(new Rectangle(x0, yIni, cw, hVis), CombineMode.Intersect); }
                        for (int i = 0; i < a.Partes.Count; i++) ParteLinha(g, d, a, i, yIni + i * hParte, x0, cw, colIcone, xTexto, hParte);
                        if (g != null) Arvore(g, colIcone, yPai + hApp / 2 + E(15), yIni, a.Partes.Count, hParte);
                        if (clipP != null) { g.Clip = clipP; clipP.Dispose(); }
                        if (fp < 1f) zonas.RemoveRange(nzP, zonas.Count - nzP);   // so da para clicar quando terminar de abrir
                        ya += hVis;
                    }
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
        bool movel = !a.Sistema && (a.Remoto || (Politica.Disponivel && a.Pids.Count > 0));
        var rLinha = new Rectangle(x0 + E(4), y, cw - E(8), h);
        // com as janelas abertas, o grupo todo (app + janelas) e uma coisa so: mesmo fundo no hover e mesma area
        int hJanelas = TemPartes(a) ? (int)Math.Round((a.Partes.Count * E(46) + E(4)) * FracPartes(d, a)) : 0;
        if (hJanelas > 0) rLinha.Height += hJanelas - E(2);
        // A linha inteira pode ser arrastada para outra saida (fica por baixo das outras zonas)
        if (movel) Add(rLinha, Z_NOME_A, d, a, null);
        else if (TemPartes(a)) Add(rLinha, Z_PARTES, d, a, null);   // fundo da linha para o clique duplo
        int ti = E(24);
        var rIc = new Rectangle(colIcone - E(17), y + (h - E(34)) / 2, E(34), E(34));
        Add(rIc, Z_MUDO_A, d, a, null);           // clique = mudo; arrastar = mover
        var rSl = new Rectangle(xTexto - E(10), y + E(24), cw - (xTexto - x0) - E(44), E(26));
        Add(rSl, Z_SLIDER_A, d, a, null);
        bool multi = TemPartes(a);
        var rMais = new Rectangle(x0 + cw - E(40), y + E(2), E(32), E(24));
        if (movel) Add(rMais, Z_SAIDA_A, d, a, null);   // "..." = escolher a saida pela lista
        // varias janelas: clique DUPLO na linha (fora da barra, do icone e do "...") abre/fecha a lista (OnMouseDoubleClick)
        if (g == null) return;

        bool linhaSobre = sobre != null && sobre.G == a && arrastoApp == null;
        bool arrastado = arrastoApp != null && arrastoApp.G == a;
        if (arrastado) Fundo(g, rLinha, Color.FromArgb(45, cAcento), E(6));
        else if (linhaSobre && movel) Fundo(g, rLinha, cHover, E(6));

        // fundo do icone ao passar o mouse: um pouco mais estreito, para nao cobrir os pontinhos de pegar
        if (Sobre(Z_MUDO_A, d, a, null)) Fundo(g, new Rectangle(rIc.X + E(4), rIc.Y, rIc.Width - E(6), rIc.Height), ControlPaint.Light(cHover, 0.1f), E(5));

        // pontinhos de "pegar" a linha (aparecem ao passar o mouse); desenhados depois, ficam sempre visiveis
        if (linhaSobre && movel)
            using (var b = new SolidBrush(cFraco))
                for (int i = 0; i < 3; i++)
                    for (int j = 0; j < 2; j++)
                        g.FillRectangle(b, x0 + E(5) + j * E(4), y + h / 2 - E(5) + i * E(4), Math.Max(1, E(2)), Math.Max(1, E(2)));
        var rImg = new Rectangle(colIcone - ti / 2, rIc.Y + (rIc.Height - ti) / 2, ti, ti);
        Bitmap bmp = a.Caminho != null ? Processos.Icone(a.Caminho, ti) : a.Remoto ? IconeRemoto(a) : null;
        if (multi)   // varias janelas: uma copia apagadinha atras do icone, como cartas empilhadas
        {
            var rTras = new Rectangle(rImg.X + E(7), rImg.Y - E(6), rImg.Width, rImg.Height);
            if (bmp != null)
                using (var ia = new System.Drawing.Imaging.ImageAttributes())
                {
                    ia.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = a.Mudo ? 0.18f : 0.42f });
                    g.DrawImage(bmp, rTras, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, ia);
                }
            else TextRenderer.DrawText(g, "", fGlifo, rTras, Color.FromArgb(90, cTexto), TC);
        }
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
        bool comMed = a.Medidores.Count > 0 || (a.Remoto && !a.Sistema && !semNiveis);
        int nomeMax = rMais.Left - xTexto - E(22) - (app.MostrarSom && comMed ? LarguraMed() + E(12) : 0);
        int nw = Math.Min(nomeMax, TextRenderer.MeasureText(g, a.Nome, fNormal, Size.Empty, TextFormatFlags.NoPadding).Width);
        TextRenderer.DrawText(g, a.Nome, fNormal, new Rectangle(xTexto, y + E(3), nomeMax, E(24)), a.Ativo ? cTexto : cFraco, TF);
        if (a.Saida != null)
            TextRenderer.DrawText(g, "", fGlifoP, new Rectangle(xTexto + nw + E(6), y + E(3), E(14), E(24)), cAcento, TC);
        if (app.MostrarSom && comMed && !arrastado)
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

    // Uma janela do app (aberta pela setinha): icone de mudo, nome da janela, barra e numero, no mesmo alinhamento dos apps
    void ParteLinha(Graphics g, Disp d, GrupoApp a, int i, int y, int x0, int cw, int colIcone, int xTexto, int h)
    {
        var pt = a.Partes[i];
        int ind = E(26);                       // recuo: as janelas ficam "penduradas" no app
        int xIc = colIcone + ind, xt = xTexto + ind;
        var rIc = new Rectangle(xIc - E(13), y + E(16), E(26), E(26));
        var zi = Add(rIc, Z_MUDO_P, d, a, null); zi.F = i;
        var rSl = new Rectangle(xt - E(10), y + E(21), cw - (xt - x0) - E(44), E(24));
        var zs = Add(rSl, Z_SLIDER_P, d, a, null); zs.F = i;
        if (g == null) return;
        if (SobreParte(Z_MUDO_P, a, i)) Fundo(g, rIc, ControlPaint.Light(cHover, 0.1f), E(5));
        // icone do proprio app, menor (clicar = mudo daquela janela); mudo: apagadinho com o selo de mudo, igual aos apps
        int ti = E(18);
        var rImg = new Rectangle(rIc.X + (rIc.Width - ti) / 2, rIc.Y + (rIc.Height - ti) / 2, ti, ti);
        Bitmap bmp = a.Caminho != null ? Processos.Icone(a.Caminho, ti) : a.Remoto ? IconeRemoto(a) : null;
        if (bmp != null)
        {
            if (pt.Mudo)
                using (var ia = new System.Drawing.Imaging.ImageAttributes())
                {
                    ia.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.35f });
                    g.DrawImage(bmp, rImg, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, ia);
                }
            else g.DrawImage(bmp, rImg);
        }
        else TextRenderer.DrawText(g, "\uE8D6", fGlifoP, rIc, pt.Mudo ? cFraco : cTexto, TC);
        if (pt.Mudo)
        {
            var rb = new Rectangle(rImg.Right - E(9), rImg.Bottom - E(9), E(12), E(12));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(cCartao)) g.FillEllipse(b, rb);
            g.SmoothingMode = SmoothingMode.None;
            TextRenderer.DrawText(g, "\uE74F", fGlifoP, rb, cTexto, TC);
        }
        TextRenderer.DrawText(g, pt.Nome, fPequena, new Rectangle(xt, y + E(2), cw - (xt - x0) - E(16), E(20)),
            pt.Mudo ? cFraco : cTexto, TF);
        Slider(g, rSl, pt.Vol, pt.Mudo, SobreParte(Z_SLIDER_P, a, i), false);
        TextRenderer.DrawText(g, ((int)Math.Round(pt.Vol * 100)).ToString(), fPequena,
            new Rectangle(rSl.Right, rSl.Y, E(40), rSl.Height), pt.Mudo ? cFraco : cTexto, TC);
    }

    // Linha que liga o icone do app as suas janelas (como uma arvore), com a ultima curva arredondada
    void Arvore(Graphics g, int x, int yTopo, int yIni, int n, int hParte)
    {
        int ind = E(26);
        float r = E(6);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pn = new Pen(Color.FromArgb(150, cTrilho), Math.Max(1f, E(1.5f))))
        {
            int yUlt = yIni + (n - 1) * hParte + E(29);
            g.DrawLine(pn, x, yTopo, x, yUlt - r);
            for (int i = 0; i < n; i++)
            {
                int yc = yIni + i * hParte + E(29);
                if (i < n - 1) g.DrawLine(pn, x, yc, x + ind - E(14), yc);
                else
                {
                    g.DrawArc(pn, x, yc - 2 * r, 2 * r, 2 * r, 90, 90);
                    g.DrawLine(pn, x + r, yc, x + ind - E(14), yc);
                }
            }
        }
        g.SmoothingMode = SmoothingMode.None;
    }

    void AplicarParte(GrupoApp a, int i, float v)
    {
        if (a == null || i < 0 || i >= a.Partes.Count) return;
        var pt = a.Partes[i];
        v = Math.Max(0f, Math.Min(1f, v));
        if (a.Remoto)   // janela de um app do outro PC: manda o pedido pela rede
        {
            pt.Vol = v; if (v > 0) pt.Mudo = false;
            var d = saidas.Find(x => x.Apps.Contains(a));
            if (d != null) EnviarRemoto("vp|" + d.Id + "|" + a.Exe + "|" + pt.Pid, "volp\t" + d.Id + "\t" + a.Exe + "\t" + pt.Pid + "\t" + Num(v), false);
            return;
        }
        foreach (var s in pt.Vols) try { s.SetMasterVolume(v, ref Audio.Ctx); if (pt.Mudo && v > 0) s.SetMute(false, ref Audio.Ctx); } catch { }
        pt.Vol = v;
        if (v > 0) pt.Mudo = false;
    }

    string NomeSaida(string id)
    {
        if (id == null) return "Padrão";
        foreach (var d in saidas) if (Politica.Igual(d.Id, id)) return d.Nome;
        return "Outra saída";
    }

    void Slider(Graphics g, Rectangle r, float v, bool mudo, bool ativo, bool grosso, bool alto = false)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float raio = E(10), x0 = r.Left + raio, x1 = r.Right - raio, cy = r.Top + r.Height / 2f,
              th = grosso ? Math.Max(3, E(5)) : Math.Max(2, E(4));
        float xv = x0 + (x1 - x0) * Math.Max(0, Math.Min(1, v));
        alto = alto && !mudo;
        float xl = x0 + (x1 - x0) * App.LimiteAlto;   // onde comeca a zona de volume alto
        using (var b = new SolidBrush(cTrilho))
        using (var p = Desenho.Arred(new RectangleF(x0, cy - th / 2, x1 - x0, th), th / 2)) g.FillPath(b, p);
        if (alto)   // o resto da barra (do volume ate o fim) fica vermelho clarinho
        {
            var clip = g.Clip;
            g.SetClip(new RectangleF(xl, cy - th, x1 - xl + th, th * 2), CombineMode.Intersect);
            using (var b = new SolidBrush(Color.FromArgb(110, cAlto)))
            using (var p = Desenho.Arred(new RectangleF(x0, cy - th / 2, x1 - x0, th), th / 2)) g.FillPath(b, p);
            g.Clip = clip;
        }
        if (xv - x0 >= 1)
        {
            using (var b = new SolidBrush(mudo ? cFraco : cAcento))
            using (var p = Desenho.Arred(new RectangleF(x0, cy - th / 2, xv - x0, th), th / 2)) g.FillPath(b, p);
            if (alto)   // ate 70% a cor normal; o que passa disso, vermelho
            {
                var clip = g.Clip;
                g.SetClip(new RectangleF(xl, cy - th, xv - xl + th, th * 2), CombineMode.Intersect);
                using (var b = new SolidBrush(cAlto))
                using (var p = Desenho.Arred(new RectangleF(x0, cy - th / 2, xv - x0, th), th / 2)) g.FillPath(b, p);
                g.Clip = clip;
            }
        }
        using (var b = new SolidBrush(cPolegar)) g.FillEllipse(b, xv - raio, cy - raio, raio * 2, raio * 2);
        using (var p = new Pen(cPolegarBorda)) g.DrawEllipse(p, xv - raio, cy - raio, raio * 2, raio * 2);
        float ri = ativo ? E(7) : E(5);
        using (var b = new SolidBrush(mudo ? cFraco : alto ? cAlto : cAcento)) g.FillEllipse(b, xv - ri, cy - ri, ri * 2, ri * 2);
        g.SmoothingMode = SmoothingMode.None;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(cFundo);
        if (fTitulo == null) return;
        Montar(g);
        g.ResetTransform();
        if (troca > 0f)
            using (var b = new SolidBrush(Color.FromArgb((int)(Math.Min(1f, troca * 1.15f) * 255), cFundo)))
                g.FillRectangle(b, 0, yConteudo, ClientSize.Width, ClientSize.Height);
        g.ResetClip();
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
                bool antes = p.Y > c.Top + c.Height / 2;
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
                SaidaApp(z.G, alvo.Id);
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
        if (apertada != null && (apertada.Tipo == Z_NOME_A || (apertada.Tipo == Z_PARTES && !apertada.G.Sistema && Politica.Disponivel) || apertada.Tipo == Z_PADRAO || (apertada.Tipo == Z_PERFIL && apertada.P != null) || (apertada.Tipo == Z_MUDO_A && apertada.G != null && !apertada.G.Sistema)) && e.Button == MouseButtons.Left &&
            Math.Abs(e.X - arrastoInicio.X) + Math.Abs(e.Y - arrastoInicio.Y) > E(6))
        {
            arrastoApp = apertada.Tipo == Z_PARTES ? new Zona { R = apertada.R, Tipo = Z_NOME_A, D = apertada.D, G = apertada.G } : apertada;
            apertada = null;
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
        if (z.Tipo == Z_SLIDER_D || z.Tipo == Z_SLIDER_A || z.Tipo == Z_SLIDER_P) { arrastando = z; Capture = true; DefinirValor(z, e.X); return; }
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
            if (z != null && z.G != null && !z.G.Sistema && (z.G.Remoto || (z.G.Pids.Count > 0 && Politica.Disponivel))) MenuSaida(z);
            else if (z != null && z.Tipo == Z_PERFIL && z.P != null) MenuPerfil(z);
            else if (z != null && z.Tipo == Z_PC) MenuPC(z);
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
            // linha mostrando onde o perfil vai ficar
            Rectangle c = arrastoIndice < chips.Count ? chips[arrastoIndice].Key : chips[chips.Count - 1].Key;
            int yl = arrastoIndice < chips.Count ? c.Top - E(2) : c.Bottom - E(1);
            Fundo(g, new Rectangle(c.X + E(8), yl, c.Width - E(16), Math.Max(2, E(3))), cAcento, E(1.5f));
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

    // Clique duplo na linha de um app com varias janelas (fora da barra, do icone e do "..."): abre/fecha as janelas
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left || arrastoApp != null) return;
        var z = ZonaEm(e.Location);
        if (z == null || z.G == null || z.D == null || !TemPartes(z.G) || (z.Tipo != Z_NOME_A && z.Tipo != Z_PARTES)) return;
        AlternarPartes(z.D, z.G);
    }

    void AlternarPartes(Disp d, GrupoApp a)
    {
        string k = d.Id + "|" + a.Exe;
        bool era = partesAbertas.Contains(k);
        if (era) partesAbertas.Remove(k); else partesAbertas.Add(k);
        if (app.Animacoes)
        {
            if (!partesAnim.ContainsKey(k)) partesAnim[k] = era ? 1f : 0f;
            Animar(); return;
        }
        Montar(null); Posicionar(); AtualizarSobre(); Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var z = ZonaEm(e.Location);
        // A roda so rola a lista; mudar volume pela roda no painel e opcional (desligado por padrao)
        if (app.RodaPainel && z != null && (z.Tipo == Z_SLIDER_D || z.Tipo == Z_SLIDER_A || z.Tipo == Z_SLIDER_P))
        {
            float passo = app.Passo / 100f * (e.Delta / 120f);
            if (z.Tipo == Z_SLIDER_P) { if (z.F >= 0 && z.F < z.G.Partes.Count) AplicarParte(z.G, z.F, z.G.Partes[z.F].Vol + passo); }
            else if (z.G != null) AplicarApp(z.G, z.G.Vol + passo);
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
        if (z.Tipo == Z_SLIDER_P) AplicarParte(z.G, z.F, v);
        else if (z.G != null) AplicarApp(z.G, v); else AplicarDisp(z.D, v);
        Invalidate();
    }

    void AplicarDisp(Disp d, float v)
    {
        v = Math.Max(0f, Math.Min(1f, v));
        if (Alvo != null)
        {
            d.Vol = v; if (v > 0) d.Mudo = false;
            EnviarRemoto("vd|" + d.Id, "vold\t" + d.Id + "\t" + Num(v), false);
            return;
        }
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
        if (a.Remoto)
        {
            a.Vol = v; if (v > 0) a.Mudo = false;
            var d = saidas.Find(x => x.Apps.Contains(a));
            if (d != null) EnviarRemoto("va|" + d.Id + "|" + a.Exe, "vola\t" + d.Id + "\t" + a.Exe + "\t" + Num(v), false);
            return;
        }
        foreach (var s in a.Vols)
            try
            {
                s.SetMasterVolume(v, ref Audio.Ctx);
                if (a.Mudo && v > 0) s.SetMute(false, ref Audio.Ctx);
            }
            catch { }
        a.Vol = v;
        if (v > 0) a.Mudo = false;
        if (!a.Sistema) app.LembrarVolume(a.Exe, v);
    }

    void Clique(Zona z)
    {
        switch (z.Tipo)
        {
            case Z_MUDO_D:
                if (Alvo != null) { z.D.Mudo = !z.D.Mudo; EnviarRemoto("md|" + z.D.Id, "mudod\t" + z.D.Id + "\t" + (z.D.Mudo ? 1 : 0), false); Invalidate(); break; }
                if (z.D.Ep != null) try { z.D.Ep.SetMute(!z.D.Mudo, ref Audio.Ctx); z.D.Mudo = !z.D.Mudo; } catch { }
                Invalidate();
                break;
            case Z_MUDO_A:
                if (z.G.Remoto)
                {
                    z.G.Mudo = !z.G.Mudo;
                    EnviarRemoto("ma|" + z.D.Id + "|" + z.G.Exe, "mudoa\t" + z.D.Id + "\t" + z.G.Exe + "\t" + (z.G.Mudo ? 1 : 0), false);
                    Invalidate(); break;
                }
                bool m = !z.G.Mudo;
                foreach (var s in z.G.Vols) try { s.SetMute(m, ref Audio.Ctx); } catch { }
                z.G.Mudo = m;
                Invalidate();
                break;
            case Z_USAR:
                if (Alvo != null) { EnviarRemoto("pad", "padrao\t" + z.D.Id, true); break; }
                if (!z.D.Padrao) { Audio.DefinirPadrao(z.D.Id); Recarregar(); }
                break;
            case Z_PC:
                {
                    var novo = z.Id == "" ? null : app.ParPorId(z.Id);
                    if ((novo == null && Alvo == null) || (novo != null && Alvo != null && novo.Id == Alvo.Id)) break;
                    int de = IndiceAlvo();
                    if (remoto != null) { remoto.Fechar(); remoto = null; }
                    Alvo = novo; estadoRemoto = null; remotoFalhou = false; pedindoEstado = false; pendentes.Clear(); enviando.Clear();
                    niveisRemotos.Clear(); pedindoNiveis = false; semNiveis = false; historico.Clear();
                    abertura.Clear(); rolagem = 0;
                    int para = IndiceAlvo();
                    Transicao(para > de ? 1 : -1);
                    Recarregar();
                    if (Alvo != null) PedirEstado();
                    Montar(null); Posicionar(); Invalidate();
                }
                break;
            case Z_LISTA_PERFIS:
                if (app.Perfis.Count == 0) { Clique(new Zona { Tipo = Z_NOVO_PERFIL, R = z.R }); break; }
                AbrirListaPerfis(!listaPerfis);
                break;
            case Z_TENTAR:
                remotoFalhou = false; estadoRemoto = null; if (remoto != null) { remoto.Fechar(); remoto = null; }
                PedirEstado(); Invalidate();
                break;
            case Z_SAIDA_A:
                MenuSaida(z);
                break;
            case Z_PERFIL:
                AbrirListaPerfis(false);
                app.AplicarPerfil(z.P, false);
                abertura.Clear();
                vistaAlvo[0] = vistaAlvo[1] = false; vista[0] = vista[1] = 0f;
                Recarregar();
                Depois();            // o Windows leva um instante para mover os apps
                Depois(1200);
                break;
            case Z_NOVO_PERFIL:
                listaPerfis = false; listaAb = 0f;
                Esconder();
                app.NovoPerfil();
                Mostrar();   // volta o painel depois de digitar o nome (ja com o perfil novo)
                break;
            case Z_MENU:
                app.AbrirConfiguracoes();
                app.AfastarConfig(Bounds);
                break;
            case Z_PRIORIDADE:
                app.AbrirConfiguracoes(5);
                app.AfastarConfig(Bounds);
                break;
            case Z_TESTE:
                {
                    string id = z.D.Id;
                    if (testando != null) break;
                    testando = id; Invalidate();
                    Action fim = () => { if (testando == id) { testando = null; Invalidate(); } };
                    if (Alvo != null)
                    {
                        EnviarRemoto("teste|" + id, "teste\t" + id, false);
                        var t = new Timer { Interval = 900 };
                        t.Tick += (s2, e2) => { t.Stop(); t.Dispose(); fim(); };
                        t.Start();
                    }
                    else SomTeste.Tocar(id, ok => { try { BeginInvoke(fim); } catch { } });
                }
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
            case Z_PARTES:
                break;   // clique simples no fundo da linha nao faz nada; o clique duplo abre/fecha (OnMouseDoubleClick)
            case Z_MUDO_P:
                if (z.F >= 0 && z.F < z.G.Partes.Count)
                {
                    var pt = z.G.Partes[z.F];
                    bool pm = !pt.Mudo;
                    if (z.G.Remoto)
                        EnviarRemoto("mp|" + z.D.Id + "|" + z.G.Exe + "|" + pt.Pid, "mudop\t" + z.D.Id + "\t" + z.G.Exe + "\t" + pt.Pid + "\t" + (pm ? 1 : 0), false);
                    else foreach (var s in pt.Vols) try { s.SetMute(pm, ref Audio.Ctx); } catch { }
                    pt.Mudo = pm;
                    Invalidate();
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

    // Clique direito num PC da barra: opcoes daquela conexao
    void MenuPC(Zona z)
    {
        var pc = z.Id == "" ? null : app.ParPorId(z.Id);
        string chave = "pc|" + z.Id;
        if (Alterna.FecharSeAberto(chave) || !Alterna.PodeAbrir(chave)) return;
        var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };
        if (pc == null)
        {
            menu.Items.Add(new ToolStripLabel("Este PC  ·  " + Environment.MachineName) { Tag = "fraco" });
            if (Alvo != null)
            {
                var ver = new ToolStripMenuItem("Mostrar este PC");
                ver.Click += (s, e) => Clique(z);
                menu.Items.Add(ver);
            }
        }
        else
        {
            bool ativo = Alvo != null && Alvo.Id == pc.Id;
            string estado = !ativo ? "" : remotoFalhou ? "  ·  sem conexão" : estadoRemoto != null ? "  ·  conectado" : "  ·  conectando…";
            menu.Items.Add(new ToolStripLabel(pc.Nome + estado) { Tag = "fraco" });
            if (!string.IsNullOrEmpty(pc.UltimoIP)) menu.Items.Add(new ToolStripLabel("Endereço " + pc.UltimoIP) { Tag = "fraco" });
            if (!ativo)
            {
                var ver = new ToolStripMenuItem("Mostrar este PC");
                ver.Click += (s, e) => Clique(z);
                menu.Items.Add(ver);
            }
            else
            {
                var rec = new ToolStripMenuItem("Reconectar");
                rec.Click += (s, e) => Clique(new Zona { Tipo = Z_TENTAR, R = z.R });
                menu.Items.Add(rec);
            }
            menu.Items.Add(new ToolStripSeparator());
            string id = pc.Id;
            var ren = new ToolStripMenuItem("Renomear…");
            ren.Click += (s, e) => BeginInvoke(new Action(() => { Esconder(); app.RenomearPar(id); Mostrar(); }));
            menu.Items.Add(ren);
            var esq = new ToolStripMenuItem("Esquecer este PC");
            var sim = new ToolStripMenuItem("Sim, esquecer \"" + pc.Nome + "\"");
            sim.Click += (s, e) => BeginInvoke(new Action(() => { app.RemoverPar(id); Recarregar(); }));
            esq.DropDownItems.Add(sim);
            esq.DropDownItems.Add(new ToolStripMenuItem("Cancelar"));
            menu.Items.Add(esq);
        }
        menu.Items.Add(new ToolStripSeparator());
        var cfg = new ToolStripMenuItem("Configurações de Outros PCs…");
        cfg.Click += (s, e) => BeginInvoke(new Action(() => { app.AbrirConfiguracoes(4); app.AfastarConfig(Bounds); }));
        menu.Items.Add(cfg);

        app.AplicarTema(menu);
        app.MenuAberto = true;
        menu.Closed += (s, e) => { Alterna.Fechou(chave); app.MenuAberto = false; app.ConferirPainel(); };
        Alterna.Abriu(chave, menu);
        MostrarDentro(menu, PointToClient(Cursor.Position), false);
    }

    void AbrirListaPerfis(bool abrir)
    {
        if (listaPerfis == abrir) return;
        listaPerfis = abrir;
        if (app.Animacoes) { Animar(); return; }
        listaAb = abrir ? 1f : 0f;
        Montar(null); Posicionar(); AtualizarSobre(); Invalidate();
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
            pad.Click += (s, e) => { if (Alvo != null) EnviarRemoto("pad", "padrao\t" + id, true); else { Audio.DefinirPadrao(id); Depois(); } };
            menu.Items.Add(pad);
        }
        var mu = new ToolStripMenuItem(mudo ? "Ativar som" : "Mudo");
        mu.Click += (s, e) =>
        {
            if (Alvo != null) { d.Mudo = !mudo; EnviarRemoto("md|" + id, "mudod\t" + id + "\t" + (d.Mudo ? 1 : 0), false); }
            else if (d.Ep != null) try { d.Ep.SetMute(!mudo, ref Audio.Ctx); d.Mudo = !mudo; } catch { }
            Invalidate();
        };
        menu.Items.Add(mu);
        var rec = new ToolStripMenuItem(app.Aberto(d) ? "Recolher" : "Expandir");
        rec.Click += (s, e) => Clique(new Zona { Tipo = Z_RECOLHER, D = d, R = z.R });
        menu.Items.Add(rec);
        if (d.Fluxo == 0)
        {
            bool liga = app.Alerta(d);
            var al = new ToolStripMenuItem("Alertar volume alto (acima de " + (int)Math.Round(App.LimiteAlto * 100) + "%)") { Checked = liga };
            al.Click += (s, e) => { app.AlertaVolume[id] = !liga; app.SalvarConfig(); Invalidate(); };
            menu.Items.Add(al);
        }
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
        pad.Click += (s, e) => { SaidaApp(a, null); if (Alvo == null) Depois(); };
        menu.Items.Add(pad);
        menu.Items.Add(new ToolStripSeparator());
        int ti = Math.Max(16, E(16));
        foreach (var d in saidas)
        {
            string id = d.Id;
            var it = new ToolStripMenuItem(d.Nome) { Checked = a.Saida != null && Politica.Igual(a.Saida, id) };
            it.Image = Desenho.Glifo(Audio.Glifo(d), ti, cTexto, 1f);
            it.Click += (s, e) => { SaidaApp(a, id); if (Alvo == null) Depois(); };
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

    void SaidaApp(GrupoApp a, string id)
    {
        if (a.Remoto) { EnviarRemoto("sa|" + a.Exe, "saida\t" + a.Exe + "\t" + (id ?? "-"), true); return; }
        app.DefinirSaidaApp(a.Exe, a.Pids, id);
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
    public Action Tema, Abrir, Sair;
    readonly uint msgSair = RegisterWindowMessage("Sintonia_Sair");
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string s);
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
        else if (m.Msg == (int)msgSair && Sair != null) Sair();
        else if (m.Msg == (int)Program.MsgConfig && Config != null) Config(m.WParam.ToInt32());
        else if (m.Msg == (int)Program.MsgPerfil && Perfil != null) Perfil(m.WParam.ToInt32());
        else if (m.Msg == (int)Program.MsgPedido && Pedido != null) Pedido();
        base.WndProc(ref m);
    }
}

// =====================================================================
//  Controle remoto entre PCs da mesma rede de casa
//  - so funciona entre PCs PAREADOS (codigo de 8 caracteres, uma vez so)
//  - cada conexao: troca de chaves ECDH P-256 nova, assinada pelas identidades
//    (ECDSA P-256) guardadas pelo proprio Windows; dados em AES-256-CBC + HMAC-SHA256
//  - nada trafega no dia a dia: so quando o painel mostra o outro PC
// =====================================================================
class ParPC
{
    public string Id, Nome, ChavePub, UltimoIP;
    public bool EuControlo;   // este PC controla o outro
    public bool MeControla;   // o outro PC pode controlar este
}

static class Rede
{
    public const int PortaTcp = 47321, PortaUdp = 47322;
    public static bool SoLocal;   // testes: escuta so no proprio PC (sem abrir para a rede)
    public const string Alfabeto = "23456789ABCDEFGHJKMNPQRSTUVWXYZ"; // sem 0/O, 1/I/L

    // ---------- identidade deste PC (chave guardada pelo Windows, nao sai do PC) ----------
    const string NomeChave = "Sintonia-Identidade";
    static CngKey Identidade()
    {
        if (CngKey.Exists(NomeChave)) return CngKey.Open(NomeChave);
        var p = new CngKeyCreationParameters { ExportPolicy = CngExportPolicies.None, KeyUsage = CngKeyUsages.Signing };
        return CngKey.Create(CngAlgorithm.ECDsaP256, NomeChave, p);
    }
    public static void ApagarIdentidade() { try { if (CngKey.Exists(NomeChave)) using (var k = CngKey.Open(NomeChave)) k.Delete(); } catch { } }
    public static byte[] MinhaChavePublica() { using (var k = Identidade()) return k.Export(CngKeyBlobFormat.EccPublicBlob); }
    public static byte[] Assinar(byte[] dados)
    {
        using (var k = Identidade()) using (var e = new ECDsaCng(k)) { e.HashAlgorithm = CngAlgorithm.Sha256; return e.SignData(dados); }
    }
    public static bool Verificar(byte[] pub, byte[] dados, byte[] assinatura)
    {
        try
        {
            using (var k = CngKey.Import(pub, CngKeyBlobFormat.EccPublicBlob)) using (var e = new ECDsaCng(k))
            { e.HashAlgorithm = CngAlgorithm.Sha256; return e.VerifyData(dados, assinatura); }
        }
        catch { return false; }
    }

    // ---------- ajudantes ----------
    public static byte[] Aleatorio(int n) { var b = new byte[n]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b); return b; }
    public static byte[] B(string s) { return Encoding.UTF8.GetBytes(s ?? ""); }
    public static string S(byte[] b) { return Encoding.UTF8.GetString(b); }
    public static byte[] Sha(params byte[][] partes)
    {
        using (var h = SHA256.Create()) { var j = Juntar(partes); return h.ComputeHash(j); }
    }
    public static byte[] Hmac(byte[] chave, params byte[][] partes) { using (var h = new HMACSHA256(chave)) return h.ComputeHash(Juntar(partes)); }
    public static bool Iguais(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        int d = 0; for (int i = 0; i < a.Length; i++) d |= a[i] ^ b[i]; return d == 0;  // tempo constante
    }
    public static byte[] Juntar(params byte[][] partes)
    {
        using (var ms = new MemoryStream())
        {
            foreach (var p in partes) { var x = p ?? new byte[0]; ms.Write(BitConverter.GetBytes(x.Length), 0, 4); ms.Write(x, 0, x.Length); }
            return ms.ToArray();
        }
    }
    public static byte[][] Separar(byte[] dados)
    {
        var l = new List<byte[]>(); int i = 0;
        while (i + 4 <= dados.Length)
        {
            int n = BitConverter.ToInt32(dados, i); i += 4;
            if (n < 0 || i + n > dados.Length) throw new InvalidDataException();
            var x = new byte[n]; Buffer.BlockCopy(dados, i, x, 0, n); l.Add(x); i += n;
        }
        return l.ToArray();
    }

    public static string NovoCodigo()
    {
        var b = Aleatorio(8); var sb = new StringBuilder();
        for (int i = 0; i < 8; i++) { sb.Append(Alfabeto[b[i] % Alfabeto.Length]); if (i == 3) sb.Append('-'); }
        return sb.ToString();
    }
    public static string NormalizarCodigo(string c) { return (c ?? "").ToUpperInvariant().Replace("-", "").Replace(" ", ""); }

    // ---------- so rede local (privada) e na mesma sub-rede de uma placa deste PC ----------
    public static bool EhRedeLocal(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily != AddressFamily.InterNetwork) return ip.IsIPv6LinkLocal;
        var b = ip.GetAddressBytes();
        bool privado = b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
        if (!privado) return false;
        foreach (var r in MinhasRedes())
        {
            var a = r.Key.GetAddressBytes(); var m = r.Value.GetAddressBytes(); bool igual = true;
            for (int i = 0; i < 4; i++) if ((a[i] & m[i]) != (b[i] & m[i])) igual = false;
            if (igual) return true;
        }
        return false;
    }
    public static List<KeyValuePair<IPAddress, IPAddress>> MinhasRedes()
    {
        var l = new List<KeyValuePair<IPAddress, IPAddress>>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var u in ni.GetIPProperties().UnicastAddresses)
                    if (u.Address.AddressFamily == AddressFamily.InterNetwork && u.IPv4Mask != null)
                        l.Add(new KeyValuePair<IPAddress, IPAddress>(u.Address, u.IPv4Mask));
            }
        }
        catch { }
        return l;
    }

    // ---------- procurar PCs com "pode ser controlado" ligado (so quando voce pede) ----------
    public class Achado { public string Id, Nome, IP; }
    public static List<Achado> Procurar(int ms)
    {
        List<Achado> porTcp = null;
        var t = new Thread(() => { try { porTcp = ProcurarTcp(ms); } catch { porTcp = new List<Achado>(); } }) { IsBackground = true };
        t.Start();
        var r = ProcurarUdp(ms);
        t.Join(ms + 2500);
        if (porTcp != null) foreach (var a in porTcp) if (!r.Exists(x => x.Id == a.Id)) r.Add(a);
        return r;
    }

    // tenta a porta do Sintonia em cada endereco da rede de casa (so redes pequenas, ate 1024 enderecos)
    public static List<Achado> ProcurarTcp(int ms)
    {
        var r = new List<Achado>();
        var tentativas = new List<KeyValuePair<TcpClient, IAsyncResult>>();
        var ips = new List<IPAddress>();
        try
        {
            foreach (var rede in MinhasRedes())
            {
                var a = rede.Key.GetAddressBytes(); var m = rede.Value.GetAddressBytes();
                if (!(a[0] == 10 || (a[0] == 172 && a[1] >= 16 && a[1] <= 31) || (a[0] == 192 && a[1] == 168))) continue;
                uint ip = (uint)(a[0] << 24 | a[1] << 16 | a[2] << 8 | a[3]), mask = (uint)(m[0] << 24 | m[1] << 16 | m[2] << 8 | m[3]);
                uint qtd = ~mask;
                if (qtd < 2 || qtd > 1024) continue;
                uint baseIp = ip & mask;
                for (uint h = 1; h < qtd; h++)
                {
                    uint x = baseIp | h;
                    if (x == ip) continue;
                    ips.Add(new IPAddress(new[] { (byte)(x >> 24), (byte)(x >> 16), (byte)(x >> 8), (byte)x }));
                }
            }
            foreach (var ip in ips)
            {
                var c = new TcpClient();
                try { tentativas.Add(new KeyValuePair<TcpClient, IAsyncResult>(c, c.BeginConnect(ip, PortaTcp, null, null))); } catch { c.Close(); }
            }
            var fim = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < fim && !tentativas.TrueForAll(x => x.Value.IsCompleted)) Thread.Sleep(50);
            foreach (var t in tentativas)
            {
                if (!t.Value.IsCompleted || !t.Key.Connected) continue;
                try
                {
                    t.Key.EndConnect(t.Value);
                    using (var c = new Canal(t.Key))
                    {
                        c.Prazo(800);
                        c.EnviarBruto(Juntar(B("QUEM")));
                        var p = Separar(c.ReceberBruto());
                        if (p.Length == 3 && S(p[0]) == "EU" && EhRedeLocal(c.Ip))
                            r.Add(new Achado { Id = S(p[1]), Nome = S(p[2]), IP = c.Ip.ToString() });
                    }
                }
                catch { }
            }
        }
        finally { foreach (var t in tentativas) try { t.Key.Close(); } catch { } }
        return r;
    }

    static List<Achado> ProcurarUdp(int ms)
    {
        var r = new List<Achado>();
        using (var u = new UdpClient(0))
        {
            u.EnableBroadcast = true;
            var msg = B("SINTONIA?1");
            foreach (var rede in MinhasRedes())
            {
                var a = rede.Key.GetAddressBytes(); var m = rede.Value.GetAddressBytes(); var bc = new byte[4];
                for (int i = 0; i < 4; i++) bc[i] = (byte)(a[i] | ~m[i]);
                try { u.Send(msg, msg.Length, new IPEndPoint(new IPAddress(bc), PortaUdp)); } catch { }
            }
            try { u.Send(msg, msg.Length, new IPEndPoint(IPAddress.Loopback, PortaUdp)); } catch { }
            var fim = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < fim)
            {
                u.Client.ReceiveTimeout = Math.Max(50, (int)(fim - DateTime.Now).TotalMilliseconds);
                try
                {
                    var de = new IPEndPoint(IPAddress.Any, 0);
                    var p = S(u.Receive(ref de)).Split('|');
                    if (p.Length == 3 && p[0] == "SINTONIA!" && EhRedeLocal(de.Address) && !r.Exists(x => x.Id == p[1]))
                        r.Add(new Achado { Id = p[1], Nome = p[2], IP = de.Address.ToString() });
                }
                catch { break; }
            }
        }
        return r;
    }
}

// Canal criptografado sobre TCP: [tamanho][dados]; depois do aperto de maos tudo vai cifrado
class Canal : IDisposable
{
    readonly TcpClient tcp;
    readonly NetworkStream ns;
    byte[] cifraEnvia, macEnvia, cifraRecebe, macRecebe;
    ulong seqEnvia, seqRecebe;
    public readonly IPAddress Ip;

    public Canal(TcpClient c)
    {
        tcp = c; ns = c.GetStream();
        Ip = ((IPEndPoint)c.Client.RemoteEndPoint).Address;
        tcp.NoDelay = true;
        Prazo(5000);
    }
    public void Prazo(int ms) { tcp.ReceiveTimeout = ms; tcp.SendTimeout = ms; }

    public void EnviarBruto(byte[] b)
    {
        var t = BitConverter.GetBytes(b.Length);
        ns.Write(t, 0, 4); ns.Write(b, 0, b.Length); ns.Flush();
    }
    public byte[] ReceberBruto()
    {
        var t = Ler(4); int n = BitConverter.ToInt32(t, 0);
        if (n < 0 || n > 4 * 1024 * 1024) throw new InvalidDataException();
        return Ler(n);
    }
    byte[] Ler(int n)
    {
        var b = new byte[n]; int i = 0;
        while (i < n) { int k = ns.Read(b, i, n - i); if (k <= 0) throw new IOException("fechado"); i += k; }
        return b;
    }

    // chaves separadas para cada direcao, tiradas do segredo da troca ECDH + resumo da conversa
    public void Chaves(byte[] segredo, byte[] resumo, bool souCliente)
    {
        var c2s = Rede.Hmac(segredo, Rede.B("cifra c->s"), resumo); var m2s = Rede.Hmac(segredo, Rede.B("mac c->s"), resumo);
        var s2c = Rede.Hmac(segredo, Rede.B("cifra s->c"), resumo); var ms2c = Rede.Hmac(segredo, Rede.B("mac s->c"), resumo);
        if (souCliente) { cifraEnvia = c2s; macEnvia = m2s; cifraRecebe = s2c; macRecebe = ms2c; }
        else { cifraEnvia = s2c; macEnvia = ms2c; cifraRecebe = c2s; macRecebe = m2s; }
    }

    public void Enviar(string texto) { EnviarBruto(Selar(texto)); }

    public string Receber()
    {
        var p = Rede.Separar(ReceberBruto());
        if (p.Length != 4 || p[0].Length != 8 || p[1].Length != 16) throw new InvalidDataException();
        if (!Rede.Iguais(Rede.Hmac(macRecebe, p[0], p[1], p[2]), p[3])) throw new InvalidDataException("mensagem alterada");
        if (BitConverter.ToUInt64(p[0], 0) != seqRecebe++) throw new InvalidDataException("mensagem repetida/fora de ordem");
        using (var aes = Aes.Create())
        {
            aes.Key = cifraRecebe; aes.IV = p[1]; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
            using (var d = aes.CreateDecryptor()) return Rede.S(d.TransformFinalBlock(p[2], 0, p[2].Length));
        }
    }

    public void Dispose() { try { ns.Close(); } catch { } try { tcp.Close(); } catch { } }

    public string Enviar2(string t) { Enviar(t); return Receber(); }
    // pacote cifrado que vai pela rede: [seq][iv][dados cifrados][HMAC de tudo]
    public byte[] Selar(string texto)
    {
        var iv = Rede.Aleatorio(16); byte[] cifrado;
        using (var aes = Aes.Create())
        {
            aes.Key = cifraEnvia; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
            using (var e = aes.CreateEncryptor()) { var p = Rede.B(texto); cifrado = e.TransformFinalBlock(p, 0, p.Length); }
        }
        var seq = BitConverter.GetBytes(seqEnvia++);
        return Rede.Juntar(seq, iv, cifrado, Rede.Hmac(macEnvia, seq, iv, cifrado));
    }

    static ECDiffieHellmanCng NovaTroca()
    {
        var dh = new ECDiffieHellmanCng(256);
        dh.KeyDerivationFunction = ECDiffieHellmanKeyDerivationFunction.Hash;
        dh.HashAlgorithm = CngAlgorithm.Sha256;
        return dh;
    }
    static byte[] Segredo(ECDiffieHellmanCng dh, byte[] pubOutro)
    {
        using (var k = ECDiffieHellmanCngPublicKey.FromByteArray(pubOutro, CngKeyBlobFormat.EccPublicBlob)) return dh.DeriveKeyMaterial(k);
    }

    // ---------- lado de quem CONTROLA ----------
    public static Canal Conectar(string ip, int msPrazo) { return Conectar(ip, msPrazo, Rede.PortaTcp); }
    public static Canal Conectar(string ip, int msPrazo, int porta)
    {
        var c = new TcpClient();
        var r = c.BeginConnect(IPAddress.Parse(ip), porta, null, null);
        if (!r.AsyncWaitHandle.WaitOne(msPrazo) || !c.Connected) { c.Close(); throw new IOException("sem resposta"); }
        c.EndConnect(r);
        return new Canal(c);
    }

    // abre uma sessao com um PC ja pareado (os dois provam quem sao com as identidades guardadas)
    public void SessaoCliente(string meuId, ParPC par)
    {
        using (var dh = NovaTroca())
        {
            var eph = dh.PublicKey.ToByteArray(); var nonce = Rede.Aleatorio(32);
            EnviarBruto(Rede.Juntar(Rede.B("SESSAO"), Rede.B(meuId), eph, nonce));
            var r = Rede.Separar(ReceberBruto());
            if (Rede.S(r[0]) != "OK") throw new IOException(r.Length > 1 ? Rede.S(r[1]) : "recusado");
            if (Rede.S(r[1]) != par.Id) throw new IOException("outro PC respondeu");
            var resumo = Rede.Sha(Rede.B("SESSAO"), Rede.B(meuId), eph, nonce, r[1], r[2], r[3]);
            if (!Rede.Verificar(Convert.FromBase64String(par.ChavePub), Rede.Juntar(Rede.B("S"), resumo), r[4])) throw new IOException("identidade nao confere");
            EnviarBruto(Rede.Juntar(Rede.B("PROVA"), Rede.Assinar(Rede.Juntar(Rede.B("C"), resumo))));
            Chaves(Segredo(dh, r[2]), resumo, true);
            if (Receber() != "PRONTO") throw new IOException("sessao recusada");
        }
    }

    // pareamento: troca as identidades, conferidas pelo codigo mostrado no outro PC
    public ParPC ParearCliente(string meuId, string meuNome, string codigo)
    {
        using (var dh = NovaTroca())
        {
            var eph = dh.PublicKey.ToByteArray(); var minhaPub = Rede.MinhaChavePublica();
            EnviarBruto(Rede.Juntar(Rede.B("PAREAR"), Rede.B(meuId), Rede.B(meuNome), minhaPub, eph));
            var r = Rede.Separar(ReceberBruto());
            if (Rede.S(r[0]) != "PAR") throw new IOException(r.Length > 1 ? Rede.S(r[1]) : "recusado");
            var resumo = Rede.Sha(Rede.B("PAREAR"), Rede.B(meuId), Rede.B(meuNome), minhaPub, eph, r[1], r[2], r[3], r[4]);
            var chave = Rede.Sha(Rede.B(Rede.NormalizarCodigo(codigo)), Segredo(dh, r[4]));
            EnviarBruto(Rede.Juntar(Rede.B("CONF"), Rede.Hmac(chave, Rede.B("C"), resumo)));
            var f = Rede.Separar(ReceberBruto());
            if (Rede.S(f[0]) != "CONF") throw new IOException(f.Length > 1 ? Rede.S(f[1]) : "codigo errado");
            if (!Rede.Iguais(f[1], Rede.Hmac(chave, Rede.B("S"), resumo))) throw new IOException("o outro PC nao confirmou o codigo");
            return new ParPC { Id = Rede.S(r[1]), Nome = Rede.S(r[2]), ChavePub = Convert.ToBase64String(r[3]), UltimoIP = Ip.ToString() };
        }
    }

    // ---------- lado de quem E CONTROLADO ----------
    // retorna: o par autenticado (sessao aberta) ou null (pareamento feito/recusado; conexao encerra)
    public ParPC AtenderServidor(string meuId, string meuNome, Func<string, ParPC> parPorId, Func<bool> permitido,
                                 Func<string> codigoAtivo, Action codigoErrado, Action<ParPC> pareado)
    {
        var r = Rede.Separar(ReceberBruto());
        string tipo = Rede.S(r[0]);
        using (var dh = NovaTroca())
        {
            var eph = dh.PublicKey.ToByteArray();
            if (tipo == "SESSAO")
            {
                var par = parPorId(Rede.S(r[1]));
                if (!permitido()) { EnviarBruto(Rede.Juntar(Rede.B("NAO"), Rede.B("este PC nao esta aceitando controle"))); return null; }
                if (par == null) { EnviarBruto(Rede.Juntar(Rede.B("NAO"), Rede.B("PC nao pareado"))); return null; }
                var nonce = Rede.Aleatorio(32);
                var resumo = Rede.Sha(r[0], r[1], r[2], r[3], Rede.B(meuId), eph, nonce);
                EnviarBruto(Rede.Juntar(Rede.B("OK"), Rede.B(meuId), eph, nonce, Rede.Assinar(Rede.Juntar(Rede.B("S"), resumo))));
                var p = Rede.Separar(ReceberBruto());
                if (Rede.S(p[0]) != "PROVA" || !Rede.Verificar(Convert.FromBase64String(par.ChavePub), Rede.Juntar(Rede.B("C"), resumo), p[1]))
                    return null;
                Chaves(Segredo(dh, r[2]), resumo, false);
                Enviar("PRONTO");
                return par;
            }
            if (tipo == "QUEM")
            {
                if (permitido()) EnviarBruto(Rede.Juntar(Rede.B("EU"), Rede.B(meuId), Rede.B(meuNome)));
                return null;
            }
            if (tipo == "PAREAR")
            {
                string codigo = codigoAtivo();
                if (codigo == null || !permitido()) { EnviarBruto(Rede.Juntar(Rede.B("NAO"), Rede.B("gere um codigo no outro PC primeiro"))); return null; }
                var minhaPub = Rede.MinhaChavePublica();
                EnviarBruto(Rede.Juntar(Rede.B("PAR"), Rede.B(meuId), Rede.B(meuNome), minhaPub, eph));
                var resumo = Rede.Sha(r[0], r[1], r[2], r[3], r[4], Rede.B(meuId), Rede.B(meuNome), minhaPub, eph);
                var chave = Rede.Sha(Rede.B(Rede.NormalizarCodigo(codigo)), Segredo(dh, r[4]));
                var f = Rede.Separar(ReceberBruto());
                if (Rede.S(f[0]) != "CONF" || !Rede.Iguais(f[1], Rede.Hmac(chave, Rede.B("C"), resumo)))
                {
                    codigoErrado();
                    EnviarBruto(Rede.Juntar(Rede.B("NAO"), Rede.B("codigo errado")));
                    return null;
                }
                EnviarBruto(Rede.Juntar(Rede.B("CONF"), Rede.Hmac(chave, Rede.B("S"), resumo)));
                pareado(new ParPC { Id = Rede.S(r[1]), Nome = Rede.S(r[2]), ChavePub = Convert.ToBase64String(r[3]), UltimoIP = Ip.ToString() });
                return null;
            }
        }
        return null;
    }
}

// Quem E CONTROLADO: espera conexoes (parado, sem gastar CPU) so enquanto a opcao estiver ligada
class Receptor
{
    readonly App app;
    TcpListener tl;
    UdpClient udp;
    volatile bool ligado;
    int conexoes;

    public Receptor(App a) { app = a; }
    public bool Ligado { get { return ligado; } }

    public void Ligar()
    {
        if (ligado) return;
        try
        {
            var end = Rede.SoLocal ? IPAddress.Loopback : IPAddress.Any;
            tl = new TcpListener(end, Rede.PortaTcp); tl.Start();
            udp = new UdpClient(new IPEndPoint(end, Rede.PortaUdp));
        }
        catch (Exception ex) { Program.Log(ex); Desligar(); return; }
        ligado = true;
        new Thread(LoopTcp) { IsBackground = true, Name = "Sintonia.RedeTcp", Priority = ThreadPriority.BelowNormal }.Start();
        new Thread(LoopUdp) { IsBackground = true, Name = "Sintonia.RedeUdp", Priority = ThreadPriority.BelowNormal }.Start();
    }

    public void Desligar()
    {
        ligado = false;
        try { if (tl != null) tl.Stop(); } catch { }
        try { if (udp != null) udp.Close(); } catch { }
        tl = null; udp = null;
    }

    void LoopTcp()
    {
        var l = tl;
        while (ligado && l != null)
        {
            TcpClient c;
            try { c = l.AcceptTcpClient(); } catch { break; }   // espera parado: zero CPU
            var ip = ((IPEndPoint)c.Client.RemoteEndPoint).Address;
            if (!Rede.EhRedeLocal(ip) || Interlocked.Increment(ref conexoes) > 4)
            {
                if (Rede.EhRedeLocal(ip)) Interlocked.Decrement(ref conexoes);
                c.Close(); continue;
            }
            var t = new Thread(() => Atender(c)) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
            t.Start();
        }
    }

    void LoopUdp()
    {
        var u = udp;
        while (ligado && u != null)
        {
            try
            {
                var de = new IPEndPoint(IPAddress.Any, 0);
                var msg = Rede.S(u.Receive(ref de));                // espera parado: zero CPU
                if (msg == "SINTONIA?1" && Rede.EhRedeLocal(de.Address))
                {
                    var r = Rede.B("SINTONIA!|" + app.MeuId + "|" + Environment.MachineName.Replace("|", ""));
                    u.Send(r, r.Length, de);
                }
            }
            catch { if (!ligado) break; }
        }
    }

    void Atender(TcpClient c)
    {
        int sessao = 0;
        try
        {
            using (var canal = new Canal(c))
            {
                var par = canal.AtenderServidor(app.MeuId, Environment.MachineName,
                    id => app.ParQueMeControla(id), () => app.Receptor,
                    () => app.CodigoValido(), () => app.CodigoErrado(),
                    p => app.Invocar(() => { app.ParearCom(p, false); return ""; }));
                if (par == null) return;
                app.Invocar(() => { app.AtualizarIpPar(par.Id, canal.Ip.ToString()); return ""; });
                sessao = app.SessaoComecou(par.Nome);
                canal.Prazo(60000);                                   // sem pedidos por 1 minuto: encerra
                while (ligado && app.Receptor)
                {
                    string pedido = canal.Receber();
                    string resposta = app.Invocar(() => app.ExecutarRemoto(pedido));
                    canal.Enviar(resposta ?? "");
                }
            }
        }
        catch { }
        finally
        {
            Interlocked.Decrement(ref conexoes);
            if (sessao != 0) app.SessaoTerminou(sessao);
        }
    }
}

// Quem CONTROLA: uma conexao com o outro PC so enquanto o painel mostra esse PC
class Remoto
{
    readonly App app;
    public readonly ParPC Par;
    readonly Control ui;
    readonly System.Collections.Concurrent.BlockingCollection<KeyValuePair<string, Action<string>>> fila =
        new System.Collections.Concurrent.BlockingCollection<KeyValuePair<string, Action<string>>>();
    volatile bool fim;
    Canal canal;

    public Remoto(App a, ParPC p, Control u)
    {
        app = a; Par = p; ui = u;
        new Thread(Rodar) { IsBackground = true, Name = "Sintonia.Remoto", Priority = ThreadPriority.BelowNormal }.Start();
    }

    // pede algo ao outro PC; a resposta chega na tela (null = falhou)
    public void Pedir(string comando, Action<string> resposta) { if (!fim) fila.Add(new KeyValuePair<string, Action<string>>(comando, resposta)); }

    public void Fechar() { fim = true; fila.Add(new KeyValuePair<string, Action<string>>(null, null)); }

    void Rodar()
    {
        while (!fim)
        {
            KeyValuePair<string, Action<string>> it;
            try { it = fila.Take(); } catch { break; }
            if (it.Key == null) break;
            string r = null;
            for (int tent = 0; tent < 2 && r == null && !fim; tent++)
            {
                try
                {
                    if (canal == null) canal = Abrir();
                    canal.Enviar(it.Key);
                    r = canal.Receber();
                }
                catch { if (canal != null) canal.Dispose(); canal = null; }
            }
            var resp = it.Value; var res = r;
            if (resp != null && !fim) try { ui.BeginInvoke(new Action(() => resp(res))); } catch { }
        }
        if (canal != null) canal.Dispose();
    }

    Canal Abrir()
    {
        Canal c = null;
        if (!string.IsNullOrEmpty(Par.UltimoIP)) try { c = Canal.Conectar(Par.UltimoIP, 1500); } catch { c = null; }
        if (c == null)
        {
            // o endereco mudou: procura o PC pela rede (so agora, so por um instante)
            var achado = Rede.Procurar(1200).Find(x => x.Id == Par.Id);
            if (achado == null) throw new IOException("nao encontrado");
            c = Canal.Conectar(achado.IP, 1500);
            var ip = achado.IP;
            try { ui.BeginInvoke(new Action(() => app.AtualizarIpPar(Par.Id, ip))); } catch { }
        }
        try { c.SessaoCliente(app.MeuId, Par); c.Prazo(8000); return c; }
        catch { c.Dispose(); throw; }
    }
}

// =====================================================================
//  Som de teste: um "plim" curto tocado so na saida escolhida
// =====================================================================
static class SomTeste
{
    [DllImport("winmm.dll")] static extern int waveOutGetNumDevs();
    [DllImport("winmm.dll")] static extern int waveOutMessage(IntPtr h, uint msg, IntPtr p1, IntPtr p2);
    [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr h, IntPtr dev, ref WAVEFORMATEX f, IntPtr cb, IntPtr inst, uint flags);
    [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int tam);
    [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, int tam);
    [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr h, IntPtr hdr, int tam);
    [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr h);
    [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr h);
    [StructLayout(LayoutKind.Sequential)]
    struct WAVEFORMATEX { public ushort wFormatTag, nChannels; public uint nSamplesPerSec, nAvgBytesPerSec; public ushort nBlockAlign, wBitsPerSample, cbSize; }
    [StructLayout(LayoutKind.Sequential)]
    struct WAVEHDR { public IntPtr lpData; public uint dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public uint dwFlags, dwLoops; public IntPtr lpNext, reserved; }

    static volatile bool tocando;

    // o indice do winmm que corresponde ao aparelho (id do Core Audio)
    static int Indice(string id)
    {
        int n = waveOutGetNumDevs();
        for (int i = 0; i < n; i++)
        {
            IntPtr pt = Marshal.AllocHGlobal(4);
            try
            {
                Marshal.WriteInt32(pt, 0);
                if (waveOutMessage(new IntPtr(i), 0x812 /*DRV_QUERYFUNCTIONINSTANCEIDSIZE*/, pt, IntPtr.Zero) != 0) continue;
                int tam = Marshal.ReadInt32(pt);
                if (tam <= 0 || tam > 4096) continue;
                IntPtr buf = Marshal.AllocHGlobal(tam);
                try
                {
                    if (waveOutMessage(new IntPtr(i), 0x811 /*DRV_QUERYFUNCTIONINSTANCEID*/, buf, new IntPtr(tam)) != 0) continue;
                    string s = Marshal.PtrToStringUni(buf);
                    if (string.Equals(s, id, StringComparison.OrdinalIgnoreCase)) return i;
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            finally { Marshal.FreeHGlobal(pt); }
        }
        return -1;
    }

    // duas notas suaves (como um "plim"), ~0,6 s, estereo 48 kHz
    static byte[] Plim()
    {
        const int taxa = 48000;
        int n = (int)(taxa * 0.62);
        var b = new byte[n * 4];
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)taxa, v = 0;
            if (t < 0.62) { double tt = t; v += Math.Sin(2 * Math.PI * 880 * tt) * Math.Exp(-tt * 9) * Math.Min(1, tt / 0.004); }
            if (t >= 0.13) { double tt = t - 0.13; v += Math.Sin(2 * Math.PI * 1318.5 * tt) * Math.Exp(-tt * 7) * Math.Min(1, tt / 0.004); }
            short x = (short)(Math.Max(-1, Math.Min(1, v * 0.28)) * 32767);
            b[i * 4] = (byte)x; b[i * 4 + 1] = (byte)(x >> 8); b[i * 4 + 2] = (byte)x; b[i * 4 + 3] = (byte)(x >> 8);
        }
        return b;
    }

    // toca em outra thread; fim e chamado quando acaba (true = tocou)
    public static void Tocar(string id, Action<bool> fim)
    {
        if (tocando) { if (fim != null) fim(false); return; }
        tocando = true;
        new Thread(() =>
        {
            bool ok = false;
            try { ok = Rodar(id); } catch { }
            tocando = false;
            if (fim != null) try { fim(ok); } catch { }
        }) { IsBackground = true, Name = "Sintonia.Teste" }.Start();
    }

    static bool Rodar(string id)
    {
        int dev = Indice(id);
        if (dev < 0) return false;
        var f = new WAVEFORMATEX { wFormatTag = 1, nChannels = 2, nSamplesPerSec = 48000, wBitsPerSample = 16, nBlockAlign = 4, nAvgBytesPerSec = 192000 };
        IntPtr h;
        if (waveOutOpen(out h, new IntPtr(dev), ref f, IntPtr.Zero, IntPtr.Zero, 0) != 0) return false;
        var pcm = Plim();
        IntPtr dados = Marshal.AllocHGlobal(pcm.Length);
        int tamH = Marshal.SizeOf(typeof(WAVEHDR));
        IntPtr ph = Marshal.AllocHGlobal(tamH);
        try
        {
            Marshal.Copy(pcm, 0, dados, pcm.Length);
            Marshal.StructureToPtr(new WAVEHDR { lpData = dados, dwBufferLength = (uint)pcm.Length }, ph, false);
            waveOutPrepareHeader(h, ph, tamH);
            waveOutWrite(h, ph, tamH);
            int ofs = (int)Marshal.OffsetOf(typeof(WAVEHDR), "dwFlags");
            for (int i = 0; i < 60 && (Marshal.ReadInt32(ph, ofs) & 1 /*WHDR_DONE*/) == 0; i++) Thread.Sleep(50);
            waveOutReset(h);
            waveOutUnprepareHeader(h, ph, tamH);
            return true;
        }
        finally { waveOutClose(h); Marshal.FreeHGlobal(dados); Marshal.FreeHGlobal(ph); }
    }
}

// =====================================================================
//  Atualizacao automatica (GitHub Releases), com assinatura digital
//  - so aceita um .exe assinado pela chave de quem publica (a publica fica aqui no codigo)
//  - nao manda nada sobre voce: so pergunta ao GitHub qual e a ultima versao
// =====================================================================
static class Atualizador
{
    public const string Repo = "oDethie/sintonia";
    // chave publica (ECDSA P-256) de quem assina as versoes
    const string ChavePublica = "RUNTMSAAAAD3tyNWHTtERezlVtajCtypF1fXGjhnAnlGYclxR/WIRNr7OefhP/3OIpvrlcH+2RcZ84dng7hY01OsdkxQj4JJ";
    const string NomeChaveAssinatura = "Sintonia-Lancamentos";

    public class Nova { public Version Versao; public string UrlExe, UrlSig; }

    public static Version Atual
    {
        get { var v = Assembly.GetExecutingAssembly().GetName().Version; return new Version(v.Major, v.Minor, Math.Max(0, v.Build)); }
    }

    static HttpWebRequest Pedido(string url)
    {
        ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
        var r = (HttpWebRequest)WebRequest.Create(url);
        r.UserAgent = "Sintonia-Atualizador";        // nome generico, sem dados seus
        r.Timeout = 20000; r.ReadWriteTimeout = 20000;
        r.AllowAutoRedirect = true;
        return r;
    }

    // pergunta ao GitHub a ultima versao; null se nao tiver nada mais novo (ou sem internet)
    public static Nova Procurar()
    {
        var req = Pedido("https://api.github.com/repos/" + Repo + "/releases/latest");
        req.Accept = "application/vnd.github+json";
        string json;
        using (var resp = req.GetResponse()) using (var sr = new StreamReader(resp.GetResponseStream())) json = sr.ReadToEnd();
        var d = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
        object o;
        if ((d.TryGetValue("draft", out o) && o is bool && (bool)o) || (d.TryGetValue("prerelease", out o) && o is bool && (bool)o)) return null;
        Version v;
        if (!Version.TryParse(((string)d["tag_name"]).TrimStart('v', 'V'), out v)) return null;
        v = new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        if (v <= Atual) return null;
        var n = new Nova { Versao = v };
        foreach (var a in (System.Collections.ArrayList)d["assets"])
        {
            var x = (Dictionary<string, object>)a;
            string nome = (string)x["name"], url = (string)x["browser_download_url"];
            if (!url.StartsWith("https://github.com/" + Repo + "/releases/download/", StringComparison.OrdinalIgnoreCase)) continue;
            if (nome == "Sintonia.exe") n.UrlExe = url;
            else if (nome == "Sintonia.exe.sig") n.UrlSig = url;
        }
        return n.UrlExe != null && n.UrlSig != null ? n : null;
    }

    public static byte[] Baixar(string url, int max)
    {
        using (var resp = Pedido(url).GetResponse())
        using (var st = resp.GetResponseStream())
        using (var ms = new MemoryStream())
        {
            var buf = new byte[65536]; int k;
            while ((k = st.Read(buf, 0, buf.Length)) > 0)
            {
                ms.Write(buf, 0, k);
                if (ms.Length > max) throw new IOException("arquivo grande demais");
            }
            return ms.ToArray();
        }
    }

    public static bool AssinaturaOk(byte[] exe, byte[] sig)
    {
        try
        {
            using (var k = CngKey.Import(Convert.FromBase64String(ChavePublica), CngKeyBlobFormat.EccPublicBlob))
            using (var e = new ECDsaCng(k)) { e.HashAlgorithm = CngAlgorithm.Sha256; return e.VerifyData(exe, sig); }
        }
        catch { return false; }
    }

    // baixa e confere tudo; devolve o .exe pronto (ou erro)
    public static byte[] BaixarConferido(Nova n, out string erro)
    {
        erro = null;
        var exe = Baixar(n.UrlExe, 8 * 1024 * 1024);
        var sig = Baixar(n.UrlSig, 4096);
        if (!AssinaturaOk(exe, sig)) { erro = "assinatura não confere"; return null; }
        return exe;
    }

    // troca o .exe: o que esta rodando pode ser renomeado (nao apagado) no Windows
    public static string Instalar(byte[] exe, string destino, Version esperada)
    {
        string dir = Path.GetDirectoryName(destino);
        string novo = Path.Combine(dir, "Sintonia.novo.exe"), velho = Path.Combine(dir, "Sintonia.velho.exe");
        try
        {
            File.WriteAllBytes(novo, exe);
            Version vf;
            if (!Version.TryParse(FileVersionInfo.GetVersionInfo(novo).FileVersion ?? "", out vf) ||
                new Version(vf.Major, vf.Minor, Math.Max(0, vf.Build)) != esperada)
            { File.Delete(novo); return "versão do arquivo não confere"; }
            if (File.Exists(velho)) File.Delete(velho);
            File.Move(destino, velho);
            try { File.Move(novo, destino); }
            catch { File.Move(velho, destino); throw; }
            return null;
        }
        catch (Exception ex) { try { if (File.Exists(novo)) File.Delete(novo); } catch { } return ex.Message; }
    }

    public static void Limpar(string exe)
    {
        string dir = Path.GetDirectoryName(exe);
        foreach (var f in new[] { "Sintonia.velho.exe", "Sintonia.novo.exe" })
            try { string c = Path.Combine(dir, f); if (File.Exists(c)) File.Delete(c); } catch { }
    }

    // ---- para quem publica ----
    public static string Assinar(string arquivo)
    {
        CngKey k = CngKey.Exists(NomeChaveAssinatura) ? CngKey.Open(NomeChaveAssinatura)
            : CngKey.Create(CngAlgorithm.ECDsaP256, NomeChaveAssinatura, new CngKeyCreationParameters { ExportPolicy = CngExportPolicies.None, KeyUsage = CngKeyUsages.Signing });
        using (k)
        using (var e = new ECDsaCng(k))
        {
            e.HashAlgorithm = CngAlgorithm.Sha256;
            if (File.Exists(arquivo)) File.WriteAllBytes(arquivo + ".sig", e.SignData(File.ReadAllBytes(arquivo)));
            return Convert.ToBase64String(k.Export(CngKeyBlobFormat.EccPublicBlob));
        }
    }

    // teste: procura, baixa, confere e instala numa copia (sem reiniciar)
    public static IEnumerable<string> Teste()
    {
        var l = new List<string>();
        try
        {
            string exe = Assembly.GetExecutingAssembly().Location;
            l.Add("versão atual: " + Atual);
            var n = Procurar();
            if (n == null) { l.Add("nada mais novo no GitHub"); return l; }
            l.Add("nova: " + n.Versao + "  " + n.UrlExe);
            string erro;
            var bin = BaixarConferido(n, out erro);
            l.Add(bin == null ? "RECUSADO: " + erro : "baixado e assinatura ok (" + bin.Length + " bytes)");
            if (bin == null) return l;
            var falso = (byte[])bin.Clone(); falso[falso.Length / 2] ^= 1;
            l.Add("arquivo alterado em 1 byte: " + (AssinaturaOk(falso, Baixar(n.UrlSig, 4096)) ? "ACEITO (ERRO)" : "recusado (ok)"));
            string r = Instalar(bin, exe, n.Versao);
            l.Add(r == null ? "instalado: " + FileVersionInfo.GetVersionInfo(exe).FileVersion : "falhou: " + r);
        }
        catch (Exception ex) { l.Add("ERRO " + ex.Message); }
        return l;
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

    // ---- outros PCs (controle pela rede) ----
    public bool Controlador;                 // este PC controla outros (padrao: desligado)
    public bool AtualizarSozinho = true;
    // prioridade: lista ordenada de aparelhos (saidas e microfones), incluindo os desconectados
    public bool PrioridadeAuto;
    public readonly List<string>[] Prioridade = { new List<string>(), new List<string>() };
    public class Conhecido { public string Id, Nome; public int Fluxo, Forma; }
    public readonly Dictionary<string, Conhecido> Conhecidos = new Dictionary<string, Conhecido>(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string>[] ultimosAtivos = { null, null };
    readonly Timer tPrio = new Timer { Interval = 1500 };
    // escolha feita a mao sobe para 1o; trocas do Windows (aparelho saiu) ou do proprio Sintonia nao contam
    DateTime ultimoEventoAparelho = DateTime.MinValue, ultimaTrocaAuto = DateTime.MinValue;
    readonly string[] ultimoPadrao = new string[2];
    public string EstadoAtualizacao = "";     // texto para as Configuracoes
    Atualizador.Nova novaVersao; byte[] novaBin;
    readonly Timer tAtualizacao = new Timer { Interval = 90 * 1000 };   // 1a vez 90 s depois de abrir; depois a cada 12 h
    bool procurandoAtualizacao;
    bool receptorLigado;                     // outros PCs podem controlar este (padrao: desligado)
    public string MeuId;
    readonly List<ParPC> pares = new List<ParPC>();
    Receptor servidor;
    readonly object travaCodigo = new object();
    string codigo; DateTime codigoAte; int codigoErros;
    List<Disp> remCache; DateTime remCacheT;
    readonly Timer tRemCache = new Timer { Interval = 15000 };
    public readonly Dictionary<string, bool> Abertos = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    public int Passo = 2;
    public int AlturaMax = 90;   // % da altura livre da tela; passando disso o painel rola
    string tema = "auto";
    public readonly uint[] AtMod = { 0, 0, 0 };   // atalhos: nenhum por padrao (a pessoa escolhe em Configuracoes > Atalhos)
    public readonly uint[] AtVk = { 0, 0, 0 };
    public static readonly string[] AtNome = { "Abrir o painel", "Próxima saída", "Mudo geral" };
    readonly bool[] atOk = { true, true, true };
    public readonly Dictionary<string, string> Regras = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string> Nomes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public readonly HashSet<string> Ocultos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    // volume alto: acima disso a barra fica vermelha e aparece um alerta (protege os ouvidos)
    public const float LimiteAlto = 0.70f;
    public readonly Dictionary<string, bool> AlertaVolume = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);   // escolha por saida
    // vem ligado nos fones e headsets; nas outras saidas so se a pessoa ligar
    public bool Alerta(Disp d)
    {
        if (d == null || d.Fluxo != 0) return false;
        bool v;
        if (AlertaVolume.TryGetValue(d.Id, out v)) return v;
        return d.Forma == 3 /*fones*/ || d.Forma == 5 /*headset*/;
    }
    public bool AlertaAtivo(Disp d) { return !d.Mudo && d.Vol > LimiteAlto + 0.004f && Alerta(d); }
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
        if (string.IsNullOrEmpty(MeuId)) { MeuId = Guid.NewGuid().ToString("N"); if (!primeiraVez) SalvarConfig(); }
        Audio.ComMedidor = MostrarSom;
        invocador.CreateControl();
        var h = invocador.Handle;

        painel = new Painel(this);
        msg = new Mensageiro();
        msg.Atalho = AoAtalho;
        msg.Tema = () => { chaveIcone = null; AtualizarIcone(); };
        msg.Abrir = () => { if (!painel.Visible) painel.Mostrar(); };
        msg.Sair = () => Sair();
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

        servidor = new Receptor(this);
        if (receptorLigado) servidor.Ligar();
        tRemCache.Tick += (s, e) => { tRemCache.Stop(); Audio.Liberar(remCache); remCache = null; Aparar(); };

        vigia = new Vigia(
            tipo => Postar(() =>
            {
                if (tipo == 1) { ultimoEventoAparelho = DateTime.Now; tPrio.Stop(); tPrio.Start(); }   // aparelho entrou/saiu: confere a prioridade (espera o Bluetooth assentar)
                if (tipo == 2) PadraoMudou();
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
            ConferirPrioridade();
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
        // instalado na pasta de programas: aparece em Configuracoes > Aplicativos e no menu Iniciar
        if (Instalador.EstaInstalado) new Thread(Instalador.GarantirRegistro) { IsBackground = true, Priority = ThreadPriority.Lowest }.Start();

        tPrio.Tick += (s, e) => { tPrio.Stop(); ConferirPrioridade(); };

        // atualizacao automatica
        try { Atualizador.Limpar(Application.ExecutablePath); } catch { }
        tAtualizacao.Tick += (s, e) => { tAtualizacao.Interval = 12 * 3600 * 1000; ProcurarAtualizacao(false); };
        if (AtualizarSozinho) tAtualizacao.Start();
        if (Program.Atualizado)
        {
            var tAv = new Timer { Interval = 2500 };
            tAv.Tick += (s, e) => { tAv.Stop(); tAv.Dispose(); Avisar('\uE895', "Sintonia atualizado para " + Atualizador.Atual.ToString(3)); };
            tAv.Start();
        }
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
        string quem = ControladoPor();
        bool atv = DateTime.Now < atvAte;
        string chave = mudoIcone + "|" + n + "|" + claro + "|" + (quem != null) + (atv ? "|" + atvCaminho + atvGlifo + atvTipo : "");
        if (chave != chaveIcone)
        {
            chaveIcone = chave;
            Color cor = claro ? Color.FromArgb(28, 28, 28) : Color.White;
            Bitmap bmp;
            if (atv)
            {
                Bitmap ic = atvCaminho != null ? Processos.Icone(atvCaminho, n) : null;   // fica no cache, nao descartar
                bmp = Desenho.Atividade(n, ic, atvGlifo, cor, atvTipo, CorDeDestaque(!claro));
            }
            else
            {
                bmp = Desenho.Bandeja(n, cor, mudoIcone);
                if (quem != null) Desenho.Conectado(bmp, CorDeDestaque(!claro), claro ? Color.White : Color.FromArgb(32, 32, 32));
            }
            using (bmp)
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
        if (quem != null) nome += "\nControlado por " + quem;
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

    // Volume de cada app (pelo nome do programa). O Windows guarda o volume pelo caminho do programa,
    // que muda a cada atualizacao em apps como o WhatsApp: ai ele volta para 100%. O Sintonia lembra por conta propria.
    public readonly Dictionary<string, float> VolApps = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    Timer tSalvarVol;
    public void LembrarVolume(string exe, float v)
    {
        if (!Lembrar || string.IsNullOrEmpty(exe)) return;
        VolApps[exe] = v;
        if (tSalvarVol == null) { tSalvarVol = new Timer { Interval = 1500 }; tSalvarVol.Tick += (s, e) => { tSalvarVol.Stop(); SalvarConfig(); }; }
        tSalvarVol.Stop(); tSalvarVol.Start();   // grava uma vez so, quando parar de arrastar
    }

    // Devolve o volume lembrado aos apps que estao tocando (soExe: so esse app; soPid: so essa janela)
    void RestaurarVolumes(string soExe, uint soPid = 0)
    {
        if (!Lembrar || VolApps.Count == 0) return;
        var ds = Audio.Listar(0, true, true, null, false);
        foreach (var d in ds)
            foreach (var g in d.Apps)
            {
                float v;
                if (g.Sistema || g.Remoto || (soExe != null && !string.Equals(g.Exe, soExe, StringComparison.OrdinalIgnoreCase))) continue;
                if (!VolApps.TryGetValue(g.Exe, out v)) continue;
                foreach (var pt in g.Partes)
                {
                    if ((soPid != 0 && pt.Pid != soPid) || Math.Abs(pt.Vol - v) < 0.005f) continue;
                    foreach (var sv in pt.Vols) try { sv.SetMasterVolume(v, ref Audio.Ctx); } catch { }
                }
            }
        Audio.Liberar(ds);
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

        if (Lembrar && VolApps.ContainsKey(exe))
        {
            RestaurarVolumes(exe, pid);
            string so = exe; uint sp = pid;   // o app pode abrir mais de uma sessao logo depois: confere de novo
            var tv = new Timer { Interval = 1200 };
            tv.Tick += (s, e) => { tv.Stop(); tv.Dispose(); RestaurarVolumes(so, sp); };
            tv.Start();
        }

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
    public const int MaxPCs = 2;   // outros PCs que este controla (a barra do painel fica com no maximo 3)

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
        if (TemNovaVersao)
        {
            var mAt = new ToolStripMenuItem("Atualizar para a versão " + NovaVersaoTexto);
            mAt.Click += (s, e) => Postar(() => TentarInstalar(true));
            menu.Items.Add(mAt);
        }
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

    // =================== outros PCs ===================
    public bool Receptor
    {
        get { return receptorLigado; }
        set
        {
            receptorLigado = value;
            if (value) servidor.Ligar(); else { servidor.Desligar(); lock (travaCodigo) codigo = null; }
            SalvarConfig();
        }
    }
    public bool ReceptorAtivo { get { return servidor != null && servidor.Ligado; } }

    // =================== prioridade de saidas e microfones ===================
    public void DefinirPrioridadeAuto(bool v)
    {
        PrioridadeAuto = v; SalvarConfig();
        if (v) { ultimosAtivos[0] = ultimosAtivos[1] = null; ConferirPrioridade(); }
        PainelMudou();
    }

    // guarda os aparelhos vistos (novos entram no fim da lista) e devolve os conectados agora
    HashSet<string> AtualizarConhecidos(int fluxo)
    {
        var ativos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ds = Audio.Listar(fluxo, false, false, null);
        bool mudou = false, vazia = Prioridade[fluxo].Count == 0;
        foreach (var d in ds)
        {
            ativos.Add(d.Id);
            Conhecido k;
            if (!Conhecidos.TryGetValue(d.Id, out k) || k.Nome != d.NomeOriginal || k.Forma != d.Forma)
            { Conhecidos[d.Id] = new Conhecido { Id = d.Id, Nome = d.NomeOriginal, Fluxo = fluxo, Forma = d.Forma }; mudou = true; }
            if (!Prioridade[fluxo].Contains(d.Id)) { Prioridade[fluxo].Add(d.Id); mudou = true; }
        }
        Audio.Liberar(ds);
        if (vazia)
        {
            // primeira vez: a que esta em uso agora comeca em primeiro (nada muda ao ligar a prioridade)
            string pad = Audio.PadraoId(fluxo);
            int i = Prioridade[fluxo].FindIndex(x => Politica.Igual(x, pad));
            if (i > 0) { var x0 = Prioridade[fluxo][i]; Prioridade[fluxo].RemoveAt(i); Prioridade[fluxo].Insert(0, x0); }
        }
        if (mudou) SalvarConfig();
        return ativos;
    }

    // um aparelho entrou ou saiu: usa o primeiro da lista que estiver conectado.
    // (so age quando o conjunto de aparelhos muda: uma troca feita a mao fica valendo ate la)
    public void ConferirPrioridade()
    {
        for (int f = 0; f < 2; f++)
        {
            if (ultimoPadrao[f] == null) ultimoPadrao[f] = Audio.PadraoId(f);
            var ativos = AtualizarConhecidos(f);
            var antes = ultimosAtivos[f];
            ultimosAtivos[f] = ativos;
            if (!PrioridadeAuto || (antes != null && antes.SetEquals(ativos))) continue;
            string melhor = Prioridade[f].Find(id => ativos.Contains(id));
            if (melhor == null || Politica.Igual(melhor, Audio.PadraoId(f))) continue;
            ultimaTrocaAuto = DateTime.Now;
            ultimoPadrao[f] = melhor;
            Audio.DefinirPadrao(melhor);
            Conhecido k = null; Conhecidos.TryGetValue(melhor, out k);
            string nome = NomeConhecido(melhor);
            Avisar(f == 0 ? Audio.Glifo(new Disp { Fluxo = 0, Forma = k != null ? k.Forma : 0 }) : '\uE720', "Usando " + nome);
        }
        AtualizarConfig();
    }

    // a saida/microfone padrao mudou: se foi escolha da pessoa (e a troca automatica esta ligada), sobe para 1o
    void PadraoMudou()
    {
        for (int f = 0; f < 2; f++)
        {
            string pad = Audio.PadraoId(f);
            if (pad == null || Politica.Igual(pad, ultimoPadrao[f])) continue;
            ultimoPadrao[f] = pad;
            if (!PrioridadeAuto) continue;
            var agora = DateTime.Now;
            if ((agora - ultimoEventoAparelho).TotalSeconds < 5 || (agora - ultimaTrocaAuto).TotalSeconds < 5) continue;
            var l = Prioridade[f];
            int i = l.FindIndex(x => Politica.Igual(x, pad));
            if (i <= 0) continue;
            string id = l[i]; l.RemoveAt(i); l.Insert(0, id);
            SalvarConfig();
            AtualizarConfig();
        }
    }

    public void MoverPrioridade(int fluxo, string id, int para)
    {
        var l = Prioridade[fluxo];
        int de = l.IndexOf(id);
        if (de < 0) return;
        l.RemoveAt(de);
        if (para > de) para--;
        l.Insert(Math.Max(0, Math.Min(l.Count, para)), id);
        SalvarConfig();
        if (PrioridadeAuto) { ultimosAtivos[fluxo] = null; ConferirPrioridade(); }
    }

    public void EsquecerAparelho(string id)
    {
        Prioridade[0].Remove(id); Prioridade[1].Remove(id); Conhecidos.Remove(id);
        SalvarConfig();
    }

    public string NomeConhecido(string id)
    {
        string n; Conhecido k;
        if (Nomes.TryGetValue(id, out n)) return n;
        return Conhecidos.TryGetValue(id, out k) ? k.Nome : "?";
    }

    // =================== atualizacao ===================
    public void DefinirAtualizarSozinho(bool v)
    {
        AtualizarSozinho = v; SalvarConfig();
        if (v) { tAtualizacao.Interval = 90 * 1000; tAtualizacao.Start(); } else tAtualizacao.Stop();
    }

    // agora: o pedido veio da pessoa (botao), entao instala sem esperar
    public void ProcurarAtualizacao(bool agora)
    {
        if (procurandoAtualizacao) return;
        if (novaVersao != null && novaBin != null) { TentarInstalar(agora); return; }
        procurandoAtualizacao = true;
        EstadoAtualizacao = "Procurando…"; AtualizarConfig();
        new Thread(() =>
        {
            Atualizador.Nova n = null; byte[] bin = null; string erro = null;
            try
            {
                n = Atualizador.Procurar();
                if (n != null) bin = Atualizador.BaixarConferido(n, out erro);
            }
            catch (Exception ex) { erro = ex is WebException ? "sem conexão" : ex.Message; }
            Postar(() =>
            {
                procurandoAtualizacao = false;
                if (n == null) EstadoAtualizacao = erro != null ? "Não deu para conferir (" + erro + ")" : "Você está na versão mais nova";
                else if (bin == null) EstadoAtualizacao = "Versão " + n.Versao + " recusada: " + erro;
                else
                {
                    novaVersao = n; novaBin = bin;
                    EstadoAtualizacao = "Versão " + n.Versao.ToString(3) + " pronta";
                    if (AtualizarSozinho || agora) TentarInstalar(agora);
                }
                AtualizarConfig();
            });
        }) { IsBackground = true, Priority = ThreadPriority.Lowest }.Start();
    }

    public bool TemNovaVersao { get { return novaVersao != null && novaBin != null; } }
    public string NovaVersaoTexto { get { return novaVersao == null ? "" : novaVersao.Versao.ToString(3); } }

    // instala so quando nao atrapalha: sem painel/menu/configuracoes abertos e sem jogo em tela cheia
    public void TentarInstalar(bool agora)
    {
        if (!TemNovaVersao) return;
        bool ocupado = painel.Visible || MenuAberto || TelaCheiaAtiva() || (!agora && config != null && !config.IsDisposed);
        if (ocupado && !agora)
        {
            var t = new Timer { Interval = 5 * 60 * 1000 };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); TentarInstalar(false); };
            t.Start();
            return;
        }
        string exe = Application.ExecutablePath;
        string r = Atualizador.Instalar(novaBin, exe, novaVersao.Versao);
        if (r != null) { EstadoAtualizacao = "Não deu para instalar (" + r + ")"; AtualizarConfig(); return; }
        // abre a versao nova e fecha esta
        SalvarConfig();
        if (servidor != null) servidor.Desligar();
        vigia.Parar();
        icone.Visible = false;
        try { Process.Start(exe, "--atualizado"); } catch { }
        Application.Exit();
    }

    // ---- quem esta controlando este PC agora (so enquanto o painel do outro mostra este) ----
    readonly Dictionary<int, string> sessoes = new Dictionary<int, string>();
    int proxSessao;
    public int SessaoComecou(string nome)
    {
        int id;
        lock (sessoes) { id = ++proxSessao; sessoes[id] = nome; }
        Postar(() => { chaveIcone = null; AtualizarIcone(); PainelMudou(); });
        return id;
    }
    public void SessaoTerminou(int id)
    {
        lock (sessoes) sessoes.Remove(id);
        Postar(() => { chaveIcone = null; AtualizarIcone(); PainelMudou(); });
    }
    public string ControladoPor()
    {
        lock (sessoes)
        {
            if (sessoes.Count == 0) return null;
            var l = new List<string>();
            foreach (var n in sessoes.Values) if (!l.Contains(n)) l.Add(n);
            return string.Join(" e ", l);
        }
    }

    // o outro PC mudou algo: o icone da bandeja mostra o item com uma seta por um instante
    string atvCaminho; char atvGlifo; int atvTipo;   // tipo: 1 subiu, -1 desceu, 2 mudo, 3 som de volta
    DateTime atvAte;
    Timer tAtv;
    void Atividade(string caminho, char glifo, int tipo)
    {
        atvCaminho = caminho; atvGlifo = glifo; atvTipo = tipo;
        atvAte = DateTime.Now.AddMilliseconds(1500);
        if (tAtv == null)
        {
            tAtv = new Timer { Interval = 1500 };
            tAtv.Tick += (s, e) => { if (DateTime.Now >= atvAte) { tAtv.Stop(); AtualizarIcone(); } };
        }
        tAtv.Stop(); tAtv.Start();
        AtualizarIcone();
    }

    public void DefinirControlador(bool v) { Controlador = v; SalvarConfig(); PainelMudou(); }

    public List<ParPC> Pares() { lock (pares) return new List<ParPC>(pares); }
    public List<ParPC> Controlados() { lock (pares) return pares.FindAll(x => x.EuControlo); }
    public ParPC ParPorId(string id) { lock (pares) return pares.Find(x => x.Id == id && x.EuControlo); }
    public ParPC ParQueMeControla(string id) { lock (pares) return pares.Find(x => x.Id == id && x.MeControla); }

    public string Invocar(Func<string> f)
    {
        try { return invocador.IsHandleCreated ? (string)invocador.Invoke(f) : null; } catch { return null; }
    }

    // codigo de pareamento: 8 caracteres, vale 2 minutos e 3 tentativas
    public string GerarCodigo()
    {
        lock (travaCodigo) { codigo = Rede.NovoCodigo(); codigoAte = DateTime.Now.AddMinutes(2); codigoErros = 0; return codigo; }
    }
    public string CodigoValido()
    {
        lock (travaCodigo) return codigo != null && DateTime.Now < codigoAte && codigoErros < 3 ? codigo : null;
    }
    public int CodigoSegundos() { lock (travaCodigo) return codigo == null ? 0 : Math.Max(0, (int)(codigoAte - DateTime.Now).TotalSeconds); }
    public void CancelarCodigo() { lock (travaCodigo) codigo = null; }
    public void CodigoErrado()
    {
        lock (travaCodigo) { codigoErros++; if (codigoErros >= 3) codigo = null; }
        Postar(AtualizarConfig);
    }

    static string NomeLimpo(string n)
    {
        n = (n ?? "").Replace("|", "").Replace("\t", " ").Replace("\r", "").Replace("\n", "").Replace("=", "").Trim();
        if (n.Length > 40) n = n.Substring(0, 40);
        return n.Length == 0 ? "Outro PC" : n;
    }

    // pareou: guarda a identidade do outro PC (euControlo: fui eu que digitei o codigo)
    public void ParearCom(ParPC p, bool euControlo)
    {
        if (!euControlo) lock (travaCodigo) codigo = null;     // o codigo so vale uma vez
        lock (pares)
        {
            var x = pares.Find(q => q.Id == p.Id);
            if (x == null) { x = new ParPC { Id = p.Id }; pares.Add(x); }
            x.Nome = NomeLimpo(p.Nome); x.ChavePub = p.ChavePub; x.UltimoIP = p.UltimoIP;
            if (euControlo && !x.EuControlo && pares.FindAll(q => q.EuControlo).Count >= MaxPCs) { /* limite: nao vira mais um botao */ }
            else if (euControlo) x.EuControlo = true;
            if (!euControlo) x.MeControla = true;
        }
        SalvarConfig();
        AtualizarConfig();
        PainelMudou();
        if (!euControlo) Avisar('\uE7F8', "Pareado com " + NomeLimpo(p.Nome));
    }

    public void RenomearPar(string id)
    {
        var pc = ParPorId(id);
        if (pc == null) return;
        string n = PedirTexto("Renomear PC", "Um nome fácil para este PC (ex.: \"Notebook\", \"Sala\")", pc.Nome);
        if (n == null) return;
        lock (pares) pc.Nome = NomeLimpo(n);
        SalvarConfig();
        AtualizarConfig();
        PainelMudou();
    }

    public void RemoverPar(string id)
    {
        lock (pares) pares.RemoveAll(x => x.Id == id);
        SalvarConfig();
        AtualizarConfig();
        PainelMudou();
    }

    public void AtualizarIpPar(string id, string ip)
    {
        bool mudou = false;
        lock (pares) { var x = pares.Find(q => q.Id == id); if (x != null && x.UltimoIP != ip) { x.UltimoIP = ip; mudou = true; } }
        if (mudou) SalvarConfig();
    }

    void AtualizarConfig() { if (config != null && !config.IsDisposed) config.Atualizar(); }

    // ---- pedidos que chegam do PC que controla este (sempre na thread da tela) ----
    static string T(string x) { return (x ?? "").Replace("\t", " ").Replace("\n", " ").Replace("\r", " "); }
    static string N(float v) { return v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture); }

    List<Disp> ListaRemota(bool nova)
    {
        if (nova || remCache == null || (DateTime.Now - remCacheT).TotalSeconds > 3)
        {
            Audio.Liberar(remCache);
            bool comMed = Audio.ComMedidor;
            Audio.ComMedidor = true;      // o outro PC pode querer as barrinhas do som
            try { remCache = Audio.Listar(0, true, Inativos, Nomes); }
            finally { Audio.ComMedidor = comMed; }
            remCache.AddRange(Audio.Listar(1, false, false, Nomes));
            remCacheT = DateTime.Now;
        }
        tRemCache.Stop(); tRemCache.Start();     // sem pedidos por 15 s: solta tudo
        return remCache;
    }

    // O que este PC manda para quem o controla: saidas/microfones (D), apps (A) e janelas de cada app (P)
    public static string EstadoTexto(List<Disp> lista)
    {
        var sb = new StringBuilder();
        foreach (var d in lista)
        {
            sb.Append("D\t").Append(d.Fluxo).Append('\t').Append(T(d.Id)).Append('\t').Append(T(d.Nome)).Append('\t').Append(d.Forma)
              .Append('\t').Append(d.Padrao ? 1 : 0).Append('\t').Append(N(d.Vol)).Append('\t').Append(d.Mudo ? 1 : 0)
              .Append('\t').Append(T(d.Adaptador)).Append('\n');
            foreach (var a in d.Apps)
                sb.Append("A\t").Append(T(d.Id)).Append('\t').Append(T(a.Exe)).Append('\t').Append(T(a.Nome)).Append('\t').Append(N(a.Vol))
                  .Append('\t').Append(a.Mudo ? 1 : 0).Append('\t').Append(a.Ativo ? 1 : 0).Append('\t').Append(T(a.Saida))
                  .Append('\t').Append(a.Sistema ? 1 : 0).Append('\n');
            foreach (var a in d.Apps)
                if (!a.Sistema && a.Partes.Count > 1)
                    foreach (var pt in a.Partes)
                        sb.Append("P\t").Append(T(d.Id)).Append('\t').Append(T(a.Exe)).Append('\t').Append(pt.Pid).Append('\t').Append(T(pt.Nome))
                          .Append('\t').Append(N(pt.Vol)).Append('\t').Append(pt.Mudo ? 1 : 0).Append('\n');
        }
        return sb.ToString();
    }

    public string ExecutarRemoto(string pedido)
    {
        try
        {
            if (!receptorLigado || pedido == null) return "nao";
            var c = pedido.Split('\t');
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            switch (c[0])
            {
                case "estado":
                    return EstadoTexto(ListaRemota(true));
                case "icone":
                    {
                        foreach (var d in ListaRemota(false))
                            foreach (var a in d.Apps)
                                if (T(a.Exe) == c[1] && a.Caminho != null)
                                {
                                    var b = Processos.Icone(a.Caminho, 32);   // so de apps tocando agora: nao le outros arquivos
                                    if (b == null) return "";
                                    using (var ms = new MemoryStream()) { b.Save(ms, System.Drawing.Imaging.ImageFormat.Png); return Convert.ToBase64String(ms.ToArray()); }
                                }
                        return "";
                    }
                case "vold":
                case "mudod":
                    {
                        var d = ListaRemota(false).Find(x => x.Id == c[1]);
                        if (d == null || d.Ep == null) return "nao";
                        if (c[0] == "vold")
                        {
                            float v = Math.Max(0f, Math.Min(1f, float.Parse(c[2], inv)));
                            float ant; bool mAnt;
                            if (d.Ep.GetMasterVolumeLevelScalar(out ant) != 0) ant = d.Vol;
                            if (d.Ep.GetMute(out mAnt) != 0) mAnt = d.Mudo;
                            d.Ep.SetMasterVolumeLevelScalar(v, ref Audio.Ctx); d.Vol = v;
                            if (mAnt && v > 0) { d.Ep.SetMute(false, ref Audio.Ctx); d.Mudo = false; }
                            if (Math.Abs(v - ant) > 0.004f) Atividade(null, Audio.Glifo(d), v > ant ? 1 : -1);
                        }
                        else { d.Mudo = c[2] == "1"; d.Ep.SetMute(d.Mudo, ref Audio.Ctx); Atividade(null, Audio.Glifo(d), d.Mudo ? 2 : 3); }
                        return "ok";
                    }
                case "vola":
                case "mudoa":
                    {
                        var d = ListaRemota(false).Find(x => x.Id == c[1]);
                        var a = d == null ? null : d.Apps.Find(x => T(x.Exe) == c[2]);
                        if (a == null) return "nao";
                        char gA = a.Sistema ? '\uE767' : '\uE8D6';
                        if (c[0] == "vola")
                        {
                            float v = Math.Max(0f, Math.Min(1f, float.Parse(c[3], inv)));
                            float ant = a.Vol;
                            foreach (var sv in a.Vols) try { sv.SetMasterVolume(v, ref Audio.Ctx); if (a.Mudo && v > 0) sv.SetMute(false, ref Audio.Ctx); } catch { }
                            a.Vol = v; if (v > 0) a.Mudo = false;
                            if (!a.Sistema) LembrarVolume(a.Exe, v);
                            if (Math.Abs(v - ant) > 0.004f) Atividade(a.Caminho, gA, v > ant ? 1 : -1);
                        }
                        else
                        {
                            a.Mudo = c[3] == "1"; foreach (var sv in a.Vols) try { sv.SetMute(a.Mudo, ref Audio.Ctx); } catch { }
                            Atividade(a.Caminho, gA, a.Mudo ? 2 : 3);
                        }
                        return "ok";
                    }
                case "volp":
                case "mudop":
                    {
                        var d = ListaRemota(false).Find(x => x.Id == c[1]);
                        var a = d == null ? null : d.Apps.Find(x => T(x.Exe) == c[2]);
                        uint pid;
                        if (a == null || c.Length < 5 || !uint.TryParse(c[3], out pid)) return "nao";
                        var pt = a.Partes.Find(q => q.Pid == pid);
                        if (pt == null) return "nao";
                        if (c[0] == "volp")
                        {
                            float v = Math.Max(0f, Math.Min(1f, float.Parse(c[4], inv)));
                            float ant = pt.Vol;
                            foreach (var sv in pt.Vols) try { sv.SetMasterVolume(v, ref Audio.Ctx); if (pt.Mudo && v > 0) sv.SetMute(false, ref Audio.Ctx); } catch { }
                            pt.Vol = v; if (v > 0) pt.Mudo = false;
                            if (Math.Abs(v - ant) > 0.004f) Atividade(a.Caminho, '\uE8D6', v > ant ? 1 : -1);
                        }
                        else
                        {
                            pt.Mudo = c[4] == "1"; foreach (var sv in pt.Vols) try { sv.SetMute(pt.Mudo, ref Audio.Ctx); } catch { }
                            Atividade(a.Caminho, '\uE8D6', pt.Mudo ? 2 : 3);
                        }
                        return "ok";
                    }
                case "padrao":
                    {
                        if (ListaRemota(false).Find(x => x.Id == c[1]) == null) return "nao";
                        Audio.DefinirPadrao(c[1]);
                        remCache = null;
                        PainelMudou();
                        return "ok";
                    }
                case "niveis":
                    {
                        var sb = new StringBuilder();
                        float v;
                        foreach (var d in ListaRemota(false))
                        {
                            if (d.Fluxo != 0) continue;
                            if (d.Medidor != null && d.Medidor.GetPeakValue(out v) == 0 && v > 0.0005f) sb.Append("d|").Append(T(d.Id)).Append('\t').Append(N(v)).Append('\n');
                            foreach (var a in d.Apps)
                            {
                                float pico = 0;
                                foreach (var m in a.Medidores) if (m.GetPeakValue(out v) == 0 && v > pico) pico = v;
                                if (pico > 0.0005f) sb.Append("a|").Append(T(d.Id)).Append('|').Append(T(a.Exe)).Append('\t').Append(N(pico)).Append('\n');
                            }
                        }
                        return sb.ToString();
                    }
                case "teste":
                    {
                        var d = ListaRemota(false).Find(x => x.Id == c[1] && x.Fluxo == 0);
                        if (d == null) return "nao";
                        SomTeste.Tocar(d.Id, null);
                        Atividade(null, Audio.Glifo(d), 3);
                        return "ok";
                    }
                case "saida":
                    {
                        string id = c[2] == "-" ? null : c[2];
                        var lista = ListaRemota(false);
                        if (id != null && lista.Find(x => x.Id == id && x.Fluxo == 0) == null) return "nao";
                        var pids = new List<uint>(); string exe = null;
                        foreach (var d in lista) foreach (var a in d.Apps) if (!a.Sistema && T(a.Exe) == c[1]) { exe = a.Exe; pids.AddRange(a.Pids); }
                        if (exe == null) return "nao";
                        DefinirSaidaApp(exe, pids, id);
                        string alvo = id;
                        GarantirSaidas(x => alvo, exe);
                        GarantirDepois(x => alvo, exe);
                        remCache = null;
                        return "ok";
                    }
            }
            return "nao";
        }
        catch (Exception ex) { Program.Log(ex); return "nao"; }
    }

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
        if (servidor != null) servidor.Desligar();
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
                    case "controlador": Controlador = v == "1"; break;
                    case "atualizar": AtualizarSozinho = v == "1"; break;
                    case "prioridade_auto": PrioridadeAuto = v == "1"; break;
                    case "prio0": case "prio1":
                        { var lp = Prioridade[k[4] - '0']; lp.Clear(); foreach (var o in v.Split(';')) if (o.Length > 0 && !lp.Contains(o)) lp.Add(o); }
                        break;
                    case "conhecido":
                        {
                            var a = v.Split('|');
                            int fl, fo;
                            if (a.Length == 4 && int.TryParse(a[0], out fl) && int.TryParse(a[1], out fo) && (fl == 0 || fl == 1))
                                Conhecidos[a[2]] = new Conhecido { Fluxo = fl, Forma = fo, Id = a[2], Nome = a[3] };
                        }
                        break;
                    case "receptor": receptorLigado = v == "1"; break;
                    case "id_pc": if (v.Length >= 16) MeuId = v; break;
                    case "par":
                        {
                            var a = v.Split('|');
                            if (a.Length >= 5 && a[0].Length > 0 && a[2].Length > 0)
                                pares.Add(new ParPC { Id = a[0], Nome = a[1], ChavePub = a[2], UltimoIP = a[3], EuControlo = a[4].Contains("C"), MeControla = a[4].Contains("R") });
                        }
                        break;
                    case "ordem": Ordem.Clear(); foreach (var o in v.Split(';')) if (o.Length > 0) Ordem.Add(o); break;
                    case "aberto": { var a = v.Split('>'); if (a.Length == 2) Abertos[a[0]] = a[1] == "1"; } break;
                    case "passo": int.TryParse(v, out Passo); if (Passo < 1 || Passo > 25) Passo = 2; break;
                    case "altura_max": int.TryParse(v, out AlturaMax); if (AlturaMax < 30 || AlturaMax > 100) AlturaMax = 90; break;
                    case "tema": if (v == "auto" || v == "claro" || v == "escuro") tema = v; break;
                    case "perfil_ativo": PerfilAtivo = v.Length == 0 ? null : v; break;
                    case "regra": { var a = v.Split('>'); if (a.Length == 2) Regras[a[0]] = a[1]; } break;
                    case "nome": { var a = v.Split('>'); if (a.Length == 2) Nomes[a[0]] = a[1]; } break;
                    case "oculto": Ocultos.Add(v); break;
                    case "volapp": { var a = v.Split('>'); float fv; if (a.Length == 2 && float.TryParse(a[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fv) && fv >= 0 && fv <= 1) VolApps[a[0]] = fv; } break;
                    case "alerta": { var a = v.Split('>'); if (a.Length == 2) AlertaVolume[a[0]] = a[1] == "1"; } break;
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
            l.Add("controlador=" + (Controlador ? 1 : 0)); l.Add("receptor=" + (receptorLigado ? 1 : 0)); l.Add("atualizar=" + (AtualizarSozinho ? 1 : 0));
            l.Add("prioridade_auto=" + (PrioridadeAuto ? 1 : 0));
            l.Add("prio0=" + string.Join(";", Prioridade[0])); l.Add("prio1=" + string.Join(";", Prioridade[1]));
            foreach (var kc in Conhecidos.Values) l.Add("conhecido=" + kc.Fluxo + "|" + kc.Forma + "|" + kc.Id + "|" + kc.Nome.Replace("|", ""));
            if (MeuId != null) l.Add("id_pc=" + MeuId);
            lock (pares)
                foreach (var pc in pares)
                    l.Add("par=" + pc.Id + "|" + pc.Nome + "|" + pc.ChavePub + "|" + (pc.UltimoIP ?? "") + "|" + (pc.EuControlo ? "C" : "") + (pc.MeControla ? "R" : ""));
            foreach (var kv in Regras) l.Add("regra=" + kv.Key + ">" + kv.Value);
            foreach (var kv in Nomes) l.Add("nome=" + kv.Key + ">" + kv.Value);
            foreach (var o in Ocultos) l.Add("oculto=" + o);
            foreach (var kv in VolApps) l.Add("volapp=" + kv.Key + ">" + kv.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            foreach (var kv in AlertaVolume) l.Add("alerta=" + kv.Key + ">" + (kv.Value ? 1 : 0));
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
    static readonly string[] Paginas = { "Geral", "Perfis", "Atalhos", "Dispositivos", "Outros PCs", "Prioridade" };
    static readonly char[] GlifosPag = { '', '', '', '' , '' , '\uE8CB' };
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
        public string Id; public int Fluxo, Ordem; public bool Ativo, Padrao;   // L_PRIO
    }
    class Zona { public Rectangle R; public int Pagina = -1; public Linha L; public bool Teste, Menu; }
    const int L_CAB = 0, L_CHAVE = 1, L_BOTAO = 2, L_NOTA = 3, L_GRUPO = 4, L_PRIO = 5;
    // arrastar na lista de prioridade
    Linha arrastoL; int arrastoY0, arrastoPara = -1; bool arrastandoL, soltou;
    readonly List<KeyValuePair<Rectangle, Linha>> prioRects = new List<KeyValuePair<Rectangle, Linha>>();
    string testandoCfg;
    // perfis abertos na pagina Perfis (comeca so com o ativo aberto)
    readonly HashSet<Perfil> perfisAbertos = new HashSet<Perfil>();
    bool perfisIniciados;

    int E(float v) { return (int)Math.Round(v * esc); }

    public void Atualizar() { Montar(); Invalidate(); }
    readonly Timer tCodigo = new Timer { Interval = 1000 };
    bool procurando;

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
        tCodigo.Tick += (s, e) => { if (app.CodigoValido() == null) tCodigo.Stop(); Montar(); Invalidate(); };
        FormClosed += (s, e) => { tCodigo.Stop(); tCodigo.Dispose(); };
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
            Botao("Desinstalar o Sintonia", "Remove o programa, o atalho, as configurações e devolve os apps à saída padrão (pede confirmação)", () => "",
                r => { try { Process.Start(Application.ExecutablePath, "--desinstalar"); } catch { } });

            Cab("Atualizações");
            Chave("Atualizar sozinho", "A cada 12 horas confere no GitHub se há versão nova. Se houver, baixa, confere a assinatura digital e se atualiza quando você não estiver jogando em tela cheia",
                () => app.AtualizarSozinho, v => app.DefinirAtualizarSozinho(v));
            Botao(app.TemNovaVersao ? "Atualizar agora para a versão " + app.NovaVersaoTexto : "Procurar atualização agora",
                "Versão instalada: " + Atualizador.Atual.ToString(3) + (app.EstadoAtualizacao.Length > 0 ? " · " + app.EstadoAtualizacao : ""),
                () => "", r => app.ProcurarAtualizacao(true), '\uE895');
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
        else if (pagina == 5)
        {
            Nota("Quando um aparelho conecta ou desconecta (ex.: o fone Bluetooth), o Sintonia passa a usar o primeiro da lista que estiver conectado. " +
                 "Arraste para mudar a ordem; ▶ toca um som de teste naquela saída.");
            Chave("Trocar sozinho pela prioridade", app.PrioridadeAuto
                    ? "Ligado: o 1º conectado vira a saída (e o microfone) padrão. Arrastar para o 1º lugar ou escolher uma saída à mão já a coloca em 1º"
                    : "Desligado: a ordem fica guardada, mas nada troca sozinho",
                () => app.PrioridadeAuto, v => app.DefinirPrioridadeAuto(v));
            foreach (var fluxo in new[] { 0, 1 })
            {
                Cab(fluxo == 0 ? "Saídas" : "Microfones");
                var ativos = Audio.Listar(fluxo, false, false, null);
                string pad = Audio.PadraoId(fluxo);
                int ordem = 0;
                foreach (var id0 in new List<string>(app.Prioridade[fluxo]))
                {
                    string id = id0;
                    var d = ativos.Find(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                    App.Conhecido k; app.Conhecidos.TryGetValue(id, out k);
                    int forma = d != null ? d.Forma : k != null ? k.Forma : 0;
                    var l = new Linha
                    {
                        Tipo = L_PRIO, Id = id, Fluxo = fluxo, Ordem = ordem++, Ativo = d != null, Padrao = d != null && Politica.Igual(id, pad),
                        Titulo = app.NomeConhecido(id), Glifo = Audio.Glifo(new Disp { Fluxo = fluxo, Forma = forma })
                    };
                    l.Clicar = r =>
                    {
                        var m = NovoMenu();
                        if (l.Ativo)
                        {
                            var ren = new ToolStripMenuItem("Renomear…");
                            string orig = k != null ? k.Nome : l.Titulo, atual = l.Titulo;
                            ren.Click += (s, e) => BeginInvoke(new Action(() => { app.RenomearDispositivo(id, orig, atual); Montar(); Invalidate(); }));
                            m.Items.Add(ren);
                            if (!l.Padrao)
                            {
                                var usar = new ToolStripMenuItem("Usar agora");
                                usar.Click += (s, e) => { Audio.DefinirPadrao(id); app.PainelMudou(); Montar(); Invalidate(); };
                                m.Items.Add(usar);
                            }
                        }
                        var topo = new ToolStripMenuItem("Colocar em primeiro") { Enabled = l.Ordem > 0 };
                        topo.Click += (s, e) => { app.MoverPrioridade(fluxo, id, 0); Montar(); Invalidate(); };
                        m.Items.Add(topo);
                        if (!l.Ativo)
                        {
                            var esq = new ToolStripMenuItem("Esquecer este aparelho");
                            esq.Click += (s, e) => { app.EsquecerAparelho(id); Montar(); Invalidate(); };
                            m.Items.Add(esq);
                        }
                        MostrarMenu(m, r);
                    };
                    linhas.Add(l);
                }
                if (app.Prioridade[fluxo].Count == 0) Nota(fluxo == 0 ? "Nenhuma saída vista ainda." : "Nenhum microfone visto ainda.");
                Audio.Liberar(ativos);
            }
        }
        else if (pagina == 4)
        {
            Nota("Controle o som de outro PC da sua casa (ex.: o notebook) pelo painel deste, e vice-versa. Os PCs se conectam só pela rede de casa, " +
                 "com tudo criptografado, e só depois de parear com um código. Nada é enviado no dia a dia: só quando o painel mostra o outro PC.");
            Cab("Este PC");
            Chave("Controlar outros PCs", "Mostra no painel um botão para cada PC pareado, para mudar o som dele daqui", () => app.Controlador, v => app.DefinirControlador(v));
            Chave("Pode ser controlado por outros PCs", "Fica esperando, parado, um PC pareado pedir algo. Na primeira vez o Windows pergunta se libera o Sintonia: marque só \"Redes privadas\"",
                () => app.Receptor, v => app.Receptor = v);
            if (app.Receptor && app.ReceptorAtivo)
            {
                var ends = new List<string>();
                foreach (var rd in Rede.MinhasRedes())
                {
                    var b = rd.Key.GetAddressBytes();
                    if (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)) ends.Add(rd.Key.ToString());
                }
                if (ends.Count > 0) Nota("Endereço deste PC na rede: " + string.Join("  ou  ", ends) + ". Se o outro PC não achar este na procura, digite esse endereço lá.");
            }
            if (app.Receptor && !app.ReceptorAtivo)
                Nota("Não foi possível esperar conexões (a porta " + Rede.PortaTcp + " pode estar em uso por outro programa).");

            Cab("Parear um PC");
            if (!app.Controlador && !app.Receptor)
                Nota("Ligue uma das opções acima. No PC que vai ser controlado, ligue \"Pode ser controlado\" e gere o código. No PC que vai controlar, ligue \"Controlar outros PCs\" e adicione o PC com esse código.");
            if (app.Receptor)
            {
                string cod = app.CodigoValido();
                if (cod != null)
                {
                    int sg = app.CodigoSegundos();
                    Botao("Código: " + cod, "Digite este código no outro PC, em \"Adicionar um PC\". Vale por " + (sg / 60) + ":" + (sg % 60).ToString("00") + " e só uma vez",
                        () => "Cancelar", r => { app.CancelarCodigo(); Montar(); Invalidate(); });
                }
                else
                    Botao("Gerar código de pareamento", "Para o outro PC poder controlar este. O código vale 2 minutos", () => "", r =>
                    {
                        app.GerarCodigo(); tCodigo.Start(); Montar(); Invalidate();
                    }, '\uE8D7');
            }
            if (app.Controlador)
                Botao(procurando ? "Procurando PCs na rede…" : "Adicionar um PC", "Procura na rede os PCs com \"Pode ser controlado\" ligado e pede o código mostrado neles. Até " + App.MaxPCs + " PCs",
                    () => "", r => AdicionarPC(r), '\uE710');

            var lista = app.Pares();
            if (lista.Count > 0)
            {
                Cab("PCs pareados");
                foreach (var pc in lista)
                {
                    var par = pc;
                    string desc = par.EuControlo && par.MeControla ? "Um controla o outro" : par.EuControlo ? "Este PC controla ele" : "Ele controla este PC";
                    if (!string.IsNullOrEmpty(par.UltimoIP)) desc += " · último endereço " + par.UltimoIP;
                    Botao(par.Nome, desc, () => "", r =>
                    {
                        var m = NovoMenu();
                        var rem = new ToolStripMenuItem("Esquecer este PC");
                        rem.Click += (s, e) => BeginInvoke(new Action(() =>
                        {
                            if (MessageBox.Show(this, "Esquecer " + par.Nome + "?\n\nOs dois PCs deixam de se conectar até parear de novo. Faça o mesmo no outro PC para ele também esquecer este.",
                                "Sintonia", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) app.RemoverPar(par.Id);
                        }));
                        m.Items.Add(rem);
                        MostrarMenu(m, r);
                    }, '\uE7F8');
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

    // procura os PCs (por um instante) e pareia com o codigo mostrado no escolhido
    void AdicionarPC(Rectangle r)
    {
        if (procurando) return;
        if (app.Controlados().Count >= App.MaxPCs)
        {
            MessageBox.Show(this, "Você já controla " + App.MaxPCs + " outros PCs, o limite do Sintonia (o painel mostra no máximo 3 PCs).\nEsqueça um deles abaixo para adicionar outro.",
                "Sintonia", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        procurando = true; Montar(); Invalidate();
        new Thread(() =>
        {
            List<Rede.Achado> achados;
            try { achados = Rede.Procurar(1500); } catch { achados = new List<Rede.Achado>(); }
            achados.RemoveAll(x => x.Id == app.MeuId);
            try
            {
                BeginInvoke(new Action(() =>
                {
                    procurando = false; Montar(); Invalidate();
                    var m = NovoMenu();
                    foreach (var a in achados)
                    {
                        var ac = a;
                        var it = new ToolStripMenuItem(ac.Nome + "  (" + ac.IP + ")");
                        it.Click += (s, e) => BeginInvoke(new Action(() => PedirCodigo(ac.IP, ac.Nome)));
                        m.Items.Add(it);
                    }
                    if (achados.Count == 0) m.Items.Add(new ToolStripMenuItem("Nenhum PC encontrado") { Enabled = false });
                    m.Items.Add(new ToolStripSeparator());
                    var ip = new ToolStripMenuItem("Digitar o endereço do PC…");
                    ip.Click += (s, e) => BeginInvoke(new Action(() =>
                    {
                        using (var f = new JanelaTexto("Endereço do outro PC", "Ex.: 192.168.0.15 (no outro PC: Configurações > Rede > Propriedades)", "", app.TemaEscuro(), "Continuar", 15))
                        {
                            IPAddress end;
                            if (f.ShowDialog(this) == DialogResult.OK && IPAddress.TryParse(f.Valor, out end) && end.AddressFamily == AddressFamily.InterNetwork)
                            {
                                if (!Rede.EhRedeLocal(end)) { MessageBox.Show(this, "Esse endereço não é da sua rede de casa.", "Sintonia"); return; }
                                PedirCodigo(end.ToString(), end.ToString());
                            }
                        }
                    }));
                    m.Items.Add(ip);
                    MostrarMenu(m, r);
                }));
            }
            catch { }
        }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
    }

    void PedirCodigo(string ip, string nome)
    {
        string cod;
        using (var f = new JanelaTexto("Parear com " + nome, "Digite o código que aparece no outro PC (Outros PCs > Gerar código)", "", app.TemaEscuro(), "Parear", 9))
        {
            if (f.ShowDialog(this) != DialogResult.OK) return;
            cod = f.Valor;
        }
        if (Rede.NormalizarCodigo(cod).Length != 8) { MessageBox.Show(this, "O código tem 8 letras e números (ex.: K7QM-4XPA).", "Sintonia"); return; }
        Cursor = Cursors.WaitCursor;
        string meuId = app.MeuId;
        new Thread(() =>
        {
            ParPC par = null; string erro = null;
            try { using (var c = Canal.Conectar(ip, 2000)) par = c.ParearCliente(meuId, Environment.MachineName, cod); }
            catch (Exception ex) { erro = ex is IOException || ex is SocketException ? ex.Message : "falha na conexão"; }
            try
            {
                BeginInvoke(new Action(() =>
                {
                    Cursor = Cursors.Default;
                    if (par != null)
                    {
                        app.ParearCom(par, true);
                        MessageBox.Show(this, "Pronto! Agora o painel mostra um botão para " + par.Nome + ".", "Sintonia", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else MessageBox.Show(this, "Não deu para parear (" + erro + ").\n\nConfira o código, se ele ainda vale e se o outro PC está com \"Pode ser controlado\" ligado.",
                        "Sintonia", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }));
            }
            catch { }
        }) { IsBackground = true }.Start();
    }

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
        prioRects.Clear();
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
            if (l.Tipo == L_PRIO)
            {
                int hp = E(52);
                var rp = new Rectangle(x0, y, w, hp);
                bool comTeste = l.Fluxo == 0 && l.Ativo;
                var rT = new Rectangle(rp.Right - E(84), y + (hp - E(34)) / 2, E(34), E(34));
                if (comTeste) zonas.Add(new Zona { R = rT, L = l, Teste = true });
                var rMenuP = new Rectangle(rp.Right - E(46), y + (hp - E(34)) / 2, E(36), E(34));
                zonas.Add(new Zona { R = rMenuP, L = l, Menu = true });
                zonas.Add(new Zona { R = rp, L = l });
                prioRects.Add(new KeyValuePair<Rectangle, Linha>(rp, l));
                bool hovp = sobre != null && sobre.L == l && !arrastandoL;
                bool movendo = arrastandoL && arrastoL == l;
                Fundo(g, rp, movendo ? cHover : hovp ? cHover : cCartao, E(6), cBorda);
                // pegador, numero, icone, nome
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    float pr = Math.Max(1.6f, E(1.6f)), cx = x0 + E(16), cy = y + hp / 2f;
                    using (var b = new SolidBrush(hovp || movendo ? cTexto : cFraco))
                        for (int ix = 0; ix < 2; ix++)
                            for (int iy = -1; iy <= 1; iy++)
                                g.FillEllipse(b, cx + (ix == 0 ? -E(3) : E(3)) - pr, cy + iy * E(6) - pr, pr * 2, pr * 2);
                    g.SmoothingMode = SmoothingMode.None;
                }
                var rn = new Rectangle(x0 + E(30), y + (hp - E(22)) / 2, E(22), E(22));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                bool primeiro = l.Ordem == 0 && app.PrioridadeAuto;
                using (var b = new SolidBrush(primeiro ? cAcento : cHover)) g.FillEllipse(b, rn);
                g.SmoothingMode = SmoothingMode.None;
                TextRenderer.DrawText(g, (l.Ordem + 1).ToString(), fPequena, rn, primeiro ? cTextoAcento : cTexto, TC);
                Color ct = l.Ativo ? cTexto : cFraco;
                TextRenderer.DrawText(g, l.Glifo.ToString(), fGlifo, new Rectangle(x0 + E(60), y, E(28), hp), ct, TC);
                string est = l.Padrao ? "em uso" : l.Ativo ? "conectado" : "desconectado";
                TextRenderer.DrawText(g, l.Titulo, fNormal, new Rectangle(x0 + E(98), y + E(8), w - E(300), E(20)), ct, TF);
                TextRenderer.DrawText(g, est, fPequena, new Rectangle(x0 + E(98), y + E(28), w - E(300), E(16)), l.Padrao ? cAcento : cFraco, TF);
                if (comTeste)
                {
                    bool tocandoEsta = testandoCfg == l.Id;
                    if (sobre != null && sobre.Teste && sobre.L == l) Fundo(g, rT, ControlPaint.Light(cHover, 0.15f), E(5), null);
                    TextRenderer.DrawText(g, tocandoEsta ? "\uE767" : "\uE768", fGlifoP, rT, tocandoEsta ? cAcento : cTexto, TC);
                }
                if (sobre != null && sobre.Menu && sobre.L == l) Fundo(g, rMenuP, ControlPaint.Light(cHover, 0.15f), E(5), null);
                TextRenderer.DrawText(g, "\uE76C", fGlifoP, rMenuP, sobre != null && sobre.Menu && sobre.L == l ? cTexto : cFraco, TC);
                y += hp + E(4);
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
        if (arrastandoL && arrastoPara >= 0)
        {
            // linha mostrando onde o aparelho vai ficar
            var mesmos = prioRects.FindAll(kv => kv.Value.Fluxo == arrastoL.Fluxo);
            if (mesmos.Count > 0)
            {
                int yl = arrastoPara < mesmos.Count ? mesmos[arrastoPara].Key.Top - E(3) : mesmos[mesmos.Count - 1].Key.Bottom + E(1);
                var rr = mesmos[0].Key;
                Fundo(g, new Rectangle(rr.X + E(8), yl, rr.Width - E(16), Math.Max(2, E(3))), cAcento, E(1.5f), null);
            }
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

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        soltou = false;
        if (e.Button != MouseButtons.Left) return;
        var z = ZonaEm(e.Location);
        if (z != null && z.L != null && z.L.Tipo == L_PRIO && !z.Teste && !z.Menu) { arrastoL = z.L; arrastoY0 = e.Y; arrastandoL = false; }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (arrastoL != null && arrastandoL)
        {
            app.MoverPrioridade(arrastoL.Fluxo, arrastoL.Id, arrastoPara);
            soltou = true;
            Capture = false;
            Montar(); Invalidate();
        }
        arrastoL = null; arrastandoL = false; arrastoPara = -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (arrastoL != null)
        {
            if (!arrastandoL && Math.Abs(e.Y - arrastoY0) > E(6)) { arrastandoL = true; Capture = true; Cursor = Cursors.SizeAll; }
            if (arrastandoL)
            {
                int i = 0, para = 0;
                foreach (var kv in prioRects)
                {
                    if (kv.Value.Fluxo != arrastoL.Fluxo) continue;
                    if (e.Y > kv.Key.Top + kv.Key.Height / 2) para = i + 1;
                    i++;
                }
                if (para != arrastoPara) { arrastoPara = para; Invalidate(); }
                return;
            }
        }
        var z = ZonaEm(e.Location);
        bool mudou = (z == null) != (sobre == null) || (z != null && (z.L != sobre.L || z.Pagina != sobre.Pagina || z.Teste != sobre.Teste || z.Menu != sobre.Menu));
        sobre = z;
        if (mudou)
        {
            Cursor = z == null ? Cursors.Default : (z.L != null && z.L.Tipo == L_PRIO && !z.Teste && !z.Menu) ? Cursors.SizeAll : Cursors.Hand;
            Invalidate();
        }
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
        if (l.Tipo == L_PRIO && (arrastandoL || soltou)) { soltou = false; return; }
        if (l.Tipo == L_PRIO && z.Teste)
        {
            if (testandoCfg != null) return;
            string id = l.Id;
            testandoCfg = id; Invalidate();
            SomTeste.Tocar(id, ok => { try { BeginInvoke(new Action(() => { testandoCfg = null; Invalidate(); })); } catch { } });
            return;
        }
        if ((l.Tipo == L_BOTAO || l.Tipo == L_GRUPO || l.Tipo == L_PRIO) && l.Clicar != null)
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

// =====================================================================
//  Instalador simples (o proprio Sintonia.exe se instala e se desinstala)
//  Em %LOCALAPPDATA%\Programs\Sintonia, sem administrador: copia o exe, atalho no menu Iniciar
//  e registro em Configuracoes do Windows > Aplicativos (para desinstalar por la).
// =====================================================================
static class Instalador
{
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string s);

    public static string Pasta { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Sintonia"); } }
    public static string ExeInstalado { get { return Path.Combine(Pasta, "Sintonia.exe"); } }
    static string Atalho { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Sintonia.lnk"); } }
    const string ChaveDesinstalar = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Sintonia";

    public static bool EstaInstalado { get { return string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(ExeInstalado), StringComparison.OrdinalIgnoreCase); } }

    public static bool TemaEscuroSistema()
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            { object v = k == null ? null : k.GetValue("AppsUseLightTheme"); if (v is int) return (int)v == 0; }
        }
        catch { }
        return false;
    }

    // true = pode seguir abrindo este exe
    public static bool Oferecer(bool escuro)
    {
        bool jaTem = File.Exists(ExeInstalado);
        Version vNova = Atualizador.Atual, vVelha = null;
        if (jaTem) try { var fv = FileVersionInfo.GetVersionInfo(ExeInstalado); vVelha = new Version(fv.FileMajorPart, fv.FileMinorPart, fv.FileBuildPart); } catch { }
        string titulo = !jaTem ? "Instalar o Sintonia" : vVelha != null && vVelha < vNova ? "Atualizar o Sintonia" : "Reinstalar o Sintonia";
        string texto = !jaTem
            ? "Ele fica na sua pasta de programas, abre sozinho com o Windows e aparece no menu Iniciar. Não precisa de administrador."
            : "O Sintonia já está instalado" + (vVelha != null ? " (versão " + vVelha.ToString(3) + ")" : "") + ". Instalar esta versão (" + vNova.ToString(3) + ") por cima? Suas configurações continuam.";
        using (var f = new JanelaInstalar(titulo, texto, escuro))
        {
            var r = f.ShowDialog();
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.No) return true;
        }
        string erro = Instalar();
        if (erro != null) { MessageBox.Show("Não deu para instalar o Sintonia:\n" + erro, "Sintonia", MessageBoxButtons.OK, MessageBoxIcon.Warning); return true; }
        try { Process.Start(ExeInstalado); } catch { }
        return false;
    }

    static void FecharAberto()
    {
        PostMessage(new IntPtr(0xFFFF), RegisterWindowMessage("Sintonia_Sair"), IntPtr.Zero, IntPtr.Zero);
        int eu = Process.GetCurrentProcess().Id;
        for (int i = 0; i < 50; i++)
        {
            bool aberto = false;
            foreach (var p in Process.GetProcessesByName("Sintonia")) if (p.Id != eu) aberto = true;
            if (!aberto) return;
            Thread.Sleep(100);
        }
        foreach (var p in Process.GetProcessesByName("Sintonia")) if (p.Id != eu) try { p.Kill(); p.WaitForExit(3000); } catch { }
    }

    static string Instalar()
    {
        try
        {
            FecharAberto();
            Directory.CreateDirectory(Pasta);
            File.Copy(Application.ExecutablePath, ExeInstalado, true);
            Registrar(true);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    // ja instalado (ou atualizado sozinho): garante o atalho e o registro em Configuracoes > Aplicativos
    public static void GarantirRegistro() { try { Registrar(false); } catch { } }

    static void Registrar(bool sempre)
    {
        if (!File.Exists(Atalho)) CriarAtalho();
        using (var k = Registry.CurrentUser.CreateSubKey(ChaveDesinstalar))
        {
            if (!sempre && k.GetValue("DisplayVersion") as string == Atualizador.Atual.ToString(3) && k.GetValue("UninstallString") != null) return;
            k.SetValue("DisplayName", "Sintonia");
            k.SetValue("DisplayIcon", ExeInstalado + ",0");
            k.SetValue("DisplayVersion", Atualizador.Atual.ToString(3));
            k.SetValue("Publisher", "oDethie");
            k.SetValue("InstallLocation", Pasta);
            k.SetValue("UninstallString", "\"" + ExeInstalado + "\" --desinstalar");
            k.SetValue("URLInfoAbout", "https://github.com/" + Atualizador.Repo);
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("EstimatedSize", (int)Math.Max(1, new FileInfo(ExeInstalado).Length / 1024), RegistryValueKind.DWord);
        }
    }

    static void CriarAtalho()
    {
        var tipo = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(tipo);
        object at = tipo.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { Atalho });
        var ta = at.GetType();
        ta.InvokeMember("TargetPath", BindingFlags.SetProperty, null, at, new object[] { ExeInstalado });
        ta.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, at, new object[] { Pasta });
        ta.InvokeMember("Description", BindingFlags.SetProperty, null, at, new object[] { "Saídas de som, volume por app e perfis" });
        ta.InvokeMember("IconLocation", BindingFlags.SetProperty, null, at, new object[] { ExeInstalado + ",0" });
        ta.InvokeMember("Save", BindingFlags.InvokeMethod, null, at, null);
        Marshal.FinalReleaseComObject(at);
        Marshal.FinalReleaseComObject(shell);
    }

    // Sintonia.exe --desinstalar (tambem pelo Windows: Configuracoes > Aplicativos)
    public static void Desinstalar()
    {
        if (MessageBox.Show("Desinstalar o Sintonia?\n\nRemove o programa, o atalho, o início com o Windows e as configurações, e devolve todos os apps à saída de som padrão.",
            "Sintonia", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        FecharAberto();
        try { Politica.LimparTudo(); } catch { }          // apps voltam para a saida padrao
        try { Rede.ApagarIdentidade(); } catch { }        // identidade usada em Outros PCs
        try { Process.Start(new ProcessStartInfo("schtasks.exe", "/delete /tn \"Sintonia\" /f") { CreateNoWindow = true, UseShellExecute = false }).WaitForExit(10000); } catch { }
        try { File.Delete(Atalho); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(ChaveDesinstalar, false); } catch { }
        string dados = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        try { File.Delete(Path.Combine(dados, "Sintonia.ini")); } catch { }
        try { File.Delete(Path.Combine(dados, "Sintonia.log")); } catch { }
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings", true))
                if (k != null)
                    foreach (var n in k.GetSubKeyNames())
                    {
                        string p; using (var s = k.OpenSubKey(n)) p = s == null ? null : s.GetValue("ExecutablePath") as string;
                        if (p != null && p.EndsWith("\\Sintonia.exe", StringComparison.OrdinalIgnoreCase)) k.DeleteSubKeyTree(n, false);
                    }
        }
        catch { }
        string pasta = Path.GetDirectoryName(Application.ExecutablePath);
        if (string.Equals(pasta, Pasta, StringComparison.OrdinalIgnoreCase))
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + pasta + "\"")
                { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
            }
            catch { }
        MessageBox.Show("O Sintonia foi desinstalado.", "Sintonia", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}

// janela "Instalar o Sintonia" no estilo do Windows 11
class JanelaInstalar : JanelaBase
{
    public JanelaInstalar(string titulo, string texto, bool temaEscuro) : base(temaEscuro)
    {
        Text = "Sintonia";
        ClientSize = new Size(E(460), E(220));
        int x = E(24), w = ClientSize.Width - E(48), y = E(22);
        Controls.Add(new PictureBox { Image = Desenho.Logo(E(40)), Size = new Size(E(40), E(40)), Location = new Point(x, y) });
        Controls.Add(new Label { Text = titulo, Font = fTitulo, AutoSize = true, Location = new Point(x + E(52), y + E(4)), ForeColor = cTexto });
        y += E(56);
        Controls.Add(new Label { Text = texto, Font = fNormal, AutoSize = false, Size = new Size(w, E(64)), Location = new Point(x, y), ForeColor = cFraco });
        var rodape = Rodape();
        var bInstalar = Botao(titulo.StartsWith("Instalar") ? "Instalar" : titulo.StartsWith("Atualizar") ? "Atualizar" : "Reinstalar", true);
        var bAbrir = Botao("Só abrir", false);
        int by = (rodape.Height - E(34)) / 2;
        bInstalar.Location = new Point(rodape.Width - E(24) - bInstalar.Width, by);
        bAbrir.Location = new Point(bInstalar.Left - E(8) - bAbrir.Width, by);
        rodape.Controls.AddRange(new Control[] { bInstalar, bAbrir });
        bInstalar.Click += (s, e) => DialogResult = DialogResult.OK;
        bAbrir.Click += (s, e) => DialogResult = DialogResult.No;
        AcceptButton = bInstalar;
    }
}
