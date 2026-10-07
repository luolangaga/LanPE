using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using LanPE.Core;
using LanPE.Disk;

namespace LanPE.UI;

/// <summary>安装到本地磁盘对话框：数据分区 + ESP + bcdedit 注册，支持卸载。</summary>
public sealed class InstallLocalDialog : Window
{
    private readonly string _stagingDir;
    private readonly ComboBox _cboData = new();
    private readonly ComboBox _cboEsp = new();
    private readonly TextBox _txtEfiDir = new();
    private readonly TextBox _txtEntryName = new();
    private readonly TextBox _txtGrubEfi = new();
    private readonly Button _btnInstall = new();
    private readonly Button _btnUninstall = new();
    private readonly Button _btnClose = new();
    private readonly ProgressBar _bar = new();
    private readonly Label _lblStatus = new();
    private readonly List<string> _driveLabels = new();

    public InstallLocalDialog(string stagingDir, Window owner)
    {
        _stagingDir = stagingDir;

        this.Title("安装到本地磁盘")
            .Fixed(700, 560)
            .StartCenterOwner()
            .Content(BuildContent(stagingDir));
    }

    public static InstallLocalDialog? Create(Window owner, string stagingDir)
    {
        if (!Directory.Exists(stagingDir))
        {
            MessageBox.Notify("尚未组装任何内容，请先执行“生成 ISO”。", PromptIconKind.Info, owner: owner);
            return null;
        }
        return new InstallLocalDialog(stagingDir, owner);
    }

    private Element BuildContent(string stagingDir)
    {
        FillDrives();
        _txtEfiDir.Text("LanPE");
        _txtEntryName.Text("LanPE");

        var btnBrowse = new Button().Content("选择 grubx64.efi / BOOTX64.EFI…").Width(260);
        btnBrowse.OnClick(BrowseGrubEfi);

        _btnInstall.Content("安装").Width(90);
        _btnInstall.OnClick(() => _ = InstallAsync());
        _btnUninstall.Content("卸载").Width(90);
        _btnUninstall.OnClick(() => _ = UninstallAsync());
        _btnClose.Content("关闭").Width(90);
        _btnClose.OnClick(Close);

        var buttons = new StackPanel().Horizontal().Spacing(8);
        buttons.Children(_lblStatus, _btnInstall, _btnUninstall, _btnClose);

        _bar.Minimum(0).Maximum(100).Value(0);
        _lblStatus.Text("就绪。");

        var stack = new StackPanel().Vertical().Spacing(10);
        stack.Children(
            Row("数据分区", _cboData),
            Row("ESP 分区", _cboEsp),
            Row("EFI 子目录", _txtEfiDir),
            Row("启动项名称", _txtEntryName),
            Row("GRUB EFI 源", _txtGrubEfi),
            btnBrowse,
            new Label().Text($"数据来自暂存目录：{stagingDir}").FontSize(11),
            new Label().Text("数据分区存放 LanPE 数据；ESP 写入引导并注册固件启动项（需管理员）。").FontSize(11),
            _bar,
            buttons
        );

        return new Border().Padding(16).Child(stack);
    }

    private static Element Row<T>(string label, T field) where T : Control
    {
        var row = new StackPanel().Horizontal().Spacing(10);
        var lbl = new Label().Text(label).Width(110);
        lbl.VerticalAlignment(VerticalAlignment.Center);
        field.Width(420);
        row.Children(lbl, field);
        return row;
    }

