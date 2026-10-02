namespace SfUi.Core;

/// <summary>
/// よく利用される代表的な SOQL / 匿名Apex / コマンド / REST API のサンプルを履歴へ投入する。
/// 同じ内容（種別 + パラメータ）が既にある場合はスキップする。
/// </summary>
public static class SampleHistorySeeder
{
    /// <summary>投入結果（追加件数 / スキップ件数）。</summary>
    public sealed record SeedResult(int Added, int Skipped);

    /// <summary>サンプル 1 件分の定義。</summary>
    private sealed record Sample(string Type, string Summary, string Payload);

    /// <summary>サンプルを履歴に追加する。</summary>
    public static SeedResult Seed(HistoryStore history, AppLog log)
    {
        var samples = BuildSamples();

        // 既存履歴（種別 + パラメータの正規化文字列）を重複判定に使う
        var existing = new HashSet<(string Type, string Params)>();
        foreach (var entry in history.Query())
        {
            if (entry.Params is { Length: > 0 } payload)
            {
                existing.Add((entry.Type.ToLowerInvariant(), Normalize(payload)));
            }
        }

        var added = 0;
        var skipped = 0;
        var order = 0;
        foreach (var sample in samples)
        {
            order++;
            var key = (sample.Type, Normalize(sample.Payload));
            if (!existing.Add(key))
            {
                skipped++;
                continue;
            }

            var durationMs = sample.Type switch
            {
                HistoryTypes.Soql => 420 + (order * 63),
                HistoryTypes.Apex => 2600 + (order * 137),
                HistoryTypes.Api => 180 + (order * 29),
                _ => 800 + (order * 57),
            };

            history.Append(new HistoryEntry
            {
                Type = sample.Type,
                Summary = sample.Summary,
                Params = sample.Payload,
                Status = "success",
                DurationMs = durationMs,
                // 先頭のサンプルほど新しくなるように 1 分ずつずらす
                Timestamp = DateTimeOffset.Now - TimeSpan.FromMinutes(order),
            });
            added++;
        }

        log.Info($"サンプル履歴を投入: 追加 {added} 件 / スキップ {skipped} 件");
        return new SeedResult(added, skipped);
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Trim();

    private static List<Sample> BuildSamples() => new()
    {
        // ---- SOQL ----
        new(HistoryTypes.Soql, "取引先の基本一覧（10 件）",
            "SELECT Id, Name FROM Account LIMIT 10"),
        new(HistoryTypes.Soql, "取引先（業種・売上つき、更新が新しい順 20 件）",
            "SELECT Id, Name, Industry, AnnualRevenue FROM Account ORDER BY LastModifiedDate DESC LIMIT 20"),
        new(HistoryTypes.Soql, "取引先の件数",
            "SELECT COUNT() FROM Account"),
        new(HistoryTypes.Soql, "直近 7 日で作成された取引先",
            "SELECT Id, Name, CreatedDate FROM Account WHERE CreatedDate = LAST_N_DAYS:7 ORDER BY CreatedDate DESC LIMIT 50"),
        new(HistoryTypes.Soql, "担当者（メールつき、更新が新しい順 20 件）",
            "SELECT Id, FirstName, LastName, Email FROM Contact ORDER BY LastModifiedDate DESC LIMIT 20"),
        new(HistoryTypes.Soql, "担当者と取引先名（リレーション）",
            "SELECT Id, Name, Account.Name FROM Contact WHERE AccountId != null LIMIT 30"),
        new(HistoryTypes.Soql, "進行中の商談（クローズ日順 50 件）",
            "SELECT Id, Name, StageName, Amount, CloseDate FROM Opportunity WHERE IsClosed = false ORDER BY CloseDate LIMIT 50"),
        new(HistoryTypes.Soql, "未完了の ToDo（期日順 50 件）",
            "SELECT Id, Subject, Status, Priority, ActivityDate FROM Task WHERE IsClosed = false ORDER BY ActivityDate LIMIT 50"),
        new(HistoryTypes.Soql, "アクティブユーザー（50 件）",
            "SELECT Id, Name FROM User WHERE IsActive = true ORDER BY Name LIMIT 50"),

        // ---- 匿名Apex ----
        new(HistoryTypes.Apex, "ユーザーと組織の情報を表示",
            "System.debug('ユーザー: ' + UserInfo.getUserName());\n"
            + "System.debug('組織 ID: ' + UserInfo.getOrganizationId());\n"
            + "System.debug('組織名: ' + UserInfo.getOrganizationName());"),
        new(HistoryTypes.Apex, "取引先を 5 件取得してログ出力",
            "for (Account a : [SELECT Id, Name FROM Account LIMIT 5]) {\n"
            + "    System.debug(a.Name + ' / ' + a.Id);\n"
            + "}"),
        new(HistoryTypes.Apex, "セーブポイントでロールバック（安全な DML サンプル）",
            "Savepoint sp = Database.setSavepoint();\n"
            + "Account a = new Account(Name = 'SfUi サンプル取引先');\n"
            + "insert a;\n"
            + "System.debug('作成 ID: ' + a.Id);\n"
            + "Database.rollback(sp);\n"
            + "System.debug('ロールバックしました');"),
        new(HistoryTypes.Apex, "ガバナ制限（Limits）の確認",
            "System.debug('SOQL 発行数: ' + Limits.getQueries() + ' / ' + Limits.getLimitQueries());\n"
            + "System.debug('DML 発行数: ' + Limits.getDmlStatements() + ' / ' + Limits.getLimitDmlStatements());"),
        new(HistoryTypes.Apex, "DmlException のハンドリング例",
            "try {\n"
            + "    insert new Account(); // Name 未設定 → DmlException\n"
            + "} catch (DmlException ex) {\n"
            + "    System.debug('想定どおりのエラー: ' + ex.getDmlMessage(0));\n"
            + "}"),
        new(HistoryTypes.Apex, "Map の JSON シリアライズ例",
            "Map<String, Object> data = new Map<String, Object>{\n"
            + "    'name' => 'SfUi',\n"
            + "    'version' => 1\n"
            + "};\n"
            + "System.debug(JSON.serializePretty(data));"),

        // ---- コマンド ----
        new(HistoryTypes.Command, "認証済み組織の一覧",
            "org list"),
        new(HistoryTypes.Command, "選択中の組織の情報（URL / API バージョン等）",
            "org display"),
        new(HistoryTypes.Command, "組織のホームをブラウザで開く",
            "org open --path /lightning/page/home"),
        new(HistoryTypes.Command, "CLI 経由の SOQL（REST のフォールバック確認用）",
            "data query --query \"SELECT Id, Name FROM Account LIMIT 10\""),
        new(HistoryTypes.Command, "デバッグログの一覧（直近分）",
            "apex list log"),
        new(HistoryTypes.Command, "最新のデバッグログを取得",
            "apex get log -n 1"),
        new(HistoryTypes.Command, "直前のデプロイ結果を確認",
            "project deploy report --use-most-recent"),
        new(HistoryTypes.Command, "エイリアス（組織の別名）一覧",
            "alias list"),

        // ---- REST API ----
        new(HistoryTypes.Api, "API 利用状況（制限値と現在の消費量）",
            "GET /services/data/v67.0/limits"),
        new(HistoryTypes.Api, "オブジェクト一覧（メタデータ）",
            "GET /services/data/v67.0/sobjects"),
        new(HistoryTypes.Api, "取引先（Account）の項目定義",
            "GET /services/data/v67.0/sobjects/Account/describe"),
        new(HistoryTypes.Api, "REST 経由の SOQL（アカウント 5 件）",
            "GET /services/data/v67.0/query?q=SELECT+Id,Name+FROM+Account+LIMIT+5"),
        new(HistoryTypes.Api, "Tooling API: Apex クラス一覧（10 件）",
            "GET /services/data/v67.0/tooling/query?q=SELECT+Id,Name+FROM+ApexClass+LIMIT+10"),
        new(HistoryTypes.Api, "最近参照したレコード",
            "GET /services/data/v67.0/recent"),
        new(HistoryTypes.Api, "取引先の件数（COUNT クエリ）",
            "GET /services/data/v67.0/query?q=SELECT+COUNT()+FROM+Account"),
    };
}
