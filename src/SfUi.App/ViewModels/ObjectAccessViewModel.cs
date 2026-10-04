using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>
/// オブジェクトアクセス タブ。選択中オブジェクトに対する
/// Permission Sets / Permission Set Groups / Profiles の権限を一覧化する。
/// </summary>
public sealed partial class ObjectAccessViewModel : ObservableObject, IDisposable
{
    private readonly PermissionAccessService _service;
    private readonly AppLog _log;
    private DataIoViewModel? _owner;
    private bool _activated;
    private bool _stale = true;
    private bool _reloadQueued;

    public ObjectAccessViewModel(PermissionAccessService service, AppLog log)
    {
        _service = service;
        _log = log;
    }

    public ObservableCollection<ObjectAccessRowViewModel> Rows { get; } = new();

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public void Attach(DataIoViewModel owner)
    {
        _owner = owner;
        owner.DescribeChanged += OnDescribeChanged;
    }

    /// <summary>タブ初回表示時に呼ぶ（表示済みなら対象オブジェクト変更時のみ再読込）。</summary>
    public async Task EnsureLoadedAsync()
    {
        _activated = true;
        if (!_stale)
        {
            return;
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (_owner is not null)
        {
            _service.Invalidate(_owner.TargetOrg);
        }

        _stale = true;
        await RefreshAsync();
    }

    private void OnDescribeChanged()
    {
        _stale = true;
        if (_activated)
        {
            _ = RefreshAsync();
        }
    }

    private async Task RefreshAsync()
    {
        if (_owner is null)
        {
            return;
        }

        if (IsBusy)
        {
            _reloadQueued = true;
            return;
        }

        var describe = _owner.Describe;
        if (string.IsNullOrEmpty(_owner.TargetOrg) || describe is null)
        {
            Rows.Clear();
            SummaryText = string.Empty;
            StatusMessage = UiText.T("Access_SelectObjectFirst");
            _stale = false;
            return;
        }

        IsBusy = true;
        StatusMessage = UiText.T("DataIo_Loading");
        try
        {
            var rows = await _service.GetObjectAccessAsync(_owner.TargetOrg, describe.Name);
            Rows.Clear();
            foreach (var row in rows)
            {
                Rows.Add(new ObjectAccessRowViewModel(row));
            }

            SummaryText = UiText.T("Access_RowCountFmt", rows.Count);
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("オブジェクトアクセスの取得に失敗しました", ex);
        }
        finally
        {
            IsBusy = false;
            _stale = false;
            if (_reloadQueued)
            {
                _reloadQueued = false;
                _stale = true;
                _ = RefreshAsync();
            }
        }
    }

    public void Dispose()
    {
        if (_owner is not null)
        {
            _owner.DescribeChanged -= OnDescribeChanged;
            _owner = null;
        }
    }
}