    private void FillDrives()
    {
        foreach (var d in DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.Fixed))
        {
            string label = d.Name.TrimEnd('\\') + $"  ({d.DriveFormat})";
            _driveLabels.Add(label);
            _cboData.Items(label);
            _cboEsp.Items(label);
        }
        if (_driveLabels.Count > 0) _cboData.SelectedIndex(0);
        if (_driveLabels.Count > 0) _cboEsp.SelectedIndex(0);
    }

    private void BrowseGrubEfi()
    {
        var dlg = new GrubEfiInputDialog(this);
        _ = dlg.ShowDialogAsync(this).ContinueWith(_ =>
        {
            var path = dlg.Result;
            if (!string.IsNullOrWhiteSpace(path))
                Application.Current?.Dispatcher?.BeginInvoke(() => _txtGrubEfi.Text(path!));
        });
    }

    private static string? LetterOf(ComboBox c) =>
        (c.SelectedItem as string)?.Split(' ')[0]?.TrimEnd('\\');

    private InstallTarget BuildTarget() => new()
    {
        DataRoot = LetterOf(_cboData) ?? "",
        EspRoot = LetterOf(_cboEsp) ?? "",
        EfiDirName = string.IsNullOrWhiteSpace(_txtEfiDir.Text) ? "LanPE" : _txtEfiDir.Text!.Trim(),
        BootEntryName = string.IsNullOrWhiteSpace(_txtEntryName.Text) ? "LanPE" : _txtEntryName.Text!.Trim()
    };

    private async Task InstallAsync()
    {
        var target = BuildTarget();
        if (string.IsNullOrWhiteSpace(target.DataRoot))
        {
            MessageBox.Notify("请选择数据分区。", PromptIconKind.Warning, owner: this);
            return;
        }
        if (string.IsNullOrWhiteSpace(_txtGrubEfi.Text) || !File.Exists(_txtGrubEfi.Text))
        {
            MessageBox.Notify("请先指定 GRUB EFI 引导文件（grubx64.efi / BOOTX64.EFI）。",
                PromptIconKind.Warning, owner: this);
            return;
        }

        var progress = new Progress<ProgressInfo>(p =>
        {
            if (!string.IsNullOrEmpty(p.Message)) _lblStatus.Text(p.Message!);
            if (p.CurrentPercent >= 0) _bar.Value(Math.Clamp(p.CurrentPercent, 0, 100));
        });

        try
        {
            SetEnabled(false);
            using var cts = new CancellationTokenSource();
            var installer = new LocalInstaller();
            await Task.Run(() => installer.InstallAsync(
                _stagingDir, _txtGrubEfi.Text!, target, progress, cts.Token), cts.Token);

            MessageBox.Notify("安装完成，重启后可在启动菜单看到该启动项。", PromptIconKind.Info, owner: this);
        }
        catch (Exception ex)
        {
            MessageBox.Notify(ex.Message, PromptIconKind.Error, owner: this);
        }
        finally { SetEnabled(true); }
    }

    private async Task UninstallAsync()
    {
        var target = BuildTarget();
        if (!MessageBox.AskYesNo("确定要卸载（删除数据目录并移除启动项）吗？", PromptIconKind.Warning, owner: this))
            return;

        var progress = new Progress<ProgressInfo>(p =>
        {
            if (!string.IsNullOrEmpty(p.Message)) _lblStatus.Text(p.Message!);
        });

        try
        {
            SetEnabled(false);
            using var cts = new CancellationTokenSource();
            var installer = new LocalInstaller();
            await Task.Run(() => installer.UninstallAsync(target, progress, cts.Token), cts.Token);
            MessageBox.Notify("卸载完成。", PromptIconKind.Info, owner: this);
        }
        catch (Exception ex)
        {
            MessageBox.Notify(ex.Message, PromptIconKind.Error, owner: this);
        }
        finally { SetEnabled(true); }
    }

    private void SetEnabled(bool enabled)
    {
        _btnInstall.IsEnabled = enabled;
        _btnUninstall.IsEnabled = enabled;
        _btnClose.IsEnabled = enabled;
    }
}

/// <summary>输入 GRUB EFI 文件路径的简易对话框（AOT 下替代文件选择框）。</summary>
public sealed class GrubEfiInputDialog : Window
{
    private readonly TextBox _txt = new();

    public string? Result { get; private set; }

    public GrubEfiInputDialog(Window owner)
    {
        this.Title("GRUB EFI 路径")
            .Fixed(560, 200)
            .StartCenterOwner()
            .Content(BuildContent());
    }

    private Element BuildContent()
    {
        _txt.Placeholder(@"例如 S:\EFI\LanPE\grubx64.efi");

        var ok = new Button().Content("确定").Width(84);
        ok.OnClick(() => { Result = _txt.Text?.Trim(); Close(); });
        var cancel = new Button().Content("取消").Width(84);
        cancel.OnClick(Close);

        var buttons = new StackPanel().Horizontal().Spacing(8);
        buttons.HorizontalAlignment(HorizontalAlignment.Right);
        buttons.Children(ok, cancel);

        var stack = new StackPanel().Vertical().Spacing(10);
        stack.Children(
            new Label().Text("请输入 grubx64.efi / BOOTX64.EFI 的完整路径："),
            _txt,
            buttons
        );

        return new Border().Padding(16).Child(stack);
    }
}
