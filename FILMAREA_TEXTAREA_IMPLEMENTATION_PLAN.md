# 動画フィルムエリアとテキストエリア実装プラン

## 概要

このドキュメントは、Wideor_design.txtに基づいた動画フィルムエリアとテキストエリアの処理実装プランです。
設計思想「テキストファースト」に基づき、テキストコマンドを中心とした編集システムを構築します。

---

## 1. 現在の実装状況

### 1.1 実装済みの機能

#### バックエンド（ロジック層）
- ✅ **CommandParser**: テキストコマンドのパース（LOAD, CUT, HIDE, SHOW, DELETE）
- ✅ **CommandExecutor**: コマンド実行ロジック
- ✅ **VideoSegmentManager**: セグメント管理（追加、削除、更新、検索）
- ✅ **TimelineViewModel**: タイムライン機能の統合（ズーム、スクロール、セグメント管理）
- ✅ **VideoEngine**: 動画再生エンジン（LibVLCSharp）

#### フロントエンド（UI層）
- ✅ **FilmStripView**: 動画セグメントの縦並び表示
- ✅ **VideoSegmentView**: 個別セグメントの表示と再生制御
- ✅ **EditorView**: AvalonEditベースのテキストエディタ
- ✅ **TimeRulerView**: タイムラインの目盛り表示
- ✅ **ScrollCoordinator**: 複数エリアのスクロール同期

### 1.2 未実装または改善が必要な機能

#### 優先度：高（コア機能）
- ❌ **Header/Bodyパース機能**: テキストをHeader（プロジェクト設定）とBody（編集操作）に分割してパース
- ❌ **テキスト変更時の自動パース＆実行**: テキストエディタの内容が変更されたら、自動的にコマンドをパースして実行
- ❌ **セパレータ形式のパース**: `--- [00:01:15.000 -> 00:01:20.500] ---` 形式のパラグラフセパレータ
- ❌ **パラグラフ管理**: セパレータとコンテンツの関連付け（SceneBlock）
- ❌ **リンクエリアの描画**: フィルムエリアとテキストエリアの対応関係を示す台形描画

#### 優先度：中（ユーザビリティ）
- ⚠️ **エンターキーでの分割**: 再生中にEnterキーで分割し、テキストエディタにCUTコマンドを自動挿入
- ⚠️ **セグメント表示の最適化**: 設計書に基づく固定サイズ（480px）への変更
- ⚠️ **スナップスクロール**: PowerPointライクなセグメント単位でのスナップ
- ⚠️ **グレーアウト表示**: 非表示セグメントの視覚的フィードバック

#### 優先度：低（拡張機能）
- ⚠️ **シアター再生モード**: 完成動画のプレビュー再生
- ⚠️ **プレビュー再生モード**: オーバーレイでの動画再生
- ⚠️ **パラグラフ装飾**: 題名、字幕、補足文の表示
- ⚠️ **ドラッグ&ドロップ**: 動画ファイルのドラッグ&ドロップ対応

---

## 2. アーキテクチャ設計

### 2.1 データフローの基本原則

```
┌──────────────────────────────────────────────────────────┐
│                   テキストファースト設計                    │
│                                                          │
│  テキストエディタ (Source of Truth)                       │
│         ↓                                                │
│   テキスト分割 (Header / Body)                           │
│         ├─────────────────┬──────────────────┐          │
│         ↓                 ↓                  ↓           │
│   HeaderParser      CommandParser      SceneParser      │
│   (プロジェクト設定)    (編集コマンド)        (パラグラフ)    │
│         ↓                 ↓                  ↓           │
│   ProjectConfig     CommandExecutor    SceneBlocks      │
│         ↓                 ↓                               │
│   UI設定適用      VideoSegmentManager (状態管理)          │
│                           ↓                               │
│                    FilmStripView (表示)                   │
└──────────────────────────────────────────────────────────┘
```

### 2.2 レイヤー構成

#### Presentation Layer (UI)
- `EditorView`: テキスト編集UI
- `FilmStripView`: 動画セグメント表示UI
- `LinkAreaView`: リンク描画UI（新規作成）
- `TimeRulerView`: タイムラインUI

