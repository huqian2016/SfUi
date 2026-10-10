using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SfUi.App.Views;
using SfUi.Core;

namespace SfUi.App.Services;

/// <summary>ソース エディタ ウィンドウを生成して開く（非モーダル・複数同時可）。</summary>
public sealed class SourceEditorWindowFactory
{
    private readonly IServiceProvider _services;

    public SourceEditorWindowFactory(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>組織を指定して開き、メタデータ一覧の読み込みを開始する。</summary>
    public SourceEditorWindow Open(OrgInfo org, Window? owner = null)
    {
        var window = _services.GetRequiredService<SourceEditorWindow>();
        window.ViewModel.Initialize(org);
        WindowPlacement.CenterOn(window, owner);
        window.Show();
        window.ViewModel.RefreshCommand.Execute(null);
        return window;
    }
}
