using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ProbarCpu;

// una muestra por segundo de la sesion de estres
public sealed class Muestra
{
    public DateTime Hora;
    public float? CpuUso, CpuTemp, CpuFrec, CpuPot;
    public float? GpuUso, GpuTemp, GpuHot, GpuFrec, GpuPot;
    public double? CpuRend, GpuRend;
}

public sealed class Sesion
{
    public string Id;
    public DateTime Inicio;
    public DateTime Fin;
    public string ModoCpu;
    public string UnidadCpu;
    public int Hilos;
    public bool ConCpu, ConGpu;
    public string NombreCpu, NombreGpu;
    public string Motivo;
    public List<Muestra> Muestras = new();

    public TimeSpan Duracion => Fin - Inicio;

    // muestras del tramo final, cuando la temperatura ya se estabilizo
    public List<Muestra> TramoFinal()
    {
        int n = Math.Min(60, Math.Max(1, Muestras.Count / 2));
        return Muestras.Skip(Math.Max(0, Muestras.Count - n)).ToList();
    }

    public object Resumen()
    {
        var f = TramoFinal();
        float? cpuTemp = Prom(f.Select(m => m.CpuTemp));
        float? gpuTemp = Prom(f.Select(m => m.GpuTemp));
        float? gpuHot = Prom(f.Select(m => m.GpuHot));
        var (vc, nc) = Veredicto.Cpu(cpuTemp);
        var (vg, ng) = Veredicto.Gpu(gpuTemp, gpuHot);
        return new
        {
            id = Id,
            inicio = Inicio.ToString("dd-MM-yyyy HH:mm"),
            duracion = Duracion.ToString(@"hh\:mm\:ss"),
            segundos = (int)Duracion.TotalSeconds,
            motivo = Motivo,
            conCpu = ConCpu,
            conGpu = ConGpu,
            modo = ModoCpu,
            unidad = UnidadCpu,
            hilos = Hilos,
            nombreCpu = NombreCpu,
            nombreGpu = NombreGpu,
            cpu = new
            {
                temp = cpuTemp,
                tempMax = Max(Muestras.Select(m => m.CpuTemp)),
                frec = Prom(f.Select(m => m.CpuFrec)),
                pot = Prom(f.Select(m => m.CpuPot)),
                rend = PromD(f.Select(m => m.CpuRend)),
                veredicto = vc,
                nivel = nc
            },
            gpu = new
            {
                temp = gpuTemp,
                tempMax = Max(Muestras.Select(m => m.GpuTemp)),
                hot = gpuHot,
                frec = Prom(f.Select(m => m.GpuFrec)),
                pot = Prom(f.Select(m => m.GpuPot)),
                rend = PromD(f.Select(m => m.GpuRend)),
                veredicto = vg,
                nivel = ng
            },
            serie = new
            {
                cpuTemp = Reducir(Muestras.Select(m => m.CpuTemp)),
                gpuTemp = Reducir(Muestras.Select(m => m.GpuTemp)),
                cpuUso = Reducir(Muestras.Select(m => m.CpuUso)),
                gpuUso = Reducir(Muestras.Select(m => m.GpuUso))
            }
        };
    }