#### Application Layer (ViewModel)
- `EditorViewModel`: テキスト編集ロジック
- `TimelineViewModel`: タイムライン統合ロジック
- `LinkAreaViewModel`: リンク描画ロジック（新規作成）

#### Domain Layer (ビジネスロジック)
- `SceneBlock`: パラグラフ（セパレータ＋コンテンツ）
- `VideoSegment`: 動画セグメント
- `EditCommand`: 編集コマンド

#### Infrastructure Layer (インフラ)
- `CommandParser`: コマンドパーサー
- `SceneParser`: シーンパーサー（新規作成）
- `CommandExecutor`: コマンド実行
- `VideoSegmentManager`: セグメント管理
- `VideoEngine`: 動画再生

---

## 3. 実装プラン

### Phase 1: コアシステムの完成（優先度：高）

#### 3.1 Header/Body パース機能

**目的**: テキストをHeader（プロジェクト設定）とBody（編集操作）に分割してパースする

**実装内容**:

1. **HeaderParser の作成**
   ```csharp
   public class HeaderParser : IHeaderParser
   {
       // Header コマンドの正規表現パターン
       private static readonly Regex ProjectPattern = new Regex(@"^\s*PROJECT\s+""(.+)""$");
       private static readonly Regex ResolutionPattern = new Regex(@"^\s*RESOLUTION\s+(\d+)x(\d+)$");
       private static readonly Regex FrameRatePattern = new Regex(@"^\s*FRAMERATE\s+(\d+)$");
       private static readonly Regex DefaultFontPattern = new Regex(@"^\s*DEFAULT_FONT\s+""(.+)""$");
       private static readonly Regex DefaultFontSizePattern = new Regex(@"^\s*DEFAULT_FONT_SIZE\s+(\d+)$");
       private static readonly Regex ColorPattern = new Regex(@"^\s*DEFAULT_(\w+)_COLOR\s+#([0-9A-Fa-f]{6})$");
       private static readonly Regex AlphaPattern = new Regex(@"^\s*DEFAULT_BACKGROUND_ALPHA\s+(0?\.\d+|1\.0|0|1)$");
       
       // Header と Body の区切り
       private static readonly Regex SeparatorPattern = new Regex(@"^={3,}$");
       
       public (ProjectConfig config, int bodyStartLine) ParseHeader(string text)
       {
           var config = new ProjectConfig();
           var lines = text.Split('\n');
           int bodyStartLine = 0;
           
           for (int i = 0; i < lines.Length; i++)
           {
               var line = lines[i].Trim();
               
               // 区切り文字を検出
               if (SeparatorPattern.IsMatch(line))
               {
                   bodyStartLine = i + 1;
                   break;
               }
               
               // 各Headerコマンドをパース
               ParseHeaderCommand(line, config);
           }
           
           return (config, bodyStartLine);
       }
   }
   ```

2. **EditorViewModel での統合**
   ```csharp
   private void OnTextChanged(string text)
   {
       // ステップ1: Header をパース
       var (projectConfig, bodyStartLine) = _headerParser.ParseHeader(text);
       
       // プロジェクト設定を更新
       ProjectConfig.Value = projectConfig;
       
       // ステップ2: Body 部分のテキストを抽出
       var lines = text.Split('\n');
       var bodyLines = lines.Skip(bodyStartLine);
       var bodyText = string.Join("\n", bodyLines);
       
       // ステップ3: Body のコマンドをパース
       var commands = _commandParser.ParseCommands(bodyText);
       
       // ステップ4: Body のシーンをパース
       var scenes = _sceneParser.ParseScenes(bodyText);
       
       // 以降は既存の処理...
   }
   ```

**ファイル**:
- `Shared/Infra/HeaderParser.cs` (新規作成)
- `Shared/Infra/IHeaderParser.cs` (新規作成)
- `Shared/Domain/ProjectConfig.cs` (新規作成)
- `Features/Editor/EditorViewModel.cs` (修正)

---

