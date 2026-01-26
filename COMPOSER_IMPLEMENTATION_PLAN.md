# Composer1 Agent 実装計画

## 概要

このドキュメントは、Composer1 Agentを使用して「動画クリップフィルム方式」を実装するための詳細なステップバイステップ計画です。各ステップは、Composer1 Agentが実行可能な単位に分割されています。

---

## Phase 1: データモデルの拡張（基盤整備）

### Step 1.1: VideoSegmentモデルの作成

**目的**: 動画セグメントを表すデータモデルを作成

**作成ファイル**:
- `Shared/Domain/VideoSegment.cs` (新規)
- `Shared/Domain/SegmentState.cs` (新規、またはVideoSegment.cs内にenumとして)

**実装内容**:
```csharp
namespace Wideor.App.Shared.Domain
{
    public enum SegmentState
    {
        Stopped,    // 停止中（最初のフレームを表示）
        Playing,    // 再生中
        Hidden      // 非表示（灰色）
    }

    public class VideoSegment
    {
        public int Id { get; set; }
        public double StartTime { get; set; }      // 元動画内の開始時間（秒）
        public double EndTime { get; set; }        // 元動画内の終了時間（秒）
        public bool Visible { get; set; }          // 表示/非表示フラグ
        public SegmentState State { get; set; }    // Stopped, Playing, Hidden
        public string VideoFilePath { get; set; }  // 元動画ファイルのパス
        
        public double Duration => EndTime - StartTime;
        
        public bool ContainsTime(double time)
        {
            return time >= StartTime && time <= EndTime;
        }
    }
}
```

**Composer1 Agentへの指示**:
```
Create a new file Shared/Domain/VideoSegment.cs with the VideoSegment class and SegmentState enum as shown above.
```

---

### Step 1.2: EditCommandモデルの作成

**目的**: 編集コマンドを表すデータモデルを作成

**作成ファイル**:
- `Shared/Domain/EditCommand.cs` (新規)
- `Shared/Domain/CommandType.cs` (新規、またはEditCommand.cs内にenumとして)

**実装内容**:
```csharp
namespace Wideor.App.Shared.Domain
{
    public enum CommandType
    {
        Load,
        Cut,
        Hide,
        Show,
        Delete
    }

    public class EditCommand
    {
        public CommandType Type { get; set; }
        public double? Time { get; set; }          // CUT用
        public double? StartTime { get; set; }     // HIDE/SHOW/DELETE用
        public double? EndTime { get; set; }       // HIDE/SHOW/DELETE用
        public string? FilePath { get; set; }      // LOAD用
        public int LineNumber { get; set; }        // テキストエディタ上の行番号
        
        public override string ToString()
        {
            return Type switch
            {
                CommandType.Load => $"LOAD {FilePath}",
                CommandType.Cut => $"CUT {Time:00:00:00.000}",
                CommandType.Hide => $"HIDE {StartTime:00:00:00.000} {EndTime:00:00:00.000}",
                CommandType.Show => $"SHOW {StartTime:00:00:00.000} {EndTime:00:00:00.000}",
                CommandType.Delete => $"DELETE {StartTime:00:00:00.000} {EndTime:00:00:00.000}",
                _ => base.ToString()
            };
        }
    }
}
```

**Composer1 Agentへの指示**:
```
Create a new file Shared/Domain/EditCommand.cs with the EditCommand class and CommandType enum as shown above.
```

---

### Step 1.3: コマンドパーサーの実装

**目的**: テキストからコマンドを解析するパーサーを作成

**作成ファイル**:
- `Shared/Infra/ICommandParser.cs` (新規)
- `Shared/Infra/CommandParser.cs` (新規)

