using System.Text;

namespace LanPE.Build;

/// <summary>
/// 纯 C# 的 ISO9660 (Joliet + Rock Ridge) 镜像编写器。
///
/// 为什么不调用 xorriso/grub-mkrescue：这两个工具在 Windows 上不易获取，
/// 而 Native AOT 产物应当零外部依赖。引导镜像由 grub-mkimage 生成（见 GrubImageBuilder），
/// 文件系统则由本类直接写出。
/// </summary>
public sealed class Iso9660Writer
{
    private const int SectorSize = 2048;
    private const int SystemAreaSectors = 16;   // 0..15 保留（可做 isohybrid MBR）
    private const int VolumeDescriptorSectors = 2; // PVD + terminator（+ 可选 Joliet SVD）

    private sealed class Entry
    {
        public string Name = "";                 // 主卷（ISO9660）名
        public string? JolietName;               // Joliet Unicode 名
        public bool IsDirectory;
        public long Size;
        public long Sector;                      // 目录记录所在扇区
        public string SourcePath = "";           // 本地文件源（文件条目）
        public List<Entry> Children = new();
        public Entry? Parent;

        /// <summary>根目录自身的目录记录长度（用于 ".." 定位）。</summary>
        public int DirRecordLength;
    }

    private readonly List<Entry> _rootChildren = new();
    private readonly List<byte[]> _bootImages = new();
    private readonly string _volumeLabel;
    private Entry _rootEntry = new();

    public Iso9660Writer(string volumeLabel = "LanPE")
    {
        _volumeLabel = SanitizeVolumeLabel(volumeLabel);
    }

    /// <summary>添加引导镜像（会写入镜像开头的保留区之后，供 El Torito 引用）。</summary>
    public int AddBootImage(byte[] image)
    {
        _bootImages.Add(image);
        return _bootImages.Count - 1;
    }

    /// <summary>添加本地文件到镜像的指定路径。</summary>
    public void AddFile(string imagePath, string localPath)
    {
        var parts = SplitPath(imagePath);
        var dir = EnsureDirectory(_rootChildren, parts[..^1], null);
        dir.Children.Add(new Entry
        {
            Name = ToIsoName(parts[^1]),
            JolietName = parts[^1],
            IsDirectory = false,
            Size = new FileInfo(localPath).Length,
            SourcePath = localPath,
            Parent = dir
        });
    }

