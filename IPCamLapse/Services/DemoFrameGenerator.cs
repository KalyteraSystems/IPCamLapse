using System.Buffers.Binary;
using System.IO.Compression;

namespace IPCamLapse.Services;

public interface IDemoFrameGenerator
{
    byte[] CreateFrame();
}

public sealed class DemoFrameGenerator : IDemoFrameGenerator
{
    private const int Width = 640;
    private const int Height = 360;
    private long _frameNumber;

    public byte[] CreateFrame()
    {
        var frame = Interlocked.Increment(ref _frameNumber);
        var raw = new byte[(Width * 3 + 1) * Height];
        var sunX = 70 + (int)(frame * 17 % 500);
        var towerHeight = 70 + (int)(frame * 5 % 140);
        for (var y = 0; y < Height; y++)
        {
            var row = y * (Width * 3 + 1);
            raw[row] = 0;
            for (var x = 0; x < Width; x++)
            {
                var offset = row + 1 + x * 3;
                var sky = y < 260;
                var red = sky ? 22 + y / 12 : 35 + (y - 260) / 4;
                var green = sky ? 65 + y / 8 : 82 + (y - 260) / 3;
                var blue = sky ? 118 + y / 7 : 54;
                var sun = (x - sunX) * (x - sunX) + (y - 78) * (y - 78) < 34 * 34;
                var tower = x is >= 285 and <= 355 && y >= Height - towerHeight;
                var window = tower && (x / 12 + y / 14 + frame / 3) % 3 == 0;
                if (sun)
                {
                    red = 255;
                    green = 205;
                    blue = 82;
                }
                if (tower)
                {
                    red = window ? 125 : 28;
                    green = window ? 211 : 48;
                    blue = window ? 252 : 70;
                }
                raw[offset] = (byte)Math.Clamp(red, 0, 255);
                raw[offset + 1] = (byte)Math.Clamp(green, 0, 255);
                raw[offset + 2] = (byte)Math.Clamp(blue, 0, 255);
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
            zlib.Write(raw);

        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), Height);
        header[8] = 8;
        header[9] = 2;
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        var crcInput = new byte[typeBytes.Length + data.Length];
        typeBytes.CopyTo(crcInput, 0);
        data.CopyTo(crcInput, typeBytes.Length);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, ComputeCrc32(crcInput));
        output.Write(crc);
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xffffffffu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
        }
        return ~crc;
    }
}
