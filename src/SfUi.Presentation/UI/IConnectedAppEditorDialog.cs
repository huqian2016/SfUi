using SfUi.Core;

namespace SfUi.Presentation;

/// <summary>接続アプリ エディタを開くための入力。</summary>
public sealed class ConnectedAppEditorRequest
{
    public string Kind { get; init; } = ConnectedAppKinds.ConnectedApp;

    public string OrgDisplayName { get; init; } = "";

    /// <summary>編集対象（新規作成時は null）。</summary>
    public ConnectedAppDocument? Document { get; init; }

    public bool IsNew => Document is null;
}

/// <summary>接続アプリ エディタの結果（ドラフト + 検証のみか）。</summary>
public sealed record ConnectedAppEditorResult(ConnectedAppDraft Draft, bool DryRun);

/// <summary>接続アプリの作成 / 編集ダイアログ。</summary>
public interface IConnectedAppEditorDialog
{
    Task<ConnectedAppEditorResult?> EditAsync(ConnectedAppEditorRequest request);
}
