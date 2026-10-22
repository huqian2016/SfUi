using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>ユーザー選択 1 行（チェックボックス付き）。</summary>
public sealed partial class UserAccessUserViewModel : ObservableObject
{
    public UserAccessUserViewModel(AccessUser user) => User = user;

    public AccessUser User { get; }

    public string Display => User.DisplayWithProfile;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>ユーザーごとの実効オブジェクト権限 1 行。</summary>
public sealed class UserAccessObjectRowViewModel
{
    public UserAccessObjectRowViewModel(UserEffectiveAccess access, int readFields, int editFields)
    {
        User = access.User.Display;
        Profile = access.User.ProfileName;
        Read = Mark(access.Object.Read);
        Create = Mark(access.Object.Create);
        Edit = Mark(access.Object.Edit);
        Delete = Mark(access.Object.Delete);
        ViewAll = Mark(access.Object.ViewAllRecords);
        ModifyAll = Mark(access.Object.ModifyAllRecords);
        ViewAllFields = Mark(access.Object.ViewAllFields);
        FieldCounts = $"{readFields} / {editFields}";
        SearchText = $"{User}\n{Profile}".ToLowerInvariant();
    }

    public string User { get; }

    public string Profile { get; }

    public string Read { get; }

    public string Create { get; }

    public string Edit { get; }

    public string Delete { get; }

    public string ViewAll { get; }

    public string ModifyAll { get; }

    public string ViewAllFields { get; }

    /// <summary>参照可能項目数 / 編集可能項目数。</summary>
    public string FieldCounts { get; }

    public string SearchText { get; }

    public static string Mark(bool value) => value ? "✓" : "—";
}

/// <summary>実効項目権限 1 行（項目 × ユーザーの長形式）。</summary>
public sealed class UserAccessFieldRowViewModel
{
    public UserAccessFieldRowViewModel(string objectApiName, AccessUser user, EffectiveFieldAccess field)
    {
        ObjectApiName = objectApiName;
        FieldApiName = field.FieldApiName;
        Field = $"{field.Label} ({field.FieldApiName})";
        User = user.Display;
        Username = user.Username;
        Profile = user.ProfileName;
        Read = UserAccessObjectRowViewModel.Mark(field.Read);
        Edit = UserAccessObjectRowViewModel.Mark(field.Edit);
        GrantedBy = FormatGrants(field);
        SearchText = $"{Field}\n{user.Display}\n{user.Username}\n{user.ProfileName}".ToLowerInvariant();
    }

    public string ObjectApiName { get; }

    public string FieldApiName { get; }

    public string Field { get; }

    public string User { get; }

    public string Username { get; }

    public string Profile { get; }

    public string Read { get; }

    public string Edit { get; }

    public string GrantedBy { get; }

    public string SearchText { get; }

    public bool CanEdit => Edit == "✓";

    private static string FormatGrants(EffectiveFieldAccess field)
    {
        var grants = field.EditGrants.Count > 0 ? field.EditGrants : field.ReadGrants;
        return grants.Count == 0
            ? string.Empty
            : string.Join("; ", grants.Select(FormatSubject));
    }

    private static string FormatSubject(PermissionSubject subject) => subject.Kind switch
    {
        PermissionSubjectKind.Profile => $"{UiText.T("Access_KindProfile")}: {subject.Label}",
        PermissionSubjectKind.PermissionSetGroup => $"{UiText.T("Access_KindPermissionSetGroup")}: {subject.Label}",
        _ => $"{UiText.T("Access_KindPermissionSet")}: {subject.Label}",
    };
}

/// <summary>
/// ユーザー中心の「実効アクセス」タブ。選択したユーザー（複数可）について、
/// プロファイル + 割り当てられた権限セット / 権限セット グループを合算した
/// オブジェクト権限と項目権限（項目 × ユーザー）を表示する。
/// </summary>
public sealed partial class UserAccessViewModel : ObservableObject, IDisposable
{
    private readonly UserAccessService _users;
    private readonly PermissionAccessService _permissions;
    private readonly IFilePickerService _filePicker;
    private readonly AppLog _log;

