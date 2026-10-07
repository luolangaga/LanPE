using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using LanPE.Core;

namespace LanPE.UI;

/// <summary>设置对话框：清单来源、仓库、Token、输出目录。</summary>
public sealed class SettingsDialog : Window
{
    private readonly AppSettings _settings;

    private readonly ComboBox _cboKind = new();
    private readonly TextBox _txtOwner = new();
    private readonly TextBox _txtRepo = new();
    private readonly TextBox _txtUrl = new();
    private readonly TextBox _txtLocal = new();
    private readonly TextBox _txtToken = new();
    private readonly TextBox _txtOutput = new();
    private readonly CheckBox _chkCache = new();

    public SettingsDialog(AppSettings settings, Window owner)
    {
        _settings = settings;

        this.Title("设置")
            .Fixed(600, 480)
            .StartCenterOwner()
            .Content(BuildContent());
    }

    private Element BuildContent()
    {
        _cboKind.Items("最新 Release", "自定义 URL", "本地文件");
        _cboKind.SelectedIndex((int)_settings.SourceKind);
        _cboKind.OnSelectionChanged(_ => UpdateEnabled());

        _txtOwner.Text(_settings.RepoOwner);
        _txtRepo.Text(_settings.RepoName);
        _txtUrl.Text(_settings.CustomManifestUrl);
        _txtLocal.Text(_settings.LocalManifestPath);
        _txtToken.Text(_settings.GitHubToken);
        _txtOutput.Text(_settings.OutputDir);
        _chkCache.Content("优先使用本地缓存").IsChecked(_settings.PreferCachedAssets);

        var btnOk = new Button().Content("确定").Width(84);
        btnOk.OnClick(() => { SaveBack(); Close(); });
        var btnCancel = new Button().Content("取消").Width(84);
        btnCancel.OnClick(Close);

        var buttons = new StackPanel().Horizontal().Spacing(8);
        buttons.HorizontalAlignment(HorizontalAlignment.Right);
        buttons.Children(btnOk, btnCancel);

        var stack = new StackPanel().Vertical().Spacing(10);
        stack.Children(
            Row("清单来源", _cboKind),
            Row("仓库 Owner", _txtOwner),
            Row("仓库名", _txtRepo),
            Row("自定义 URL", _txtUrl),
            Row("本地文件", _txtLocal),
            Row("GitHub Token", _txtToken),
            Row("输出目录", _txtOutput),
            _chkCache,
            buttons
        );

        UpdateEnabled();
        return new Border().Padding(16).Child(stack);
    }

    private static Element Row<T>(string label, T field) where T : Control
    {
        var row = new StackPanel().Horizontal().Spacing(10);
        var lbl = new Label().Text(label).Width(110);
        lbl.VerticalAlignment(VerticalAlignment.Center);
        field.Width(360);
        row.Children(lbl, field);
        return row;
    }

    private void UpdateEnabled()
    {
        var kind = (ManifestSourceKind)_cboKind.SelectedIndex;
        _txtUrl.IsEnabled = kind == ManifestSourceKind.CustomUrl;
        _txtLocal.IsEnabled = kind == ManifestSourceKind.LocalFile;
        _txtOwner.IsEnabled = kind == ManifestSourceKind.LatestRelease;
        _txtRepo.IsEnabled = kind == ManifestSourceKind.LatestRelease;
    }

    private void SaveBack()
    {
        _settings.SourceKind = (ManifestSourceKind)_cboKind.SelectedIndex;
        _settings.RepoOwner = _txtOwner.Text?.Trim() ?? "";
        _settings.RepoName = _txtRepo.Text?.Trim() ?? "";
        _settings.CustomManifestUrl = _txtUrl.Text?.Trim() ?? "";
        _settings.LocalManifestPath = _txtLocal.Text?.Trim() ?? "";
        _settings.GitHubToken = _txtToken.Text?.Trim() ?? "";
        _settings.OutputDir = _txtOutput.Text?.Trim() ?? "";
        _settings.PreferCachedAssets = _chkCache.IsChecked == true;
        _settings.Save();
    }
}