#### 3.2 テキスト変更監視と自動実行

**目的**: テキストエディタの内容が変更されたら、自動的にコマンドをパースして実行する

**実装内容**:

1. **EditorViewModel の拡張**
   ```csharp
   // Reactive.Bindings を使用してテキスト変更を監視
   Text
       .Throttle(TimeSpan.FromMilliseconds(500)) // 500ms待ってから実行（連続入力対策）
       .Subscribe(text => OnTextChanged(text))
       .AddTo(_disposables);
   
   private void OnTextChanged(string text)
   {
       // コマンドをパース
       var commands = _commandParser.ParseCommands(text);
       
       // セグメントマネージャーをクリア（再構築）
       _segmentManager.Clear();
       
       // コマンドを順次実行
       _commandExecutor.ExecuteCommands(commands);
   }
   ```

2. **課題と対策**:
   - **問題**: 毎回セグメントを再構築するとパフォーマンスが悪い
   - **対策**: 差分検出を行い、変更があった部分のみ更新
   - **実装**: コマンドのハッシュ値を保存し、前回と比較

**ファイル**:
- `Features/Editor/EditorViewModel.cs`
- `Shared/Infra/CommandExecutor.cs`

---

#### 3.2 セパレータ形式のパース（SceneBlock）

**目的**: `--- [time -> time] ---` 形式のパラグラフセパレータをパースし、対応するコンテンツと関連付ける

**実装内容**:

1. **SceneParser の作成**
   ```csharp
   public class SceneParser : ISceneParser
   {
       // 正規表現: ^[-]{3,}\s*\[(\d{2}:\d{2}:\d{2}\.\d{3})\s*->\s*(\d{2}:\d{2}:\d{2}\.\d{3})\]\s*[-]{3,}$
       private static readonly Regex SeparatorPattern = new Regex(...);
       
       public List<SceneBlock> ParseScenes(string text)
       {
           var scenes = new List<SceneBlock>();
           var lines = text.Split('\n');
           
           for (int i = 0; i < lines.Length; i++)
           {
               var match = SeparatorPattern.Match(lines[i]);
               if (match.Success)
               {
                   var startTime = ParseTime(match.Groups);
                   var endTime = ParseTime(match.Groups);
                   
                   // 次のセパレータまでのテキストを取得
                   var contentLines = new List<string>();
                   int j = i + 1;
                   while (j < lines.Length && !SeparatorPattern.IsMatch(lines[j]))
                   {
                       contentLines.Add(lines[j]);
                       j++;
                   }
                   
                   scenes.Add(new SceneBlock
                   {
                       StartTime = TimeSpan.FromSeconds(startTime),
                       EndTime = TimeSpan.FromSeconds(endTime),
                       CommandLineLineNumber = i + 1,
                       ContentText = string.Join("\n", contentLines)
                   });
               }
           }
           
           return scenes;
       }
   }
   ```

2. **EditorViewModel の拡張**
   ```csharp
   // シーンブロックのコレクション
   public ReactiveCollection<SceneBlock> SceneBlocks { get; }
   
   private void OnTextChanged(string text)
   {
       // コマンドをパース
       var commands = _commandParser.ParseCommands(text);
       
       // シーンをパース
       var scenes = _sceneParser.ParseScenes(text);
       
       // セグメントマネージャーをクリア
       _segmentManager.Clear();
       
       // コマンドを実行
       _commandExecutor.ExecuteCommands(commands);
       
       // シーンブロックを更新
       SceneBlocks.Clear();
       foreach (var scene in scenes)
       {
           SceneBlocks.Add(scene);
       }
   }
   ```

**ファイル**:
- `Features/Editor/Internal/SceneParser.cs` (既存ファイルを拡張)
- `Shared/Domain/SceneBlock.cs`
- `Shared/Infra/ISceneParser.cs` (新規作成)

---

#### 3.3 リンクエリアの実装

**目的**: フィルムエリアのセグメントとテキストエリアのパラグラフを台形で結ぶ

**実装内容**:

1. **LinkAreaView の作成（XAML）**
   ```xml
   <UserControl x:Class="Wideor.App.Features.Timeline.LinkAreaView">
       <Canvas x:Name="LinkCanvas" Background="Transparent">
           <!-- 台形描画はコードビハインドで動的生成 -->
       </Canvas>
   </UserControl>
   ```

2. **LinkAreaView のコードビハインド**
   ```csharp
   public partial class LinkAreaView : UserControl
   {
       public void DrawLinks(
           IEnumerable<VideoSegment> segments,
           IEnumerable<SceneBlock> scenes,
           double pixelsPerSecond)
       {
           LinkCanvas.Children.Clear();
           
           // セグメントとシーンの対応関係を計算
           foreach (var scene in scenes)
           {
               var matchingSegment = segments.FirstOrDefault(s =>
                   s.StartTime <= scene.StartTime.TotalSeconds &&
                   s.EndTime >= scene.EndTime.TotalSeconds);
               
               if (matchingSegment != null)
               {
                   DrawTrapezoid(matchingSegment, scene, pixelsPerSecond);
               }
           }
       }
       
       private void DrawTrapezoid(VideoSegment segment, SceneBlock scene, double pps)
       {
           // Film Area側の座標（左端）
           var filmY1 = segment.StartTime * pps;
           var filmY2 = segment.EndTime * pps;
           
           // Text Area側の座標（右端）
           var textY1 = GetTextLineY(scene.CommandLineLineNumber);
           var textY2 = GetTextLineY(scene.CommandLineLineNumber + scene.ContentText.Split('\n').Length);
           
           // PathGeometryで台形を描画
           var path = new Path
           {
               Stroke = Brushes.Gray,
               StrokeThickness = 1,
               Fill = new SolidColorBrush(Color.FromArgb(30, 128, 128, 128))
           };
           
           var geometry = new PathGeometry();
           var figure = new PathFigure { StartPoint = new Point(0, filmY1) };
           
           figure.Segments.Add(new LineSegment(new Point(80, textY1), true));
           figure.Segments.Add(new LineSegment(new Point(80, textY2), true));
           figure.Segments.Add(new LineSegment(new Point(0, filmY2), true));
           figure.IsClosed = true;
           
           geometry.Figures.Add(figure);
           path.Data = geometry;
           
           LinkCanvas.Children.Add(path);
       }
   }
   ```

3. **TimelinePage への統合**
   ```xml
   <!-- TimelinePage.xaml -->
   <Grid>
       <Grid.ColumnDefinitions>
           <ColumnDefinition Width="60"/>      <!-- Time Ruler -->
           <ColumnDefinition Width="*"/>       <!-- Film Area -->
           <ColumnDefinition Width="80"/>      <!-- Link Area -->
           <ColumnDefinition Width="2*"/>     <!-- Text Area -->
       </Grid.ColumnDefinitions>
       
       <local:TimeRulerView Grid.Column="0" ViewModel="{Binding}"/>
       <local:FilmStripView Grid.Column="1" ViewModel="{Binding}"/>
       <local:LinkAreaView Grid.Column="2" x:Name="LinkArea"/>
       <editor:EditorView Grid.Column="3" ViewModel="{Binding EditorViewModel}"/>
   </Grid>
   ```

**ファイル**:
- `Features/Timeline/LinkAreaView.xaml` (新規作成)
- `Features/Timeline/LinkAreaView.xaml.cs` (新規作成)
- `Features/Timeline/TimelinePage.xaml` (修正)

---

### Phase 2: ユーザビリティの向上（優先度：中）

#### 3.4 エンターキーでの分割機能

**目的**: 動画再生中にEnterキーを押すと、現在位置で分割してCUTコマンドを自動挿入

**実装内容**:

1. **EditorView のキーイベント監視**
   ```csharp
   private void EditorView_Loaded(object sender, RoutedEventArgs e)
   {
       // ...既存コード...
       
       // Enterキーイベントを監視
       TextEditorControl.PreviewKeyDown += OnPreviewKeyDown;
   }
   
   private void OnPreviewKeyDown(object sender, KeyEventArgs e)
   {
       if (e.Key == Key.Enter && ViewModel != null)
       {
           // 現在再生中のセグメントを確認
           var currentSegment = ViewModel.TimelineViewModel.CurrentPlayingSegment.Value;
           if (currentSegment != null && currentSegment.State == SegmentState.Playing)
           {
               // 現在の再生位置を取得
               var currentPosition = ViewModel.TimelineViewModel.CurrentPosition.Value;
               
               // CUTコマンドを挿入
               var cutCommand = $"CUT {FormatTime(currentPosition)}\n";
               TextEditorControl.Document.Insert(TextEditorControl.CaretOffset, cutCommand);
               
               e.Handled = true; // Enterキーの通常動作をキャンセル
           }
       }
   }
   
   private string FormatTime(double seconds)
   {
       var ts = TimeSpan.FromSeconds(seconds);
       return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
   }
   ```

**ファイル**:
- `Features/Editor/EditorView.xaml.cs`

---

#### 3.5 セグメント表示の最適化（固定サイズ）

**目的**: 設計書に基づき、セグメントの高さを480pxの固定サイズに変更

**現在の実装**:
- `FilmStripView.xaml.cs`: クリップ高さを動的計算（アスペクト比に基づく）
- `VideoSegmentView.xaml`: 可変サイズ

**変更内容**:

1. **VideoSegmentView.xaml の修正**
   ```xml
   <!-- 固定サイズに変更 -->
   <UserControl Height="480" MinWidth="640">
       <!-- ...既存のコンテンツ... -->
   </UserControl>
   ```

2. **FilmStripView.xaml.cs の修正**
   ```csharp
   private void UpdateClipHeight()
   {
       // 固定高さ（設計書に基づく）
       _clipHeight = 480.0;
       UpdateAllClipsHeight();
   }
   ```

**ファイル**:
- `Features/Timeline/VideoSegmentView.xaml`
- `Features/Timeline/FilmStripView.xaml.cs`

---

#### 3.6 スナップスクロール

**目的**: PowerPointのスライドのように、セグメント単位でスナップする

**実装内容**:

1. **FilmStripView のマウスホイールイベント**
   ```csharp
   private void FilmStripScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
   {
       // Ctrlキーが押されていない場合のみスナップスクロール
       if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
       {
           e.Handled = true;
           
           // 現在表示中のクリップインデックス
           var currentIndex = CurrentClipIndex;
           
           // スクロール方向に応じて次のクリップへ移動
           var nextIndex = e.Delta > 0 ? Math.Max(0, currentIndex - 1) : Math.Min(_totalClips - 1, currentIndex + 1);
           
           // スナップアニメーション付きでスクロール
           ScrollToClipIndexWithAnimation(nextIndex);
       }
   }
   
   private void ScrollToClipIndexWithAnimation(int index)
   {
       var container = FilmStripItemsControl.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
       if (container != null)
       {
           // スムーズスクロールアニメーション
           var targetOffset = FilmStripScrollViewer.VerticalOffset;
           var transform = container.TransformToAncestor(FilmStripScrollViewer);
           var targetPosition = transform.Transform(new Point(0, 0)).Y;
           
           var animation = new DoubleAnimation
           {
               From = FilmStripScrollViewer.VerticalOffset,
               To = FilmStripScrollViewer.VerticalOffset + targetPosition,
               Duration = TimeSpan.FromMilliseconds(300),
               EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
           };
           
           var storyboard = new Storyboard();
           Storyboard.SetTarget(animation, FilmStripScrollViewer);
           Storyboard.SetTargetProperty(animation, new PropertyPath("VerticalOffset"));
           storyboard.Children.Add(animation);
           storyboard.Begin();
       }
   }
   ```

**ファイル**:
- `Features/Timeline/FilmStripView.xaml.cs`
- `Features/Timeline/FilmStripView.xaml` (イベントハンドラーの追加)

---

#### 3.7 グレーアウト表示（非表示セグメント）

**目的**: 非表示（HIDE）セグメントを灰色で表示

**実装内容**:

1. **VideoSegmentView.xaml の修正**
   ```xml
   <Grid>
       <!-- グレーアウトオーバーレイ -->
       <Rectangle x:Name="GrayoutOverlay"
                  Fill="#80808080"
                  Visibility="{Binding Visible, Converter={StaticResource InverseBooleanToVisibilityConverter}}"/>
       
       <!-- 既存のコンテンツ -->
       <Grid IsEnabled="{Binding Visible}">
           <!-- ...動画プレーヤー等... -->
       </Grid>
   </Grid>
   ```

2. **Converter の作成**
   ```csharp
   public class InverseBooleanToVisibilityConverter : IValueConverter
   {
       public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
       {
           if (value is bool boolValue)
           {
               return boolValue ? Visibility.Collapsed : Visibility.Visible;
           }
           return Visibility.Collapsed;
       }
       
       public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
       {
           throw new NotImplementedException();
       }
   }
   ```

**ファイル**:
- `Features/Timeline/VideoSegmentView.xaml`
- `Features/Timeline/Converters/InverseBooleanToVisibilityConverter.cs` (既存ファイルを活用)

---

### Phase 3: 拡張機能（優先度：低）

#### 3.8 シアター再生モード

**目的**: 完成動画のプレビュー再生（非表示セグメントをスキップ）

**実装内容**:

1. **TimelineViewModel にシアター再生コマンド追加**
   ```csharp
   public ReactiveCommand TheaterPlayCommand { get; }
   
   private void InitializeTheaterPlayCommand()
   {
       TheaterPlayCommand = new ReactiveCommand()
           .WithSubscribe(async () =>
           {
               await PlayTheaterMode();
           })
           .AddTo(_disposables);
   }
   
   private async Task PlayTheaterMode()
   {
       // 表示されているセグメントのみを順番に再生
       var visibleSegments = VideoSegments.Where(s => s.Visible).OrderBy(s => s.StartTime).ToList();
       
       foreach (var segment in visibleSegments)
       {
           // セグメントを再生
           await PlaySegmentAsync(segment);
           
           // 次のセグメントまで待機
           await Task.Delay(100);
       }
   }
   
   private async Task PlaySegmentAsync(VideoSegment segment)
   {
       // セグメントの動画をロード
       if (_currentLoadedVideoPath != segment.VideoFilePath)
       {
           await _videoEngine.LoadAsync(segment.VideoFilePath);
           _currentLoadedVideoPath = segment.VideoFilePath;
       }
       
       // セグメントの開始位置にシーク
       await _videoEngine.SeekAsync(segment.StartTime);
       
       // 再生開始
       segment.State = SegmentState.Playing;
       CurrentPlayingSegment.Value = segment;
       _videoEngine.Play();
       
       // セグメントの終了まで待機
       var duration = segment.Duration;
       await Task.Delay(TimeSpan.FromSeconds(duration));
       
       // 停止
       _videoEngine.Stop();
       segment.State = SegmentState.Stopped;
       CurrentPlayingSegment.Value = null;
   }
   ```

**ファイル**:
- `Features/Timeline/TimelineViewModel.cs`
- `Shell/ShellRibbon.xaml` (リボンボタン追加)

---

#### 3.9 パラグラフ装飾（題名、字幕、補足文）

**目的**: セグメントに題名、字幕、補足文をオーバーレイ表示

**実装内容**:

1. **SceneBlock の拡張**
   ```csharp
   public class SceneBlock
   {
       public TimeSpan StartTime { get; set; }
       public TimeSpan EndTime { get; set; }
       public int CommandLineLineNumber { get; set; }
       public string ContentText { get; set; }
       
       // 新規追加
       public string? Title { get; set; }          // 題名
       public string? Subtitle { get; set; }       // 字幕
       public string? Supplement { get; set; }     // 補足文
   }
   ```

