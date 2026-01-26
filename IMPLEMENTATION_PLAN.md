# 動画クリップフィルム方式 実装計画

## 概要

このドキュメントは、現在のサムネイルベースのタイムラインから「動画クリップフィルム方式」への移行計画を記載しています。

## 実装フェーズ

### Phase 1: データモデルの拡張（基盤整備）

#### 1.1 VideoSegmentモデルの作成
**ファイル**: `Shared/Domain/VideoSegment.cs` (新規)

```csharp
public class VideoSegment
{
    public int Id { get; set; }
    public double StartTime { get; set; }      // 元動画内の開始時間（秒）
    public double EndTime { get; set; }        // 元動画内の終了時間（秒）
    public bool Visible { get; set; }          // 表示/非表示フラグ
    public SegmentState State { get; set; }    // Stopped, Playing, Hidden
    public string VideoFilePath { get; set; }  // 元動画ファイルのパス
}

public enum SegmentState
{
    Stopped,    // 停止中（最初のフレームを表示）
    Playing,    // 再生中
    Hidden      // 非表示（灰色）
}
```

**タスク**:
- [ ] `VideoSegment`クラスの作成
- [ ] `SegmentState`列挙型の作成
- [ ] ユニットテストの作成

**見積もり**: 2時間

---

#### 1.2 EditCommandモデルの作成
**ファイル**: `Shared/Domain/EditCommand.cs` (新規)

```csharp
public class EditCommand
{
    public CommandType Type { get; set; }
    public double? Time { get; set; }          // CUT用
    public double? StartTime { get; set; }     // HIDE/SHOW/DELETE用
    public double? EndTime { get; set; }       // HIDE/SHOW/DELETE用
    public string? FilePath { get; set; }      // LOAD用
    public int LineNumber { get; set; }        // テキストエディタ上の行番号
}

public enum CommandType
{
    Load,
    Cut,
    Hide,
    Show,
    Delete
}
```

**タスク**:
- [ ] `EditCommand`クラスの作成
- [ ] `CommandType`列挙型の作成
- [ ] ユニットテストの作成

**見積もり**: 1時間

---

#### 1.3 コマンドパーサーの実装
**ファイル**: `Shared/Infra/CommandParser.cs` (新規)

**機能**:
- テキストからコマンドを解析
- `LOAD`, `CUT`, `HIDE`, `SHOW`, `DELETE`コマンドのパース
- 従来のセパレータ形式（`--- [Start -> End] ---`）もサポート

**タスク**:
- [ ] 正規表現パターンの定義
- [ ] コマンドパースロジックの実装
- [ ] エラーハンドリング
- [ ] ユニットテストの作成

**見積もり**: 4時間

---

### Phase 2: VideoSegment管理システムの実装

#### 2.1 VideoSegmentManagerの作成
**ファイル**: `Shared/Infra/IVideoSegmentManager.cs`, `Shared/Infra/VideoSegmentManager.cs` (新規)

**機能**:
- セグメントの追加・削除・更新
- セグメントの状態管理（Stopped, Playing, Hidden）
- セグメントの検索（時間範囲、IDなど）

**タスク**:
- [ ] インターフェースの定義
- [ ] 実装クラスの作成
- [ ] セグメントコレクションの管理
- [ ] イベント通知（セグメント変更時）
- [ ] ユニットテストの作成

**見積もり**: 6時間

---

#### 2.2 コマンド実行エンジンの実装
**ファイル**: `Shared/Infra/ICommandExecutor.cs`, `Shared/Infra/CommandExecutor.cs` (新規)

**機能**:
- `LOAD`: 動画を読み込んでセグメントを作成
- `CUT`: 指定位置でセグメントを分割
- `HIDE`: セグメントを非表示にする
- `SHOW`: 非表示を解除
- `DELETE`: セグメントを削除

**タスク**:
- [ ] インターフェースの定義
- [ ] 各コマンドの実行ロジック実装
- [ ] セグメント分割アルゴリズムの実装
- [ ] エラーハンドリング
- [ ] ユニットテストの作成

**見積もり**: 8時間

---

### Phase 3: ViewModel層の拡張

#### 3.1 TimelineViewModelの拡張
**ファイル**: `Features/Timeline/TimelineViewModel.cs` (既存ファイルの拡張)

**変更内容**:
- `ThumbnailItems`の代わりに`VideoSegments`コレクションを追加
- `VideoSegmentManager`との統合
- `CommandExecutor`との統合
- 現在再生中のセグメントの管理
- エンターキーでの分割機能

**タスク**:
- [ ] `VideoSegments`プロパティの追加
- [ ] `VideoSegmentManager`の注入
- [ ] `CommandExecutor`の注入
- [ ] セグメント再生制御の実装
- [ ] エンターキーイベントハンドラの追加
- [ ] 既存の`ThumbnailItems`との互換性維持（段階的移行）

**見積もり**: 10時間

---

