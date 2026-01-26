# フィルムエリアの動画クリップ実装 - 引き継ぎ用プロンプト

## 📋 プロジェクト概要

**Wideor (ウィデオ)** - 動画編集アプリケーション（WPF + MVVM）
- **Framework**: .NET 8.0 (Desktop)
- **GUI Framework**: WPF (Windows Presentation Foundation)
- **Architecture Pattern**: MVVM (Model-View-ViewModel)
- **Video Engine**: LibVLCSharp (動画再生)

---

## 🎯 フィルムエリアの基本コンセプト

### 1. タイムラインの意味（最重要）

**⚠️ 重要な理解ポイント：**

- **タイムラインは「完成動画のタイムライン」を表す**
  - フィルムエリアのタイムラインは、編集して完成する動画（作ろうとしている動画）の再生時間と一致している
  - 完成動画はタイムラインの00:00:00のところから順番に再生される
  - **各セグメントの`StartTime`/`EndTime`は、完成動画のタイムライン上の時間を表す**

- **例：**
  - セグメント1: `StartTime=0.0秒, EndTime=5.0秒` → 完成動画の0～5秒に表示される
  - セグメント2: `StartTime=5.0秒, EndTime=10.0秒` → 完成動画の5～10秒に表示される

### 2. 動画クリップの固定サイズ

- **サイズは固定**（再生時間に関係なく一定）
  - **高さ**: 480ピクセル（固定）
  - **幅**: 最小640ピクセル（親要素の幅に合わせて伸縮）
- **理由**: PowerPointのスライドのように、各クリップは統一された表示サイズを持つ

### 3. セグメントの配置方法

- **Y座標の計算**: `Canvas.Top = StartTime × PixelsPerSecond`
  - `StartTime`: 完成動画のタイムライン上の開始時間（秒）
  - `PixelsPerSecond`: 1秒あたりのピクセル数（ズームレベル）
- **Canvasベースの配置**: `Canvas`を使用して、セグメントを完成動画のタイムライン上の位置に配置

### 4. ズーム機能

- **基本コンセプト**: タイムラインの時間目盛りを調整する機能
- **動作**:
  - **ズームイン**: `PixelsPerSecond`が増える → 表示されるクリップ数が減る（時間目盛りが細かくなる）
  - **ズームアウト**: `PixelsPerSecond`が減る → 表示されるクリップ数が増える（時間目盛りが粗くなる）
- **操作**: Ctrl + マウスホイール、またはリボンボタン
- **イメージ**: PowerPointのスライド拡大縮小と同じ（表示されるスライド数が増減する）

---

## 📁 主要ファイルとその役割

### 1. `VideoSegmentView.xaml` / `VideoSegmentView.xaml.cs`
**役割**: 個別の動画セグメント（クリップ）を表示するView

**重要な実装ポイント：**

- **固定サイズ**: `Height="480"`, `MinWidth="640"`
- **VideoView**: LibVLCSharpの`vlc:VideoView`を使用して動画を表示
- **MediaPlayer設定**: DependencyProperty + `VideoPlayer_Loaded`イベントで設定
  - `MediaPlayer`プロパティは`FilmStripView`からバインディングされる
  - `VideoPlayer.IsLoaded`を確認してから`MediaPlayer`を設定（タイミング問題を回避）

**コードの要点：**
```csharp
// MediaPlayerの設定は、VideoPlayerが完全にロードされた後に実行
private void VideoPlayer_Loaded(object sender, RoutedEventArgs e)
{
    Dispatcher.InvokeAsync(() =>
    {
        TrySetMediaPlayer(MediaPlayer);
    }, System.Windows.Threading.DispatcherPriority.Loaded);
}
```

### 2. `FilmStripView.xaml` / `FilmStripView.xaml.cs`
**役割**: 動画セグメントのリストを表示するView（フィルムエリア全体）

**重要な実装ポイント：**

- **ItemsControl**: `VideoSegmentView`を`ItemsControl`で表示
- **Canvas配置**: `ItemsControl.ItemsPanel`に`Canvas`を使用
- **Y座標バインディング**: `Canvas.Top`に`TimeToYConverter`を使用して`StartTime`を変換
- **MediaPlayerバインディング**: `VideoSegmentView`に`MediaPlayer`をバインディング
  ```xaml
  <local:VideoSegmentView Segment="{Binding}" 
                          MediaPlayer="{Binding DataContext.MediaPlayer, RelativeSource={RelativeSource AncestorType=ItemsControl}}" />
  ```

**⚠️ 現在の問題点：**
- `UpdateCanvasSize()`メソッドで固定値`100.0`を使用している（行206-207）
  ```csharp
  // 固定スケール: 1秒 = 100ピクセル
  FilmStripCanvas.Height = maxEndTime * 100.0;
  ```
- **修正が必要**: `TimelineViewModel.PixelsPerSecond`を使用するように変更すべき

### 3. `TimelineViewModel.cs`
**役割**: タイムライン機能のViewModel（スクロール同期、ズーム管理、動画セグメント管理）

