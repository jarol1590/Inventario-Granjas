using System.IO.Compression;

namespace UCaldas.Granjas.Tests.Infraestructura;

/// <summary>
/// Genera un PNG con fondo transparente que imita una firma manuscrita, sin depender de librerías de imágenes.
/// Sirve para revisar visualmente cómo queda la firma incrustada en los informes.
/// </summary>
public static class FirmaDePrueba
{
    public static byte[] GenerarPng(int ancho = 300, int alto = 100)
    {
        var pixeles = new byte[ancho * alto * 4];

        void Punto(int cx, int cy, int radio)
        {
            for (var dy = -radio; dy <= radio; dy++)
            {
                for (var dx = -radio; dx <= radio; dx++)
                {
                    if ((dx * dx) + (dy * dy) > radio * radio)
                    {
                        continue;
                    }

                    var x = cx + dx;
                    var y = cy + dy;
                    if (x < 0 || y < 0 || x >= ancho || y >= alto)
                    {
                        continue;
                    }

                    var i = ((y * ancho) + x) * 4;
                    pixeles[i] = 20;
                    pixeles[i + 1] = 40;
                    pixeles[i + 2] = 120;
                    pixeles[i + 3] = 255;
                }
            }
        }

        // Trazo principal: una curva con oscilaciones decrecientes, como una rúbrica.
        for (var x = 10; x < ancho - 20; x++)
        {
            var t = (x - 10d) / (ancho - 30d);
            var envolvente = Math.Sin(Math.PI * t) * 0.85 + 0.15;
            var y = (alto * 0.55) + (Math.Sin(t * 22) * alto * 0.3 * envolvente) - (t * alto * 0.12);
            Punto(x, (int)y, 2);
        }

        // Subrayado final.
        for (var x = 40; x < ancho - 40; x++)
        {
            Punto(x, (int)(alto * 0.88 - (x - 40) * 0.04), 1);
        }

        return CodificarPng(pixeles, ancho, alto);
    }

    private static byte[] CodificarPng(byte[] rgba, int ancho, int alto)
    {
        using var crudo = new MemoryStream();
        for (var fila = 0; fila < alto; fila++)
        {
            crudo.WriteByte(0); // filtro "none"
            crudo.Write(rgba, fila * ancho * 4, ancho * 4);
        }

        using var comprimido = new MemoryStream();
        using (var z = new ZLibStream(comprimido, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(crudo.ToArray());
        }

        using var salida = new MemoryStream();
        salida.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var cabecera = new byte[13];
        EscribirEntero(cabecera, 0, ancho);
        EscribirEntero(cabecera, 4, alto);
        cabecera[8] = 8;  // profundidad de bits
        cabecera[9] = 6;  // RGBA
        Chunk(salida, "IHDR", cabecera);
        Chunk(salida, "IDAT", comprimido.ToArray());
        Chunk(salida, "IEND", []);
        return salida.ToArray();
    }

    private static void Chunk(Stream salida, string tipo, byte[] datos)
    {
        var longitud = new byte[4];
        EscribirEntero(longitud, 0, datos.Length);
        salida.Write(longitud);

        var cuerpo = new byte[4 + datos.Length];
        for (var i = 0; i < 4; i++)
        {
            cuerpo[i] = (byte)tipo[i];
        }

        datos.CopyTo(cuerpo, 4);
        salida.Write(cuerpo);

        var crc = new byte[4];
        EscribirEntero(crc, 0, unchecked((int)Crc32(cuerpo)));
        salida.Write(crc);
    }

    private static void EscribirEntero(byte[] destino, int posicion, int valor)
    {
        destino[posicion] = (byte)(valor >> 24);
        destino[posicion + 1] = (byte)(valor >> 16);
        destino[posicion + 2] = (byte)(valor >> 8);
        destino[posicion + 3] = (byte)valor;
    }

    private static uint Crc32(byte[] datos)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in datos)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