#### 3.2 VideoSegmentViewModelの作成
**ファイル**: `Features/Timeline/VideoSegmentViewModel.cs` (新規)

**機能**:
- 個別のセグメントの状態管理
- 再生制御（Play, Stop, Seek）
- UIバインディング用のプロパティ

**タスク**:
- [ ] ViewModelクラスの作成
- [ ] 再生状態の管理
- [ ] LibVLCSharpとの統合
- [ ] イベント通知の実装

**見積もり**: 6時間

---

### Phase 4: View層の実装

#### 4.1 VideoSegmentViewの作成
**ファイル**: `Features/Timeline/VideoSegmentView.xaml`, `Features/Timeline/VideoSegmentView.xaml.cs` (新規)

**機能**:
- 動画セグメントの表示
- LibVLCSharpのVideoViewの統合
- クリックイベントの処理
- 非表示時のグレーアウト表示

**タスク**:
- [ ] XAMLの作成
- [ ] VideoViewの統合
- [ ] クリックイベントハンドラ
- [ ] 状態に応じた表示制御（Stopped, Playing, Hidden）
- [ ] スタイリング（非表示時のグレーアウト）

**見積もり**: 8時間

---

#### 4.2 FilmStripViewの改修
**ファイル**: `Features/Timeline/FilmStripView.xaml`, `Features/Timeline/FilmStripView.xaml.cs` (既存ファイルの改修)

**変更内容**:
- `ListBox`の`ItemsSource`を`ThumbnailItems`から`VideoSegments`に変更
- `ItemTemplate`を`VideoSegmentView`に変更
- スナップスクロールの実装

**タスク**:
- [ ] XAMLの変更（ItemsSource, ItemTemplate）
- [ ] スナップスクロールの実装
- [ ] マウスホイールイベントハンドラ
- [ ] スナップアニメーションの実装
- [ ] 仮想化の確認（VirtualizingStackPanel）

**見積もり**: 8時間

---

#### 4.3 スナップスクロールの実装
**ファイル**: `Features/Timeline/SnapScrollBehavior.cs` (新規)

**機能**:
- セグメント単位でのスナップ
- スナップ位置の計算
- スムーズなアニメーション

**タスク**:
- [ ] スナップ位置の計算ロジック
- [ ] アニメーションの実装（Storyboard）
- [ ] マウスホイールイベントの処理
- [ ] ドラッグ時の連続スクロール（オプション）

**見積もり**: 6時間

---

### Phase 5: テキストエディタとの統合

#### 5.1 テキストエディタコマンド挿入機能
**ファイル**: `Features/Editor/EditorViewModel.cs` (既存ファイルの拡張)

**機能**:
- エンターキー押下時に`CUT`コマンドを挿入
- セグメント選択時に`HIDE`コマンドを挿入
- コマンド実行時にテキストエディタを更新

**タスク**:
- [ ] エンターキーイベントハンドラの追加
- [ ] コマンド挿入ロジックの実装
- [ ] テキストエディタとの同期

**見積もり**: 4時間

---

#### 5.2 コマンドパースと実行の統合
**ファイル**: `Features/Editor/EditorViewModel.cs` (既存ファイルの拡張)

**機能**:
- テキスト変更時にコマンドをパース
- パースしたコマンドを`CommandExecutor`に渡して実行
- エラー時のフィードバック

**タスク**:
- [ ] テキスト変更イベントの購読
- [ ] コマンドパーサーの呼び出し
- [ ] コマンド実行の統合
- [ ] エラーハンドリング

**見積もり**: 6時間

---

### Phase 6: 再生制御の実装

#### 6.1 単一再生制御の実装
**ファイル**: `Features/Timeline/VideoSegmentViewModel.cs` (既存ファイルの拡張)

**機能**:
- セグメントクリック時の再生開始
- 他のセグメント再生中の停止処理
- 最初のフレームへのシーク

**タスク**:
- [ ] セグメントクリックイベントの処理
- [ ] 現在再生中のセグメントの管理
- [ ] 停止→シーク→再生のフロー実装
- [ ] 状態変更の通知

**見積もり**: 6時間

---

#### 6.2 LibVLCSharp統合の最適化
**ファイル**: `Shared/Infra/VideoSegmentPlayer.cs` (新規)

**機能**:
- セグメント単位での動画プレーヤー管理
- メモリ効率的なプレーヤーの作成・破棄
- セグメント範囲内での再生制御

**タスク**:
- [ ] プレーヤー管理クラスの作成
- [ ] セグメント範囲での再生制限
- [ ] メモリ管理の最適化
- [ ] エラーハンドリング

**見積もり**: 8時間

---

### Phase 7: パフォーマンス最適化

#### 7.1 仮想化の最適化
**ファイル**: `Features/Timeline/FilmStripView.xaml` (既存ファイルの改修)

**機能**:
- `VirtualizingStackPanel`の設定確認
- 表示範囲外のセグメントのプレーヤー破棄
- 遅延読み込みの実装

