namespace SfUi.App.ViewModels;

/// <summary>インポート結果の 1 行（行番号・結果・Id・エラー）。</summary>
public sealed class ImportResultRowViewModel
{
    public string RowText { get; init; } = string.Empty;

    public string StatusText { get; init; } = string.Empty;

    public bool Success { get; init; }

    public string? Id { get; init; }

    public string? Error { get; init; }
}
