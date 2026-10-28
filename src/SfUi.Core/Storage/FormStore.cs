namespace SfUi.Core;

/// <summary>
/// ユーザーが作成した帳票定義（data/forms/forms.json）の読み書き。
/// 組み込みテンプレート（<see cref="FormTemplateLibrary"/>）は含めず、複製したものだけを保存する。
/// </summary>
public sealed class FormStore
{
    private readonly string _filePath;
    private readonly AppLog _log;
    private readonly object _gate = new();
    private readonly FormDefinitionDocument _document;

    public FormStore(AppPaths paths, AppLog log)
    {
        _filePath = paths.FormsFile;
        _log = log;
        _document = AtomicJsonFile.Load<FormDefinitionDocument>(_filePath, log);
        _document.Forms ??= new List<FormDefinition>();

        var repaired = false;
        foreach (var form in _document.Forms)
        {
            if (string.IsNullOrWhiteSpace(form.Id))
            {
                form.Id = FormDefinition.NewId();
                repaired = true;
            }

            if (form.CreatedAt == default)
            {
                form.CreatedAt = DateTimeOffset.Now;
                repaired = true;
            }
        }

        if (repaired)
        {
            Save();
        }
    }

    /// <summary>帳票が変更されたときに発火する。</summary>
    public event Action? Changed;

    /// <summary>forms.json のフル パス。</summary>
    public string FilePath => _filePath;

    /// <summary>ユーザー定義の帳票（名前順）。</summary>
    public IReadOnlyList<FormDefinition> Forms
    {
        get
        {
            lock (_gate)
            {
                return _document.Forms.OrderBy(f => f.Name, StringComparer.CurrentCulture).ToList();
            }
        }
    }

    /// <summary>組み込みテンプレート + ユーザー定義（一覧表示用）。</summary>
    public IReadOnlyList<FormDefinition> AllForms
    {
        get
        {
            var list = new List<FormDefinition>();
            list.AddRange(Forms);
            list.AddRange(FormTemplateLibrary.All);
            return list;
        }
    }

    /// <summary>Id で 1 件取得する（組み込みテンプレートも対象）。</summary>
    public FormDefinition? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        lock (_gate)
        {
            var user = _document.Forms.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal));
            if (user is not null)
            {
                return user;
            }
        }

        return FormTemplateLibrary.Find(id);
    }

    /// <summary>追加または更新する（組み込みテンプレートの Id は保存できない）。</summary>
    public FormDefinition Upsert(FormDefinition form)
    {
        ArgumentNullException.ThrowIfNull(form);

        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(form.Id) || FormTemplateLibrary.Find(form.Id) is not null)
            {
                form.Id = FormDefinition.NewId();
            }

            if (form.CreatedAt == default)
            {
                form.CreatedAt = DateTimeOffset.Now;
            }

            form.UpdatedAt = DateTimeOffset.Now;
            form.IsBuiltIn = false;

            var index = _document.Forms.FindIndex(f => string.Equals(f.Id, form.Id, StringComparison.Ordinal));
            if (index >= 0)
            {
                _document.Forms[index] = form;
            }
            else
            {
                _document.Forms.Add(form);
            }
        }

        Save();
        Changed?.Invoke();
        return form;
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
            removed = _document.Forms.RemoveAll(f => string.Equals(f.Id, id, StringComparison.Ordinal)) > 0;
        }

        if (removed)
        {
            Save();
            Changed?.Invoke();
        }

        return removed;
    }

    /// <summary>テンプレートを複製してユーザー定義として保存する（未保存の複製を返す）。</summary>
    public FormDefinition CopyFromTemplate(string? templateId, string? name = null)
    {
        var template = FormTemplateLibrary.Find(templateId)
            ?? throw new ArgumentOutOfRangeException(nameof(templateId), templateId, "unknown form template");

        var copy = template.Clone();
        copy.Id = "";
        copy.Name = string.IsNullOrWhiteSpace(name) ? template.Name : name.Trim();
        return copy;
    }

    /// <summary>テンプレートを複製してそのまま保存する。</summary>
    public FormDefinition CreateFromTemplate(string? templateId, string? name = null) =>
        Upsert(CopyFromTemplate(templateId, name));

    private void Save()
    {
        lock (_gate)
        {
            _document.Version = 1;
            AtomicJsonFile.Save(_filePath, _document, _log);
        }
    }
}
