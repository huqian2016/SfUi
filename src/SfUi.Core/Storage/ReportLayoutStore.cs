namespace SfUi.Core;

/// <summary>
/// 帳票レイアウト定義（data/reports/layouts.json）の読み書き。
/// 保存は <see cref="AtomicJsonFile"/> による原子的書き込み。
/// </summary>
public sealed class ReportLayoutStore
{
    private readonly string _filePath;
    private readonly AppLog _log;
    private readonly object _gate = new();
    private readonly ReportLayoutDocument _document;

    public ReportLayoutStore(AppPaths paths, AppLog log)
    {
        _filePath = paths.ReportLayoutsFile;
        _log = log;
        _document = AtomicJsonFile.Load<ReportLayoutDocument>(_filePath, log);
        _document.Layouts ??= new List<ReportLayoutDefinition>();

        var repaired = false;
        foreach (var layout in _document.Layouts)
        {
            if (string.IsNullOrWhiteSpace(layout.Id))
            {
                layout.Id = ReportLayoutDefinition.NewId();
                repaired = true;
            }

            if (layout.CreatedAt == default)
            {
                layout.CreatedAt = DateTimeOffset.Now;
                repaired = true;
            }
        }

        if (repaired)
        {
            Save();
        }
    }

    /// <summary>レイアウトが変更されたときに発火する。</summary>
    public event Action? Changed;

    /// <summary>layouts.json のフル パス。</summary>
    public string FilePath => _filePath;

    /// <summary>現在のレイアウト一覧（スナップショット・名前順）。</summary>
    public IReadOnlyList<ReportLayoutDefinition> Layouts
    {
        get
        {
            lock (_gate)
            {
                return _document.Layouts
                    .OrderBy(l => l.Name, StringComparer.CurrentCulture)
                    .ToList();
            }
        }
    }

    /// <summary>既定レイアウトの Id（未設定なら先頭）。</summary>
    public string? DefaultLayoutId
    {
        get
        {
            lock (_gate)
            {
                return _document.DefaultLayoutId;
            }
        }
    }

    /// <summary>Id で 1 件取得する。</summary>
    public ReportLayoutDefinition? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        lock (_gate)
        {
            return _document.Layouts.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.Ordinal));
        }
    }

    /// <summary>既定レイアウト（未設定なら名前順の先頭）。</summary>
    public ReportLayoutDefinition? FindDefault()
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(_document.DefaultLayoutId))
            {
                var byId = _document.Layouts.FirstOrDefault(l =>
                    string.Equals(l.Id, _document.DefaultLayoutId, StringComparison.Ordinal));
                if (byId is not null)
                {
                    return byId;
                }
            }

            return _document.Layouts.OrderBy(l => l.Name, StringComparer.CurrentCulture).FirstOrDefault();
        }
    }

    /// <summary>追加または更新する（Id が空なら新しい Id を採番）。</summary>
    public ReportLayoutDefinition Upsert(ReportLayoutDefinition layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(layout.Id))
            {
                layout.Id = ReportLayoutDefinition.NewId();
            }

            if (layout.CreatedAt == default)
            {
                layout.CreatedAt = DateTimeOffset.Now;
            }

            layout.UpdatedAt = DateTimeOffset.Now;

            var index = _document.Layouts.FindIndex(l =>
                string.Equals(l.Id, layout.Id, StringComparison.Ordinal));
            if (index >= 0)
            {
                _document.Layouts[index] = layout;
            }
            else
            {
                _document.Layouts.Add(layout);
            }

            if (string.IsNullOrWhiteSpace(_document.DefaultLayoutId))
            {
                _document.DefaultLayoutId = layout.Id;
            }
        }

        Save();
        Changed?.Invoke();
        return layout;
    }

    /// <summary>既定レイアウトを切り替える。</summary>
    public void SetDefault(string? id)
    {
        lock (_gate)
        {
            _document.DefaultLayoutId = string.IsNullOrWhiteSpace(id) ? null : id;
        }

        Save();
        Changed?.Invoke();
    }

    /// <summary>削除する（存在しなければ false）。</summary>
    public bool Delete(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        bool removed;
        lock (_gate)
        {
            removed = _document.Layouts.RemoveAll(l => string.Equals(l.Id, id, StringComparison.Ordinal)) > 0;
            if (removed && string.Equals(_document.DefaultLayoutId, id, StringComparison.Ordinal))
            {
                _document.DefaultLayoutId = null;
            }
        }

        if (removed)
        {
            Save();
            Changed?.Invoke();
        }

        return removed;
    }

    /// <summary>組み込みの既定レイアウト（未保存でも使えるサンプル）。</summary>
    public static ReportLayoutDefinition CreateDefault(string name) => new()
    {
        Name = name,
        PaperSize = ReportPapers.A4,
        Orientation = "portrait",
        MarginMm = 12,
        Header = new ReportLayoutRegion
        {
            Text = "{Title}\n{Org} / {Date}",
            Align = "left",
            HeightMm = 18,
        },
        Detail = new ReportLayoutRegion { HeightMm = 10, RowHeightMm = 7, Text = "" },
        Summary = new ReportLayoutRegion
        {
            Text = UiText.T("Report_FormSummaryFmt"),
            Align = "right",
            HeightMm = 12,
        },
        Footer = new ReportLayoutRegion
        {
            Text = "{Page} / {Pages}",
            Align = "center",
            HeightMm = 10,
        },
        RepeatHeaderOnPageBreak = true,
    };

    private void Save()
    {
        lock (_gate)
        {
            _document.Version = 1;
            AtomicJsonFile.Save(_filePath, _document, _log);
        }
    }
}