    private DataIoViewModel? _owner;
    private bool _activated;

    private readonly List<UserAccessUserViewModel> _allUsers = new();
    private readonly List<UserAccessFieldRowViewModel> _allFieldRows = new();
    private readonly Dictionary<string, IReadOnlyList<UserPermissionAssignment>> _assignmentCache = new(StringComparer.Ordinal);

    private PermissionCatalog? _catalog;
    private IReadOnlyList<ObjectAccessRow> _objectRows = Array.Empty<ObjectAccessRow>();
    private FieldAccessSnapshot? _snapshot;
    private string _loadedObjectKey = string.Empty;
    private IReadOnlyList<UserEffectiveAccess> _computed = Array.Empty<UserEffectiveAccess>();

    public UserAccessViewModel(
        UserAccessService users,
        PermissionAccessService permissions,
        IFilePickerService filePicker,
        AppLog log)
    {
        _users = users;
        _permissions = permissions;
        _filePicker = filePicker;
        _log = log;
    }

    public ObservableCollection<UserAccessUserViewModel> VisibleUsers { get; } = new();

    public ObservableCollection<UserAccessObjectRowViewModel> ObjectRows { get; } = new();

    public ObservableCollection<UserAccessFieldRowViewModel> FieldRows { get; } = new();

    public IReadOnlyList<UserAccessUserViewModel> SelectedUsers => _allUsers.Where(u => u.IsSelected).ToList();

    [ObservableProperty]
    private string _userFilter = string.Empty;

    [ObservableProperty]
    private string _fieldSearch = string.Empty;

    [ObservableProperty]
    private bool _editOnly;

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

    /// <summary>タブ初回表示時に呼ぶ（有効ユーザー一覧の取得）。</summary>
    public async Task EnsureLoadedAsync()
    {
        _activated = true;
        await EnsureUsersAsync();
    }

    private async Task EnsureUsersAsync(bool forceRefresh = false)
    {
        var org = _owner?.TargetOrg;
        if (string.IsNullOrEmpty(org) || _allUsers.Count > 0 && !forceRefresh)
        {
            return;
        }

        try
        {
            var users = await _users.ListUsersAsync(org, forceRefresh);
            _allUsers.Clear();
            foreach (var user in users)
            {
                _allUsers.Add(new UserAccessUserViewModel(user));
            }

            RebuildVisibleUsers();
            StatusMessage = UiText.T("UserAccess_UsersLoadedFmt", _allUsers.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("ユーザー アクセス: 有効ユーザー一覧の取得に失敗しました", ex);
        }
    }

    private void OnDescribeChanged()
    {
        if (_activated && SelectedUsers.Count > 0)
        {
            _ = ShowAsync();
        }
    }

    [RelayCommand]
    private async Task ShowAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var org = _owner?.TargetOrg;
        var describe = _owner?.Describe;
        if (string.IsNullOrEmpty(org) || describe is null)
        {
            StatusMessage = UiText.T("Access_SelectObjectFirst");
            return;
        }

        var selected = SelectedUsers.Select(u => u.User).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = UiText.T("UserAccess_NeedUsers");
            return;
        }