**実装内容**:
```csharp
namespace Wideor.App.Shared.Infra
{
    public interface ICommandParser
    {
        List<EditCommand> ParseCommands(string text);
        EditCommand? ParseLine(string line, int lineNumber);
    }

    public class CommandParser : ICommandParser
    {
        // LOAD video.mp4
        private static readonly Regex LoadPattern = new Regex(@"^\s*LOAD\s+(.+)$", RegexOptions.IgnoreCase);
        
        // CUT 00:01:23.456
        private static readonly Regex CutPattern = new Regex(@"^\s*CUT\s+(\d{2}):(\d{2}):(\d{2})\.(\d{3})$");
        
        // HIDE 00:01:00.000 00:01:30.000
        private static readonly Regex HidePattern = new Regex(@"^\s*HIDE\s+(\d{2}):(\d{2}):(\d{2})\.(\d{3})\s+(\d{2}):(\d{2}):(\d{2})\.(\d{3})$");
        
        // SHOW 00:01:00.000 00:01:30.000
        private static readonly Regex ShowPattern = new Regex(@"^\s*SHOW\s+(\d{2}):(\d{2}):(\d{2})\.(\d{3})\s+(\d{2}):(\d{2}):(\d{2})\.(\d{3})$");
        
        // DELETE 00:00:30.000 00:01:00.000
        private static readonly Regex DeletePattern = new Regex(@"^\s*DELETE\s+(\d{2}):(\d{2}):(\d{2})\.(\d{3})\s+(\d{2}):(\d{2}):(\d{2})\.(\d{3})$");
        
        // --- [00:01:15.000 -> 00:01:20.500] --- (従来形式)
        private static readonly Regex SeparatorPattern = new Regex(@"^[-]{3,}\s*\[(\d{2}):(\d{2}):(\d{2})\.(\d{3})\s*->\s*(\d{2}):(\d{2}):(\d{2})\.(\d{3})\]\s*[-]{3,}$");

        public List<EditCommand> ParseCommands(string text)
        {
            var commands = new List<EditCommand>();
            var lines = text.Split('\n');
            
            for (int i = 0; i < lines.Length; i++)
            {
                var command = ParseLine(lines[i], i + 1);
                if (command != null)
                {
                    commands.Add(command);
                }
            }
            
            return commands;
        }

        public EditCommand? ParseLine(string line, int lineNumber)
        {
            // LOAD コマンド
            var loadMatch = LoadPattern.Match(line);
            if (loadMatch.Success)
            {
                return new EditCommand
                {
                    Type = CommandType.Load,
                    FilePath = loadMatch.Groups[1].Value.Trim(),
                    LineNumber = lineNumber
                };
            }

            // CUT コマンド
            var cutMatch = CutPattern.Match(line);
            if (cutMatch.Success)
            {
                var time = ParseTime(cutMatch.Groups);
                return new EditCommand
                {
                    Type = CommandType.Cut,
                    Time = time,
                    LineNumber = lineNumber
                };
            }

            // HIDE コマンド
            var hideMatch = HidePattern.Match(line);
            if (hideMatch.Success)
            {
                var (startTime, endTime) = ParseTimeRange(hideMatch.Groups);
                return new EditCommand
                {
                    Type = CommandType.Hide,
                    StartTime = startTime,
                    EndTime = endTime,
                    LineNumber = lineNumber
                };
            }

            // SHOW コマンド
            var showMatch = ShowPattern.Match(line);
            if (showMatch.Success)
            {
                var (startTime, endTime) = ParseTimeRange(showMatch.Groups);
                return new EditCommand
                {
                    Type = CommandType.Show,
                    StartTime = startTime,
                    EndTime = endTime,
                    LineNumber = lineNumber
                };
            }

            // DELETE コマンド
            var deleteMatch = DeletePattern.Match(line);
            if (deleteMatch.Success)
            {
                var (startTime, endTime) = ParseTimeRange(deleteMatch.Groups);
                return new EditCommand
                {
                    Type = CommandType.Delete,
                    StartTime = startTime,
                    EndTime = endTime,
                    LineNumber = lineNumber
                };
            }

            return null;
        }

        private double ParseTime(GroupCollection groups)
        {
            var hours = int.Parse(groups[1].Value);
            var minutes = int.Parse(groups[2].Value);
            var seconds = int.Parse(groups[3].Value);
            var milliseconds = int.Parse(groups[4].Value);
            return hours * 3600 + minutes * 60 + seconds + milliseconds / 1000.0;
        }

        private (double startTime, double endTime) ParseTimeRange(GroupCollection groups)
        {
            var startHours = int.Parse(groups[1].Value);
            var startMinutes = int.Parse(groups[2].Value);
            var startSeconds = int.Parse(groups[3].Value);
            var startMilliseconds = int.Parse(groups[4].Value);
            var startTime = startHours * 3600 + startMinutes * 60 + startSeconds + startMilliseconds / 1000.0;

            var endHours = int.Parse(groups[5].Value);
            var endMinutes = int.Parse(groups[6].Value);
            var endSeconds = int.Parse(groups[7].Value);
            var endMilliseconds = int.Parse(groups[8].Value);
            var endTime = endHours * 3600 + endMinutes * 60 + endSeconds + endMilliseconds / 1000.0;

            return (startTime, endTime);
        }
    }
}
```

