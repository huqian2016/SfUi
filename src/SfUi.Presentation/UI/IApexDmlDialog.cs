namespace SfUi.Presentation;

/// <summary>DML 文の生成ダイアログを開くための入力。</summary>
public sealed record ApexDmlDialogRequest(string TargetOrg, string OrgDisplayName = "");

/// <summary>匿名 Apex へ挿入する DML 文（Insert / Update / Delete）を生成するダイアログ。</summary>
public interface IApexDmlDialog
{
    /// <summary>ダイアログを表示し、生成された Apex を返す（キャンセル時は null）。</summary>
    Task<string?> GenerateAsync(ApexDmlDialogRequest request);
}