**重要な実装ポイント：**

- **PixelsPerSecond**: `ITimeRulerService.PixelsPerSecond`を購読（現在は固定値100.0）
- **MediaPlayer**: `IVideoEngine.MediaPlayer`を公開（`VideoSegmentView`で使用）
- **VideoSegments**: `ReadOnlyObservableCollection<VideoSegment>`でセグメントを管理
- **OnSegmentClicked**: セグメントクリック時の再生処理

**⚠️ 現在の問題点：**
- `PixelsPerSecond`が固定値`100.0`に設定されている（行139）
  ```csharp
  // 固定スケール: 1秒 = 100ピクセル（ズーム機能なし）
  PixelsPerSecond = new ReactiveProperty<double>(100.0)
      .ToReadOnlyReactiveProperty()
      .AddTo(_disposables);
  ```
- **修正が必要**: `_timeRulerService.PixelsPerSecond`を購読するように変更すべき

### 4. `TimeToYConverter.cs`
**役割**: 時間（秒）をY座標（ピクセル）に変換するコンバーター

**⚠️ 現在の問題点：**
- 固定値`100.0`を使用している（行13）
  ```csharp
  private const double PixelsPerSecond = 100.0;
  ```
- **修正が必要**: `TimelineViewModel.PixelsPerSecond`を参照するように変更すべき
- **解決策**: コンバーターに`PixelsPerSecond`をパラメータとして渡す、または`MultiBinding`を使用

### 5. `VideoSegment.cs`
**役割**: 動画セグメントのデータモデル

**⚠️ 重要な不整合：**
- コメントが「元動画内の開始時間」となっているが、実際は「完成動画のタイムライン上の時間」である
  ```csharp
  public double StartTime { get; set; }      // 元動画内の開始時間（秒） ← 誤り
  public double EndTime { get; set; }        // 元動画内の終了時間（秒） ← 誤り
  ```
- **修正が必要**: コメントを「完成動画のタイムライン上の開始時間（秒）」に変更すべき

---

## 🔧 MediaPlayerの設定フロー

### 1. 初期化フロー
```
TimelineViewModel (コンストラクタ)
  ↓
MediaPlayer = _videoEngine.MediaPlayer (初期値)
  ↓
IsLoadedがtrueになったら
  ↓
MediaPlayer = _videoEngine.MediaPlayer (更新)
  ↓
OnPropertyChanged("MediaPlayer")
  ↓
FilmStripView.xaml (バインディング)
  ↓
VideoSegmentView.MediaPlayer (DependencyProperty)
  ↓
OnMediaPlayerChanged (コールバック)
  ↓
Dispatcher.InvokeAsync (UIスレッドで実行)
  ↓
TrySetMediaPlayer()
  ↓
VideoPlayer.IsLoadedを確認
  ↓
VideoPlayer.MediaPlayer = mediaPlayer (設定)
```

### 2. VideoPlayer_Loadedイベント
- `VideoSegmentView_Loaded`で`VideoPlayer.Loaded`イベントを購読
- `VideoPlayer`がロードされた時点で`MediaPlayer`を設定
- `Dispatcher.InvokeAsync`で`DispatcherPriority.Loaded`を使用してタイミングを調整

---

## ⚠️ 現在の実装上の問題点

### 1. ズーム機能が正しく動作しない

**問題：**
- `TimeToYConverter.cs`: 固定値`100.0`を使用
- `FilmStripView.xaml.cs`: `UpdateCanvasSize()`で固定値`100.0`を使用
- `TimelineViewModel.cs`: `PixelsPerSecond`が固定値`100.0`に設定されている

**影響：**
- ズームイン/ズームアウトしても、セグメントの位置とCanvasの高さが変わらない
- `ITimeRulerService.SetZoomLevel()`を呼んでも、視覚的な変化がない

**修正方法：**
1. `TimelineViewModel.cs`: `_timeRulerService.PixelsPerSecond`を購読するように変更
2. `TimeToYConverter.cs`: `PixelsPerSecond`をパラメータとして受け取るように変更（`MultiBinding`を使用）
3. `FilmStripView.xaml.cs`: `UpdateCanvasSize()`で`ViewModel.PixelsPerSecond.Value`を使用

### 2. VideoSegment.csのコメント不整合

**問題：**
- `StartTime`/`EndTime`のコメントが「元動画内の開始時間」となっている
- 実際は「完成動画のタイムライン上の時間」である

**修正方法：**
- コメントを「完成動画のタイムライン上の開始時間（秒）」に変更

---

## 🎨 UI構造

### FilmStripViewの構造
```
FilmStripView (UserControl)
  └─ ScrollViewer (FilmStripScrollViewer)
      └─ Canvas (FilmStripCanvas)
          └─ ItemsControl (FilmStripItemsControl)
              └─ VideoSegmentView (DataTemplate)
                  └─ Border (固定サイズ: Height=480px, MinWidth=640px)
                      └─ Grid
                          └─ VideoView (vlc:VideoView)
```

