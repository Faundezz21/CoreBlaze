using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ProbarCpu;

// logo de la app: chip con un rayo sobre degradado azul a naranjo
public static class Logo
{
    public static void Dibujar(Graphics g, RectangleF r)
    {
        var modo = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = Math.Min(r.Width, r.Height);
        bool chico = s < 28;

        using (var fondo = Tema.Redondeado(r, s * 0.24f))
        using (var grad = new LinearGradientBrush(r, Color.Blue, Color.Red, 45f))
        {
            grad.InterpolationColors = new ColorBlend
            {
                Colors = new[] { Color.FromArgb(36, 112, 255), Color.FromArgb(140, 72, 240), Color.FromArgb(255, 88, 50) },
                Positions = new[] { 0f, 0.5f, 1f }
            };
            g.FillPath(grad, fondo);
        }

        // brillo sutil en la mitad superior
        if (!chico)
        {
            using var brillo = new LinearGradientBrush(r, Color.FromArgb(55, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f);
            using var fondo = Tema.Redondeado(r, s * 0.24f);
            g.FillPath(brillo, fondo);
        }

        float margen = chico ? s * 0.14f : s * 0.25f;
        var chip = RectangleF.Inflate(r, -margen, -margen);

        if (!chico)
        {
            // patas del chip
            using var pata = new Pen(Color.FromArgb(235, 255, 255, 255), s * 0.045f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            float largo = s * 0.1f;
            for (int i = 1; i <= 3; i++)
            {
                float fx = chip.X + chip.Width * i / 4f;
                float fy = chip.Y + chip.Height * i / 4f;
                g.DrawLine(pata, fx, chip.Top, fx, chip.Top - largo);
                g.DrawLine(pata, fx, chip.Bottom, fx, chip.Bottom + largo);
                g.DrawLine(pata, chip.Left, fy, chip.Left - largo, fy);
                g.DrawLine(pata, chip.Right, fy, chip.Right + largo, fy);
            }

            using var cuerpo = Tema.Redondeado(chip, s * 0.08f);
            using var relleno = new SolidBrush(Color.FromArgb(70, 10, 12, 30));
            using var borde = new Pen(Color.White, s * 0.05f) { LineJoin = LineJoin.Round };
            g.FillPath(relleno, cuerpo);
            g.DrawPath(borde, cuerpo);
        }

        // rayo central
        var forma = new (float x, float y)[]
        {
            (0.60f, 0.06f), (0.20f, 0.56f), (0.46f, 0.56f), (0.37f, 0.94f), (0.80f, 0.40f), (0.54f, 0.40f), (0.66f, 0.06f)
        };
        var zona = chico ? chip : RectangleF.Inflate(chip, -chip.Width * 0.07f, -chip.Height * 0.07f);
        var pts = forma.Select(p => new PointF(zona.X + p.x * zona.Width, zona.Y + p.y * zona.Height)).ToArray();
        g.FillPolygon(Brushes.White, pts);

        g.SmoothingMode = modo;
    }

    public static Bitmap Crear(int tam)
    {
        var bmp = new Bitmap(tam, tam, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        Dibujar(g, new RectangleF(0, 0, tam, tam));
        return bmp;
    }

    // guarda un archivo ico con varias resoluciones en formato png
    public static void GuardarIco(string ruta)
    {
        int[] tams = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var pngs = tams.Select(t =>
        {
            using var b = Crear(t);
            using var ms = new MemoryStream();
            b.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }).ToList();

        using var fs = File.Create(ruta);
        using var w = new BinaryWriter(fs);
        w.Write((short)0);
        w.Write((short)1);
        w.Write((short)tams.Length);
        int offset = 6 + 16 * tams.Length;
        for (int i = 0; i < tams.Length; i++)
        {
            byte lado = (byte)(tams[i] >= 256 ? 0 : tams[i]);
            w.Write(lado);
            w.Write(lado);
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((short)1);
            w.Write((short)32);
            w.Write(pngs[i].Length);
            w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var p in pngs) w.Write(p);
    }
}