2. **SceneParser での解析**
   ```csharp
   private SceneBlock ParseSceneContent(string content)
   {
       var lines = content.Split('\n');
       var scene = new SceneBlock();
       
       foreach (var line in lines)
       {
           if (line.StartsWith("# "))
           {
               scene.Title = line.Substring(2).Trim();
           }
           else if (line.StartsWith("> "))
           {
               scene.Subtitle = line.Substring(2).Trim();
           }
           else if (line.StartsWith("* "))
           {
               scene.Supplement = line.Substring(2).Trim();
           }
       }
       
       return scene;
   }
   ```

3. **VideoSegmentView へのオーバーレイ追加**
   ```xml
   <Grid>
       <!-- 動画プレーヤー -->
       <vlc:VideoView />
       
       <!-- オーバーレイ -->
       <StackPanel VerticalAlignment="Top" Margin="20">
           <TextBlock Text="{Binding Title}" 
                      FontSize="24" 
                      FontWeight="Bold"
                      Foreground="White"/>
       </StackPanel>
       
       <StackPanel VerticalAlignment="Bottom" Margin="20">
           <TextBlock Text="{Binding Subtitle}" 
                      FontSize="18" 
                      Foreground="White"
                      TextAlignment="Center"/>
       </StackPanel>
   </Grid>
   ```

**ファイル**:
- `Shared/Domain/SceneBlock.cs`
- `Features/Editor/Internal/SceneParser.cs`
- `Features/Timeline/VideoSegmentView.xaml`

---

## 4. データモデルの関係図

```
┌─────────────────────────────────────────────────────────────┐
│                      テキストエディタ                         │
│  (AvalonEdit TextEditor - Source of Truth)                  │
└─────────────────┬───────────────────────────────────────────┘
                  │
                  │ Text.Value (ReactiveProperty<string>)
                  ↓
         ┌────────┴────────┐
         │  テキスト分割     │
         │  Header / Body  │
         └────┬────────┬───┘
              │        │
    ┌─────────┘        └─────────┐
    ↓                            ↓
┌──────────────────┐    ┌─────────────────────────┐
│  HeaderParser    │    │  Body パース             │
│  (新規)          │    │  ├→ CommandParser       │
└────┬─────────────┘    │  └→ SceneParser         │
     │                  └────┬────────────┬────────┘
     ↓                       ↓            ↓
┌──────────────────┐    ┌─────────┐  ┌─────────┐
│ ProjectConfig    │    │EditCmd  │  │SceneBlk │
│ (新規)           │    └────┬────┘  └────┬────┘
└────┬─────────────┘         │            │
     │                       ↓            │
     │              ┌──────────────────┐  │
     │              │ CommandExecutor  │  │
     │              └────┬─────────────┘  │
     │                   ↓                │
     │         ┌──────────────────────┐   │
     │         │VideoSegmentManager   │   │
     │         └────┬─────────────────┘   │
     │              │                     │
     └──────────────┼─────────────────────┘
                    ↓
┌─────────────────────────────────────────────────────────────┐
│                 TimelineViewModel                           │
│  - ProjectConfig: ReactiveProperty<ProjectConfig> (新規)    │
│  - VideoSegments: ObservableCollection<VideoSegment>        │
│  - SceneBlocks: ObservableCollection<SceneBlock>            │
└─────────────────┬───────────────────────────────────────────┘
                  │
                  ├──────────────────────┬──────────────────────┐
                  ↓                      ↓                      ↓
         ┌────────────────┐   ┌────────────────┐   ┌────────────────┐
         │ FilmStripView  │   │ LinkAreaView   │   │   EditorView   │
         │ (ItemsControl) │   │  (Canvas)      │   │ (AvalonEdit)   │
         └────────────────┘   └────────────────┘   └────────────────┘
```

---

## 5. 実装スケジュールの推奨順序

### Step 1: テキスト→セグメント同期の確立（1-2日）
1. テキスト変更監視の実装
2. 自動パース＆実行の実装
3. セグメントの再構築最適化

### Step 2: パラグラフ機能の実装（2-3日）
1. SceneParser の作成
2. セパレータ形式のパース
3. SceneBlock と VideoSegment の関連付け

### Step 3: リンクエリアの実装（2-3日）
1. LinkAreaView の作成
2. 台形描画ロジック
3. スクロール同期と再描画