### スクロール同期
- `IScrollCoordinator`を使用して、`TimeRulerView`と`FilmStripView`のScrollViewerを同期
- `TimelineViewModel.ScrollPosition`を購読して、すべてのScrollViewerを同期更新

---

## 📝 データモデル

### VideoSegment
```csharp
public class VideoSegment
{
    public int Id { get; set; }
    public double StartTime { get; set; }      // 完成動画のタイムライン上の開始時間（秒）
    public double EndTime { get; set; }        // 完成動画のタイムライン上の終了時間（秒）
    public bool Visible { get; set; }          // 表示/非表示フラグ
    public SegmentState State { get; set; }    // Stopped, Playing, Hidden
    public string VideoFilePath { get; set; }  // 元動画ファイルのパス
    
    public double Duration => EndTime - StartTime;
}
```

**重要なポイント：**
- `StartTime`/`EndTime`は**完成動画のタイムライン上の時間**を表す
- セグメントのサイズは固定（Height=480px、再生時間に関係なく一定）

---

## 🔍 デバッグのポイント

### 1. MediaPlayerが設定されない問題
- **ログ**: `LogHelper.WriteLog`で`VideoPlayer.IsLoaded`と`MediaPlayer`の状態を確認
- **タイミング**: `VideoPlayer_Loaded`イベントと`OnMediaPlayerChanged`の実行順序を確認
- **解決策**: `Dispatcher.InvokeAsync`で`DispatcherPriority.Loaded`を使用

### 2. セグメントが表示されない問題
- **ログ**: `VideoSegments`のコレクション変更を確認
- **バインディング**: `ItemsControl.ItemsSource`が正しく設定されているか確認
- **Canvasサイズ**: `FilmStripCanvas.Height`が正しく計算されているか確認

### 3. ズーム機能が動作しない問題
- **PixelsPerSecond**: `TimelineViewModel.PixelsPerSecond.Value`が変更されているか確認
- **TimeToYConverter**: 固定値`100.0`を使用していないか確認
- **Canvas更新**: `UpdateCanvasSize()`が`PixelsPerSecond`の変更時に呼ばれているか確認

---

## 📚 参考資料

### 設計ドキュメント
- `Wideor_design.txt`: フィルムエリアの設計仕様（3.2節、9章）

### インターフェース定義
- `ITimeRulerService`: ズームレベルと座標変換を管理
- `IScrollCoordinator`: スクロール同期を管理
- `IVideoEngine`: 動画再生を管理
- `IVideoSegmentManager`: セグメント管理を管理

---

## 🚀 次のステップ

### 優先度：高
1. **ズーム機能の修正**
   - `TimelineViewModel.cs`: `_timeRulerService.PixelsPerSecond`を購読
   - `TimeToYConverter.cs`: `PixelsPerSecond`をパラメータとして受け取る
   - `FilmStripView.xaml.cs`: `UpdateCanvasSize()`で`ViewModel.PixelsPerSecond.Value`を使用

2. **VideoSegment.csのコメント修正**
   - `StartTime`/`EndTime`のコメントを「完成動画のタイムライン上の時間」に変更

### 優先度：中
3. **TimeRulerView.xaml.csの修正**
   - `UpdateRuler()`で固定値`100.0`を使用している箇所を修正（行200）

---

## 💡 重要な注意事項

1. **タイムラインの意味を常に意識する**
   - `StartTime`/`EndTime`は「完成動画のタイムライン上の時間」である
   - 「元動画内の時間」ではない

2. **動画クリップのサイズは固定**
   - Height=480px、再生時間に関係なく一定
   - PowerPointのスライドと同じイメージ

3. **MediaPlayerの設定タイミング**
   - `VideoPlayer.IsLoaded`を確認してから設定
   - `Dispatcher.InvokeAsync`で`DispatcherPriority.Loaded`を使用

4. **ズーム機能の実装**
   - `PixelsPerSecond`の変更が、すべての座標計算に反映される必要がある
   - 固定値`100.0`を使用している箇所をすべて修正する必要がある

---

## 📞 トラブルシューティング

### Q: 動画が表示されない
- **確認**: `MediaPlayer`が設定されているか（ログで確認）
- **確認**: `VideoPlayer.IsLoaded`が`true`か
- **確認**: `VideoSegments`コレクションにセグメントが含まれているか

### Q: セグメントの位置が正しくない
- **確認**: `TimeToYConverter`が正しく動作しているか
- **確認**: `Canvas.Top`のバインディングが正しいか
- **確認**: `PixelsPerSecond`が固定値`100.0`になっていないか

### Q: ズーム機能が動作しない
- **確認**: `TimelineViewModel.PixelsPerSecond`が変更されているか
- **確認**: `TimeToYConverter`が`PixelsPerSecond`を参照しているか
- **確認**: `FilmStripView.UpdateCanvasSize()`が`PixelsPerSecond`を使用しているか

---

**最終更新日**: 2024年（現在の日付）
**作成者**: AI Assistant
**目的**: フィルムエリアの動画クリップ実装の引き継ぎ
