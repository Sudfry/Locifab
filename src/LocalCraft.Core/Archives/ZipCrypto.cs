using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace LocalCraft.Core.Archives;

/// <summary>
/// 传统 ZIP 密码（ZipCrypto）读写。兼容 Windows 资源管理器、7-Zip 等常见工具。
/// 不依赖外部压缩程序，保持完全离线。
/// </summary>
public static class ZipCrypto
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    private static uint Crc32(uint crc, byte b) => (crc >> 8) ^ CrcTable[(crc ^ b) & 0xFF];

    private sealed class Keys
    {
        public uint K0 = 0x12345678;
        public uint K1 = 0x23456789;
        public uint K2 = 0x34567890;

        public void Update(byte b)
        {
            K0 = Crc32(K0, b);
            K1 = (K1 + (K0 & 0xFF)) * 134775813u + 1u;
            K2 = Crc32(K2, (byte)(K1 >> 24));
        }

        public byte Mask()
        {
            uint temp = (K2 | 2u) & 0xFFFFu;
            return (byte)((temp * (temp ^ 1u)) >> 8);
        }
    }

    private static Keys MakeKeys(string password)
    {
        var keys = new Keys();
        foreach (byte b in Encoding.UTF8.GetBytes(password)) keys.Update(b);
        return keys;
    }

    private static uint ComputeCrc(byte[] data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data) crc = Crc32(crc, b);
        return ~crc;
    }

    public static void Create(
        IReadOnlyList<ArchivePlanEntry> entries,
        string sourceRoot,
        string outputPath,
        string password,
        Action<int, int, string>? report = null,
        CancellationToken cancellationToken = default)
    {
        using var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        var central = new List<(string Name, uint Crc, uint Compressed, uint Uncompressed, uint Offset, ushort Time, ushort Date)>();

        for (int i = 0; i < entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArchivePlanEntry entry = entries[i];
            report?.Invoke(i, entries.Count, entry.Name);
            string fullPath = Path.Combine(sourceRoot, entry.Name.Replace('/', Path.DirectorySeparatorChar));
            byte[] plain = File.ReadAllBytes(fullPath);
            uint crc = ComputeCrc(plain);
            byte[] compressed = Compress(plain);
            if ((long)compressed.Length > uint.MaxValue || (long)plain.Length > uint.MaxValue)
                throw new IOException("单个文件超过 4GB，暂不支持加密 ZIP：" + entry.Name);

            DateTime modified = File.GetLastWriteTime(fullPath);
            (ushort dosTime, ushort dosDate) = ToDosTime(modified);
            byte[] name = Encoding.UTF8.GetBytes(entry.Name);
            uint headerOffset = (uint)stream.Position;

            writer.Write(0x04034b50u);
            writer.Write((ushort)20);
            writer.Write((ushort)0x0801); // UTF-8 + encrypted
            writer.Write((ushort)8);
            writer.Write(dosTime);
            writer.Write(dosDate);
            writer.Write(crc);
            writer.Write((uint)(compressed.Length + 12));
            writer.Write((uint)plain.Length);
            writer.Write((ushort)name.Length);
            writer.Write((ushort)0);
            writer.Write(name);

            var keys = MakeKeys(password);
            var header = new byte[12];
            RandomNumberGenerator.Fill(header.AsSpan(0, 11));
            header[11] = (byte)(crc >> 24);
            EncryptWrite(writer, keys, header);
            EncryptWrite(writer, keys, compressed);

            central.Add((entry.Name, crc, (uint)(compressed.Length + 12), (uint)plain.Length,
                headerOffset, dosTime, dosDate));
            report?.Invoke(i + 1, entries.Count, entry.Name);
        }

        long centralStart = stream.Position;
        foreach (var item in central)
        {
            byte[] name = Encoding.UTF8.GetBytes(item.Name);
            writer.Write(0x02014b50u);
            writer.Write((ushort)20);
            writer.Write((ushort)20);
            writer.Write((ushort)0x0801);
            writer.Write((ushort)8);
            writer.Write(item.Time);
            writer.Write(item.Date);
            writer.Write(item.Crc);
            writer.Write(item.Compressed);
            writer.Write(item.Uncompressed);
            writer.Write((ushort)name.Length);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write(0u);
            writer.Write(item.Offset);
            writer.Write(name);
        }

        long centralEnd = stream.Position;
        writer.Write(0x06054b50u);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)central.Count);
        writer.Write((ushort)central.Count);
        writer.Write((uint)(centralEnd - centralStart));
        writer.Write((uint)centralStart);
        writer.Write((ushort)0);
        writer.Flush();
    }

    public static void Extract(
        ArchivePlan plan,
        string password,
        Action<int, int, string>? report = null,
        CancellationToken cancellationToken = default)
    {
        var entries = ReadCentralDirectory(plan.SourcePath);
        var byName = entries
            .GroupBy(x => Normalize(x.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        using var stream = new FileStream(plan.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        for (int i = 0; i < plan.Entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArchivePlanEntry planned = plan.Entries[i];
            report?.Invoke(i, plan.Entries.Count, planned.Name);
            if (!byName.TryGetValue(Normalize(planned.Name), out CentralEntry? entry))
                throw new IOException("ZIP 条目不存在：" + planned.Name);

            stream.Position = entry.LocalOffset;
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            if (reader.ReadUInt32() != 0x04034b50u) throw new IOException("无效的 ZIP 本地头：" + planned.Name);
            reader.ReadUInt16();
            reader.ReadUInt16();
            reader.ReadUInt16();
            reader.ReadUInt16();
            reader.ReadUInt16();
            reader.ReadUInt32();
            reader.ReadUInt32();
            reader.ReadUInt32();
            ushort nameLen = reader.ReadUInt16();
            ushort extraLen = reader.ReadUInt16();
            stream.Position += nameLen + extraLen;

            byte[] compressed = reader.ReadBytes((int)entry.CompressedSize);
            byte[] data;
            if ((entry.Flags & 1) != 0)
            {
                if (string.IsNullOrEmpty(password)) throw new IOException("这个 ZIP 已加密，请输入密码。");
                data = Decrypt(compressed, password, entry.Crc);
            }
            else data = compressed;

            byte[] plain = entry.Method == 8 ? Decompress(data) : data;
            if (ComputeCrc(plain) != entry.Crc)
                throw new IOException("密码错误或文件已损坏：" + planned.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(planned.TargetPath) ?? string.Empty);
            File.WriteAllBytes(planned.TargetPath, plain);
            report?.Invoke(i + 1, plan.Entries.Count, planned.Name);
        }
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(data, 0, data.Length);
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }

    private static void EncryptWrite(BinaryWriter writer, Keys keys, byte[] data)
    {
        foreach (byte b in data)
        {
            byte mask = keys.Mask();
            writer.Write((byte)(b ^ mask));
            keys.Update(b);
        }
    }

    private static byte[] Decrypt(byte[] data, string password, uint crc)
    {
        if (data.Length < 12) throw new IOException("加密 ZIP 条目过短。");
        var keys = MakeKeys(password);
        var output = new byte[data.Length - 12];
        for (int i = 0; i < 12; i++)
        {
            byte b = (byte)(data[i] ^ keys.Mask());
            keys.Update(b);
            if (i == 11 && b != (byte)(crc >> 24))
                throw new IOException("密码错误。");
        }
        for (int i = 0; i < output.Length; i++)
        {
            byte b = (byte)(data[i + 12] ^ keys.Mask());
            keys.Update(b);
            output[i] = b;
        }
        return output;
    }

    private sealed record CentralEntry(
        string Name, ushort Flags, ushort Method, uint Crc,
        uint CompressedSize, uint UncompressedSize, uint LocalOffset);

    private static List<CentralEntry> ReadCentralDirectory(string zipPath)
    {
        using var stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (stream.Length < 22) throw new IOException("ZIP 文件不完整。");

        long searchStart = Math.Max(0, stream.Length - 65557);
        long eocd = -1;
        stream.Position = searchStart;
        while (stream.Position <= stream.Length - 4)
        {
            if (reader.ReadUInt32() == 0x06054b50u) { eocd = stream.Position - 4; break; }
            stream.Position -= 3;
        }
        if (eocd < 0) throw new IOException("找不到 ZIP 目录。");

        stream.Position = eocd + 10;
        ushort count = reader.ReadUInt16();
        reader.ReadUInt32();
        uint offset = reader.ReadUInt32();
        stream.Position = offset;

        var result = new List<CentralEntry>(count);
        for (int i = 0; i < count; i++)
        {
            if (reader.ReadUInt32() != 0x02014b50u) throw new IOException("ZIP 中央目录损坏。");
            reader.ReadUInt16();
            reader.ReadUInt16();
            ushort flags = reader.ReadUInt16();
            ushort method = reader.ReadUInt16();
            reader.ReadUInt16();
            reader.ReadUInt16();
            uint crc = reader.ReadUInt32();
            uint compressed = reader.ReadUInt32();
            uint uncompressed = reader.ReadUInt32();
            ushort nameLen = reader.ReadUInt16();
            ushort extraLen = reader.ReadUInt16();
            ushort commentLen = reader.ReadUInt16();
            reader.ReadUInt16();
            reader.ReadUInt16();
            reader.ReadUInt32();
            uint localOffset = reader.ReadUInt32();
            string name = Encoding.UTF8.GetString(reader.ReadBytes(nameLen));
            stream.Position += extraLen + commentLen;
            if (!string.IsNullOrEmpty(name) && !name.EndsWith('/'))
                result.Add(new CentralEntry(name, flags, method, crc, compressed, uncompressed, localOffset));
        }
        return result;
    }

    private static string Normalize(string name) => name.Replace('\\', '/').TrimStart('/');

    private static (ushort Time, ushort Date) ToDosTime(DateTime value)
    {
        ushort time = (ushort)((value.Hour << 11) | (value.Minute << 5) | (value.Second / 2));
        ushort date = (ushort)(((value.Year - 1980) << 9) | (value.Month << 5) | value.Day);
        return (time, date);
    }
}
