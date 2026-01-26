# Header/Body 機能 実装要約

## 概要

テキストエディタにHeader（プロジェクト設定）とBody（編集操作）の概念を追加しました。これにより、プロジェクト全体の設定と個々の編集操作を明確に分離できます。

---

## 1. 基本構造

### 1.1 テキストフォーマット

```
[Header セクション]
PROJECT "プロジェクト名"
RESOLUTION 1920x1080
FRAMERATE 30
DEFAULT_FONT "メイリオ"
DEFAULT_FONT_SIZE 24
DEFAULT_TITLE_COLOR #FFFFFF
DEFAULT_SUBTITLE_COLOR #FFFFFF
DEFAULT_BACKGROUND_ALPHA 0.8
===
[Body セクション]
LOAD video.mp4
CUT 00:00:30.000
--- [00:00:00.000 -> 00:00:10.000] ---
# タイトル
> 字幕テキスト
```

### 1.2 区切り文字

- **`===`**: Header と Body の境界を示す
- 3つ以上の `=` 記号で構成
- この行より前がHeader、後がBody

---

## 2. Header コマンド一覧

### 2.1 プロジェクト設定

| コマンド | 構文 | デフォルト値 | 説明 |
|---------|------|------------|------|
| **PROJECT** | `PROJECT "<プロジェクト名>"` | "無題のプロジェクト" | プロジェクト名 |
| **RESOLUTION** | `RESOLUTION <幅>x<高さ>` | 1920x1080 | 出力解像度 |
| **FRAMERATE** | `FRAMERATE <fps>` | 30 | フレームレート |

### 2.2 デフォルトスタイル設定

| コマンド | 構文 | デフォルト値 | 説明 |
|---------|------|------------|------|
| **DEFAULT_FONT** | `DEFAULT_FONT "<フォント名>"` | "メイリオ" | フォント |
| **DEFAULT_FONT_SIZE** | `DEFAULT_FONT_SIZE <サイズ>` | 24 | フォントサイズ |
| **DEFAULT_TITLE_COLOR** | `DEFAULT_TITLE_COLOR #RRGGBB` | #FFFFFF | 題名の色 |
| **DEFAULT_SUBTITLE_COLOR** | `DEFAULT_SUBTITLE_COLOR #RRGGBB` | #FFFFFF | 字幕の色 |
| **DEFAULT_BACKGROUND_ALPHA** | `DEFAULT_BACKGROUND_ALPHA <0.0-1.0>` | 0.8 | 背景の透明度 |

---

## 3. 完全な使用例

```
PROJECT "作業手順マニュアル動画"
RESOLUTION 1920x1080
FRAMERATE 30
DEFAULT_FONT "メイリオ"
DEFAULT_FONT_SIZE 28
DEFAULT_TITLE_COLOR #FFFF00
DEFAULT_SUBTITLE_COLOR #FFFFFF
DEFAULT_BACKGROUND_ALPHA 0.7
===

LOAD C:\Videos\work_process.mp4

CUT 00:00:15.000
CUT 00:00:45.000
CUT 00:01:30.000

HIDE 00:00:45.000 00:01:00.000

--- [00:00:00.000 -> 00:00:15.000] ---
# オープニング
> 本日の作業内容を説明します

--- [00:00:15.000 -> 00:00:45.000] ---
# 工程1: 準備
> 工具を準備します
* スパナ: 10mm, 12mm
* ドライバー: プラス2番

--- [00:01:30.000 -> 00:02:00.000] ---
# 工程2: 分解
> ボルトを緩めます
* 時計回りに回転
* 力を入れすぎない
```

---

## 4. 実装コンポーネント

### 4.1 新規作成ファイル

| ファイル | 役割 |
|---------|------|
| `Shared/Domain/ProjectConfig.cs` | プロジェクト設定のデータモデル |
| `Shared/Infra/IHeaderParser.cs` | Header パーサーのインターフェース |
| `Shared/Infra/HeaderParser.cs` | Header パーサーの実装 |

### 4.2 修正ファイル

| ファイル | 修正内容 |
|---------|---------|
| `Features/Editor/EditorViewModel.cs` | Header/Bodyの分割処理を追加 |
| `Features/Timeline/TimelineViewModel.cs` | ProjectConfigプロパティを追加 |

---

## 5. データフロー

```
[ユーザーがテキスト入力]
        ↓
[Text.Value 変更]
        ↓
[Throttle 500ms]
        ↓
[OnTextChanged()]
        ↓
┌───────┴───────┐
│ テキスト分割   │
│ Header / Body │
└───┬───────┬───┘
    │       │
    ↓       ↓
[Header]  [Body]
    ↓       ↓
[HeaderParser]  [CommandParser/SceneParser]
    ↓             ↓
[ProjectConfig]  [EditCommand/SceneBlock]
    ↓             ↓
[UI設定適用]  [VideoSegmentManager]
                  ↓
              [FilmStripView]
```

