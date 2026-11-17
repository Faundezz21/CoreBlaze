using System.Diagnostics;
using System.Globalization;
using System.Media;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ProbarCpu;

// ventana que aloja la interfaz html y la conecta con el motor de estres
public sealed class FormPrincipal : Form
{
    private const int SegundosParaDetener = 10;
    private const int FaseBench = 30;
    private const string Repo = "Faundezz21/CoreBlaze";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static readonly string Version = (typeof(FormPrincipal).Assembly.GetName().Version ?? new Version(1, 0, 0)).ToString(3);
    private static readonly bool Espanol = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es";

    private enum Ejecucion { Nada, Estres, Bench }

    private readonly WebView2 web;
    private readonly Estres estresCpu = new();
    private readonly EstresGpu estresGpu = new();
    private readonly MonitorHardware monitor;
    private readonly List<AdaptadorGpu> adaptadores;
    private readonly List<Sesion> historial;
    private readonly List<ResultadoBench> benchs;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly Stopwatch reloj = Stopwatch.StartNew();

    private bool listo;
    private Ejecucion ejecucion = Ejecucion.Nada;
    private Sesion sesion;
    private DateTime inicio;
    private TimeSpan ultimaDuracion;
    private float limite = 100;
    private int minutos;
    private float tempMaxCpu, tempMaxGpu, potMaxCpu, potMaxGpu, picoFrecCpu;
    private int sobreLimiteCpu, sobreLimiteGpu;
    private long trabajoAnterior, lotesAnterior;
    private double segundosAnterior;

    // datos de la prueba fija en curso
    private AdaptadorGpu benchGpu;
    private bool benchConBateria;
    private readonly List<double> benchCpu = new(), benchGpuRend = new();
    private readonly List<float?> benchTempCpu = new(), benchTempGpu = new(), benchPotCpu = new(), benchPotGpu = new();

    private bool Corriendo => ejecucion != Ejecucion.Nada;

    public FormPrincipal()
    {
        float k = DeviceDpi / 96f;
        Text = "CoreBlaze";
        BackColor = Tema.Fondo;
        var icono = CargarIcono();
        if (icono != null) Icon = icono;

        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1366, 768);
        MinimumSize = new Size(Math.Min((int)(1040 * k), area.Width), Math.Min((int)(680 * k), area.Height));
        Size = new Size(Math.Min((int)(1380 * k), area.Width - 20), Math.Min((int)(900 * k), area.Height - 20));
        StartPosition = FormStartPosition.CenterScreen;

        monitor = new MonitorHardware();
        adaptadores = EstresGpu.ListarAdaptadores();
        historial = Historial.Cargar();
        benchs = Historial.CargarBench();

