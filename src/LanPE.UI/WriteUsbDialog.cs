using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using LanPE.Core;
using LanPE.Disk;

namespace LanPE.UI;

/// <summary>写入 U 盘（DD 模式）对话框，含安全护栏。</summary>
public sealed class WriteUsbDialog : Window
{
    private readonly string _isoPath;
    private readonly ListBox _list = new();
    private readonly Label _lblInfo = new();
    private readonly TextBox _txtConfirm = new();
    private readonly Button _btnWrite = new();
    private readonly Button _btnClose = new();
    private readonly ProgressBar _bar = new();
    private readonly Label _lblStatus = new();

    private IReadOnlyList<PhysicalDrive> _drives = Array.Empty<PhysicalDrive>();
    private CancellationTokenSource? _cts;

    private WriteUsbDialog(string isoPath, Window owner)
    {
        _isoPath = isoPath;

        this.Title("写入 U 盘（DD 模式）")
            .Fixed(680, 560)
            .StartCenterOwner()
            .Content(BuildContent(isoPath));
    }

    public static WriteUsbDialog? Create(Window owner)
    {
        // 最近一次生成的 ISO 位于应用数据根
        string dir = AppPaths.DataRoot;
        string? iso = Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.iso").OrderByDescending(f => new FileInfo(f).LastWriteTime).FirstOrDefault()
            : null;

        if (iso == null)
        {
            MessageBox.Notify("尚未生成 ISO，请先执行“生成 ISO”。", PromptIconKind.Info, owner: owner);
            return null;
        }
        return new WriteUsbDialog(iso, owner);
    }

    private Element BuildContent(string isoPath)
    {
        var warn = new Label()
            .Text("警告：将对所选磁盘扇区级写入，该磁盘所有数据将被永久销毁！")
            .Foreground(Color.Firebrick)
            .Bold();

        var pathLabel = new Label().Text($"镜像：{isoPath}").FontSize(11);

        _list.Height(200);
        _list.OnSelectionChanged(_ => UpdateButton());

        _txtConfirm.Placeholder("输入 ERASE 以确认");
        _txtConfirm.OnTextChanged(_ => UpdateButton());

        _bar.Minimum(0).Maximum(100).Value(0);
        _lblStatus.Text("就绪。");

        _btnWrite.Content("开始写入").Width(104);
        _btnWrite.OnClick(() => _ = WriteAsync());
        _btnClose.Content("关闭").Width(84);
        _btnClose.OnClick(() => { _cts?.Cancel(); Close(); });

        var buttons = new StackPanel().Horizontal().Spacing(8);
        buttons.HorizontalAlignment(HorizontalAlignment.Right);
        buttons.Children(_lblStatus, _btnWrite, _btnClose);

        var stack = new StackPanel().Vertical().Spacing(10);
        stack.Children(
            warn,
            pathLabel,
            new Border().BorderThickness(1).CornerRadius(5).Child(_list),
            _lblInfo,
            _txtConfirm,
            _bar,
            buttons
        );

        RefreshDrives();
        return new Border().Padding(16).Child(stack);
    }

    private void RefreshDrives()
    {
        _drives = PhysicalDriveEnumerator.List();
        foreach (var d in _drives) _list.Items(d.DisplayName);
        if (_drives.Count > 0) _list.SelectedIndex(0);
        UpdateButton();
    }

    private PhysicalDrive? Selected =>
        _list.SelectedIndex >= 0 && _list.SelectedIndex < _drives.Count ? _drives[_list.SelectedIndex] : null;

    private void UpdateButton()
    {
        var d = Selected;
        bool ok = d != null && !d.IsSystem
                  && string.Equals(_txtConfirm.Text?.Trim(), "ERASE", StringComparison.Ordinal);
        _btnWrite.IsEnabled = ok && _cts == null;

        _lblInfo.Text(d == null ? "请选择目标磁盘。"
            : d.IsSystem ? "已选择系统盘 —— 禁止写入，请改选 U 盘。"
            : $"目标：{d.DisplayName}");
    }

    private async Task WriteAsync()
    {
        var target = Selected;
        if (target == null) return;

        if (!MessageBox.AskYesNo($"确定要清空并写入 {target.DisplayName} 吗？此操作不可撤销！",
                PromptIconKind.Warning, owner: this))
            return;

        _cts = new CancellationTokenSource();
        UpdateButton();
        _btnClose.IsEnabled = false;

        var progress = new Progress<ProgressInfo>(p =>
        {
            _ = OnUiAsync(() =>
            {
                if (!string.IsNullOrEmpty(p.Message)) _lblStatus.Text(p.Message!);
                if (p.CurrentPercent >= 0) _bar.Value(Math.Clamp(p.CurrentPercent, 0, 100));
            });
        });

        try
        {
            var writer = new RawImageWriter();
            await Task.Run(() => writer.WriteAsync(_isoPath, target, progress, _cts!.Token), _cts.Token);
            await OnUiAsync(() =>
            {
                _lblStatus.Text("写入完成。");
                MessageBox.Notify("写入完成！", PromptIconKind.Info, owner: this);
            });
        }
        catch (OperationCanceledException) { await OnUiAsync(() => _lblStatus.Text("已取消。")); }
        catch (Exception ex)
        {
            var message = ex.Message;
            await OnUiAsync(() =>
            {
                _lblStatus.Text("失败。");
                MessageBox.Notify(message, PromptIconKind.Error, owner: this);
            });
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            await OnUiAsync(() => { _btnClose.IsEnabled = true; UpdateButton(); });
        }
    }

    /// <summary>把动作投递到 UI 线程（进度回调来自后台线程）。</summary>
    private static async Task OnUiAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) { action(); return; }

        var tcs = new TaskCompletionSource();
        dispatcher.BeginInvoke(() =>
        {
            try { action(); tcs.SetResult(); }
            catch (Exception e) { tcs.SetException(e); }
        });
        await tcs.Task.ConfigureAwait(false);
    }
}
