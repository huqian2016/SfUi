namespace SfUi.Core;

/// <summary>ER 図出力ダイアログで前回選んだ設定（形式・表示名・オプション・出力先）。</summary>
public sealed class ErdExportDefaults
{
    public ErdNameStyle NameStyle { get; set; } = ErdNameStyle.LabelAndApiName;

    public bool IncludeStandardObjects { get; set; } = true;

    public bool IncludeReferencedParents { get; set; } = true;

    public bool Mermaid { get; set; } = true;

    public bool Dbml { get; set; }

    public bool Drawio { get; set; }

    public bool PlantUml { get; set; }

    public bool Dot { get; set; }

    public bool Html { get; set; }

    public bool Svg { get; set; } = true;

    public bool Png { get; set; }

    public bool Jpeg { get; set; }

    public bool Webp { get; set; }

    public bool OpenAfterSave { get; set; }

    /// <summary>前回の出力先フォルダー（未設定なら null）。</summary>
    public string? OutputDirectory { get; set; }
}

/// <summary>レポート出力ダイアログで前回選んだ設定（形式・分析・行数・出力先）。</summary>
public sealed class ReportExportDefaults
{
    public bool Csv { get; set; } = true;

    public bool Markdown { get; set; }

    public bool Html { get; set; }

    public bool Excel { get; set; }

    public bool Pdf { get; set; }

    public bool Tsv { get; set; }

    public bool Json { get; set; }

    public bool JsonLines { get; set; }

    public bool Xml { get; set; }

    public bool Yaml { get; set; }

    public bool Png { get; set; }

    public bool Jpeg { get; set; }

    public bool IncludeAnalysis { get; set; } = true;

    /// <summary>表ごとの最大行数（0 = 制限なし）。</summary>
    public int MaxRows { get; set; } = 5000;

    public bool OpenAfterSave { get; set; } = true;

    /// <summary>前回の出力先フォルダー（未設定なら null）。</summary>
    public string? OutputDirectory { get; set; }
}

/// <summary>出力ダイアログの設定一式（data/export-settings.json のルート）。</summary>
public sealed class ExportSettings
{
    public int SchemaVersion { get; set; } = ExportSettingsStore.CurrentSchemaVersion;

    public ErdExportDefaults Erd { get; set; } = new();

    public ReportExportDefaults Report { get; set; } = new();
}

/// <summary>
/// 出力ダイアログ（ER 図 / レポート）の設定を data/export-settings.json へ保存する。
/// 次回起動時に同じ形式・出力先で開けるよう、ダイアログを閉じたタイミングで保存する。
/// </summary>
public sealed class ExportSettingsStore
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>最大行数の上限（入力ミスで極端な値を保存しないための安全弁）。</summary>
    public const int MaxRowsLimit = 1_000_000;

    private readonly string _filePath;
    private readonly AppLog _log;
    private readonly object _sync = new();

    public ExportSettingsStore(AppPaths paths, AppLog log)
    {
        _filePath = Path.Combine(paths.DataRoot, "export-settings.json");
        _log = log;
    }

    public string FilePath => _filePath;

    /// <summary>設定を読み込む（無い・壊れている・スキーマ不一致は既定値）。</summary>
    public ExportSettings Load()
    {
        lock (_sync)
        {
            var settings = AtomicJsonFile.Load<ExportSettings>(_filePath, _log);
            if (settings.SchemaVersion != CurrentSchemaVersion)
            {
                _log.Info("出力設定: スキーマが古いため既定値からやり直します");
                settings = new ExportSettings();
            }

            Sanitize(settings);
            return settings;
        }
    }

    /// <summary>設定を保存する（保存前に値を正規化する）。</summary>
    public void Save(ExportSettings settings)
    {
        lock (_sync)
        {
            Sanitize(settings);
            AtomicJsonFile.Save(_filePath, settings, _log);
        }
    }

    public ErdExportDefaults LoadErd() => Load().Erd;

    public ReportExportDefaults LoadReport() => Load().Report;

    /// <summary>ER 図出力の設定だけを更新して保存する。</summary>
    public void SaveErd(ErdExportDefaults defaults)
    {
        var settings = Load();
        settings.Erd = defaults;
        Save(settings);
    }

    /// <summary>レポート出力の設定だけを更新して保存する。</summary>
    public void SaveReport(ReportExportDefaults defaults)
    {
        var settings = Load();
        settings.Report = defaults;
        Save(settings);
    }

    private static void Sanitize(ExportSettings settings)
    {
        settings.SchemaVersion = CurrentSchemaVersion;
        settings.Erd ??= new ErdExportDefaults();
        settings.Report ??= new ReportExportDefaults();

        if (!Enum.IsDefined(settings.Erd.NameStyle))
        {
            settings.Erd.NameStyle = ErdNameStyle.LabelAndApiName;
        }

        settings.Erd.OutputDirectory = NormalizeDirectory(settings.Erd.OutputDirectory);
        settings.Report.OutputDirectory = NormalizeDirectory(settings.Report.OutputDirectory);
        settings.Report.MaxRows = Math.Clamp(settings.Report.MaxRows, 0, MaxRowsLimit);
    }

    private static string? NormalizeDirectory(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