        IsBusy = true;
        StatusMessage = UiText.T("UserAccess_Loading");
        try
        {
            var objectKey = org + "\n" + describe.Name;
            if (_catalog is null || _snapshot is null || !string.Equals(_loadedObjectKey, objectKey, StringComparison.Ordinal))
            {
                _catalog = await _permissions.GetCatalogAsync(org);
                _objectRows = await _permissions.GetObjectAccessAsync(org, describe.Name);
                _snapshot = await _permissions.GetFieldAccessAsync(org, describe.Name);
                _loadedObjectKey = objectKey;
                _assignmentCache.Clear();
            }

            var missing = selected.Where(u => !_assignmentCache.ContainsKey(u.Id)).Select(u => u.Id).ToList();
            if (missing.Count > 0)
            {
                var assignments = await _users.ListAssignmentsAsync(org, missing);
                foreach (var group in assignments.GroupBy(a => a.UserId, StringComparer.Ordinal))
                {
                    _assignmentCache[group.Key] = group.ToList();
                }

                foreach (var id in missing)
                {
                    _assignmentCache.TryAdd(id, Array.Empty<UserPermissionAssignment>());
                }
            }

            _computed = selected
                .Select(user => EffectiveAccessCalculator.Compute(
                    user,
                    _assignmentCache[user.Id],
                    _catalog!,
                    _objectRows,
                    _snapshot!,
                    describe.Fields,
                    describe.Name))
                .ToList();

            Rebuild();
            StatusMessage = string.Empty;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _computed = Array.Empty<UserEffectiveAccess>();
            ObjectRows.Clear();
            FieldRows.Clear();
            _allFieldRows.Clear();
            SummaryText = string.Empty;
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("ユーザー アクセス: 実効アクセスの計算に失敗しました", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (_owner is not null)
        {
            _permissions.Invalidate(_owner.TargetOrg);
            _users.Invalidate(_owner.TargetOrg);
        }

        _catalog = null;
        _snapshot = null;
        _loadedObjectKey = string.Empty;
        _assignmentCache.Clear();
        await EnsureUsersAsync(forceRefresh: true);
        await ShowAsync();
    }

    [RelayCommand]
    private void SelectAllUsers()
    {
        foreach (var user in VisibleUsers)
        {
            user.IsSelected = true;
        }
    }

    [RelayCommand]
    private void ClearUsers()
    {
        foreach (var user in _allUsers)
        {
            user.IsSelected = false;
        }
    }

    partial void OnUserFilterChanged(string value) => RebuildVisibleUsers();

    partial void OnFieldSearchChanged(string value) => ApplyFieldFilter();

    partial void OnEditOnlyChanged(bool value) => ApplyFieldFilter();

    private void RebuildVisibleUsers()
    {
        var filter = UserFilter?.Trim() ?? string.Empty;
        VisibleUsers.Clear();
        foreach (var user in _allUsers)
        {
            if (filter.Length == 0
                || user.User.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || user.User.Username.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || user.User.ProfileName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                VisibleUsers.Add(user);
            }
        }
    }

    /// <summary>計算済みの実効アクセスから行を組み立てる（クエリなし）。</summary>
    private void Rebuild()
    {
        ObjectRows.Clear();
        _allFieldRows.Clear();
        if (_computed.Count == 0 || _owner?.Describe is not { } describe)
        {
            FieldRows.Clear();
            SummaryText = string.Empty;
            return;
        }

        foreach (var access in _computed.OrderBy(a => a.User.Name, StringComparer.CurrentCulture))
        {
            var readFields = access.Fields.Count(f => f.Read);
            var editFields = access.Fields.Count(f => f.Edit);
            ObjectRows.Add(new UserAccessObjectRowViewModel(access, readFields, editFields));

            foreach (var field in access.Fields)
            {
                _allFieldRows.Add(new UserAccessFieldRowViewModel(describe.Name, access.User, field));
            }
        }

        ApplyFieldFilter();
    }

    private void ApplyFieldFilter()
    {
        var terms = (FieldSearch ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var editOnly = EditOnly;

        FieldRows.Clear();
        foreach (var row in _allFieldRows)
        {
            if (editOnly && !row.CanEdit)
            {
                continue;
            }

            if (terms.Length > 0 && !terms.All(t => row.SearchText.Contains(t.ToLowerInvariant(), StringComparison.Ordinal)))
            {
                continue;
            }

            FieldRows.Add(row);
        }

        var totalRead = _allFieldRows.Count(r => r.Read == "✓");
        var totalEdit = _allFieldRows.Count(r => r.CanEdit);
        SummaryText = UiText.T("UserAccess_SummaryFmt", _computed.Count, _allFieldRows.Count, totalRead, totalEdit);
    }

    [RelayCommand]
    private Task ExportObjectCsvAsync() => ExportCsvAsync(objectRows: true);

    [RelayCommand]
    private Task ExportFieldCsvAsync() => ExportCsvAsync(objectRows: false);

    private async Task ExportCsvAsync(bool objectRows)
    {
        if (_computed.Count == 0 || _owner?.Describe is not { } describe)
        {
            return;
        }

        var suffix = objectRows ? "object" : "field";
        var suggestedName = $"user-access-{suffix}-{describe.Name}-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
        var path = await _filePicker.SaveFileAsync(
            UiText.T(objectRows ? "UserAccess_ExportObject" : "UserAccess_ExportFields"),
            suggestedName,
            "CSV|*.csv");
        if (path is null)
        {
            return;
        }

        try
        {
            var builder = new StringBuilder();
            if (objectRows)
            {
                builder.AppendLine(string.Join(',',
                    CsvExporter.Escape("Object"),
                    CsvExporter.Escape(UiText.T("UserAccess_ColUser")),
                    CsvExporter.Escape("Username"),
                    CsvExporter.Escape(UiText.T("UserAccess_ColProfile")),
                    CsvExporter.Escape(UiText.T("Access_ColRead")),
                    CsvExporter.Escape(UiText.T("Access_ColCreate")),
                    CsvExporter.Escape(UiText.T("Access_ColEdit")),
                    CsvExporter.Escape(UiText.T("Access_ColDelete")),
                    CsvExporter.Escape(UiText.T("Access_ColViewAllRecords")),
                    CsvExporter.Escape(UiText.T("Access_ColModifyAllRecords")),
                    CsvExporter.Escape(UiText.T("Access_ColViewAllFields")),
                    CsvExporter.Escape(UiText.T("UserAccess_ColFieldCounts"))));
                foreach (var access in _computed)
                {
                    builder.AppendLine(string.Join(',',
                        CsvExporter.Escape(describe.Name),
                        CsvExporter.Escape(access.User.Name),
                        CsvExporter.Escape(access.User.Username),
                        CsvExporter.Escape(access.User.ProfileName),
                        Flag(access.Object.Read),
                        Flag(access.Object.Create),
                        Flag(access.Object.Edit),
                        Flag(access.Object.Delete),
                        Flag(access.Object.ViewAllRecords),
                        Flag(access.Object.ModifyAllRecords),
                        Flag(access.Object.ViewAllFields),
                        CsvExporter.Escape($"{access.Fields.Count(f => f.Read)} / {access.Fields.Count(f => f.Edit)}")));
                }
            }
            else
            {
                builder.AppendLine(string.Join(',',
                    CsvExporter.Escape("Object"),
                    CsvExporter.Escape(UiText.T("FieldAccess_ColField")),
                    CsvExporter.Escape("API Name"),
                    CsvExporter.Escape(UiText.T("UserAccess_ColUser")),
                    CsvExporter.Escape("Username"),
                    CsvExporter.Escape(UiText.T("UserAccess_ColProfile")),
                    CsvExporter.Escape(UiText.T("Access_ColRead")),
                    CsvExporter.Escape(UiText.T("Access_ColEdit")),
                    CsvExporter.Escape(UiText.T("UserAccess_ColGrants"))));
                foreach (var row in FieldRows)
                {
                    builder.AppendLine(string.Join(',',
                        CsvExporter.Escape(row.ObjectApiName),
                        CsvExporter.Escape(row.Field),
                        CsvExporter.Escape(row.FieldApiName),
                        CsvExporter.Escape(row.User),
                        CsvExporter.Escape(row.Username),
                        CsvExporter.Escape(row.Profile),
                        CsvExporter.Escape(row.Read),
                        CsvExporter.Escape(row.Edit),
                        CsvExporter.Escape(row.GrantedBy)));
                }
            }

            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusMessage = UiText.T("UserAccess_ExportDoneFmt", path);
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("ユーザー アクセス: CSV 出力に失敗しました", ex);
        }
    }

    private static string Flag(bool value) => value ? "TRUE" : "FALSE";

    public void Dispose() => _owner = null;
}
