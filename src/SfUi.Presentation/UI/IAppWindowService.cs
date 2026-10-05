using SfUi.Core;

namespace SfUi.Presentation;

/// <summary>
/// アプリ固有ウィンドウ（組織情報 / 比較 / データ入出力 / バックアップ / 組織管理 / レコード詳細 / ようこそ）の
/// 生成・表示の抽象化。実装は各 UI プロジェクトの WindowFactory 群に委譲する。
/// </summary>
public interface IAppWindowService
{
    /// <summary>組織情報ウィンドウを開く。</summary>
    void OpenOrgInfo(OrgInfo org);

    /// <summary>組織比較ウィンドウを開く。</summary>
    void OpenCompareOrgs(IReadOnlyList<OrgInfo> orgs);

    /// <summary>データ入出力ウィンドウを開く（オブジェクト名 / 初期 SOQL は任意）。</summary>
    void OpenDataIo(OrgInfo org, string? objectName = null, string? initialSoql = null);

    /// <summary>バックアップと復元ウィンドウを開く。</summary>
    void OpenBackup(OrgInfo org);

    /// <summary>組織管理ウィンドウを開く（初期選択組織は任意）。</summary>
    void OpenOrgManage(OrgInfo? initial);

    /// <summary>バックアップ内のレコード詳細ウィンドウを開く。</summary>
    void OpenBackupRecords(string backupId, BackupObjectInfo info, string displayName, OrgInfo? currentOrg);

    /// <summary>バックアップ比較のレコード単位差分ウィンドウを開く。</summary>
    void OpenBackupCompareRecords(string backupIdA, string backupIdB, string objectName, string displayName, OrgInfo? currentOrg);

    /// <summary>ようこそ画面をモーダルで開く（「設定を開く」が選ばれたら openSettings を呼ぶ）。</summary>
    void OpenWelcome(Action? openSettings = null);
}
