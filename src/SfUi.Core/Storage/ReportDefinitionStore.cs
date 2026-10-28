namespace SfUi.Core;

/// <summary>reports.json のルート要素。</summary>
public sealed class ReportDefinitionDocument
{
    public int Version { get; set; } = 1;

    public List<ReportDefinition> Reports { get; set; } = new();
}

/// <summary>
/// ローカル レポート定義（data/reports/reports.json）の読み書き。
/// 保存は <see cref="AtomicJsonFile"/> による原子的書き込み。
/// </summary>
public sealed class ReportDefinitionStore
{
    private readonly string _filePath;
    private readonly AppLog _log;
    private readonly object _gate = new();
    private readonly ReportDefinitionDocument _document;

    public ReportDefinitionStore(AppPaths paths, AppLog log)
    {
        _filePath = paths.ReportsFile;
        _log = log;
        _document = AtomicJsonFile.Load<ReportDefinitionDocument>(_filePath, log);
        _document.Reports ??= new List<ReportDefinition>();

        var repaired = false;
        foreach (var report in _document.Reports)
        {
            if (string.IsNullOrWhiteSpace(report.Id))
            {
                report.Id = ReportDefinition.NewId();
                repaired = true;
            }

            if (report.CreatedAt == default)
            {
                report.CreatedAt = DateTimeOffset.Now;
                repaired = true;
            }
        }

        if (repaired)
        {
            Save();
        }
    }

    /// <summary>レポート定義が変更されたときに発火する。</summary>
    public event Action? Changed;

    /// <summary>reports.json のフル パス。</summary>
    public string FilePath => _filePath;

    /// <summary>現在のレポート一覧（スナップショット・名前順）。</summary>
    public IReadOnlyList<ReportDefinition> Reports
    {
        get
        {
            lock (_gate)
            {
                return _document.Reports
                    .OrderBy(r => r.DisplayName, StringComparer.CurrentCulture)
                    .ToList();
            }
        }
    }

    /// <summary>Id で 1 件取得する。</summary>
    public ReportDefinition? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        lock (_gate)
        {
            return _document.Reports.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal));
        }
    }

    /// <summary>追加または更新する（Id が空なら新しい Id を採番）。</summary>
    public ReportDefinition Upsert(ReportDefinition report)
    {
        ArgumentNullException.ThrowIfNull(report);

        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(report.Id))
            {
                report.Id = ReportDefinition.NewId();
            }

            if (report.CreatedAt == default)
            {
                report.CreatedAt = DateTimeOffset.Now;
            }

            report.UpdatedAt = DateTimeOffset.Now;

            var index = _document.Reports.FindIndex(r => string.Equals(r.Id, report.Id, StringComparison.Ordinal));
            if (index >= 0)
            {
                _document.Reports[index] = report;
            }
            else
            {
                _document.Reports.Add(report);
            }

            Save();
        }

        return report;
    }

    /// <summary>削除する（存在しなければ false）。</summary>
    public bool Delete(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        lock (_gate)
        {
            var removed = _document.Reports.RemoveAll(r => string.Equals(r.Id, id, StringComparison.Ordinal));
            if (removed == 0)
            {
                return false;
            }

            Save();
            return true;
        }
    }

    private void Save()
    {
        AtomicJsonFile.Save(_filePath, _document, _log);
        Changed?.Invoke();
    }
}