**Composer1 Agentへの指示**:
```
Create ICommandParser interface and CommandParser implementation in Shared/Infra/ directory.
The parser should support LOAD, CUT, HIDE, SHOW, DELETE commands and the traditional separator format.
```

---

## Phase 2: VideoSegment管理システムの実装

### Step 2.1: VideoSegmentManagerの作成

**目的**: セグメントの管理を行うマネージャーを作成

**作成ファイル**:
- `Shared/Infra/IVideoSegmentManager.cs` (新規)
- `Shared/Infra/VideoSegmentManager.cs` (新規)

**実装内容**:
```csharp
namespace Wideor.App.Shared.Infra
{
    public interface IVideoSegmentManager
    {
        event EventHandler<VideoSegmentEventArgs>? SegmentAdded;
        event EventHandler<VideoSegmentEventArgs>? SegmentRemoved;
        event EventHandler<VideoSegmentEventArgs>? SegmentUpdated;
        
        IReadOnlyList<VideoSegment> Segments { get; }
        VideoSegment? GetSegmentById(int id);
        List<VideoSegment> GetSegmentsByTimeRange(double startTime, double endTime);
        VideoSegment? GetSegmentAtTime(double time);
        
        void AddSegment(VideoSegment segment);
        void RemoveSegment(int id);
        void UpdateSegment(VideoSegment segment);
        void Clear();
    }

    public class VideoSegmentEventArgs : EventArgs
    {
        public VideoSegment Segment { get; }
        public VideoSegmentEventArgs(VideoSegment segment) => Segment = segment;
    }

    public class VideoSegmentManager : IVideoSegmentManager
    {
        private readonly List<VideoSegment> _segments = new();
        private int _nextId = 1;

        public event EventHandler<VideoSegmentEventArgs>? SegmentAdded;
        public event EventHandler<VideoSegmentEventArgs>? SegmentRemoved;
        public event EventHandler<VideoSegmentEventArgs>? SegmentUpdated;

        public IReadOnlyList<VideoSegment> Segments => _segments.AsReadOnly();

        public VideoSegment? GetSegmentById(int id)
        {
            return _segments.FirstOrDefault(s => s.Id == id);
        }

        public List<VideoSegment> GetSegmentsByTimeRange(double startTime, double endTime)
        {
            return _segments.Where(s => s.StartTime < endTime && s.EndTime > startTime).ToList();
        }

        public VideoSegment? GetSegmentAtTime(double time)
        {
            return _segments.FirstOrDefault(s => s.ContainsTime(time));
        }

        public void AddSegment(VideoSegment segment)
        {
            if (segment.Id == 0)
            {
                segment.Id = _nextId++;
            }
            _segments.Add(segment);
            _segments.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            SegmentAdded?.Invoke(this, new VideoSegmentEventArgs(segment));
        }

        public void RemoveSegment(int id)
        {
            var segment = GetSegmentById(id);
            if (segment != null)
            {
                _segments.Remove(segment);
                SegmentRemoved?.Invoke(this, new VideoSegmentEventArgs(segment));
            }
        }

        public void UpdateSegment(VideoSegment segment)
        {
            var index = _segments.FindIndex(s => s.Id == segment.Id);
            if (index >= 0)
            {
                _segments[index] = segment;
                _segments.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
                SegmentUpdated?.Invoke(this, new VideoSegmentEventArgs(segment));
            }
        }

        public void Clear()
        {
            _segments.Clear();
            _nextId = 1;
        }
    }
}
```

