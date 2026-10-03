using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>データ入出力ウィンドウの親 ViewModel（組織とオブジェクト選択を 2 タブで共有する）。</summary>
public sealed partial class DataIoViewModel : ObservableObject, IDisposable
{
    private readonly SObjectDescribeService _describes;
    private readonly AppLog _log;
    private bool _initialized;
    private bool _suppressObjectText;
    private string? _pendingObjectName;

    public DataIoViewModel(SObjectDescribeService describes, DataExportViewModel export, DataImportViewModel import, AppLog log)
    {
        _describes = describes;
        Export = export;
        Import = import;
        _log = log;
        export.Attach(this);
        import.Attach(this);
    }

    public DataExportViewModel Export { get; }

    public DataImportViewModel Import { get; }

    public OrgInfo? Org { get; private set; }

    public string TargetOrg => Org is null ? string.Empty : (string.IsNullOrWhiteSpace(Org.Alias) ? Org.Username : Org.Alias!);

    [ObservableProperty]
    private string _title = string.Empty;

    private ObservableCollection<DataIoObject> _objects = new();

    [ObservableProperty]
    private DataIoObject? _selectedObject;

    [ObservableProperty]
    private DataIoObjectDescribe? _describe;

    [ObservableProperty]
    private string _objectText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _describeSummary = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>describe（項目メタデータ）が変わったとき（選択解除・読込完了・失敗）。</summary>
    public event Action? DescribeChanged;

    public ObservableCollection<DataIoObject> Objects
    {
        get => _objects;
        private set => SetProperty(ref _objects, value);
    }

    /// <summary>ウィンドウ生成時に組織と（任意で）対象オブジェクト・SOQL を受け取る。</summary>
    public void Initialize(OrgInfo org, string? objectApiName = null, string? soql = null)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        Org = org;
        _pendingObjectName = objectApiName;
        Title = UiText.T("DataIo_TitleFmt", org.DisplayName);

        if (!string.IsNullOrWhiteSpace(soql))
        {
            Export.PresetSoql(soql!);
        }
    }

    /// <summary>オブジェクト一覧を読み込む（ウィンドウ表示後に 1 回）。</summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var objects = await _describes.ListObjectsAsync(TargetOrg);
            Objects = new ObservableCollection<DataIoObject>(objects);

            if (!string.IsNullOrWhiteSpace(_pendingObjectName))
            {
                var target = _pendingObjectName!;
                var match = Objects.FirstOrDefault(o => string.Equals(o.Name, target, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    match = new DataIoObject(target, target, true, true, true, true);
                    Objects.Insert(0, match);
                }

                SelectedObject = match;
                _pendingObjectName = null;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = UiText.T("DataIo_LoadObjectsFailedFmt", ex.Message);
            _log.Error("データ入出力: オブジェクト一覧の取得に失敗しました", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedObjectChanged(DataIoObject? value)
    {
        // 選択されたときだけテキストを同期する（null はユーザーの手入力の可能性があるため触らない）
        if (value is not null)
        {
            _suppressObjectText = true;
            ObjectText = value.Display;
            _suppressObjectText = false;
        }

        Describe = null;
        DescribeSummary = value is null ? string.Empty : UiText.T("DataIo_Loading");
        DescribeChanged?.Invoke();

        if (value is not null)
        {
            _ = LoadDescribeAsync(value);
        }
    }

    partial void OnObjectTextChanged(string value)
    {
        if (_suppressObjectText)
        {
            return;
        }

        // 手入力で選択中項目と異なるテキストになったら選択を解除する（実行時に ResolveDescribeAsync で解決）
        if (SelectedObject is not null &&
            !string.Equals(value, SelectedObject.Display, StringComparison.Ordinal))
        {
            _suppressObjectText = true;
            SelectedObject = null;
            _suppressObjectText = false;
        }
    }

    private async Task LoadDescribeAsync(DataIoObject target)
    {
        try
        {
            var describe = await _describes.DescribeAsync(TargetOrg, target.Name);
            if (!ReferenceEquals(SelectedObject, target))
            {
                return;
            }

            Describe = describe;
            DescribeSummary = UiText.T("DataIo_FieldsFmt", describe.Fields.Count);
            DescribeChanged?.Invoke();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!ReferenceEquals(SelectedObject, target))
            {
                return;
            }

            DescribeSummary = string.Empty;
            StatusMessage = UiText.T("DataIo_LoadFieldsFailedFmt", ex.Message);
            _log.Error($"データ入出力: {target.Name} の項目情報取得に失敗しました", ex);
        }
    }

    /// <summary>実行時に、選択または手入力された API 名から describe を解決する（一覧にない名前も対象）。</summary>
    public async Task<DataIoObjectDescribe?> ResolveDescribeAsync(CancellationToken cancellationToken = default)
    {
        var text = ObjectText?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            ShowMessage(UiText.T("DataIo_Err_ObjectRequired"));
            return null;
        }

        var match = Objects.FirstOrDefault(o =>
            string.Equals(o.Name, text, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(o.Display, text, StringComparison.Ordinal));

        if (match is null)
        {
            match = new DataIoObject(text, text, true, true, true, true);
            Objects.Insert(0, match);
        }

        if (!ReferenceEquals(SelectedObject, match))
        {
            SelectedObject = match;
        }

        try
        {
            var describe = await _describes.DescribeAsync(TargetOrg, match.Name, cancellationToken: cancellationToken);
            Describe = describe;
            DescribeSummary = UiText.T("DataIo_FieldsFmt", describe.Fields.Count);
            DescribeChanged?.Invoke();
            return describe;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = UiText.T("DataIo_LoadFieldsFailedFmt", ex.Message);
            _log.Error($"データ入出力: {match.Name} の項目情報取得に失敗しました", ex);
            return null;
        }
    }

    private static void ShowMessage(string message) =>
        MessageBox.Show(message, "SfUi", MessageBoxButton.OK, MessageBoxImage.Warning);

    public void Dispose()
    {
        Export.Dispose();
        Import.Dispose();
        DescribeChanged = null;
    }
}
