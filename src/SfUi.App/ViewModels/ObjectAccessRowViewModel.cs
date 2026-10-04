using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>オブジェクトアクセス 1 行分の表示用 ViewModel。</summary>
public sealed class ObjectAccessRowViewModel
{
    public ObjectAccessRowViewModel(ObjectAccessRow row)
    {
        Kind = UiText.T(row.Subject.Kind switch
        {
            PermissionSubjectKind.Profile => "Access_KindProfile",
            PermissionSubjectKind.PermissionSet => "Access_KindPermissionSet",
            _ => "Access_KindPermissionSetGroup",
        });
        Label = row.Subject.Label;
        ApiName = row.Subject.ApiName;
        Custom = Tick(row.Subject.IsCustom);
        Read = Tick(row.Read);
        Create = Tick(row.Create);
        Edit = Tick(row.Edit);
        Delete = Tick(row.Delete);
        ViewAllRecords = Tick(row.ViewAllRecords);
        ModifyAllRecords = Tick(row.ModifyAllRecords);
        ViewAllFields = Tick(row.ViewAllFields);
    }

    public string Kind { get; }

    public string Label { get; }

    public string ApiName { get; }

    public string Custom { get; }

    public string Read { get; }

    public string Create { get; }

    public string Edit { get; }

    public string Delete { get; }

    public string ViewAllRecords { get; }

    public string ModifyAllRecords { get; }

    public string ViewAllFields { get; }

    public static string Tick(bool value) => value ? "✓" : "−";
}