**Composer1 Agentへの指示**:
```
Create IVideoSegmentManager interface and VideoSegmentManager implementation in Shared/Infra/ directory.
The manager should handle adding, removing, updating segments and provide search functionality.
```

---

### Step 2.2: コマンド実行エンジンの実装

**目的**: コマンドを実行してセグメントを操作するエンジンを作成

**作成ファイル**:
- `Shared/Infra/ICommandExecutor.cs` (新規)
- `Shared/Infra/CommandExecutor.cs` (新規)

**実装内容**:
```csharp
namespace Wideor.App.Shared.Infra
{
    public interface ICommandExecutor
    {
        void ExecuteCommand(EditCommand command);
        void ExecuteCommands(IEnumerable<EditCommand> commands);
    }

    public class CommandExecutor : ICommandExecutor
    {
        private readonly IVideoSegmentManager _segmentManager;
        private readonly IVideoEngine _videoEngine;

        public CommandExecutor(IVideoSegmentManager segmentManager, IVideoEngine videoEngine)
        {
            _segmentManager = segmentManager ?? throw new ArgumentNullException(nameof(segmentManager));
            _videoEngine = videoEngine ?? throw new ArgumentNullException(nameof(videoEngine));
        }

        public void ExecuteCommand(EditCommand command)
        {
            switch (command.Type)
            {
                case CommandType.Load:
                    ExecuteLoad(command);
                    break;
                case CommandType.Cut:
                    ExecuteCut(command);
                    break;
                case CommandType.Hide:
                    ExecuteHide(command);
                    break;
                case CommandType.Show:
                    ExecuteShow(command);
                    break;
                case CommandType.Delete:
                    ExecuteDelete(command);
                    break;
            }
        }

        public void ExecuteCommands(IEnumerable<EditCommand> commands)
        {
            foreach (var command in commands)
            {
                ExecuteCommand(command);
            }
        }

        private void ExecuteLoad(EditCommand command)
        {
            if (string.IsNullOrEmpty(command.FilePath))
                throw new ArgumentException("FilePath is required for LOAD command");

            if (!File.Exists(command.FilePath))
                throw new FileNotFoundException($"Video file not found: {command.FilePath}");

            // 動画の長さを取得
            var videoInfo = _videoEngine.GetVideoInfoAsync().Result;
            if (videoInfo == null)
                throw new InvalidOperationException("Failed to get video information");

            var duration = videoInfo.Duration;

            // 0秒から動画の終了までを1つのセグメントとして作成
            var segment = new VideoSegment
            {
                StartTime = 0.0,
                EndTime = duration,
                Visible = true,
                State = SegmentState.Stopped,
                VideoFilePath = command.FilePath
            };

            _segmentManager.Clear(); // 既存のセグメントをクリア
            _segmentManager.AddSegment(segment);
        }

        private void ExecuteCut(EditCommand command)
        {
            if (!command.Time.HasValue)
                throw new ArgumentException("Time is required for CUT command");

            var cutTime = command.Time.Value;
            var segment = _segmentManager.GetSegmentAtTime(cutTime);

            if (segment == null)
                throw new InvalidOperationException($"No segment found at time {cutTime}");

            // セグメントを分割
            var segment1 = new VideoSegment
            {
                StartTime = segment.StartTime,
                EndTime = cutTime,
                Visible = segment.Visible,
                State = SegmentState.Stopped,
                VideoFilePath = segment.VideoFilePath
            };

            var segment2 = new VideoSegment
            {
                StartTime = cutTime,
                EndTime = segment.EndTime,
                Visible = segment.Visible,
                State = SegmentState.Stopped,
                VideoFilePath = segment.VideoFilePath
            };

            _segmentManager.RemoveSegment(segment.Id);
            _segmentManager.AddSegment(segment1);
            _segmentManager.AddSegment(segment2);
        }

        private void ExecuteHide(EditCommand command)
        {
            if (!command.StartTime.HasValue || !command.EndTime.HasValue)
                throw new ArgumentException("StartTime and EndTime are required for HIDE command");

            var segments = _segmentManager.GetSegmentsByTimeRange(command.StartTime.Value, command.EndTime.Value);
            
            foreach (var segment in segments)
            {
                segment.Visible = false;
                segment.State = SegmentState.Hidden;
                _segmentManager.UpdateSegment(segment);
            }
        }

        private void ExecuteShow(EditCommand command)
        {
            if (!command.StartTime.HasValue || !command.EndTime.HasValue)
                throw new ArgumentException("StartTime and EndTime are required for SHOW command");

            var segments = _segmentManager.GetSegmentsByTimeRange(command.StartTime.Value, command.EndTime.Value);
            
            foreach (var segment in segments)
            {
                segment.Visible = true;
                segment.State = SegmentState.Stopped;
                _segmentManager.UpdateSegment(segment);
            }
        }

        private void ExecuteDelete(EditCommand command)
        {
            if (!command.StartTime.HasValue || !command.EndTime.HasValue)
                throw new ArgumentException("StartTime and EndTime are required for DELETE command");

            var segments = _segmentManager.GetSegmentsByTimeRange(command.StartTime.Value, command.EndTime.Value);
            
            foreach (var segment in segments)
            {
                _segmentManager.RemoveSegment(segment.Id);
            }
        }
    }
}
```

