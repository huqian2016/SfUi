using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SfUi.Core;

/// <summary>
/// JSON ファイルの読み書きユーティリティ。
/// - 保存は一時ファイル → File.Replace による原子的書き込み（直前の内容は .bak に退避）
/// - 読み込み失敗時は .bak から復元し、それも失敗した場合は破損ファイルを .bad-* に退避して既定値を返す
/// </summary>
public static class AtomicJsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>ファイルを読み込む。存在しない・破損している場合は .bak → 既定値の順でフォールバックする。</summary>
    public static T Load<T>(string filePath, AppLog? log = null)
        where T : class, new()
    {
        var result = TryLoad<T>(filePath, log);
        if (result is not null)
        {
            return result;
        }

        if (File.Exists(filePath))
        {
            var backupPath = filePath + ".bak";
            var fromBackup = TryLoad<T>(backupPath, log);
            if (fromBackup is not null)
            {
                log?.Warn($"{filePath} を解析できないため {backupPath} から復元しました。");
                return fromBackup;
            }

            try
            {
                var badPath = filePath + $".bad-{DateTime.Now:yyyyMMddHHmmss}";
                File.Move(filePath, badPath, overwrite: true);
                log?.Warn($"{filePath} を解析できないため {badPath} に退避し、初期値で続行します。");
            }
            catch (Exception ex)
            {
                log?.Warn($"{filePath} の退避に失敗: {ex.Message}");
            }
        }

        return new T();
    }

    /// <summary>原子的に保存する（一時ファイル → File.Replace、既存内容は .bak へ）。</summary>
    public static void Save<T>(string filePath, T value, AppLog? log = null)
        where T : class
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = filePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(value, Options), Encoding.UTF8);

            if (File.Exists(filePath))
            {
                File.Replace(tempPath, filePath, filePath + ".bak");
            }
            else
            {
                File.Move(tempPath, filePath);
            }
        }
        catch (Exception ex)
        {
            log?.Error($"ファイルの保存に失敗しました: {filePath}", ex);
        }
    }

    private static T? TryLoad<T>(string filePath, AppLog? log)
        where T : class
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            var json = File.ReadAllText(filePath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (Exception ex)
        {
            log?.Warn($"{filePath} の読み込みに失敗: {ex.Message}");
            return null;
        }
    }
}