        web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Tema.Fondo };
        Controls.Add(web);

        timer.Tick += (_, _) => Actualizar();
        Load += async (_, _) => await IniciarWeb();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Tema.TituloOscuro(Handle);
    }

    private static Icon CargarIcono()
    {
        try
        {
            using var s = typeof(FormPrincipal).Assembly.GetManifestResourceStream("ProbarCpu.logo.ico");
            return s != null ? new Icon(s) : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task IniciarWeb()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Historial.Base, "WebView2"));
            await web.EnsureCoreWebView2Async(env);

            var s = web.CoreWebView2.Settings;
            s.AreDefaultContextMenusEnabled = false;
            s.AreDevToolsEnabled = Debugger.IsAttached;
            s.IsZoomControlEnabled = false;
            s.IsStatusBarEnabled = false;
            s.AreBrowserAcceleratorKeysEnabled = false;
            s.IsPinchZoomEnabled = false;

            web.CoreWebView2.WebMessageReceived += Recibir;
            // los enlaces externos se abren en el navegador del sistema
            web.CoreWebView2.NewWindowRequested += (_, a) => { a.Handled = true; AbrirUrl(a.Uri); };

            using var st = typeof(FormPrincipal).Assembly.GetManifestResourceStream("ProbarCpu.interfaz.html");
            using var lector = new StreamReader(st!);
            web.NavigateToString(lector.ReadToEnd());
        }
        catch (Exception ex)
        {
            Program.Log("error al iniciar webview2: " + ex);
            MessageBox.Show(this,
                (Espanol ? "No se pudo iniciar la interfaz (WebView2).\n\n" : "Could not start the interface (WebView2).\n\n") + ex.Message +
                (Espanol ? "\n\nInstala el runtime de Microsoft Edge WebView2 desde:\n" : "\n\nInstall the Microsoft Edge WebView2 runtime from:\n") +
                "https://developer.microsoft.com/microsoft-edge/webview2/",
                "CoreBlaze", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private void Enviar(object mensaje)
    {
        if (!listo || web.CoreWebView2 == null) return;
        try
        {
            web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(mensaje, Json));
        }
        catch (Exception ex)
        {
            Program.Log("error al enviar a la interfaz: " + ex.Message);
        }
    }

    private void Recibir(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var m = doc.RootElement;
            switch (m.GetProperty("accion").GetString())
            {
                case "listo":
                    listo = true;
                    EnviarInicio();
                    timer.Start();
                    Actualizar();
                    _ = BuscarActualizacion(false);
                    break;
                case "iniciar":
                    Iniciar(m);
                    break;
                case "benchmark":
                    IniciarBench(m);
                    break;
                case "detener":
                    if (ejecucion == Ejecucion.Bench) CancelarBench();
                    else Detener("usuario");
                    break;
                case "gpu":
                    SeleccionarGpu(m.GetProperty("indice").GetInt32());
                    break;
                case "encima":
                    TopMost = m.GetProperty("valor").GetBoolean();
                    break;
                case "exportar":
                    Exportar(m.GetProperty("id").GetString());
                    break;
                case "borrarHistorial":
                    Historial.Borrar();
                    historial.Clear();
                    Enviar(new { tipo = "historial", lista = Array.Empty<object>() });
                    break;
                case "borrarBench":
                    benchs.Clear();
                    Historial.GuardarBench(benchs);
                    Enviar(new { tipo = "benchs", lista = Array.Empty<object>() });
                    break;
                case "config":
                    Directory.CreateDirectory(Historial.Base);
                    File.WriteAllText(RutaConfig, m.GetProperty("cfg").GetRawText());
                    break;
                case "carpeta":
                    Directory.CreateDirectory(Historial.Carpeta);
                    Process.Start("explorer.exe", Historial.Carpeta);
                    break;
                case "verificar":
                    _ = BuscarActualizacion(true);
                    break;
                case "abrirUrl":
                    AbrirUrl(m.GetProperty("url").GetString());
                    break;
            }
        }
        catch (Exception ex)
        {
            Program.Log("error al procesar mensaje: " + ex);
        }
    }

    // solo abre enlaces del repositorio del proyecto
    private static void AbrirUrl(string url)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith("https://github.com/" + Repo, StringComparison.OrdinalIgnoreCase)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private static string RutaConfig => Path.Combine(Historial.Base, "config.json");

    // configuracion guardada de la interfaz, o null si no existe
    private static JsonElement? LeerConfig()
    {
        try
        {
            if (!File.Exists(RutaConfig)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(RutaConfig));
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private void EnviarInicio()
    {
        int gpuSel = adaptadores.Count > 0 ? adaptadores.IndexOf(adaptadores.OrderByDescending(a => a.Memoria).First()) : -1;
        if (gpuSel >= 0) monitor.SeleccionarGpu(adaptadores[gpuSel].Nombre);
        Enviar(new
        {
            tipo = "init",
            version = Version,
            idioma = Espanol ? "es" : "en",
            repo = "https://github.com/" + Repo,
            cpu = monitor.NombreCpu,
            hilos = Program.Hilos,
            avx = System.Runtime.Intrinsics.X86.Fma.IsSupported,
            gpus = adaptadores.Select((a, i) => new { indice = i, nombre = a.Nombre, memoria = a.Memoria / 1024 / 1024 }).ToList(),
            gpuSel,
            config = LeerConfig(),
            modos = Estres.Nombres.Select((n, i) => new { indice = i, unidad = Estres.Unidad((ModoCpu)i) }).ToList(),
            historial = historial.Select(h => h.Resumen()).ToList(),
            benchs = benchs.Select(b => b.Dto()).ToList()
        });
    }

    private void SeleccionarGpu(int indice)
    {
        if (indice >= 0 && indice < adaptadores.Count) monitor.SeleccionarGpu(adaptadores[indice].Nombre);
    }

    private void Reiniciar()
    {
        tempMaxCpu = tempMaxGpu = potMaxCpu = potMaxGpu = picoFrecCpu = 0;
        sobreLimiteCpu = sobreLimiteGpu = 0;
        trabajoAnterior = lotesAnterior = 0;
        segundosAnterior = reloj.Elapsed.TotalSeconds;
        inicio = DateTime.Now;
    }

    private static bool EnBateria()
    {
        var ps = SystemInformation.PowerStatus;
        return ps.PowerLineStatus == PowerLineStatus.Offline && ps.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery;
    }

    private void Iniciar(JsonElement m)
    {
        if (Corriendo) return;
        bool conCpu = m.GetProperty("cpu").GetBoolean();
        bool conGpu = m.GetProperty("gpu").GetBoolean() && adaptadores.Count > 0;
        if (!conCpu && !conGpu)
        {
            Enviar(new { tipo = "aviso", nivel = "warn", codigo = "sinComponentes" });
            return;
        }

        var modo = (ModoCpu)Math.Clamp(m.GetProperty("modo").GetInt32(), 0, 2);
        int hilos = Math.Clamp(m.GetProperty("hilos").GetInt32(), 1, Program.Hilos);
        int gpuIdx = Math.Clamp(m.GetProperty("gpuIndice").GetInt32(), 0, Math.Max(0, adaptadores.Count - 1));
        minutos = Math.Max(0, m.GetProperty("minutos").GetInt32());
        limite = Math.Clamp(m.GetProperty("limite").GetSingle(), 60, 110);
        if (conGpu) SeleccionarGpu(gpuIdx);

        Reiniciar();
        sesion = new Sesion
        {
            Id = inicio.ToString("yyyyMMdd-HHmmss"), Inicio = inicio, ConCpu = conCpu, ConGpu = conGpu, Hilos = hilos,
            ModoCpu = Estres.Nombres[(int)modo], ModoIndice = (int)modo, UnidadCpu = Estres.Unidad(modo),
            NombreCpu = monitor.NombreCpu, NombreGpu = conGpu ? adaptadores[gpuIdx].Nombre : "", ConBateria = EnBateria()
        };
        ejecucion = Ejecucion.Estres;

        if (conCpu) estresCpu.Iniciar(hilos, modo);
        if (conGpu) estresGpu.Iniciar(adaptadores[gpuIdx]);

        Enviar(new { tipo = "aviso", nivel = "info", codigo = "iniciada", cpu = conCpu, modo = (int)modo, hilos, gpu = conGpu ? adaptadores[gpuIdx].Nombre : null });
        if (sesion.ConBateria) Enviar(new { tipo = "aviso", nivel = "warn", codigo = "bateria" });
        Actualizar();
    }

    private void Detener(string motivo, bool alarma = false)
    {
        if (ejecucion != Ejecucion.Estres) return;
        ejecucion = Ejecucion.Nada;
        estresCpu.Detener();
        estresGpu.Detener();

        sesion.Fin = DateTime.Now;
        sesion.Motivo = motivo;
        ultimaDuracion = sesion.Duracion;
        bool valida = sesion.Muestras.Count >= 3;
        if (valida)
        {
            historial.Insert(0, sesion);
            Historial.Guardar(sesion);
        }
        if (alarma) SystemSounds.Exclamation.Play();

        Enviar(new
        {
            tipo = "detenido",
            motivo,
            limite,
            alarma,
            mostrar = valida && sesion.Duracion.TotalSeconds >= 15,
            resumen = valida ? sesion.Resumen() : null
        });
        Actualizar();
    }

    // prueba fija: 30 s de cpu en modo extremo y luego 30 s de gpu
    private void IniciarBench(JsonElement m)
    {
        if (Corriendo) return;
        int gpuIdx = m.GetProperty("gpuIndice").GetInt32();
        benchGpu = gpuIdx >= 0 && gpuIdx < adaptadores.Count ? adaptadores[gpuIdx] : null;
        if (benchGpu != null) SeleccionarGpu(gpuIdx);
        limite = Math.Clamp(m.GetProperty("limite").GetSingle(), 60, 110);
        minutos = 0;

        Reiniciar();
        benchCpu.Clear(); benchGpuRend.Clear();
        benchTempCpu.Clear(); benchTempGpu.Clear(); benchPotCpu.Clear(); benchPotGpu.Clear();
        benchConBateria = EnBateria();
        ejecucion = Ejecucion.Bench;
        estresCpu.Iniciar(Program.Hilos, ModoCpu.Extremo);

        Enviar(new { tipo = "aviso", nivel = "info", codigo = "benchIniciado" });
        if (benchConBateria) Enviar(new { tipo = "aviso", nivel = "warn", codigo = "bateria" });
        Actualizar();
    }

    private void CancelarBench(string codigo = "benchCancelado", bool alarma = false)
    {
        if (ejecucion != Ejecucion.Bench) return;
        ejecucion = Ejecucion.Nada;
        estresCpu.Detener();
        estresGpu.Detener();
        ultimaDuracion = DateTime.Now - inicio;
        if (alarma) SystemSounds.Exclamation.Play();
        Enviar(new { tipo = "aviso", nivel = alarma ? "bad" : "warn", codigo, limite });
        Enviar(new { tipo = "benchFin" });
        Actualizar();
    }

    private void TerminarBench()
    {
        ejecucion = Ejecucion.Nada;
        estresCpu.Detener();
        estresGpu.Detener();
        ultimaDuracion = DateTime.Now - inicio;

        double cpu = benchCpu.Count > 0 ? benchCpu.Average() : 0;
        double gpu = benchGpuRend.Count > 0 ? benchGpuRend.Average() : 0;
        var r = new ResultadoBench
        {
            Id = inicio.ToString("yyyyMMdd-HHmmss"),
            Fecha = inicio,
            Version = Version,
            NombreCpu = monitor.NombreCpu,
            NombreGpu = benchGpu?.Nombre ?? "",
            Hilos = Program.Hilos,
            CpuGflops = Math.Round(cpu, 1),
            GpuGflops = Math.Round(gpu),
            PuntajeCpu = (int)Math.Round(cpu * ResultadoBench.PesoCpu),
            PuntajeGpu = (int)Math.Round(gpu * ResultadoBench.PesoGpu),
            CpuTempMax = Sesion.Max(benchTempCpu),
            GpuTempMax = Sesion.Max(benchTempGpu),
            CpuPot = Sesion.Prom(benchPotCpu),
            GpuPot = Sesion.Prom(benchPotGpu),
            ConBateria = benchConBateria,
            ConGpu = benchGpu != null,
            SerieCpu = Sesion.Reducir(benchTempCpu, 60),
            SerieGpu = Sesion.Reducir(benchTempGpu, 60)
        };
        r.Total = r.PuntajeCpu + r.PuntajeGpu;

        int mejorAnterior = benchs.Where(b => !b.ConBateria).Select(b => b.Total).DefaultIfEmpty(0).Max();
        benchs.Insert(0, r);
        Historial.GuardarBench(benchs);
        Enviar(new { tipo = "benchFin", resultado = r.Dto(), mejorAnterior });
        Actualizar();
    }

    private void Exportar(string id)
    {
        var s = historial.FirstOrDefault(h => h.Id == id);
        if (s == null) return;
        using var dlg = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = "coreblaze-" + s.Id + ".csv",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            s.ExportarCsv(dlg.FileName);
            Enviar(new { tipo = "aviso", nivel = "ok", codigo = "csvOk", ruta = dlg.FileName });
        }
        catch (Exception ex)
        {
            Enviar(new { tipo = "aviso", nivel = "bad", codigo = "csvError", error = ex.Message });
        }
    }

    // consulta el ultimo release publicado en github y avisa si hay una version mas nueva
    private async Task BuscarActualizacion(bool manual)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CoreBlaze/" + Version);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            if (!resp.IsSuccessStatusCode)
            {
                if (manual) Enviar(new { tipo = "actualizacion", hay = false, error = resp.StatusCode != System.Net.HttpStatusCode.NotFound });
                return;
            }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var raiz = doc.RootElement;
            string tag = raiz.GetProperty("tag_name").GetString() ?? "";
            string url = raiz.GetProperty("html_url").GetString() ?? "";
            string notas = raiz.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            bool hay = System.Version.TryParse(tag.TrimStart('v', 'V'), out var remota) &&
                       System.Version.TryParse(Version, out var local) && remota > local;
            if (hay || manual)
                Enviar(new { tipo = "actualizacion", hay, version = tag.TrimStart('v', 'V'), url, notas = notas.Length > 700 ? notas[..700] + "..." : notas });
        }
        catch (Exception ex)
        {
            Program.Log("no se pudo revisar actualizaciones: " + ex.Message);
            if (manual) Enviar(new { tipo = "actualizacion", hay = false, error = true });
        }
    }

    private void Actualizar()
    {
        var l = monitor.Leer();

        // rendimiento medido desde la ultima lectura
        double ahora = reloj.Elapsed.TotalSeconds;
        double dt = Math.Max(0.2, ahora - segundosAnterior);
        segundosAnterior = ahora;
        double? rendCpu = null, rendGpu = null;
        if (estresCpu.Activo)
        {
            long t = estresCpu.Trabajo;
            rendCpu = (t - trabajoAnterior) / dt * Estres.Factor(estresCpu.Modo);
            trabajoAnterior = t;
        }
        if (estresGpu.Activo)
        {
            long t = estresGpu.Lotes;
            rendGpu = (t - lotesAnterior) / dt * EstresGpu.FlopsPorLote / 1e9;
            lotesAnterior = t;
        }

        if (l.Cpu.Temperatura is float tc && tc > tempMaxCpu) tempMaxCpu = tc;
        if (l.Gpu.Temperatura is float tg && tg > tempMaxGpu) tempMaxGpu = tg;
        if (l.Cpu.Potencia is float pc && pc > potMaxCpu) potMaxCpu = pc;
        if (l.Gpu.Potencia is float pg && pg > potMaxGpu) potMaxGpu = pg;
        if (estresCpu.Activo && l.Cpu.Frecuencia is float fc && fc > picoFrecCpu) picoFrecCpu = fc;

        bool throttling = estresCpu.Activo && l.Cpu.Temperatura is float tt && l.Cpu.Frecuencia is float ff &&
                          (tt >= 99 || (picoFrecCpu > 0 && ff < picoFrecCpu * 0.7f && tt >= 90));

        var dur = Corriendo ? DateTime.Now - inicio : ultimaDuracion;
        int seg = (int)dur.TotalSeconds;
        bool bateria = EnBateria();
        var ps = SystemInformation.PowerStatus;

        if (ejecucion == Ejecucion.Estres)
        {
            if (bateria) sesion.ConBateria = true;
            sesion.Muestras.Add(new Muestra
            {
                Hora = DateTime.Now, CpuUso = l.Cpu.Uso, CpuTemp = l.Cpu.Temperatura, CpuFrec = l.Cpu.Frecuencia, CpuPot = l.Cpu.Potencia, CpuRend = rendCpu,
                GpuUso = l.Gpu.Uso, GpuTemp = l.Gpu.Temperatura, GpuHot = l.Gpu.Hotspot, GpuFrec = l.Gpu.Frecuencia, GpuPot = l.Gpu.Potencia, GpuRend = rendGpu
            });
        }
        else if (ejecucion == Ejecucion.Bench)
        {
            if (bateria) benchConBateria = true;
            benchTempCpu.Add(l.Cpu.Temperatura);
            benchTempGpu.Add(l.Gpu.Temperatura);
            // se descartan los primeros segundos de cada fase mientras sube el turbo
            if (seg < FaseBench)
            {
                if (seg >= 6 && rendCpu is double rc) benchCpu.Add(rc);
                if (seg >= 6) benchPotCpu.Add(l.Cpu.Potencia);
            }
            else if (seg >= FaseBench + 6)
            {
                if (rendGpu is double rg) benchGpuRend.Add(rg);
                benchPotGpu.Add(l.Gpu.Potencia);
            }
        }

        Enviar(new
        {
            tipo = "estado",
            corriendo = Corriendo,
            ejecucion = ejecucion.ToString().ToLowerInvariant(),
            tiempo = dur.ToString(@"hh\:mm\:ss"),
            segundos = seg,
            restante = ejecucion == Ejecucion.Estres && minutos > 0 ? Math.Max(0, minutos * 60 - seg) : -1,
            limite,
            bench = ejecucion == Ejecucion.Bench ? new { fase = seg < FaseBench ? 1 : 2, segundos = seg, total = benchGpu != null ? FaseBench * 2 : FaseBench } : null,
            energia = new
            {
                bateria,
                tieneBateria = ps.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery,
                carga = ps.BatteryLifePercent <= 1 ? (int?)Math.Round(ps.BatteryLifePercent * 100) : null
            },
            cpu = new
            {
                uso = l.Cpu.Uso, temp = l.Cpu.Temperatura, frec = l.Cpu.Frecuencia, pot = l.Cpu.Potencia, rend = rendCpu,
                tempMax = Nulo(tempMaxCpu), potMax = Nulo(potMaxCpu), picoFrec = Nulo(picoFrecCpu), throttling,
                activo = estresCpu.Activo, unidad = Estres.Unidad(estresCpu.Modo), modo = (int)estresCpu.Modo
            },
            gpu = new
            {
                uso = l.Gpu.Uso, temp = l.Gpu.Temperatura, hot = l.Gpu.Hotspot, frec = l.Gpu.Frecuencia, pot = l.Gpu.Potencia, rend = rendGpu,
                tempMax = Nulo(tempMaxGpu), potMax = Nulo(potMaxGpu), activo = estresGpu.Activo, nombre = monitor.NombreGpu,
                sinSensores = monitor.GpuSinSensores, error = estresGpu.Error
            },
            sensores = l.Sensores.Select(s => new { d = s.Dispositivo, t = s.Tipo, n = s.Nombre, v = s.Valor, m = s.Maximo })
        });

        if (!Corriendo) return;

        // solo se detiene si la temperatura se mantiene sobre el limite varios segundos seguidos
        sobreLimiteCpu = l.Cpu.Temperatura >= limite ? sobreLimiteCpu + 1 : 0;
        sobreLimiteGpu = l.Gpu.Temperatura >= limite ? sobreLimiteGpu + 1 : 0;

        if (ejecucion == Ejecucion.Bench)
        {
            if (sobreLimiteCpu >= SegundosParaDetener || sobreLimiteGpu >= SegundosParaDetener) CancelarBench("benchTemp", true);
            else if (seg >= FaseBench && estresCpu.Activo)
            {
                // cambio de fase: termina la cpu y empieza la gpu
                estresCpu.Detener();
                if (benchGpu != null) estresGpu.Iniciar(benchGpu);
                else TerminarBench();
            }
            else if (seg >= FaseBench * 2) TerminarBench();
            else if (benchGpu != null && seg > FaseBench + 2 && !estresGpu.Activo && estresGpu.Error != null) CancelarBench("benchErrorGpu", true);
            return;
        }

        if (minutos > 0 && dur.TotalMinutes >= minutos) Detener("tiempo");
        else if (sobreLimiteCpu >= SegundosParaDetener) Detener("tempCpu", true);
        else if (sobreLimiteGpu >= SegundosParaDetener) Detener("tempGpu", true);
        else if (sesion.ConGpu && !estresGpu.Activo && estresGpu.Error != null && !estresCpu.Activo) Detener("errorGpu", true);
    }

    private static float? Nulo(float v) => v > 0 ? v : null;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        timer.Stop();
        if (ejecucion == Ejecucion.Estres) Detener("cierre");
        ejecucion = Ejecucion.Nada;
        estresCpu.Detener();
        estresGpu.Detener();
        monitor.Dispose();
        base.OnFormClosing(e);
    }
}