**Composer1 Agentへの指示**:
```
Create ICommandExecutor interface and CommandExecutor implementation in Shared/Infra/ directory.
The executor should implement LOAD, CUT, HIDE, SHOW, DELETE command execution logic.
```

---

## Phase 3: DIコンテナへの登録

### Step 3.1: App.xaml.csへの登録

**目的**: 新しいサービスをDIコンテナに登録

**編集ファイル**:
- `App.xaml.cs` (既存ファイルの拡張)

**実装内容**:
```csharp
// ConfigureServicesメソッド内に追加
services.AddSingleton<ICommandParser, CommandParser>();
services.AddSingleton<IVideoSegmentManager, VideoSegmentManager>();
services.AddSingleton<ICommandExecutor, CommandExecutor>();
```

**Composer1 Agentへの指示**:
```
Add the new services (ICommandParser, IVideoSegmentManager, ICommandExecutor) to the DI container in App.xaml.cs ConfigureServices method.
```

---

## Phase 4: ViewModel層の拡張

### Step 4.1: TimelineViewModelの拡張

**目的**: TimelineViewModelにVideoSegment管理機能を追加

**編集ファイル**:
- `Features/Timeline/TimelineViewModel.cs` (既存ファイルの拡張)

**実装内容**:
```csharp
// コンストラクタに追加
private readonly IVideoSegmentManager _segmentManager;
private readonly ICommandExecutor _commandExecutor;
private readonly ICommandParser _commandParser;

// プロパティを追加
public ReadOnlyObservableCollection<VideoSegment> VideoSegments { get; }
private readonly ObservableCollection<VideoSegment> _videoSegments = new();

// 現在再生中のセグメント
public ReactiveProperty<VideoSegment?> CurrentPlayingSegment { get; }

// コンストラクタで初期化
public TimelineViewModel(
    IScrollCoordinator scrollCoordinator,
    ITimeRulerService timeRulerService,
    IThumbnailProvider thumbnailProvider,
    IVideoSegmentManager segmentManager,
    ICommandExecutor commandExecutor,
    ICommandParser commandParser)
{
    // ... 既存の初期化コード ...
    
    _segmentManager = segmentManager;
    _commandExecutor = commandExecutor;
    _commandParser = commandParser;
    
    VideoSegments = new ReadOnlyObservableCollection<VideoSegment>(_videoSegments);
    CurrentPlayingSegment = new ReactiveProperty<VideoSegment?>();
    
    // セグメント変更イベントの購読
    _segmentManager.SegmentAdded += OnSegmentAdded;
    _segmentManager.SegmentRemoved += OnSegmentRemoved;
    _segmentManager.SegmentUpdated += OnSegmentUpdated;
}

private void OnSegmentAdded(object? sender, VideoSegmentEventArgs e)
{
    Application.Current.Dispatcher.InvokeAsync(() =>
    {
        _videoSegments.Add(e.Segment);
    });
}

private void OnSegmentRemoved(object? sender, VideoSegmentEventArgs e)
{
    Application.Current.Dispatcher.InvokeAsync(() =>
    {
        var segment = _videoSegments.FirstOrDefault(s => s.Id == e.Segment.Id);
        if (segment != null)
        {
            _videoSegments.Remove(segment);
        }
    });
}

private void OnSegmentUpdated(object? sender, VideoSegmentEventArgs e)
{
    Application.Current.Dispatcher.InvokeAsync(() =>
    {
        var index = _videoSegments.ToList().FindIndex(s => s.Id == e.Segment.Id);
        if (index >= 0)
        {
            _videoSegments[index] = e.Segment;
        }
    });
}

// セグメントをクリックした時の処理
public void OnSegmentClicked(VideoSegment segment)
{
    // 現在再生中のセグメントを停止
    var currentSegment = CurrentPlayingSegment.Value;
    if (currentSegment != null && currentSegment.Id != segment.Id)
    {
        currentSegment.State = SegmentState.Stopped;
        // TODO: 実際のプレーヤーを停止して最初のフレームに戻す
    }
    
    // クリックされたセグメントを再生
    segment.State = SegmentState.Playing;
    CurrentPlayingSegment.Value = segment;
    // TODO: 実際のプレーヤーを再生開始
}

// エンターキーで分割
public void CutAtCurrentTime(double currentTime)
{
    var command = new EditCommand
    {
        Type = CommandType.Cut,
        Time = currentTime
    };
    _commandExecutor.ExecuteCommand(command);
}
```