---

## 6. ProjectConfig の使用方法

### 6.1 ViewModelでの取得

```csharp
// EditorViewModel でプロジェクト設定を取得
var projectName = editorViewModel.ProjectConfig.Value.ProjectName;
var resolution = $"{editorViewModel.ProjectConfig.Value.ResolutionWidth}x{editorViewModel.ProjectConfig.Value.ResolutionHeight}";
```

### 6.2 バインディング（XAML）

```xml
<TextBlock Text="{Binding EditorViewModel.ProjectConfig.Value.ProjectName}"/>
<TextBlock Text="{Binding EditorViewModel.ProjectConfig.Value.ResolutionWidth}"/>
```

### 6.3 変更通知

```csharp
// ProjectConfigの変更を監視
editorViewModel.ProjectConfig.Subscribe(config =>
{
    // 設定が変更された時の処理
    ApplyProjectSettings(config);
});
```

---

## 7. パース処理の詳細

### 7.1 Header パース

```csharp
// HeaderParser.ParseHeader() の処理フロー
1. テキストを行ごとに分割
2. 各行を順番に処理
3. === を発見したら Body の開始位置を記録
4. === より前の行を Header コマンドとしてパース
5. ProjectConfig オブジェクトを生成して返す
```

### 7.2 Body パース

```csharp
// Body部分のパース
1. Header の終了位置（bodyStartLine）以降の行を抽出
2. CommandParser でコマンドをパース
3. SceneParser でシーンをパース
4. CommandExecutor でコマンドを実行
```

---

## 8. エラーハンドリング

### 8.1 Header が存在しない場合

- 区切り文字 `===` が見つからない場合
- テキスト全体を Body として扱う
- すべての設定がデフォルト値になる

### 8.2 不正なコマンド

- 正規表現にマッチしない行はスキップ
- ログに警告を出力
- パース処理は続行

### 8.3 無効な値

- 範囲外の値（例: 解像度が負の数）
- デフォルト値を使用
- ログに警告を出力

---

## 9. 設計上の利点

### 9.1 プロジェクト管理の容易さ

- プロジェクト全体の設定を一箇所で管理
- 設定変更が全体に即座に反映

### 9.2 テンプレート化

- Header 部分をテンプレートとして保存
- 新規プロジェクト作成時に流用

### 9.3 バージョン管理

- 設定の変更履歴が明確
- Git などで差分が分かりやすい

### 9.4 拡張性

- 新しい設定項目を簡単に追加可能
- 後方互換性を保ちやすい

---

## 10. 今後の拡張案

### 10.1 追加可能な Header コマンド

```
OUTPUT_FORMAT MP4
VIDEO_CODEC H264
AUDIO_CODEC AAC
BITRATE 5000
AUTHOR "作成者名"
DESCRIPTION "動画の説明"
TAGS "タグ1, タグ2, タグ3"
```

### 10.2 プリセット機能

```
PRESET "youtube_1080p"
# 上記で以下が自動設定される:
# RESOLUTION 1920x1080
# FRAMERATE 30
# OUTPUT_FORMAT MP4
```

### 10.3 条件付き設定

```
IF RESOLUTION == "1920x1080"
    DEFAULT_FONT_SIZE 24
ELSE
    DEFAULT_FONT_SIZE 18
ENDIF
```

---

## 11. 実装チェックリスト

### Phase 1: 基本実装
- [x] ProjectConfig クラスの作成
- [x] IHeaderParser インターフェースの作成
- [x] HeaderParser クラスの実装
- [x] EditorViewModel への統合
- [x] 正規表現パターンの定義
- [x] デフォルト値の設定

### Phase 2: テスト
- [ ] Header パースの単体テスト
- [ ] 不正な形式のテスト
- [ ] デフォルト値のテスト
- [ ] Body との統合テスト

### Phase 3: UI 統合
- [ ] プロジェクト設定ダイアログの作成
- [ ] リボンへの設定ボタン追加
- [ ] リアルタイムプレビュー
- [ ] 設定の保存/読み込み

---

## まとめ

Header/Body 機能により、Wideor のテキストファースト設計がさらに強化されました。プロジェクト設定と編集操作を明確に分離することで、管理性・拡張性・保守性が大幅に向上しました。

この機能を基盤として、今後さまざまな設定項目やプリセット機能を追加できます。