    public void ExportarCsv(string ruta)
    {
        var c = CultureInfo.CurrentCulture;
        string sep = c.NumberFormat.NumberDecimalSeparator == "," ? ";" : ",";
        string F(float? v) => v?.ToString("0.0", c) ?? "";
        string D(double? v) => v?.ToString("0.0", c) ?? "";

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(sep, "hora", "cpu_uso_%", "cpu_temp_c", "cpu_mhz", "cpu_w", "cpu_rend_" + UnidadCpu,
            "gpu_uso_%", "gpu_temp_c", "gpu_hotspot_c", "gpu_mhz", "gpu_w", "gpu_gflops"));
        foreach (var m in Muestras)
        {
            sb.AppendLine(string.Join(sep, m.Hora.ToString("HH:mm:ss"), F(m.CpuUso), F(m.CpuTemp), F(m.CpuFrec), F(m.CpuPot), D(m.CpuRend),
                F(m.GpuUso), F(m.GpuTemp), F(m.GpuHot), F(m.GpuFrec), F(m.GpuPot), D(m.GpuRend)));
        }
        File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(true));
    }

    // reduce una serie a un maximo de puntos para graficarla
    private static float[] Reducir(IEnumerable<float?> v, int max = 150)
    {
        var l = v.Select(x => x ?? 0).ToArray();
        if (l.Length <= max) return l;
        var r = new float[max];
        double paso = l.Length / (double)max;
        for (int i = 0; i < max; i++)
        {
            int a = (int)(i * paso), b = Math.Max(a + 1, (int)((i + 1) * paso));
            r[i] = l.Skip(a).Take(b - a).Average();
        }
        return r;
    }

    private static float? Prom(IEnumerable<float?> v)
    {
        var l = v.Where(x => x is > 0).Select(x => x.Value).ToList();
        return l.Count > 0 ? l.Average() : null;
    }

    private static double? PromD(IEnumerable<double?> v)
    {
        var l = v.Where(x => x is > 0).Select(x => x.Value).ToList();
        return l.Count > 0 ? l.Average() : null;
    }

    private static float? Max(IEnumerable<float?> v)
    {
        var l = v.Where(x => x is > 0).Select(x => x.Value).ToList();
        return l.Count > 0 ? l.Max() : null;
    }
}

// criterios de evaluacion termica pensados para laptops serie h
public static class Veredicto
{
    public static (string, string) Cpu(float? t) => t switch
    {
        null => ("Sin datos de temperatura (ejecuta como administrador).", "na"),
        < 85 => ("Excelente: la refrigeración del CPU sobra para esta carga.", "ok"),
        < 92 => ("Bueno: temperatura normal para un laptop a plena carga.", "ok"),
        < 97 => ("Aceptable: cerca del límite, normal en laptops gamer delgados.", "warn"),
        _ => ("Alto: el CPU vive en su límite térmico. Revisa polvo, pasta térmica o el modo de ventiladores.", "bad")
    };

    public static (string, string) Gpu(float? t, float? hot)
    {
        if (t == null) return ("Sin datos de temperatura de la GPU.", "na");
        if (hot is float h && h - t.Value > 20)
            return ($"Diferencia de hot spot alta ({h - t.Value:0} °C): posible pasta térmica seca o mal contacto.", "bad");
        return t.Value switch
        {
            < 75 => ("Excelente: la GPU está muy bien refrigerada.", "ok"),
            < 83 => ("Bueno: temperatura normal bajo carga máxima.", "ok"),
            < 87 => ("Aceptable: algo caliente, pero dentro de lo esperado.", "warn"),
            _ => ("Alto: la GPU se acerca a su límite. Revisa la ventilación del equipo.", "bad")
        };
    }
}

// guarda las sesiones en la carpeta de datos local del usuario
public static class Historial
{
    private const int Maximo = 30;
    private static readonly JsonSerializerOptions Opciones = new() { IncludeFields = true };

    public static string Carpeta => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoreBlaze", "historial");

    public static void Guardar(Sesion s)
    {
        try
        {
            Directory.CreateDirectory(Carpeta);
            File.WriteAllText(Path.Combine(Carpeta, s.Id + ".json"), JsonSerializer.Serialize(s, Opciones));
            // conserva solo las sesiones mas recientes
            foreach (var viejo in new DirectoryInfo(Carpeta).GetFiles("*.json").OrderByDescending(f => f.Name).Skip(Maximo))
                viejo.Delete();
        }
        catch (Exception ex)
        {
            Program.Log("error al guardar historial: " + ex.Message);
        }
    }

    public static List<Sesion> Cargar()
    {
        var lista = new List<Sesion>();
        try
        {
            if (!Directory.Exists(Carpeta)) return lista;
            foreach (var f in new DirectoryInfo(Carpeta).GetFiles("*.json").OrderByDescending(f => f.Name))
            {
                try
                {
                    var s = JsonSerializer.Deserialize<Sesion>(File.ReadAllText(f.FullName), Opciones);
                    if (s != null) lista.Add(s);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Program.Log("error al cargar historial: " + ex.Message);
        }
        return lista;
    }

    public static void Borrar()
    {
        try
        {
            if (Directory.Exists(Carpeta))
                foreach (var f in Directory.GetFiles(Carpeta, "*.json")) File.Delete(f);
        }
        catch { }
    }
}