**Composer1 Agentへの指示**:
```
Extend TimelineViewModel.cs to add VideoSegment management functionality:
1. Add IVideoSegmentManager, ICommandExecutor, ICommandParser dependencies
2. Add VideoSegments collection property
3. Add CurrentPlayingSegment property
4. Add event handlers for segment changes
5. Add OnSegmentClicked method
6. Add CutAtCurrentTime method
```

---

## Phase 5: View層の実装

### Step 5.1: VideoSegmentViewの作成

**目的**: 個別のセグメントを表示するViewを作成

**作成ファイル**:
- `Features/Timeline/VideoSegmentView.xaml` (新規)
- `Features/Timeline/VideoSegmentView.xaml.cs` (新規)

**実装内容**:
```xml
<!-- VideoSegmentView.xaml -->
<UserControl x:Class="Wideor.App.Features.Timeline.VideoSegmentView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vlc="clr-namespace:LibVLCSharp.WPF;assembly=LibVLCSharp.WPF"
             xmlns:domain="clr-namespace:Wideor.App.Shared.Domain;assembly=Wideor.App.Shared">
    <Border Background="#2A2A2A"
            BorderBrush="#444444"
            BorderThickness="0,0,0,1"
            MouseDown="OnSegmentClicked">
        <Grid>
            <vlc:VideoView x:Name="VideoPlayer"
                          MediaPlayer="{Binding MediaPlayer}"
                          Visibility="{Binding IsVideoVisible, Converter={StaticResource BooleanToVisibilityConverter}}" />
            
            <!-- 非表示時のオーバーレイ -->
            <Border Background="#80000000"
                    Visibility="{Binding IsHidden, Converter={StaticResource BooleanToVisibilityConverter}}">
                <TextBlock Text="非表示"
                          Foreground="Gray"
                          HorizontalAlignment="Center"
                          VerticalAlignment="Center" />
            </Border>
            
            <!-- 停止時の最初のフレーム表示 -->
            <Image x:Name="ThumbnailImage"
                   Visibility="{Binding IsThumbnailVisible, Converter={StaticResource BooleanToVisibilityConverter}}" />
        </Grid>
    </Border>
</UserControl>
```

