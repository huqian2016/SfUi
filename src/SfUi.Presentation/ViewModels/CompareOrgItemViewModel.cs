using CommunityToolkit.Mvvm.ComponentModel;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>比較に含める組織のチェックボックス 1 つ分。</summary>
public partial class CompareOrgItemViewModel : ObservableObject
{
    public CompareOrgItemViewModel(OrgInfo org)
    {
        Org = org;
    }

    public OrgInfo Org { get; }

    public string DisplayName => Org.DisplayName;

    public string Username => Org.Username;

    [ObservableProperty]
    private bool _isSelected;
}
