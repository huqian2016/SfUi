namespace SfUi.Core;

/// <summary>
/// ユーザー中心の実効アクセス（プロファイル + 割り当てられた権限セット / 権限セット グループの和集合）を計算する純関数。
/// ObjectPermissions / FieldPermissions は既存の主体単位データ（<see cref="PermissionCatalog"/> /
/// <see cref="ObjectAccessRow"/> / <see cref="FieldAccessSnapshot"/>）を再利用する。
/// </summary>
public static class EffectiveAccessCalculator
{
    /// <summary>
    /// ユーザーの権限主体 Id を解決する（プロファイルの権限セット + 割当 PS + 割当 PSG）。
    /// PSG の構成 PS はカタログ側で和集合済みのため、ここでは PSG の Id のみを追加する。
    /// </summary>
    public static IReadOnlySet<string> ResolveSubjectIds(
        AccessUser user,
        IEnumerable<UserPermissionAssignment> assignments)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(user.ProfileId))
        {
            set.Add(user.ProfileId);
        }

        foreach (var assignment in assignments)
        {
            if (!string.Equals(assignment.UserId, user.Id, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(assignment.PermissionSetId))
            {
                set.Add(assignment.PermissionSetId);
            }

            if (!string.IsNullOrWhiteSpace(assignment.PermissionSetGroupId))
            {
                set.Add(assignment.PermissionSetGroupId!);
            }
        }

        return set;
    }

    /// <summary>主体単位のオブジェクト権限を対象主体で OR 合成する。</summary>
    public static EffectiveObjectAccess ComputeObjectAccess(
        IEnumerable<ObjectAccessRow> rows,
        IReadOnlySet<string> subjectIds)
    {
        var read = false;
        var create = false;
        var edit = false;
        var delete = false;
        var viewAll = false;
        var modifyAll = false;
        var viewAllFields = false;

        foreach (var row in rows)
        {
            if (!subjectIds.Contains(row.Subject.Id))
            {
                continue;
            }

            read |= row.Read;
            create |= row.Create;
            edit |= row.Edit;
            delete |= row.Delete;
            viewAll |= row.ViewAllRecords;
            modifyAll |= row.ModifyAllRecords;
            viewAllFields |= row.ViewAllFields;
        }

        return new EffectiveObjectAccess(read, create, edit, delete, viewAll, modifyAll, viewAllFields);
    }

    /// <summary>主体単位の項目権限を対象主体で OR 合成し、付与元の主体も返す。</summary>
    public static IReadOnlyList<EffectiveFieldAccess> ComputeFieldAccess(
        IReadOnlyList<DataIoField> fields,
        string objectApiName,
        FieldAccessSnapshot snapshot,
        PermissionCatalog catalog,
        IReadOnlySet<string> subjectIds)
    {
        // 付与元は主体の表示順（カタログ順）で安定させる
        var orderedSubjectIds = catalog.Subjects
            .Select(s => s.Id)
            .Where(subjectIds.Contains)
            .Concat(subjectIds.Where(id => catalog.Find(id) is null))
            .ToList();

        var rows = new List<EffectiveFieldAccess>(fields.Count);
        foreach (var field in fields)
        {
            var fullName = objectApiName + "." + field.Name;
            var read = false;
            var edit = false;
            var readGrants = new List<PermissionSubject>();
            var editGrants = new List<PermissionSubject>();

            foreach (var id in orderedSubjectIds)
            {
                if (!snapshot.BySubject.TryGetValue(id, out var map) || !map.TryGetValue(fullName, out var cell))
                {
                    continue;
                }

                if (!cell.Read && !cell.Edit)
                {
                    continue;
                }

                var subject = catalog.Find(id);
                if (subject is null)
                {
                    continue;
                }

                if (cell.Read)
                {
                    read = true;
                    readGrants.Add(subject);
                }

                if (cell.Edit)
                {
                    edit = true;
                    editGrants.Add(subject);
                }
            }

            rows.Add(new EffectiveFieldAccess(field.Name, field.Label, read, edit, readGrants, editGrants));
        }

        return rows;
    }

    /// <summary>1 ユーザー分の実効アクセスを計算する。</summary>
    public static UserEffectiveAccess Compute(
        AccessUser user,
        IReadOnlyList<UserPermissionAssignment> assignments,
        PermissionCatalog catalog,
        IReadOnlyList<ObjectAccessRow> objectRows,
        FieldAccessSnapshot fieldSnapshot,
        IReadOnlyList<DataIoField> fields,
        string objectApiName)
    {
        var subjectIds = ResolveSubjectIds(user, assignments);
        return new UserEffectiveAccess(
            user,
            ComputeObjectAccess(objectRows, subjectIds),
            ComputeFieldAccess(fields, objectApiName, fieldSnapshot, catalog, subjectIds));
    }

    /// <summary>付与元の表示文字列（プロファイル / PS / PSG の区別つき）。</summary>
    public static string FormatGrant(PermissionSubject subject) => subject.Kind switch
    {
        PermissionSubjectKind.Profile => $"Profile: {subject.Label}",
        PermissionSubjectKind.PermissionSetGroup => $"PSG: {subject.Label}",
        _ => $"PS: {subject.Label}",
    };
}