```csharp
// VideoSegmentView.xaml.cs
namespace Wideor.App.Features.Timeline
{
    public partial class VideoSegmentView : UserControl
    {
        public VideoSegment Segment
        {
            get => (VideoSegment)GetValue(SegmentProperty);
            set => SetValue(SegmentProperty, value);
        }

        public static readonly DependencyProperty SegmentProperty =
            DependencyProperty.Register(nameof(Segment), typeof(VideoSegment), typeof(VideoSegmentView),
                new PropertyMetadata(null, OnSegmentChanged));

        private static void OnSegmentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VideoSegmentView view)
            {
                view.UpdateView();
            }
        }

        private void UpdateView()
        {
            // セグメントの状態に応じてViewを更新
            // TODO: 実装
        }

        private void OnSegmentClicked(object sender, MouseButtonEventArgs e)
        {
            if (Segment != null)
            {
                // TimelineViewModelに通知
                // TODO: 実装
            }
        }
    }
}
```

**Composer1 Agentへの指示**:
```
Create VideoSegmentView.xaml and VideoSegmentView.xaml.cs files in Features/Timeline/ directory.
The view should display a video segment with support for LibVLCSharp VideoView, hidden state overlay, and click handling.
```

---

### Step 5.2: FilmStripViewの改修

**目的**: FilmStripViewをVideoSegment表示用に改修

**編集ファイル**:
- `Features/Timeline/FilmStripView.xaml` (既存ファイルの改修)

**変更内容**:
```xml
<!-- ItemsSourceをThumbnailItemsからVideoSegmentsに変更 -->
<ListBox x:Name="FilmStripListBox"
         ItemsSource="{Binding VideoSegments}"
         ItemTemplate="{StaticResource VideoSegmentTemplate}">
    <!-- ... -->
</ListBox>

<!-- DataTemplateを追加 -->
<DataTemplate x:Key="VideoSegmentTemplate" DataType="{x:Type domain:VideoSegment}">
    <local:VideoSegmentView Segment="{Binding}" />
</DataTemplate>
```

**Composer1 Agentへの指示**:
```
Modify FilmStripView.xaml to use VideoSegments instead of ThumbnailItems.
Add VideoSegmentTemplate DataTemplate that uses VideoSegmentView.
```

---

## Composer1 Agent実行手順

### 実行方法

1. **Phase 1を実行**:
   ```
   Step 1.1から1.3までを順番に実行してください。
   各ステップで指定されたファイルを作成し、実装内容を反映してください。
   ```

2. **Phase 2を実行**:
   ```
   Step 2.1と2.2を実行してください。
   インターフェースと実装クラスを作成してください。
   ```

3. **Phase 3を実行**:
   ```
   App.xaml.csにDI登録を追加してください。
   ```

4. **Phase 4を実行**:
   ```
   TimelineViewModelを拡張してください。
   ```

5. **Phase 5を実行**:
   ```
   VideoSegmentViewを作成し、FilmStripViewを改修してください。
   ```

### 注意事項

- 各ステップは独立して実行可能ですが、依存関係がある場合は順番に実行してください
- エラーが発生した場合は、そのステップを修正してから次に進んでください
- 既存のコードを壊さないように注意してください

---

## 次のステップ

Phase 1から順番に実行を開始してください。各ステップが完了したら、次のステップに進みます。