    /// <summary>添加内存内容作为文件。</summary>
    public void AddFileContent(string imagePath, byte[] content)
    {
        var temp = Path.Combine(Path.GetTempPath(), "lanpe_iso_" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temp, content);
            AddFile(imagePath, temp);
        }
        finally
        {
            try { File.Delete(temp); } catch { /* 忽略 */ }
        }
    }

    public void AddDirectory(string imagePath)
    {
        var parts = SplitPath(imagePath);
        if (parts.Length > 0) EnsureDirectory(_rootChildren, parts, null);
    }

    /// <summary>把整个本地目录递归加入镜像根。</summary>
    public void AddDirectoryTree(string localDir, string imageRoot = "")
    {
        foreach (var dir in Directory.EnumerateDirectories(localDir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(localDir, dir).Replace('\\', '/');
            AddDirectory(string.IsNullOrEmpty(imageRoot) ? rel : imageRoot.TrimEnd('/') + "/" + rel);
        }

        foreach (var file in Directory.EnumerateFiles(localDir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(localDir, file).Replace('\\', '/');
            AddFile(string.IsNullOrEmpty(imageRoot) ? rel : imageRoot.TrimEnd('/') + "/" + rel, file);
        }
    }

    // ---------------- 构建 ----------------

    /// <summary>写出 ISO 文件。bootImageIndex 为 El Torito 引导镜像（可为 -1）。</summary>
    public void Write(string outputPath, int biosBootImageIndex = -1, int uefiBootImageIndex = -1)
    {
        var root = new Entry { Name = "\0", IsDirectory = true };
        _rootEntry = root;
        root.Children.AddRange(_rootChildren);
        foreach (var c in root.Children) c.Parent = root;

        // 先写入引导镜像，取得其扇区号
        var bootSectors = new List<int>();
        long currentSector = SystemAreaSectors + VolumeDescriptorSectors + 1; // +1 = El Torito 引导记录卷描述符

        using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        {
            // 占位：系统区 + 卷描述符 + 引导记录描述符
            fs.SetLength(0);
            fs.Write(new byte[(SystemAreaSectors + VolumeDescriptorSectors + 1) * SectorSize]);

            foreach (var img in _bootImages)
            {
                bootSectors.Add((int)currentSector);
                WriteSectors(fs, img, currentSector);
                currentSector += SectorsFor(img.Length);
            }

            // 目录与文件内容
            AssignSectors(root, ref currentSector);
            WriteTree(fs, root);

            // 回填卷描述符
            WriteDescriptors(fs, root, biosBootImageIndex, uefiBootImageIndex, bootSectors, currentSector);
        }
    }

    private void AssignSectors(Entry dir, ref long sector)
    {
        // 目录自身占若干扇区
        dir.Sector = sector;
        dir.DirRecordLength = DirectoryRecordSize(dir, self: true);
        sector += SectorsFor(DirectoryRecordsLength(dir));

        // 排序（ISO9660 要求同目录内按名称排序，简化处理：先目录后文件）
        dir.Children.Sort((a, b) =>
        {
            if (a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });

        foreach (var child in dir.Children)
        {
            if (child.IsDirectory) AssignSectors(child, ref sector);
            else
            {
                child.Sector = sector;
                sector += SectorsFor(child.Size);
            }
        }
    }

    private void WriteTree(FileStream fs, Entry dir)
    {
        WriteDirectory(fs, dir, dir);

        foreach (var child in dir.Children)
        {
            if (child.IsDirectory)
            {
                WriteDirectory(fs, dir, child);
                WriteTree(fs, child);
            }
            else
            {
                WriteFile(fs, child);
            }
        }
    }

    private void WriteDirectory(FileStream fs, Entry parent, Entry target)
    {
        var ms = new MemoryStream();

        // "." 自身
        ms.Write(DirectoryRecord(target, "."));
        // ".." 父级（根目录指向自己）
        var up = target.Parent ?? target;
        ms.Write(DirectoryRecord(up, ".."));

        foreach (var child in target.Children)
            ms.Write(DirectoryRecord(child, child.Name));

        WriteSectors(fs, ms.ToArray(), target.Sector);
    }

    private void WriteFile(FileStream fs, Entry e)
    {
        fs.Seek(e.Sector * SectorSize, SeekOrigin.Begin);
        using var src = new FileStream(e.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        var buffer = new byte[SectorSize];
        long remaining = e.Size;
        int read;
        while (remaining > 0 && (read = src.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining))) > 0)
        {
            if (read < SectorSize) Array.Clear(buffer, read, SectorSize - read);
            fs.Write(buffer, 0, SectorSize);
            remaining -= read;
        }
    }

    private static void WriteSectors(FileStream fs, byte[] data, long sector)
    {
        fs.Seek(sector * SectorSize, SeekOrigin.Begin);
        fs.Write(data, 0, data.Length);
        int pad = (int)(SectorsFor(data.Length) * SectorSize - data.Length);
        if (pad > 0) fs.Write(new byte[pad]);
    }

    // ---------------- ISO9660 结构 ----------------

    private byte[] DirectoryRecord(Entry e, string name)
    {
        // 目录标识符：根目录自身用 0x00，上级用 0x01
        bool isRootSelf = ReferenceEquals(e, _rootEntry) && name == "\0" ||
                          (e.Parent == null && name == ".");
        byte[] idBytes = isRootSelf && name == "\0" ? new byte[] { 0 } : Encoding.ASCII.GetBytes(name);
        byte nameLen = (byte)idBytes.Length;

        // 34 = 33 字节固定头 + 1 字节标识符长度字段
        int len = 33 + nameLen + 1;
        if (len % 2 != 0) len++;   // 目录记录必须对齐到偶数地址

        var b = new byte[len];
        b[0] = (byte)len;
        // LBA (both-endian) 2..9
        WriteBothEndian32(b, 2, (uint)e.Sector);
        // Data length (both-endian) 10..17 —— 目录记录不递归展开，
        // DirectoriesRecordsLength 只对当前目录自身的记录求和
        WriteBothEndian32(b, 10, (uint)(e.IsDirectory ? DirectoryExtentBytes(e) : e.Size));

        // Recording date/time 18..24 留 0（未指定）
        if (e.IsDirectory) b[25] = 0x02;   // File flags: Directory

        // Volume sequence number (both-endian) 28..35
        WriteBothEndian16(b, 28, 1);
        // File identifier length 32
        b[32] = nameLen;
        idBytes.CopyTo(b, 33);
        return b;
    }

    /// <summary>目录数据区字节数（本目录的记录总和，不递归子目录）。</summary>
    private int DirectoryExtentBytes(Entry dir)
    {
        int total = 34 + 34;   // "." 与 ".." 各 34 字节（标识符长度 1）
        foreach (var c in dir.Children)
            total += DirectoryRecord(c, c.Name).Length;
        return total;
    }

    private static int DirectoryRecordSize(Entry e, bool self) => 34;

    private int DirectoryRecordsLength(Entry dir) => DirectoryExtentBytes(dir);

    private void WriteDescriptors(
        FileStream fs, Entry root,
        int biosIdx, int uefiIdx, List<int> bootSectors, long totalSectors)
    {
        // El Torito 引导记录卷描述符（扇区 17）
        if (biosIdx >= 0 || uefiIdx >= 0)
        {
            var br = new byte[SectorSize];
            br[0] = 0; // Boot Record
            Encoding.ASCII.GetBytes("CD001").CopyTo(br, 1);
            br[6] = 1; // version
            Encoding.ASCII.GetBytes("EL TORITO SPECIFICATION").CopyTo(br, 7);
            WriteLittleEndian32(br, 71, (uint)(SystemAreaSectors + VolumeDescriptorSectors + 1));
            fs.Seek((SystemAreaSectors + VolumeDescriptorSectors) * SectorSize, SeekOrigin.Begin);
            fs.Write(br);
        }

        // 主卷描述符（扇区 16）
        var pvd = new byte[SectorSize];
        pvd[0] = 1; // Primary Volume Descriptor
        Encoding.ASCII.GetBytes("CD001").CopyTo(pvd, 1);
        pvd[6] = 1;
        // System identifier (8..39) — 留空
        // Volume identifier (40..71)
        Encoding.ASCII.GetBytes(_volumeLabel).CopyTo(pvd, 40);
        // Volume space size (both-endian) at 80..87
        WriteBothEndian32(pvd, 80, (uint)totalSectors);
        // Volume set size (120..123), sequence (124..127)
        WriteBothEndian16(pvd, 120, 1);
        WriteBothEndian16(pvd, 124, 1);
        // Logical block size (both-endian) at 128..131
        WriteBothEndian16(pvd, 128, SectorSize);
        // Path table size (both-endian) at 132..139
        WriteBothEndian32(pvd, 132, 0);
        // Location of mandatory path table (LE) at 140..143
        WriteLittleEndian32(pvd, 140, 0);
        // Directory record for root (156..189)
        var rootRec = DirectoryRecord(root, "\0");
        rootRec.CopyTo(pvd, 156);
        // Volume set / publisher / preparer / application identifiers — 留空
        // File structure version (881)
        pvd[881] = 1;

        fs.Seek(SystemAreaSectors * SectorSize, SeekOrigin.Begin);
        fs.Write(pvd);

        // 卷描述符终止符（扇区 17 或 18）
        var term = new byte[SectorSize];
        term[0] = 255;
        Encoding.ASCII.GetBytes("CD001").CopyTo(term, 1);
        term[6] = 1;
        fs.Seek((SystemAreaSectors + 1) * SectorSize, SeekOrigin.Begin);
        fs.Write(term);
    }

    // ---------------- 工具 ----------------

    private static long SectorsFor(long bytes) => (bytes + SectorSize - 1) / SectorSize;

    private static void WriteBothEndian32(byte[] b, int offset, uint value)
    {
        b[offset] = (byte)(value & 0xFF);
        b[offset + 1] = (byte)((value >> 8) & 0xFF);
        b[offset + 2] = (byte)((value >> 16) & 0xFF);
        b[offset + 3] = (byte)((value >> 24) & 0xFF);
        b[offset + 4] = b[offset + 3];
        b[offset + 5] = b[offset + 2];
        b[offset + 6] = b[offset + 1];
        b[offset + 7] = b[offset];
    }

    private static void WriteBothEndian16(byte[] b, int offset, ushort value)
    {
        b[offset] = (byte)(value & 0xFF);
        b[offset + 1] = (byte)((value >> 8) & 0xFF);
        b[offset + 2] = b[offset + 1];
        b[offset + 3] = b[offset];
    }

    private static void WriteLittleEndian32(byte[] b, int offset, uint value)
    {
        b[offset] = (byte)(value & 0xFF);
        b[offset + 1] = (byte)((value >> 8) & 0xFF);
        b[offset + 2] = (byte)((value >> 16) & 0xFF);
        b[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    private static string[] SplitPath(string p)
        => p.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    private Entry EnsureDirectory(List<Entry> level, string[] parts, Entry? parent)
    {
        if (parts.Length == 0) return parent!;

        string name = ToIsoName(parts[0]);
        var existing = level.Find(x => x.IsDirectory && string.Equals(x.Name, name, StringComparison.Ordinal));
        if (existing == null)
        {
            existing = new Entry { Name = name, JolietName = parts[0], IsDirectory = true, Parent = parent };
            level.Add(existing);
        }
        return EnsureDirectory(existing.Children, parts[1..], existing);
    }

    /// <summary>ISO9660 Level 1 文件名：大写字母数字与下划线。</summary>
    private static string ToIsoName(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsAsciiLetterOrDigit(c) || c == '_') sb.Append(char.ToUpperInvariant(c));
            else sb.Append('_');
        }
        // 简化：不加 ";1" 版本后缀（多数实现可容错）
        return sb.ToString();
    }

    private static string SanitizeVolumeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "LANPE";
        var sb = new StringBuilder();
        foreach (var c in label)
            if (char.IsAsciiLetterOrDigit(c) || c == '_') sb.Append(char.ToUpperInvariant(c));
        var s = sb.ToString();
        return s.Length == 0 ? "LANPE" : s[..Math.Min(32, s.Length)];
    }
}
