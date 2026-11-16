using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace ProbarCpu;

public sealed class AdaptadorGpu
{
    public int Indice;
    public string Nombre;
    public long Memoria;
    public override string ToString() => Nombre + " (" + (Memoria / 1024 / 1024) + " mb)";
}

// genera carga maxima en la gpu con un compute shader de directx 11
public sealed class EstresGpu
{
    private const int Elementos = 65536;
    private const int Grupos = 1024;

    private const string Shader = @"
RWStructuredBuffer<float4> salida : register(u0);

[numthreads(256, 1, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    float f = (float)id.x * 0.0001;
    float4 a = float4(f, f + 0.1, f + 0.2, f + 0.3);
    float4 b = float4(1.0001, 0.9999, 1.00001, 0.99999);
    float4 c = float4(0.0001, 0.0002, 0.0003, 0.0004);
    float4 d = a * 0.5;
    float4 e = b * 0.5;
    float4 g = c * 2.0;
    float4 h = a + b;
    float4 k = b - c;
    [loop]
    for (int i = 0; i < 16384; i++)
    {
        a = mad(a, b, c);
        d = mad(d, e, g);
        h = mad(h, k, c);
        k = mad(k, a, -g);
        b = mad(b, d, -c);
        e = mad(e, h, g);
        c = mad(c, k, a);
        g = mad(g, a, -d);
    }
    salida[id.x % 65536] = a + b + c + d + e + g + h + k;
}";

    // 256 hilos por grupo, 16384 iteraciones de 8 mad float4 (64 flops)
    public const double FlopsPorLote = (double)Grupos * 256 * 16384 * 64;

    private Thread hilo;
    private volatile bool activo;
    private long lotes;

    public bool Activo => activo;
    public long Lotes => Interlocked.Read(ref lotes);
    public string Error { get; private set; }

    public static List<AdaptadorGpu> ListarAdaptadores()
    {
        var lista = new List<AdaptadorGpu>();
        try
        {
            using var fabrica = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint i = 0; fabrica.EnumAdapters1(i, out IDXGIAdapter1 adaptador).Success; i++)
            {
                using (adaptador)
                {
                    var desc = adaptador.Description1;
                    // se omite el adaptador de software de microsoft
                    if ((desc.Flags & AdapterFlags.Software) != 0) continue;
                    lista.Add(new AdaptadorGpu { Indice = (int)i, Nombre = desc.Description.Trim(), Memoria = (long)(nuint)desc.DedicatedVideoMemory });
                }
            }
        }
        catch (Exception ex)
        {
            Program.Log("error al listar gpus: " + ex);
        }
        return lista;
    }

    public void Iniciar(AdaptadorGpu adaptador)
    {
        if (activo || adaptador == null) return;
        Error = null;
        Interlocked.Exchange(ref lotes, 0);
        activo = true;
        hilo = new Thread(() => Trabajar(adaptador)) { IsBackground = true, Name = "estres-gpu" };
        hilo.Start();
    }

    public void Detener()
    {
        activo = false;
        hilo?.Join(5000);
        hilo = null;
    }

    private void Trabajar(AdaptadorGpu info)
    {
        IDXGIFactory1 fabrica = null;
        IDXGIAdapter1 adaptador = null;
        ID3D11Device device = null;
        ID3D11DeviceContext context = null;
        ID3D11ComputeShader cs = null;
        ID3D11Buffer buffer = null, copia = null;
        ID3D11UnorderedAccessView uav = null;
        try
        {
            fabrica = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            fabrica.EnumAdapters1((uint)info.Indice, out adaptador).CheckError();

            D3D11.D3D11CreateDevice(adaptador, DriverType.Unknown, DeviceCreationFlags.None,
                new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 }, out device, out context).CheckError();

            var bytecode = Compiler.Compile(Shader, "main", "estres.hlsl", "cs_5_0");
            cs = device.CreateComputeShader(bytecode.Span);

            buffer = device.CreateBuffer(new BufferDescription(Elementos * 16, BindFlags.UnorderedAccess, ResourceUsage.Default,
                CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 16));
            copia = device.CreateBuffer(new BufferDescription(Elementos * 16, BindFlags.None, ResourceUsage.Staging,
                CpuAccessFlags.Read, ResourceOptionFlags.None, 0));
            uav = device.CreateUnorderedAccessView(buffer, new UnorderedAccessViewDescription
            {
                ViewDimension = UnorderedAccessViewDimension.Buffer,
                Format = Format.Unknown,
                Buffer = new BufferUnorderedAccessView { FirstElement = 0, NumElements = Elementos }
            });

            context.CSSetShader(cs);
            context.CSSetUnorderedAccessView(0, uav);

            while (activo)
            {
                // varios lotes cortos para no gatillar el reinicio del driver por timeout
                for (int i = 0; i < 4; i++) context.Dispatch(Grupos, 1, 1);
                // espera a que la gpu termine antes de encolar mas trabajo
                context.CopyResource(copia, buffer);
                context.Map(copia, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                context.Unmap(copia, 0);
                Interlocked.Add(ref lotes, 4);
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Program.Log("error en estres gpu: " + ex);
            activo = false;
        }
        finally
        {
            context?.ClearState();
            uav?.Dispose();
            copia?.Dispose();
            buffer?.Dispose();
            cs?.Dispose();
            context?.Dispose();
            device?.Dispose();
            adaptador?.Dispose();
            fabrica?.Dispose();
        }
    }
}
