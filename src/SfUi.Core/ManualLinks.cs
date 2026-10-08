namespace SfUi.Core;

/// <summary>
/// 各ウィンドウのヘルプ アイコンが開くユーザーマニュアル（GitHub）の URL を組み立てる。
/// 現在の UI 言語とウィンドウ（<see cref="Topic"/>）に対応する章アンカー付きの URL を返す。
/// 例: 日本語 UI の組織情報ウィンドウ →
/// https://github.com/huqian2016/SfUi/blob/main/docs/manual/ja.md#7-%E7%B5%84%E7%B9%94%E6%83%85%E5%A0%B1org-info
/// </summary>
public static class ManualLinks
{
    /// <summary>GitHub 上のユーザーマニュアルのベース URL。</summary>
    public const string BaseUrl = "https://github.com/huqian2016/SfUi/blob/main/docs/manual/";

    /// <summary>マニュアルの章（ウィンドウに対応）。</summary>
    public enum Topic
    {
        /// <summary>メイン ウィンドウ（3. 画面の基本）。</summary>
        MainWindow,

        /// <summary>ようこそ画面（2. インストールと初期設定）。</summary>
        Welcome,

        /// <summary>組織情報ウィンドウ（7. 組織情報）。</summary>
        OrgInfo,

        /// <summary>項目の使用箇所ウィンドウ（7.1 オブジェクト項目タブ）。</summary>
        FieldUsage,

        /// <summary>組織比較ウィンドウ（10. 組織比較）。</summary>
        CompareOrgs,

        /// <summary>レコード差分詳細ウィンドウ（10. 組織比較）。</summary>
        RecordDetail,

        /// <summary>データ入出力ウィンドウ（8. データ入出力）。</summary>
        DataIo,

        /// <summary>バックアップと復元ウィンドウ（9. バックアップと復元）。</summary>
        Backup,

        /// <summary>バックアップのレコード ウィンドウ（9.2 復元タブ）。</summary>
        RestoreRecords,

        /// <summary>バックアップ比較のレコード ウィンドウ（9.3 比較タブ）。</summary>
        BackupCompareRecords,

        /// <summary>組織管理ウィンドウ（6. 組織管理）。</summary>
        OrgManage,

        /// <summary>デバッグログ解析ウィンドウ（4.3 デバッグログと解析）。</summary>
        LogAnalyzer,
    }

    /// <summary>言語コード → 章アンカー（GitHub が生成する見出し ID と一致させること）。</summary>
    private static readonly Dictionary<string, Dictionary<Topic, string>> AnchorsByLanguage = new()
    {
        [UiText.English] = new()
        {
            [Topic.MainWindow] = "3-the-basics-of-the-ui",
            [Topic.Welcome] = "2-installation--initial-setup",
            [Topic.OrgInfo] = "7-org-info",
            [Topic.FieldUsage] = "71-object-fields-tab-and-find-field-usage",
            [Topic.CompareOrgs] = "10-compare-orgs",
            [Topic.RecordDetail] = "10-compare-orgs",
            [Topic.DataIo] = "8-data-io",
            [Topic.Backup] = "9-backup--restore",
            [Topic.RestoreRecords] = "92-restore-tab",
            [Topic.BackupCompareRecords] = "93-compare-tab",
            [Topic.OrgManage] = "6-org-management",
            [Topic.LogAnalyzer] = "43-debug-logs-and-analysis",
        },
        [UiText.Japanese] = new()
        {
            [Topic.MainWindow] = "3-画面の基本",
            [Topic.Welcome] = "2-インストールと初期設定",
            [Topic.OrgInfo] = "7-組織情報org-info",
            [Topic.FieldUsage] = "71-オブジェクト項目タブと使用箇所を検索",
            [Topic.CompareOrgs] = "10-組織比較",
            [Topic.RecordDetail] = "10-組織比較",
            [Topic.DataIo] = "8-データ入出力",
            [Topic.Backup] = "9-バックアップと復元",
            [Topic.RestoreRecords] = "92-復元タブ",
            [Topic.BackupCompareRecords] = "93-比較タブ",
            [Topic.OrgManage] = "6-組織管理",
            [Topic.LogAnalyzer] = "43-デバッグログと解析",
        },
        [UiText.Chinese] = new()
        {
            [Topic.MainWindow] = "3-界面基础",
            [Topic.Welcome] = "2-安装与初始设置",
            [Topic.OrgInfo] = "7-组织信息org-info",
            [Topic.FieldUsage] = "71-对象字段选项卡与查找使用位置",
            [Topic.CompareOrgs] = "10-组织比较",
            [Topic.RecordDetail] = "10-组织比较",
            [Topic.DataIo] = "8-数据导入导出",
            [Topic.Backup] = "9-备份与恢复",
            [Topic.RestoreRecords] = "92-恢复选项卡",
            [Topic.BackupCompareRecords] = "93-比较选项卡",
            [Topic.OrgManage] = "6-组织管理",
            [Topic.LogAnalyzer] = "43-调试日志与解析",
        },
        [UiText.Korean] = new()
        {
            [Topic.MainWindow] = "3-화면-기본",
            [Topic.Welcome] = "2-설치와-초기-설정",
            [Topic.OrgInfo] = "7-조직-정보-org-info",
            [Topic.FieldUsage] = "71-오브젝트-필드-탭과-사용-위치-검색",
            [Topic.CompareOrgs] = "10-조직-비교",
            [Topic.RecordDetail] = "10-조직-비교",
            [Topic.DataIo] = "8-데이터-입출력",
            [Topic.Backup] = "9-백업과-복원",
            [Topic.RestoreRecords] = "92-복원-탭",
            [Topic.BackupCompareRecords] = "93-비교-탭",
            [Topic.OrgManage] = "6-조직-관리",
            [Topic.LogAnalyzer] = "43-디버그-로그와-분석",
        },
    };

    /// <summary>現在の UI 言語で、指定した章の URL を返す。</summary>
    public static string BuildUrl(Topic topic) => BuildUrl(topic, UiText.Language);

    /// <summary>
    /// 指定した言語コード（"en" / "ja" / "zh" / "ko"）で、指定した章の URL を返す。
    /// 不明な言語は英語のマニュアルにフォールバックする。アンカーは URL エンコードして返す。
    /// </summary>
    public static string BuildUrl(Topic topic, string? language)
    {
        var key = language is not null && AnchorsByLanguage.ContainsKey(language)
            ? language
            : UiText.English;
        var anchor = AnchorsByLanguage[key][topic];
        return $"{BaseUrl}{key}.md#{Uri.EscapeDataString(anchor)}";
    }

    /// <summary>現在の UI 言語で、指定した章のページを既定のブラウザーで開く。</summary>
    public static bool TryOpen(Topic topic) => UrlLauncher.TryOpen(BuildUrl(topic));
}
