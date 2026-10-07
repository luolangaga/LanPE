using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using LanPE.Build;
using LanPE.Core;
using LanPE.Core.Manifest;
using LanPE.Core.Net;
using LanPE.Disk;
using Component = LanPE.Core.Manifest.Component;

namespace LanPE.UI;

/// <summary>LanPE 主窗口：清单拉取 → 组件勾选 → 下载/打包 → 三种输出。</summary>
public sealed class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Load();
    private Manifest? _manifest;
    private CancellationTokenSource? _cts;
    private bool _busy;

    private readonly Label _lblSource = new();
    private readonly Label _lblStatus = new();

    private readonly StackPanel _componentList = new();

    private readonly Label _lblDetailTitle = new();
    private readonly Label _lblDetailBody = new();
    private readonly StackPanel _optionHost = new();
    private readonly StackPanel _softwareHost = new();

    private readonly ProgressBar _barOverall = new();
    private readonly ProgressBar _barCurrent = new();
    private readonly MultiLineTextBox _logBox = new();

    private readonly Button _btnFetch = new();
    private readonly Button _btnDownload = new();
    private readonly Button _btnBuildIso = new();
    private readonly Button _btnWriteUsb = new();
    private readonly Button _btnInstallLocal = new();
    private readonly Button _btnOpenOutput = new();
    private readonly Button _btnSettings = new();
    private readonly Button _btnCancel = new();

    private readonly Dictionary<string, ComponentSelectionState> _states = new(StringComparer.OrdinalIgnoreCase);

    private sealed class ComponentSelectionState
    {
        public bool Selected;
        public HashSet<string> SoftwareIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Options { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public MainWindow()
    {
        this.Title("LanPE 多启动打包器")
            .Resizable(1120, 740)
            .StartCenterScreen()
            .Content(BuildLayout());

        RefreshSourceLabel();
        SetBusy(false);
    }

    // ---------------- 布局 ----------------

    private Element BuildLayout()
        => new Grid()
            .Rows("Auto,*,Auto,200")
            .Children(
                BuildHeader().Row(0),
                BuildCenter().Row(1),
                BuildActionBar().Row(2),
                BuildProgressArea().Row(3)
            );

    private Element BuildHeader()
    {
        _btnFetch.Content("拉取清单").Width(96);
        _btnFetch.OnClick(() => _ = FetchManifestAsync());
        _btnSettings.Content("设置").Width(72);
        _btnSettings.OnClick(OpenSettings);

        var buttons = new StackPanel().Horizontal().Spacing(8).DockRight();
        buttons.Children(_btnSettings, _btnFetch);

        var dock = new DockPanel().LastChildFill(true);
        dock.Children(buttons, _lblSource);

        return new Border().Padding(16, 12, 16, 12).Child(dock);
    }

    private Element BuildCenter()
    {
        var left = new ScrollViewer().AutoVerticalScroll().NoHorizontalScroll()
            .Content(new Border().Padding(12).Child(_componentList.Vertical().Spacing(10)));

        var detail = new StackPanel().Vertical().Spacing(12);
        _lblDetailTitle.FontSize(18).Bold();
        _lblDetailBody.FontSize(12);
        var optLabel = new Label().Text("启动选项").FontSize(13).SemiBold();
        var swLabel = new Label().Text("内置软件").FontSize(13).SemiBold();
        detail.Children(_lblDetailTitle, _lblDetailBody, optLabel, _optionHost.Vertical().Spacing(6),
                       swLabel, _softwareHost.Vertical().Spacing(6));

        var right = new ScrollViewer().AutoVerticalScroll().NoHorizontalScroll()
            .Content(new Border().Padding(16).Child(detail));

        return new Grid().Columns("360,*").Spacing(8).Children(
            new Border().BorderThickness(1).CornerRadius(6).Child(left).Column(0),
            new Border().BorderThickness(1).CornerRadius(6).Child(right).Column(1)
        );
    }

    private Element BuildActionBar()
    {
        _btnDownload.Content("下载所选").Width(104);
        _btnDownload.OnClick(() => _ = DownloadAsync());
        _btnBuildIso.Content("生成 ISO").Width(104);
        _btnBuildIso.OnClick(() => _ = BuildIsoAsync());
        _btnWriteUsb.Content("写入 U 盘").Width(104);
        _btnWriteUsb.OnClick(WriteUsb);
        _btnInstallLocal.Content("安装到本地").Width(104);
        _btnInstallLocal.OnClick(InstallLocal);
        _btnOpenOutput.Content("打开输出目录").Width(112);
        _btnOpenOutput.OnClick(OpenOutput);
        _btnCancel.Content("取消").Width(80);
        _btnCancel.OnClick(Cancel);

        var wrap = new WrapPanel().Horizontal().Spacing(8);
        wrap.Children(_btnFetch, _btnDownload, _btnBuildIso, _btnWriteUsb,
                      _btnInstallLocal, _btnOpenOutput, _btnCancel);

        return new Border().Padding(16, 8, 16, 8).Child(wrap);
    }

    private Element BuildProgressArea()
    {
        _lblStatus.Text("就绪。").FontSize(12);
        _barOverall.Minimum(0).Maximum(100).Value(0);
        _barCurrent.Minimum(0).Maximum(100).Value(0);
        _logBox.Placeholder("日志").Wrap(true).IsReadOnly(true);

        var stack = new StackPanel().Vertical().Spacing(6);
        stack.Children(_lblStatus, _barOverall, _barCurrent,
            new ScrollViewer().AutoVerticalScroll().Content(_logBox));

        return new Border().Padding(16, 0, 16, 12).Child(stack);
    }

    // ---------------- 行为 ----------------

    private string SourceKindText() => _settings.SourceKind switch
    {
        ManifestSourceKind.CustomUrl => "自定义 URL",
        ManifestSourceKind.LocalFile => "本地文件",
        _ => "最新 Release"
    };

    private void RefreshSourceLabel()
        => _lblSource.Text($"来源：{_settings.RepoSlug} · {SourceKindText()}");

    private async Task FetchManifestAsync()
    {
        using var pipe = new PackagerPipeline(_settings);
        await RunBusyAsync("正在拉取清单…", async (progress, ct) =>
        {
            var manifest = await pipe.FetchManifestAsync(progress, ct);

            // UI 集合（组件树 / 选项 / 软件列表）必须在 UI 线程上重建 ——
            // 在后台线程直接改集合会与布局测量的枚举并发，导致
            // "Collection was modified; enumeration operation may not execute."
            _manifest = manifest;
            await RunOnUiAsync(RenderComponents);
        });
    }

    /// <summary>把动作投递到 UI 线程执行（若当前无 Dispatcher 则直接执行）。</summary>
    private static async Task RunOnUiAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            action();
            return;
        }

        var tcs = new TaskCompletionSource();
        dispatcher.BeginInvoke(() =>
        {
            try { action(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        });

        await tcs.Task.ConfigureAwait(false);
    }

    private void RenderComponents()
    {
        _componentList.Clear();
        _states.Clear();
        if (_manifest == null) return;

        foreach (var comp in _manifest.Components)
        {
            var state = new ComponentSelectionState { Selected = comp.DefaultSelected };
            _states[comp.Id ?? ""] = state;

            foreach (var sw in comp.Software)
                if (sw.DefaultSelected && sw.Id != null) state.SoftwareIds.Add(sw.Id);
            foreach (var opt in comp.Options)
                if (opt.Id != null) state.Options[opt.Id] = opt.Default ?? "";

            string id = comp.Id ?? "";
            var cb = new CheckBox().Content($"{comp.Name}  ·  {comp.Version}  ({comp.Kind})").IsChecked(state.Selected);
            cb.OnCheckedChanged(v => { state.Selected = v; });

            var row = new Border().Padding(10, 8, 10, 8).CornerRadius(5).Child(cb);
            row.OnMouseDown(_ => ShowComponent(id));
            _componentList.Children(row);
        }

        if (_manifest.Components.Count > 0)
            ShowComponent(_manifest.Components[0].Id);

        Log($"已加载清单：{_manifest.Name} {_manifest.Version}");
    }

    private void ShowComponent(string? id)
    {
        var comp = _manifest?.FindComponent(id);
        if (comp == null) return;

        long size = 0;
        foreach (var kv in comp.Payload) size += kv.Value.SizeBytes;

        _lblDetailTitle.Text(comp.Name ?? comp.Id ?? "");
        _lblDetailBody.Text(
            $"ID：{comp.Id}{Environment.NewLine}" +
            $"类型：{comp.Kind}　架构：{comp.Arch}　版本：{comp.Version}{Environment.NewLine}" +
            $"载荷：{size / 1024.0 / 1024.0:F1} MB{Environment.NewLine}" +
            $"{comp.Description}");

        RenderOptions(comp);
        RenderSoftware(comp);
    }

    private void RenderOptions(Component comp)
    {
        _optionHost.Clear();
        if (!_states.TryGetValue(comp.Id ?? "", out var state)) return;

        foreach (var opt in comp.Options)
        {
            string key = opt.Id ?? "";
            if (string.Equals(opt.Type, "bool", StringComparison.OrdinalIgnoreCase))
            {
                bool def = opt.Default is "true" or "1";
                state.Options[key] = def ? "true" : "false";
                var cb = new CheckBox().Content(opt.Label ?? key).IsChecked(def);
                cb.OnCheckedChanged(v => state.Options[key] = v ? "true" : "false");
                _optionHost.Children(cb);
            }
            else if (string.Equals(opt.Type, "choice", StringComparison.OrdinalIgnoreCase))
            {
                var combo = new ComboBox();
                foreach (var c in opt.Choices) combo.Items(c);

                int idx = opt.Default != null ? opt.Choices.IndexOf(opt.Default) : -1;
                if (idx < 0) idx = 0;
                if (opt.Choices.Count > 0)
                {
                    combo.SelectedIndex(idx);
                    state.Options[key] = opt.Choices[idx];
                    combo.OnSelectionChanged(v => { if (v is string sv) state.Options[key] = sv; });
                }

                var row = new StackPanel().Horizontal().Spacing(8);
                row.Children(new Label().Text((opt.Label ?? key) + "："), combo);
                _optionHost.Children(row);
            }
        }

        if (comp.Options.Count == 0) _optionHost.Children(new Label().Text("（无）"));
    }

    private void RenderSoftware(Component comp)
    {
        _softwareHost.Clear();
        if (!_states.TryGetValue(comp.Id ?? "", out var state)) return;

        if (comp.Software.Count == 0)
        {
            _softwareHost.Children(new Label().Text("（无）"));
            return;
        }

        foreach (var sw in comp.Software)
        {
            string sid = sw.Id ?? "";
            bool def = state.SoftwareIds.Contains(sid);
            var cb = new CheckBox().Content($"{sw.Name}（{sw.SizeBytes / 1024.0 / 1024.0:F2} MB）").IsChecked(def);
            cb.OnCheckedChanged(v =>
            {
                if (v) state.SoftwareIds.Add(sid);
                else state.SoftwareIds.Remove(sid);
            });
            _softwareHost.Children(cb);
        }
    }

    private Selection BuildSelection()
    {
        var sel = new Selection();
        if (_manifest == null) return sel;

        foreach (var comp in _manifest.Components)
        {
            var cs = sel.For(comp.Id);
            if (_states.TryGetValue(comp.Id ?? "", out var st))
            {
                cs.Selected = st.Selected;
                foreach (var id in st.SoftwareIds) cs.SoftwareIds.Add(id);
                foreach (var kv in st.Options) cs.Options[kv.Key] = kv.Value;
            }
        }
        return sel;
    }

    private async Task DownloadAsync()
    {
        if (!EnsureManifest()) return;
        var sel = BuildSelection();
        using var pipe = new PackagerPipeline(_settings);
        await RunBusyAsync("正在下载资产…", (progress, ct) => pipe.DownloadAllAsync(_manifest!, sel, progress, ct));
        Log($"下载完成，缓存目录：{AppPaths.CacheDir}");
    }

    private async Task BuildIsoAsync()
    {
        if (!EnsureManifest()) return;
        var sel = BuildSelection();

        string dir = string.IsNullOrWhiteSpace(_settings.OutputDir) ? AppPaths.DataRoot : _settings.OutputDir;
        string outPath = Path.Combine(dir, $"{_manifest!.Name}-{_manifest.Version}.iso");

        using var pipe = new PackagerPipeline(_settings);
        await RunBusyAsync("正在生成 ISO…", async (progress, ct) =>
        {
            var resolved = await pipe.DownloadAllAsync(_manifest!, sel, progress, ct);

            var tools = new Toolchain(AppPaths.ToolsDir);
            if (resolved.TryGetValue("__tools__", out var toolsZip)) tools.Prepare(toolsZip);
            else tools.Locate();

            string staging = await pipe.StageAsync(_manifest!, sel, resolved, tools, progress, ct);
            await pipe.BuildIsoAsync(staging, outPath, _manifest!.Name, tools, progress, ct);

            _settings.OutputDir = Path.GetDirectoryName(outPath) ?? "";
            _settings.Save();
        });
    }

    private void WriteUsb()
    {
        if (!EnsureManifest()) return;
        var dlg = WriteUsbDialog.Create(this);
        if (dlg != null) _ = dlg.ShowDialogAsync(this);
    }

    private void InstallLocal()
    {
        if (!EnsureManifest()) return;
        var dlg = InstallLocalDialog.Create(this, AppPaths.StagingDir);
        if (dlg != null) _ = dlg.ShowDialogAsync(this);
    }

    private void OpenOutput()
    {
        string dir = !string.IsNullOrWhiteSpace(_settings.OutputDir) && Directory.Exists(_settings.OutputDir)
            ? _settings.OutputDir : AppPaths.DataRoot;
        try { System.Diagnostics.Process.Start("explorer.exe", dir); }
        catch { /* 忽略 */ }
    }

    private void OpenSettings()
    {
        var dlg = new SettingsDialog(_settings, this);
        _ = dlg.ShowDialogAsync(this).ContinueWith(_ =>
            Application.Current?.Dispatcher?.BeginInvoke(RefreshSourceLabel));
    }

    private bool EnsureManifest()
    {
        if (_manifest != null) return true;
        MessageBox.Notify("请先拉取清单。", PromptIconKind.Info, owner: this);
        return false;
    }

    private void Cancel() => _cts?.Cancel();

    private async Task RunBusyAsync(string status, Func<IProgress<ProgressInfo>, CancellationToken, Task> work)
    {
        if (_busy) return;
        _busy = true;
        SetBusy(true);
        _cts = new CancellationTokenSource();

        var progress = new Progress<ProgressInfo>(OnProgress);
        try
        {
            await RunOnUiAsync(() => _lblStatus.Text(status));
            await Task.Run(() => work(progress, _cts.Token), _cts.Token);
            await RunOnUiAsync(() => { _lblStatus.Text("完成。"); _barOverall.Value(100); });
        }
        catch (OperationCanceledException)
        {
            await RunOnUiAsync(() => { _lblStatus.Text("已取消。"); Log("操作已取消。"); });
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            await RunOnUiAsync(() =>
            {
                _lblStatus.Text("失败。");
                Log("错误：" + message);
                MessageBox.Notify(message, PromptIconKind.Error, owner: this);
            });
        }
        finally
        {
            _busy = false;
            await RunOnUiAsync(() => SetBusy(false));
            _cts.Dispose();
            _cts = null;
        }
    }

    private void OnProgress(ProgressInfo p)
    {
        // 进度回调来自后台线程，所有 UI 写入都投递到 UI 线程
        _ = RunOnUiAsync(() =>
        {
            if (!string.IsNullOrEmpty(p.Message))
            {
                _lblStatus.Text(p.Message!);
                Log(p.Message!);
            }
            if (p.OverallPercent >= 0) _barOverall.Value(Math.Clamp(p.OverallPercent, 0, 100));
            if (p.CurrentPercent >= 0) _barCurrent.Value(Math.Clamp(p.CurrentPercent, 0, 100));
        });
    }

    private void SetBusy(bool busy)
    {
        _btnFetch.IsEnabled = !busy;
        _btnDownload.IsEnabled = !busy;
        _btnBuildIso.IsEnabled = !busy;
        _btnWriteUsb.IsEnabled = !busy;
        _btnInstallLocal.IsEnabled = !busy;
        _btnSettings.IsEnabled = !busy;
        _btnCancel.IsEnabled = busy;
    }

    private void Log(string message)
        => _logBox.Text((_logBox.Text ?? "") + $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
}