### Step 4: ユーザビリティの向上（2-3日）
1. エンターキーでの分割
2. グレーアウト表示
3. スナップスクロール

### Step 5: 拡張機能（3-5日）
1. シアター再生モード
2. パラグラフ装飾
3. プレビュー再生モード

**合計見積もり**: 10-16日（約2-3週間）

---

## 6. 技術的な課題と対策

### 課題1: テキスト変更のたびにセグメントを再構築すると重い
**対策**:
- Throttle（500ms）で連続変更を抑制
- コマンドのハッシュ値を保存し、差分検出
- 変更があった部分のみ更新

### 課題2: リンク描画の座標計算が複雑
**対策**:
- TimeRulerService を活用（TimeToY変換）
- AvalonEdit の行番号から Y座標を取得するヘルパー関数
- スクロール変更時の再描画はThrottleで制限

### 課題3: 非表示セグメントのメモリ管理
**対策**:
- VirtualizingStackPanel で表示範囲外は破棄
- MediaPlayer は共有インスタンスを使用
- 非表示セグメントは MediaPlayer を null に設定

### 課題4: シアター再生中の同期
**対策**:
- CurrentPosition を監視してセグメント切り替え
- 非表示セグメントは自動スキップ
- セグメント間の遷移はスムーズに（100ms待機）

---

## 7. テストシナリオ

### シナリオ1: 基本的な編集フロー
1. テキストエディタに `LOAD video.mp4` を入力
2. フィルムエリアにセグメントが表示される
3. テキストエディタに `CUT 00:00:30.000` を入力
4. フィルムエリアのセグメントが2つに分割される
5. 分割されたセグメントをクリックして再生確認

### シナリオ2: パラグラフ対応
1. テキストエディタに以下を入力:
   ```
   LOAD video.mp4
   
   --- [00:00:00.000 -> 00:00:10.000] ---
   1. ボルトを緩めます
   ```
2. フィルムエリアにセグメントが表示される
3. リンクエリアに台形が表示される
4. テキストエディタのカーソル位置に応じてリンクがハイライト

### シナリオ3: 非表示機能
1. テキストエディタに `HIDE 00:00:10.000 00:00:20.000` を入力
2. 該当セグメントがグレーアウトされる
3. シアター再生で非表示セグメントがスキップされる

### シナリオ4: エンターキーでの分割
1. セグメントをクリックして再生開始
2. 任意の位置でEnterキーを押す
3. テキストエディタに `CUT 00:00:15.234` が自動挿入される
4. フィルムエリアのセグメントが分割される

---

## 8. パフォーマンス最適化

### 最適化1: 仮想化（VirtualizingStackPanel）
- 表示範囲外のセグメントはレンダリングしない
- スクロールに応じて動的に生成/破棄

### 最適化2: サムネイル生成の遅延
- セグメントが表示範囲に入った時点でサムネイル生成
- 生成済みサムネイルはキャッシュ

### 最適化3: リンク描画の最適化
- スクロール変更時の再描画をThrottle（60fps）
- 表示範囲外のリンクは描画しない

### 最適化4: コマンド実行の最適化
- コマンドのハッシュ値で差分検出
- 変更がない場合はスキップ

---

## 9. まとめ

### 実装の優先順位
1. **Phase 1**: コアシステム（テキスト→セグメント同期、パラグラフ、リンク）
2. **Phase 2**: ユーザビリティ（分割、グレーアウト、スナップ）
3. **Phase 3**: 拡張機能（シアター再生、装飾）

### 成功基準
- ✅ テキストを編集するだけで動画編集が完結する
- ✅ セグメントとパラグラフの対応が視覚的に明確
- ✅ 再生、分割、非表示などの基本操作が直感的
- ✅ 大量のセグメント（100個以上）でもスムーズに動作

### 次のステップ
1. Phase 1 の実装開始（テキスト変更監視）
2. 既存コードのリファクタリング（必要に応じて）
3. 単体テスト＆統合テストの作成
4. ユーザーフィードバックの収集と改善

---

以上
