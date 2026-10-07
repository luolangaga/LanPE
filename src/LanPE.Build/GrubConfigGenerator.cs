using System.Text;
using LanPE.Core;
using Manifest = LanPE.Core.Manifest.Manifest;
using Component = LanPE.Core.Manifest.Component;

namespace LanPE.Build;

/// <summary>
/// 生成 GRUB2 主菜单。
///  - Windows PE：wimboot 引导（多个 PE 共存于同一分区的唯一可行方案 ——
///    直接链式引导 bootmgr 会让它到卷根查找固定的 \boot\bcd 与 \sources\boot.wim，
///    多个 PE 会互相冲突。wimboot 动态构造 ramdisk，按路径加载指定 wim。）
///  - Linux：loopback 挂载 ISO 引导。
/// </summary>
public static class GrubConfigGenerator
{
    public const string MarkerPath = "/LanPE/marker";

    /// <summary>
    /// 生成菜单。
    /// bootFiles：共享引导文件的介质内相对路径（wimboot / bcd / boot.sdi / bootmgr / bootmgr.efi）。
    /// </summary>
    public static string Generate(
        Manifest manifest,
        Selection selection,
        IReadOnlyDictionary<string, string>? bootFiles = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        bootFiles ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("# 由 LanPE 自动生成，请勿手工修改。");
        sb.AppendLine("set timeout=15");
        sb.AppendLine("set default=0");
        sb.AppendLine();
        sb.AppendLine("insmod part_msdos");
        sb.AppendLine("insmod part_gpt");
        sb.AppendLine("insmod fat");
        sb.AppendLine("insmod ntfs");
        sb.AppendLine("insmod search_fs_file");
        sb.AppendLine("insmod loopback");
        sb.AppendLine("insmod all_video");
        sb.AppendLine("terminal_output gfxterm");
        sb.AppendLine();

        var entries = new List<string>();

        foreach (var comp in manifest.Components)
        {
            var sel = selection?.For(comp.Id);
            if (sel is null || !sel.Selected) continue;

            if (comp.IsWindowsPe)
                entries.Add(BuildWinPeEntry(comp, bootFiles));
            else if (comp.IsLinux)
                entries.Add(BuildLinuxEntry(comp));
        }

        if (entries.Count == 0)
            sb.AppendLine("menuentry \"(未选择任何组件)\" { echo 请在打包器中至少选择一个组件。 }");
        else
            foreach (var e in entries) sb.AppendLine(e);

        return sb.ToString();
    }

    private static string BuildWinPeEntry(Component comp, IReadOnlyDictionary<string, string> bootFiles)
    {
        string wimPath = TrimSlashes(comp.Boot?.Path ?? "");
        string title = Escape(comp.Name ?? comp.Id ?? "Windows PE");
        int index = comp.Boot?.WimIndex ?? 2;

        // 默认引导文件布局（与 release/assets 中的实际文件名一致，全小写 ——
        // GRUB 在 ISO9660/FAT 上大小写敏感，必须与落盘文件名严格一致）
        string wimboot = Get(bootFiles, "wimboot", "boot/wimboot");
        string bcd = Get(bootFiles, "bcd", "boot/bcd");
        string sdi = Get(bootFiles, "boot.sdi", "boot/boot.sdi");
        string bootmgr = Get(bootFiles, "bootmgr", "boot/bootmgr");
        string bootmgrEfi = Get(bootFiles, "bootmgr.efi", "boot/bootmgr.efi");

        var sb = new StringBuilder();
        sb.AppendLine($"menuentry \"{title}\" {{");
        sb.AppendLine($"    search --no-floppy --set=root --file {MarkerPath}");
        sb.AppendLine("    if [ \"$grub_platform\" = \"efi\" ]; then");
        sb.AppendLine($"        linuxefi /{wimboot} index={index}");
        sb.AppendLine($"        initrdefi newc:bcd:(/{bcd}) newc:boot.sdi:(/{sdi}) newc:bootmgfw.efi:(/{bootmgrEfi}) newc:boot.wim:(/{wimPath})");
        sb.AppendLine("    else");
        sb.AppendLine($"        linux16 /{wimboot} index={index}");
        sb.AppendLine($"        initrd16 newc:bcd:(/{bcd}) newc:boot.sdi:(/{sdi}) newc:bootmgr:(/{bootmgr}) newc:boot.wim:(/{wimPath})");
        sb.AppendLine("    fi");
        sb.AppendLine("    boot");
        sb.AppendLine("}");
        return sb.ToString().TrimEnd();
    }

    private static string BuildLinuxEntry(Component comp)
    {
        string isoPath = TrimSlashes(comp.Boot?.Path ?? "");
        string title = Escape(comp.Name ?? comp.Id ?? "Linux");

        var sb = new StringBuilder();
        sb.AppendLine($"menuentry \"{title}\" {{");
        sb.AppendLine($"    search --no-floppy --set=root --file {MarkerPath}");
        sb.AppendLine($"    loopback loop /{isoPath}");
        sb.AppendLine($"    linux (loop)/sysresccd/boot/x86_64/vmlinuz img_loop=/{isoPath} img_dev=($root) archisobasedir=sysresccd copytoram setkmap=us");
        sb.AppendLine("    initrd (loop)/sysresccd/boot/x86_64/sysresccd.img");
        sb.AppendLine("}");
        return sb.ToString().TrimEnd();
    }

    private static string Get(IReadOnlyDictionary<string, string> map, string key, string fallback) =>
        map.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    private static string TrimSlashes(string s) => (s ?? "").Trim('/').Replace('\\', '/');

    private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
}
