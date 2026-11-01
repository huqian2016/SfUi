# 接続アプリ（Connected App / External Client App）対応 設計書

最終更新: 2026-11-01 / ステータス: **P1 実装完了（接続アプリ + 外部クライアント アプリ、WPF / Avalonia）**

対象リポジトリ: `c:\huqian\vscode\SfUi`

---

## 1. 目的

Salesforce の接続アプリを SfUi から **閲覧・編集・新規作成** できるようにする。

| 項目 | 内容 |
| --- | --- |
| 閲覧 | 接続アプリの一覧 + 詳細（OAuth 設定・スコープ・コールバック URL・ポリシー・consumer key） |
| 編集 | 既存アプリの設定変更 → Metadata API でデプロイ |
| 新規作成 | 新規アプリの作成 → Metadata API でデプロイ |
| 対象シェル | WPF + Avalonia（同一 ViewModel） |

---

## 2. 調査結果（重要）

### 2.1 接続アプリの新規作成は Salesforce 側で制限されている（実測）

`hks4sand1`（API 67.0）に対して Metadata API で `ConnectedApp` を **dry-run デプロイ**した結果:

```
You can't create a connected app. To enable connected app creation,
contact Salesforce Customer Support.
```

- Salesforce の Metadata API リファレンスにも明記:
  > Connected apps creation is restricted as of Spring '26. … If you must continue creating connected apps, contact Salesforce Support.
  > 推奨は **External Client Apps（外部クライアント アプリ）**。
- **既存の接続アプリの参照・更新は引き続き可能**（閲覧・編集は成立する）。
- したがって「接続アプリの新規作成」は、Support で作成を許可された組織でのみ成功する。

### 2.2 外部クライアント アプリ（ECA）は作成できる（実測）

同じ組織で `ExternalClientApplication` の **dry-run デプロイが成功**（`distributionState=Local`）。

| メタデータ型 | ファイル | ディレクトリ | 役割 |
| --- | --- | --- | --- |
| `ExternalClientApplication` | `*.eca` | `externalClientApps/` | アプリ本体（label / contactEmail / description / distributionState） |
| `ExtlClntAppGlobalOauthSettings` | `*.ecaGlblOauth` | `extlClntAppGlobalOauthSets/` | **OAuth の機密設定**（callbackUrl / scopes / consumerKey / consumerSecret） |
| `ExtlClntAppOauthSettings` | `*.ecaOauth` | `extlClntAppOauthSettings/` | OAuth プラグイン設定（label / externalClientApplication / scopes / IP 範囲） |
| `ExtlClntAppOauthConfigurablePolicies` | `*.ecaOauthPlcy` | `extlClntAppOauthPolicies/` | 管理者ポリシー（refreshToken / ipRelaxation / permittedUsers / session） |
| `ExtlClntAppConfigurablePolicies` | `*.ecaPlcy` | `extlClntAppPolicies/` | プラグインの有効/無効 |

> ECA のメタデータ型を扱うには組織で **「Opt in to External Client Apps」** の有効化が必要（実測組織は有効）。
> グローバル OAuth 設定には consumer secret が含まれるため、**SfUi はリポジトリ/ログに保存しない**（アプリ内の一時フォルダーのみ、権限を絞る）。

### 2.3 既存の SfUi 実装（再利用できるもの）

| 機能 | 場所 | 状態 |
| --- | --- | --- |
| 組織情報の「接続アプリ」タブ | `OrgInfoSection.ConnectedApps` / `OrgInfoService.FetchConnectedAppsAsync` | **REST `ConnectedApplication` の読み取りのみ**（Name / Created / LastModified）。実測では Ant Migration Tool など**システム標準の 8 件しか返らない**（カスタム接続アプリはメタデータ側にしか無い） |
| メタデータ取得 | `ScratchResourceService.RetrieveAsync`（`sf project retrieve start`） | 再利用可 |
| メタデータ デプロイ | `ScratchOrgService.BuildDeployArgs` / `SourceEditorService`（`sf project deploy start`） | 再利用可 |
| コマンド実行 | `SfCliRunner`（`--json`、タイムアウト、ログ） | 再利用可 |
| 設定の秘匿 | `CredentialProtector`（DPAPI / AES-GCM） | 再利用可（consumer secret を保存する場合） |

---

## 3. 設計

### 3.1 画面構成

上部バー **「接続アプリ」** → 独立ウィンドウ（`ConnectedAppsWindow`）。3 タブ:

```
┌ 接続アプリ ────────────────────────────────────────────────┐
│ [接続アプリ] [外部クライアント アプリ] [履歴]                  │
│ ┌ 一覧 ─────────────┐ ┌ 詳細 ───────────────────────────┐ │
│ │ 名前 / ラベル / 更新日 │ │ 基本情報・OAuth・ポリシー・XML     │ │
│ │                     │ │                                │ │
│ └────────────────────┘ └────────────────────────────────┘ │
│ [新規作成] [編集] [削除] [↗ Setup で開く] [再読み込み]        │
└────────────────────────────────────────────────────────────┘
```

