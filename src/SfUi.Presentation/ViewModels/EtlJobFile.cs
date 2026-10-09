using System.IO;
using System.Text;
using System.Text.Json;
using SfUi.Core;
using SfUi.Etl.Staging;

namespace SfUi.App.ViewModels;

/// <summary>ETL ジョブ定義（data/etl/jobs/&lt;name&gt;.json）。ステップ構成と実行設定を保存する。</summary>
public sealed class EtlJobDefinition
{
    public string Name { get; set; } = string.Empty;

    public string ErrorRateText { get; set; } = "5";

    public int BatchSize { get; set; } = 200;

    public bool RunBackupBefore { get; set; } = true;

    /// <summary>実行後に移行結果を自動検証するか。</summary>
    public bool VerifyAfterRun { get; set; } = true;

    public List<EtlStepDefinition> Steps { get; set; } = new();
}

/// <summary>1 ステップ分の保存内容（EtlStepViewModel と 1:1）。</summary>
public sealed class EtlStepDefinition
{
    public string StepId { get; set; } = string.Empty;

    public string SelectedSourceType { get; set; } = "CSV";

    public string SourcePath { get; set; } = string.Empty;

    public bool SourceHasHeader { get; set; } = true;

    public string ExcelSheet { get; set; } = string.Empty;

    public string XmlRowElement { get; set; } = string.Empty;

    public string SourceDbProvider { get; set; } = "Sqlite";

    public string SourceDbConnectionString { get; set; } = string.Empty;

    public string SourceDbQuery { get; set; } = string.Empty;

    public string SourceRestUrl { get; set; } = string.Empty;

    public string SourceRestAuth { get; set; } = "None";

    public string SourceRestToken { get; set; } = string.Empty;

    public string SourceRestUser { get; set; } = string.Empty;

    public string SourceRestPassword { get; set; } = string.Empty;

    public string SourceRestHeaders { get; set; } = string.Empty;

    public string SourceRestPaging { get; set; } = "None";

    public string SourceSoql { get; set; } = string.Empty;

    public bool SourceUseBulk { get; set; }

    public string DeltaColumn { get; set; } = string.Empty;

    public string DeltaStateKey { get; set; } = string.Empty;

    public string SelectedTargetType { get; set; } = "Salesforce";

    public string ObjectApiName { get; set; } = "Account";

    public string SelectedOp { get; set; } = RowOp.Insert;

    public string MatchKeyField { get; set; } = string.Empty;

    public string OutputPath { get; set; } = string.Empty;

    public string CrosswalkKeyField { get; set; } = string.Empty;

    public string TargetDbProvider { get; set; } = "Sqlite";

    public string TargetDbConnectionString { get; set; } = string.Empty;

    public string TargetDbTable { get; set; } = string.Empty;

    public string TargetDbKeyField { get; set; } = string.Empty;

    public List<EtlMappingDefinition> Mappings { get; set; } = new();
}

/// <summary>マッピング 1 行分の保存内容。</summary>
public sealed class EtlMappingDefinition
{
    public string SourceColumn { get; set; } = string.Empty;

    public string TargetField { get; set; } = string.Empty;

    public string Expression { get; set; } = string.Empty;

    public StagingColumnType Type { get; set; } = StagingColumnType.Text;
}

/// <summary>ジョブのエクスポート ファイル（他環境への受け渡し用。形式マーカー付き）。</summary>
public sealed class EtlJobExportFile
{
    public string Format { get; set; } = EtlJobExport.FormatName;

    public int Version { get; set; } = 1;

    public EtlJobDefinition? Job { get; set; }
}

/// <summary>ジョブのエクスポート / インポート（任意のファイル パス。接続情報は含まれない）。</summary>
public static class EtlJobExport
{
    public const string FormatName = "sfui-etl-job";

    /// <summary>エクスポート用の JSON 文字列を作る。</summary>
    public static string Serialize(EtlJobDefinition job)
        => JsonSerializer.Serialize(new EtlJobExportFile { Job = job }, AtomicJsonFile.Options);

    /// <summary>ファイルへ書き出す（UTF-8 BOM なし）。</summary>
    public static void Export(EtlJobDefinition job, string filePath)
        => File.WriteAllText(filePath, Serialize(job), new UTF8Encoding(false));

    /// <summary>
    /// エクスポート ファイルを読み込む（旧形式の素のジョブ JSON も受け付ける）。
    /// ジョブとして解釈できない場合は <see cref="InvalidDataException"/>。
    /// </summary>
    public static EtlJobDefinition Import(string filePath)
    {
        var text = File.ReadAllText(filePath);

        EtlJobDefinition? job = null;
        try
        {
            var wrapper = JsonSerializer.Deserialize<EtlJobExportFile>(text, AtomicJsonFile.Options);
            if (wrapper?.Job is not null && wrapper.Job.Steps.Count > 0)
            {
                job = wrapper.Job;
            }
        }
        catch (JsonException)
        {
            // 素のジョブ JSON として再試行する
        }

        if (job is null)
        {
            try
            {
                var plain = JsonSerializer.Deserialize<EtlJobDefinition>(text, AtomicJsonFile.Options);
                if (plain is not null && plain.Steps.Count > 0)
                {
                    job = plain;
                }
            }
            catch (JsonException)
            {
            }
        }

        if (job is null)
        {
            throw new InvalidDataException("Not an ETL job file.");
        }

        return job;
    }
}

/// <summary>ジョブ ファイルの読み書き（data/etl/jobs/&lt;name&gt;.json、AtomicJsonFile で原子的に保存）。</summary>
public static class EtlJobStore
{
    /// <summary>保存済みジョブ名の一覧（delta の .state.json は除外、名前順）。</summary>
    public static IReadOnlyList<string> List(AppPaths paths)
    {
        var names = new List<string>();
        if (Directory.Exists(paths.EtlJobsRoot))
        {
            foreach (var file in Directory.EnumerateFiles(paths.EtlJobsRoot, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrWhiteSpace(name) && !name.EndsWith(".state", StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(name);
                }
            }
        }

        names.Sort(StringComparer.CurrentCulture);
        return names;
    }

    /// <summary>ジョブ名からファイル パスを作る（名前はファイル名として安全化する）。</summary>
    public static string FilePath(AppPaths paths, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return Path.Combine(paths.EtlJobsRoot, (safe.Length == 0 ? "job" : safe) + ".json");
    }

    public static EtlJobDefinition Load(AppPaths paths, string name, AppLog? log = null)
        => AtomicJsonFile.Load<EtlJobDefinition>(FilePath(paths, name), log);

    public static void Save(AppPaths paths, EtlJobDefinition job, AppLog? log = null)
        => AtomicJsonFile.Save(FilePath(paths, job.Name), job, log);

    public static bool Delete(AppPaths paths, string name)
    {
        var path = FilePath(paths, name);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        if (File.Exists(path + ".bak"))
        {
            File.Delete(path + ".bak");
        }

        return true;
    }
}