**タスク**:
- [ ] 仮想化設定の確認
- [ ] 表示範囲の検出
- [ ] プレーヤーの遅延初期化
- [ ] メモリ使用量の監視

**見積もり**: 6時間

---

#### 7.2 スナップスクロールの最適化
**ファイル**: `Features/Timeline/SnapScrollBehavior.cs` (既存ファイルの拡張)

**機能**:
- スナップ位置の事前計算とキャッシュ
- 二分探索による高速なセグメント検索
- 60fpsアニメーションの実現

**タスク**:
- [ ] スナップ位置のキャッシュ実装
- [ ] 二分探索アルゴリズムの実装
- [ ] アニメーションの最適化
- [ ] パフォーマンステスト

**見積もり**: 4時間

---

### Phase 8: テストとデバッグ

#### 8.1 ユニットテスト
**タスク**:
- [ ] `VideoSegment`モデルのテスト
- [ ] `EditCommand`モデルのテスト
- [ ] `CommandParser`のテスト
- [ ] `VideoSegmentManager`のテスト
- [ ] `CommandExecutor`のテスト

**見積もり**: 8時間

---

#### 8.2 統合テスト
**タスク**:
- [ ] コマンド実行フローのテスト
- [ ] セグメント分割のテスト
- [ ] 再生制御のテスト
- [ ] スナップスクロールのテスト

**見積もり**: 6時間

---

#### 8.3 UIテスト
**タスク**:
- [ ] セグメント表示のテスト
- [ ] クリック操作のテスト
- [ ] スクロール操作のテスト
- [ ] 非表示セグメントの表示テスト

**見積もり**: 4時間

---

## 実装順序の推奨

### 第1段階: 基盤整備（Phase 1-2）
1. Phase 1: データモデルの拡張
2. Phase 2: VideoSegment管理システムの実装

**期間**: 約3週間（21時間）

### 第2段階: ViewModel層の実装（Phase 3）
3. Phase 3: ViewModel層の拡張

**期間**: 約2週間（16時間）

### 第3段階: View層の実装（Phase 4）
4. Phase 4: View層の実装

**期間**: 約2週間（22時間）

### 第4段階: 統合と最適化（Phase 5-7）
5. Phase 5: テキストエディタとの統合
6. Phase 6: 再生制御の実装
7. Phase 7: パフォーマンス最適化

**期間**: 約3週間（30時間）

### 第5段階: テストとデバッグ（Phase 8）
8. Phase 8: テストとデバッグ

**期間**: 約2週間（18時間）

---

## 総見積もり時間

| フェーズ | 時間 |
|---------|------|
| Phase 1: データモデルの拡張 | 7時間 |
| Phase 2: VideoSegment管理システム | 14時間 |
| Phase 3: ViewModel層の拡張 | 16時間 |
| Phase 4: View層の実装 | 22時間 |
| Phase 5: テキストエディタとの統合 | 10時間 |
| Phase 6: 再生制御の実装 | 14時間 |
| Phase 7: パフォーマンス最適化 | 10時間 |
| Phase 8: テストとデバッグ | 18時間 |
| **合計** | **111時間** |

**期間**: 約12週間（3ヶ月、週20時間作業の場合）

---

## リスクと対策

### リスク1: LibVLCSharpのメモリ管理
**問題**: 複数の動画プレーヤーを同時に保持するとメモリ使用量が増加
**対策**: 
- 表示範囲外のプレーヤーを破棄
- 非表示セグメントのプレーヤーを破棄
- メモリ使用量の監視とログ出力

### リスク2: パフォーマンスの低下
**問題**: 多くのセグメントがある場合のスクロール性能
**対策**:
- 仮想化の徹底
- スナップ位置のキャッシュ
- パフォーマンステストの実施

### リスク3: 既存機能との互換性
**問題**: 既存のサムネイル機能との互換性維持
**対策**:
- 段階的な移行
- 既存コードの保持（削除は後で）
- 機能フラグによる切り替え

---

## 移行戦略

### 段階的移行アプローチ

1. **Phase 1-2**: 新しいデータモデルと管理システムを実装（既存機能は維持）
2. **Phase 3-4**: 新しいViewを実装し、既存Viewと並行して動作
3. **Phase 5-6**: 新しい機能を統合し、既存機能を段階的に置き換え
4. **Phase 7-8**: 最適化とテスト後、既存コードを削除

### 機能フラグの使用

```csharp
public class FeatureFlags
{
    public static bool UseVideoSegmentMode { get; set; } = false; // デフォルトはfalse
}
```

段階的に`true`に切り替えて、問題があれば`false`に戻せるようにする。

---

## 次のステップ

1. **Phase 1の開始**: `VideoSegment`モデルの作成から開始
2. **設計レビュー**: Phase 1完了後に設計をレビュー
3. **実装開始**: Phase 2以降を順次実装