- **接続アプリ**タブ: Metadata API で `ConnectedApp` を全件取得して一覧化（`sf project retrieve start --metadata ConnectedApp --output-dir <tmp>`）。
  - 詳細は取得した XML を解析 + REST の `ConnectedApplication`（Options* 項目）で補完。
  - **新規作成ボタン**は押せるが、組織が制限されている場合は Salesforce のエラーをそのまま表示し、**「外部クライアント アプリで作成」へ誘導**する。
- **外部クライアント アプリ**タブ: `ExternalClientApplication` と関連 4 型をまとめて取得し、アプリ単位で 1 行に集約。
  - **新規作成・編集・削除**が可能（OAuth 設定・ポリシー含む）。
- **履歴**タブ: デプロイ結果（成功/失敗、コンポーネント、メッセージ）を保持。

### 3.2 エディタ（共通フォーム + XML タブ）

| セクション | 項目 |
| --- | --- |
| 基本 | 名前（API 名・変更不可） / ラベル / 連絡先メール / 説明 / 情報 URL / ロゴ URL / 開始 URL / モバイル開始 URL |
| OAuth | コールバック URL（複数行） / スコープ（チェックボックス: Api / Web / Full / RefreshToken / OpenID / Profile / Email / Address / Phone / Chatter / CustomApplications / CustomPermissions） / consumer key（表示のみ） / シークレット（表示のみ・マスク） / consumer secret 必須 / PKCE 必須 / リフレッシュ トークン ローテーション |
| ポリシー | IP 緩和（ENFORCE / BYPASS / BYPASS_2FACTOR / ENFORCE_RELAXREFRESH） / リフレッシュ トークン ポリシー（infinite / 有効期限） / 許可ユーザー（全員 / 管理者承認） / セッション タイムアウト |
| XML | **生のメタデータ XML を直接編集**（上級者向け。フォームで扱わない項目を保持・追加できる） |

> 編集は「取得した XML を保持し、フォームで変更したノードだけ差し替える」方式にする（未知の項目を失わない）。
> XML タブで直接編集した場合はその内容をそのままデプロイする。

### 3.3 Core（`src/SfUi.Core/ConnectedApps/`）

```csharp
public static class ConnectedAppKinds { public const string ConnectedApp = "connectedapp"; public const string ExternalClientApp = "eca"; }

public sealed class ConnectedAppSummary { string Kind; string Name; string Label; string? Description; DateTimeOffset? RetrievedAt; }
public sealed class ConnectedAppDocument { string Kind; string Name; string Xml; IReadOnlyDictionary<string,string> Files; }  // ECA は複数ファイル
public sealed record ConnectedAppDeployResult(bool Success, string Status, IReadOnlyList<ConnectedAppDeployProblem> Problems, string Output, string? DeployUrl);

public sealed class ConnectedAppService
{
    Task<IReadOnlyList<ConnectedAppSummary>> ListAsync(OrgInfo org, string kind, CancellationToken ct);
    Task<ConnectedAppDocument> GetAsync(OrgInfo org, string kind, string name, CancellationToken ct);
    Task<ConnectedAppDeployResult> DeployAsync(OrgInfo org, ConnectedAppDocument doc, bool dryRun, CancellationToken ct);
    Task<ConnectedAppDeployResult> DeleteAsync(OrgInfo org, string kind, string name, CancellationToken ct);
}
```

- 実装は一時フォルダーに `sfdx-project.json` + `force-app/main/default/...` を作り、`SfCliRunner` で
  `sf project retrieve start --metadata <Type>:<Name>` / `sf project deploy start --source-dir ... --target-org ... --json`
  を実行。削除は `destructiveChanges/destructiveChanges.xml` + `--pre-destructive-changes`。
- **`--json` 出力の解析**: `status` / `result.details.componentFailures[].problem` / `deployUrl`。
- タイムアウト: 取得 10 分 / デプロイ 20 分。ログは `AppLog` に。
- XML 解析は `XDocument`（名前空間 `http://soap.sforce.com/2006/04/metadata` を考慮）。

### 3.4 Presentation

| クラス | 役割 |
| --- | --- |
| `ConnectedAppsViewModel` | 組織・種別・一覧・検索・選択・詳細・作成/編集/削除コマンド・状態メッセージ |
| `ConnectedAppEditorViewModel` | フォーム項目、スコープのチェックボックス、XML タブ、検証（必須: 名前・ラベル・連絡先メール・コールバック URL）、デプロイ前プレビュー |

ダイアログ抽象: `IConnectedAppEditorDialog`（WPF/Avalonia 実装）。

