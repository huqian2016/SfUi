using SfUi.Core;

namespace SfUi.Etl.Engine;

/// <summary>
/// delta（差分）モードの watermark 永続化（data/etl/jobs/&lt;key&gt;.state.json）。
/// ジョブ単位で run 削除の影響を受けない。ファイルは AtomicJsonFile で原子的に書き込む。
/// </summary>
public static class DeltaState
{
    /// <summary>状態ファイルの中身（ステップ ID → watermark）。</summary>
    public sealed class StateFile
    {
        public Dictionary<string, StepState> Steps { get; set; } = new(StringComparer.Ordinal);
    }

    /// <summary>1 ステップ分の状態。</summary>
    public sealed class StepState
    {
        public DateTimeOffset Watermark { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }

    /// <summary>キーから状態ファイル パスを作る（キーはファイル名として安全化する）。</summary>
    public static string FilePath(string jobsRoot, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobsRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(key.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return Path.Combine(jobsRoot, (safe.Length == 0 ? "default" : safe) + ".state.json");
    }

    /// <summary>ステップの watermark を読む（未保存・破損時は null）。</summary>
    public static DateTimeOffset? GetWatermark(string filePath, string stepId, AppLog? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(stepId);

        var state = AtomicJsonFile.Load<StateFile>(filePath, log);
        return state.Steps.TryGetValue(stepId, out var step) ? step.Watermark : null;
    }

    /// <summary>ステップの watermark を保存する（他ステップの値は保持）。</summary>
    public static void SetWatermark(string filePath, string stepId, DateTimeOffset watermark, AppLog? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(stepId);

        var state = AtomicJsonFile.Load<StateFile>(filePath, log);
        state.Steps[stepId] = new StepState { Watermark = watermark, UpdatedAt = DateTimeOffset.Now };
        AtomicJsonFile.Save(filePath, state, log);
    }
}
