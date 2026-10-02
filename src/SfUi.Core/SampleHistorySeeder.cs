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
        new(HistoryTypes.Soql, UiText.T("Sample_Soql_Basic"),
            "SELECT Id, Name FROM Account LIMIT 10"),
        new(HistoryTypes.Soql, UiText.T("Sample_Soql_Industry"),
            "SELECT Id, Name, Industry, AnnualRevenue FROM Account ORDER BY LastModifiedDate DESC LIMIT 20"),
        new(HistoryTypes.Soql, UiText.T("Sample_Soql_Count"),
            "SELECT COUNT() FROM Account"),
        new(HistoryTypes.Soql, UiText.T("Sample_Soql_Recent"),
            "SELECT Id, Name, CreatedDate FROM Account WHERE CreatedDate = LAST_N_DAYS:7 ORDER BY CreatedDate DESC LIMIT 50"),
        new(HistoryTypes.Soql, UiText.T("Sample_Soql_Contacts"),
            "SELECT Id, FirstName, LastName, Email FROM Contact ORDER BY LastModifiedDate DESC LIMIT 20"),
        new(HistoryTypes.Soql, UiText.T("Sample_Soql_Rel"),
            "SELECT Id, Name, Account.Name FROM Contact WHERE AccountId != null LIMIT 30"),
        new(HistoryTypes.Soql, UiText.T("Sample_Soql_Opps"),
            "SELECT Id, Name, StageName, Amount, CloseDate FROM Opportunity WHERE IsClosed = false ORDER BY CloseDate LIMIT 50"),
        new(HistoryTypes.Soql, UiText.T("Sample_Soql_Tasks"),
            "SELECT Id, Subject, Status, Priority, ActivityDate FROM Task WHERE IsClosed = false ORDER BY ActivityDate LIMIT 50"),
        new(HistoryTypes.Soql, UiText.T("Sample_Soql_Users"),
            "SELECT Id, Name FROM User WHERE IsActive = true ORDER BY Name LIMIT 50"),

        // ---- 匿名Apex ----
        new(HistoryTypes.Apex, UiText.T("Sample_Apex_UserInfo"),
            "System.debug('User: ' + UserInfo.getUserName());\n"
            + "System.debug('Org ID: ' + UserInfo.getOrganizationId());\n"
            + "System.debug('Org name: ' + UserInfo.getOrganizationName());"),
        new(HistoryTypes.Apex, UiText.T("Sample_Apex_Query"),
            "for (Account a : [SELECT Id, Name FROM Account LIMIT 5]) {\n"
            + "    System.debug(a.Name + ' / ' + a.Id);\n"
            + "}"),
        new(HistoryTypes.Apex, UiText.T("Sample_Apex_Savepoint"),
            "Savepoint sp = Database.setSavepoint();\n"
            + "Account a = new Account(Name = 'SfUi Sample Account');\n"
            + "insert a;\n"
            + "System.debug('Created ID: ' + a.Id);\n"
            + "Database.rollback(sp);\n"
            + "System.debug('Rolled back');"),
        new(HistoryTypes.Apex, UiText.T("Sample_Apex_Limits"),
            "System.debug('SOQL queries: ' + Limits.getQueries() + ' / ' + Limits.getLimitQueries());\n"
            + "System.debug('DML statements: ' + Limits.getDmlStatements() + ' / ' + Limits.getLimitDmlStatements());"),
        new(HistoryTypes.Apex, UiText.T("Sample_Apex_DmlCatch"),
            "try {\n"
            + "    insert new Account(); // No Name -> DmlException\n"
            + "} catch (DmlException ex) {\n"
            + "    System.debug('Expected error: ' + ex.getDmlMessage(0));\n"
            + "}"),
        new(HistoryTypes.Apex, UiText.T("Sample_Apex_Json"),
            "Map<String, Object> data = new Map<String, Object>{\n"
            + "    'name' => 'SfUi',\n"
            + "    'version' => 1\n"
            + "};\n"
            + "System.debug(JSON.serializePretty(data));"),

        // ---- コマンド ----
        new(HistoryTypes.Command, UiText.T("Sample_Command_OrgList"),
            "org list"),
        new(HistoryTypes.Command, UiText.T("Sample_Command_OrgDisplay"),
            "org display"),
        new(HistoryTypes.Command, UiText.T("Sample_Command_OrgOpen"),
            "org open --path /lightning/page/home"),
        new(HistoryTypes.Command, UiText.T("Sample_Command_DataQuery"),
            "data query --query \"SELECT Id, Name FROM Account LIMIT 10\""),
        new(HistoryTypes.Command, UiText.T("Sample_Command_LogList"),
            "apex list log"),
        new(HistoryTypes.Command, UiText.T("Sample_Command_LogGet"),
            "apex get log -n 1"),
        new(HistoryTypes.Command, UiText.T("Sample_Command_DeployReport"),
            "project deploy report --use-most-recent"),
        new(HistoryTypes.Command, UiText.T("Sample_Command_AliasList"),
            "alias list"),

        // ---- REST API ----
        new(HistoryTypes.Api, UiText.T("Sample_Api_Limits"),
            "GET /services/data/v67.0/limits"),
        new(HistoryTypes.Api, UiText.T("Sample_Api_Sobjects"),
            "GET /services/data/v67.0/sobjects"),
        new(HistoryTypes.Api, UiText.T("Sample_Api_Describe"),
            "GET /services/data/v67.0/sobjects/Account/describe"),
        new(HistoryTypes.Api, UiText.T("Sample_Api_Query"),
            "GET /services/data/v67.0/query?q=SELECT+Id,Name+FROM+Account+LIMIT+5"),
        new(HistoryTypes.Api, UiText.T("Sample_Api_Tooling"),
            "GET /services/data/v67.0/tooling/query?q=SELECT+Id,Name+FROM+ApexClass+LIMIT+10"),
        new(HistoryTypes.Api, UiText.T("Sample_Api_Recent"),
            "GET /services/data/v67.0/recent"),
        new(HistoryTypes.Api, UiText.T("Sample_Api_Count"),
            "GET /services/data/v67.0/query?q=SELECT+COUNT()+FROM+Account"),
    };
}
