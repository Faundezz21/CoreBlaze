using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace ProbarCpu;

public enum ModoCpu
{
    Extremo,
    Estandar,
    Memoria
}

// genera carga maxima en el cpu usando un hilo por nucleo logico
public sealed class Estres
{
    private readonly List<Thread> hilos = new();
    private volatile bool activo;
    private long trabajo;
    private double resultado;

    public bool Activo => activo;
    public ModoCpu Modo { get; private set; }
    public long Trabajo => Interlocked.Read(ref trabajo);

    public static readonly string[] Nombres = { "Extremo (AVX2 + FMA)", "Estándar (FPU)", "Memoria RAM" };

    public static string Unidad(ModoCpu m) => m switch
    {
        ModoCpu.Extremo => "GFLOPS",
        ModoCpu.Estandar => "Mop/s",
        _ => "GB/s"
    };

    // convierte unidades de trabajo por segundo a la unidad mostrada
    public static double Factor(ModoCpu m) => m == ModoCpu.Estandar ? 1e-6 : 1e-9;

    public void Iniciar(int cantidadHilos, ModoCpu modo)
    {
        if (activo) return;
        Modo = modo;
        activo = true;
        Interlocked.Exchange(ref trabajo, 0);
        hilos.Clear();
        for (int i = 0; i < cantidadHilos; i++)
        {
            int semilla = i + 1;
            var hilo = new Thread(() => Trabajar(semilla))
            {
                IsBackground = true,
                Priority = ThreadPriority.Normal,
                Name = "estres-" + semilla
            };
            hilos.Add(hilo);
            hilo.Start();
        }
    }

    public void Detener()
    {
        activo = false;
        foreach (var h in hilos) h.Join(3000);
        hilos.Clear();
    }

    private void Trabajar(int semilla)
    {
        switch (Modo)
        {
            case ModoCpu.Extremo: Avx(semilla); break;
            case ModoCpu.Memoria: Memoria(); break;
            default: Fpu(semilla); break;
        }
    }

    // cadenas fma independientes en registros de 256 bits, la carga que mas calienta
    private void Avx(int semilla)
    {
        if (!Fma.IsSupported || !Avx2.IsSupported)
        {
            Fpu(semilla);
            return;
        }
        var m = Vector256.Create(0.9999999);
        var c = Vector256.Create(1e-7);
        var a0 = Vector256.Create(semilla * 0.01); var a1 = Vector256.Create(semilla * 0.02);
        var a2 = Vector256.Create(semilla * 0.03); var a3 = Vector256.Create(semilla * 0.04);
        var a4 = Vector256.Create(semilla * 0.05); var a5 = Vector256.Create(semilla * 0.06);
        var a6 = Vector256.Create(semilla * 0.07); var a7 = Vector256.Create(semilla * 0.08);
        var a8 = Vector256.Create(semilla * 0.09); var a9 = Vector256.Create(semilla * 0.10);
        const int n = 400_000;
        while (activo)
        {
            for (int i = 0; i < n; i++)
            {
                a0 = Fma.MultiplyAdd(a0, m, c); a1 = Fma.MultiplyAdd(a1, m, c);
                a2 = Fma.MultiplyAdd(a2, m, c); a3 = Fma.MultiplyAdd(a3, m, c);
                a4 = Fma.MultiplyAdd(a4, m, c); a5 = Fma.MultiplyAdd(a5, m, c);
                a6 = Fma.MultiplyAdd(a6, m, c); a7 = Fma.MultiplyAdd(a7, m, c);
                a8 = Fma.MultiplyAdd(a8, m, c); a9 = Fma.MultiplyAdd(a9, m, c);
            }
            // 10 fma de 4 doubles, 2 flops cada una
            Interlocked.Add(ref trabajo, (long)n * 10 * 4 * 2);
        }
        var suma = a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9;
        resultado += suma.GetElement(0);
    }

    // mezcla de operaciones de punto flotante y enteras
    private void Fpu(int semilla)
    {
        double a = semilla * 1.1, b = 0.5, c = 1.0000001;
        ulong x = (ulong)semilla * 2654435761UL;
        double[] buffer = new double[4096];
        while (activo)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                a = a * c + Math.Sqrt(b + i);
                b = Math.FusedMultiplyAdd(b, 0.9999999, a * 1e-9);
                x ^= x << 13; x ^= x >> 7; x ^= x << 17;
                buffer[i] = a + b + (x & 0xFFFF);
            }
            if (a > 1e12) a = semilla;
            if (b > 1e12) b = 0.5;
            Interlocked.Add(ref trabajo, buffer.Length * 10L);
        }
        resultado += buffer[0];
    }

    // lectura y escritura continua de un bloque grande para estresar la memoria
    private void Memoria()
    {
        var bloques = new Vector<long>[4 * 1024 * 1024 / Vector<long>.Count];
        var uno = Vector<long>.One;
        long bytes = (long)bloques.Length * Vector<long>.Count * sizeof(long) * 2;
        while (activo)
        {
            var span = bloques.AsSpan();
            for (int i = 0; i < span.Length; i++) span[i] += uno;
            Interlocked.Add(ref trabajo, bytes);
        }
        resultado += bloques[0][0];
    }
}