### 3.5 ローカライズ（`Conn_*` 60 キー前後 × 4 言語）

`Conn_Title` / `Conn_TabConnectedApps` / `Conn_TabEca` / `Conn_New` / `Conn_Edit` / `Conn_Delete` / `Conn_DeleteConfirm` / `Conn_DeployDryRun` / `Conn_DeploySucceeded` / `Conn_DeployFailedFmt` / `Conn_ErrorCreationRestricted`（Spring '26 制限の説明と ECA への誘導）/ `Conn_LabelCallbackUrls` / `Conn_LabelScopes` / `Conn_ConsumerKeyReadOnly` …

### 3.6 安全性

- 削除は `IDialogService.ConfirmDestructive` で二重確認（API 名を入力させる）。
- 既存の `ConfirmPolicy` / `ConfirmPolicies` に従う。
- consumer secret は**画面に表示しない**（Setup へのリンクのみ）。ログにも出さない（`CredentialMask`）。
- デプロイは **dry-run（検証のみ）を先に実行**できるようにする。

---

## 4. テスト計画

| 種別 | 内容 |
| --- | --- |
| 単体 | `ConnectedAppXml`（XML 生成・解析・未知ノード保持・スコープ整形）、`ConnectedAppService`（デプロイ結果 JSON の解析、エラー抽出、破壊的変更 XML 生成）を `HttpMessageHandler`/フェイク `SfCliRunner` で検証 |
| VM | 一覧・検索・作成/編集の検証エラー・XML タブ制御・制限エラーのメッセージ変換 |
| E2E | 実組織（`hks4sand1`）で **ECA の作成 → 一覧に出る → 編集（ラベル変更）→ 削除** を WPF/Avalonia 両方で確認。接続アプリは既存 1 件の閲覧 + 編集（ラベル変更して戻す）を確認 |

---

## 5. フェーズ

| フェーズ | 内容 | 目安 |
| --- | --- | --- |
| **P1** | Core（retrieve/deploy/parse/delete）+ 接続アプリ タブ（一覧・詳細・編集）+ ECA タブ（一覧・詳細・編集・新規作成）+ 両シェル + ローカライズ + テスト | 3〜4 日 |
| **P2** | XML タブ / dry-run プレビュー / 削除 / 履歴タブ / Org Info タブからの導線 / マニュアル | 1〜2 日 |
| **P3** | 参照関係（接続アプリを使う権限セット・プロファイルの表示）/ 差分表示 / 別組織へのコピー | 1〜2 日 |

---

## 6. 実装状況（2026-11-01）

| 項目 | 実装 |
| --- | --- |
| Core | `src/SfUi.Core/ConnectedApps/`（`ConnectedAppModels` / `ConnectedAppXml` / `ConnectedAppService`）。取得は `sf project retrieve start --target-org … --output-dir …`、作成・更新は一時 SFDX プロジェクトからの `sf project deploy start`、削除は `--pre-destructive-changes` |
| ViewModel | `ConnectedAppsViewModel`（一覧・検索・作成・編集・削除・dry-run・作成制限の案内）、`ConnectedAppEditorViewModel`（フォーム + XML タブ、スコープ、検証） |
| WPF | `Views/ConnectedAppsWindow.xaml` / `Views/ConnectedAppEditorDialog.xaml`、`WpfConnectedAppEditor`、ツールバー `OpenConnectedApps` |
| Avalonia | `Views/ConnectedAppsWindow.axaml` / `Dialogs/ConnectedAppEditorDialog.axaml`、`AvaloniaConnectedAppEditor`、同じ AutomationId |
| ローカライズ | `Conn_*` 66 キー + `Main_ConnectedApps*` × 4 言語 |
| テスト | `ConnectedAppTests`（21 件）。合計 1,419 |
| UI E2E | `C:\huqian\sfui-connected-app-e2e.ps1`（WPF・実組織）**12/12**: 外部クライアント アプリの作成 → 詳細確認 → 編集 → 削除。`-avalonia.ps1` **6/6**（一覧・エディタ開閉） |

### 決定事項（確定）

- 対象は **接続アプリ + 外部クライアント アプリ** の両方（接続アプリの新規作成は Salesforce 制限のため、エラー時に案内を表示）
- エディタは **主要項目のフォーム + 生 XML タブ**
- **削除**も P1 に含める（二重確認 + destructiveChanges）

---

## 7. 決定が必要な点（履歴）

1. **ECA（外部クライアント アプリ）も対象に含めるか** — 含めない場合、現行組織では新規作成ができません（Salesforce の制限）。
2. **編集可能な項目の範囲** — 主要項目 + 生 XML（推奨）か、全項目をフォーム化するか。
3. **削除機能** — 必要か（破壊的操作のため P2 送りでも可）。
