using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SfUi.App.Views;
using SfUi.Core;

namespace SfUi.App.Services;

/// <summary>レコード差分詳細ウィンドウを新しい非モーダル ウィンドウとして開く（複数同時表示対応）。</summary>
public sealed class CompareRecordDetailWindowFactory
{
    private readonly IServiceProvider _services;

    public CompareRecordDetailWindowFactory(IServiceProvider services)
    {
        _services = services;
    }

    public CompareRecordDetailWindow Open(CompareRecordDetailModel detail, Window? owner = null)
    {
        var window = _services.GetRequiredService<CompareRecordDetailWindow>();
        window.ViewModel.Initialize(detail);
        if (owner is not null && !ReferenceEquals(owner, window))
        {
            window.Owner = owner;
        }

        window.Show();
        return window;
    }
}
