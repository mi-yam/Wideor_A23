# 動画フィルムエリアとテキストエリア 詳細実装ロジック

## 目次
1. [Phase 1: コアシステム - Header/Body パース機能](#phase1-0)
2. [Phase 1: コアシステム - テキスト変更監視と自動実行](#phase1-1)
3. [Phase 1: コアシステム - セパレータ形式のパース](#phase1-2)
4. [Phase 1: コアシステム - リンクエリアの実装](#phase1-3)
5. [Phase 2: ユーザビリティ - エンターキーでの分割](#phase2-1)
6. [Phase 2: ユーザビリティ - グレーアウト表示](#phase2-2)
7. [Phase 2: ユーザビリティ - スナップスクロール](#phase2-3)
8. [Phase 3: 拡張機能 - シアター再生モード](#phase3-1)

---

<a name="phase1-0"></a>
## 1. Header/Body パース機能の実装ロジック

### 1.1 概要
テキストエディタの内容をHeader（プロジェクト設定）とBody（編集操作）に分割してパースする仕組みを実装します。

### 1.2 処理フロー

```
[テキスト全体]
    ↓
[行ごとに分割]
    ↓
[===（区切り文字）を検索]
    ↓
┌────────────────┐
│ 区切り文字発見？│
└────┬───────────┘
     │Yes: Header と Body に分割
     ↓
┌────────┴────────┐
│                 │
↓                 ↓
[Header部分]    [Body部分]
    ↓                 ↓
[HeaderParser]    [CommandParser/SceneParser]
    ↓                 ↓
[ProjectConfig]   [EditCommand/SceneBlock]
```

### 1.3 ProjectConfig データモデル

```csharp
/// <summary>
/// プロジェクト設定（Header から生成）
/// </summary>
public class ProjectConfig : INotifyPropertyChanged
{
    private string _projectName = "無題のプロジェクト";
    private int _resolutionWidth = 1920;
    private int _resolutionHeight = 1080;
    private int _frameRate = 30;
    private string _defaultFont = "メイリオ";
    private int _defaultFontSize = 24;
    private string _defaultTitleColor = "#FFFFFF";
    private string _defaultSubtitleColor = "#FFFFFF";
    private double _defaultBackgroundAlpha = 0.8;
    
    public string ProjectName
    {
        get => _projectName;
        set { _projectName = value; OnPropertyChanged(nameof(ProjectName)); }
    }
    
    public int ResolutionWidth
    {
        get => _resolutionWidth;
        set { _resolutionWidth = value; OnPropertyChanged(nameof(ResolutionWidth)); }
    }
    
    public int ResolutionHeight
    {
        get => _resolutionHeight;
        set { _resolutionHeight = value; OnPropertyChanged(nameof(ResolutionHeight)); }
    }
    
    public int FrameRate
    {
        get => _frameRate;
        set { _frameRate = value; OnPropertyChanged(nameof(FrameRate)); }
    }
    
    public string DefaultFont
    {
        get => _defaultFont;
        set { _defaultFont = value; OnPropertyChanged(nameof(DefaultFont)); }
    }
    
    public int DefaultFontSize
    {
        get => _defaultFontSize;
        set { _defaultFontSize = value; OnPropertyChanged(nameof(DefaultFontSize)); }
    }
    
    public string DefaultTitleColor
    {
        get => _defaultTitleColor;
        set { _defaultTitleColor = value; OnPropertyChanged(nameof(DefaultTitleColor)); }
    }
    
    public string DefaultSubtitleColor
    {
        get => _defaultSubtitleColor;
        set { _defaultSubtitleColor = value; OnPropertyChanged(nameof(DefaultSubtitleColor)); }
    }
    
    public double DefaultBackgroundAlpha
    {
        get => _defaultBackgroundAlpha;
        set { _defaultBackgroundAlpha = value; OnPropertyChanged(nameof(DefaultBackgroundAlpha)); }
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
```

### 1.4 IHeaderParser インターフェース

```csharp
/// <summary>
/// Header パーサーのインターフェース
/// </summary>
public interface IHeaderParser
{
    /// <summary>
    /// テキストから Header をパースして ProjectConfig を生成します
    /// </summary>
    /// <param name="text">パース対象のテキスト全体</param>
    /// <returns>ProjectConfig と Body 開始行番号のタプル</returns>
    (ProjectConfig config, int bodyStartLine) ParseHeader(string text);
}
```

### 1.5 HeaderParser の実装ロジック

```csharp
public class HeaderParser : IHeaderParser
{
    // Header コマンドの正規表現パターン
    private static readonly Regex ProjectPattern = new Regex(
        @"^\s*PROJECT\s+""(.+)""$", 
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    private static readonly Regex ResolutionPattern = new Regex(
        @"^\s*RESOLUTION\s+(\d+)x(\d+)$", 
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    private static readonly Regex FrameRatePattern = new Regex(
        @"^\s*FRAMERATE\s+(\d+)$", 
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    private static readonly Regex DefaultFontPattern = new Regex(
        @"^\s*DEFAULT_FONT\s+""(.+)""$", 
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    private static readonly Regex DefaultFontSizePattern = new Regex(
        @"^\s*DEFAULT_FONT_SIZE\s+(\d+)$", 
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    private static readonly Regex DefaultTitleColorPattern = new Regex(
        @"^\s*DEFAULT_TITLE_COLOR\s+#([0-9A-Fa-f]{6})$", 
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    private static readonly Regex DefaultSubtitleColorPattern = new Regex(
        @"^\s*DEFAULT_SUBTITLE_COLOR\s+#([0-9A-Fa-f]{6})$", 
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    private static readonly Regex DefaultBackgroundAlphaPattern = new Regex(
        @"^\s*DEFAULT_BACKGROUND_ALPHA\s+(0?\.\d+|1\.0|0|1)$", 
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    // Header と Body の区切り
    private static readonly Regex SeparatorPattern = new Regex(
        @"^={3,}$", 
        RegexOptions.Compiled);
    
    /// <summary>
    /// テキストから Header をパースして ProjectConfig を生成します
    /// </summary>
    public (ProjectConfig config, int bodyStartLine) ParseHeader(string text)
    {
        var config = new ProjectConfig();
        
        if (string.IsNullOrEmpty(text))
        {
            return (config, 0);
        }
        
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        int bodyStartLine = 0;
        
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            
            // 区切り文字を検出（=== で Header と Body を分離）
            if (SeparatorPattern.IsMatch(line))
            {
                bodyStartLine = i + 1;
                LogHelper.WriteLog(
                    "HeaderParser:ParseHeader",
                    "Header/Body separator found",
                    new { separatorLine = i, bodyStartLine = bodyStartLine });
                break;
            }
            
            // 各 Header コマンドをパース
            ParseHeaderCommand(line, config);
        }
        
        LogHelper.WriteLog(
            "HeaderParser:ParseHeader",
            "Header parsing completed",
            new { 
                projectName = config.ProjectName,
                resolution = $"{config.ResolutionWidth}x{config.ResolutionHeight}",
                frameRate = config.FrameRate,
                bodyStartLine = bodyStartLine 
            });
        
        return (config, bodyStartLine);
    }
    
    /// <summary>
    /// 1行の Header コマンドをパースして ProjectConfig に反映します
    /// </summary>
    private void ParseHeaderCommand(string line, ProjectConfig config)
    {
        // 空行やコメント行はスキップ
        var trimmedLine = line.Trim();
        if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
        {
            return;
        }
        
        // PROJECT コマンド
        var projectMatch = ProjectPattern.Match(line);
        if (projectMatch.Success)
        {
            config.ProjectName = projectMatch.Groups[1].Value;
            return;
        }
        
        // RESOLUTION コマンド
        var resolutionMatch = ResolutionPattern.Match(line);
        if (resolutionMatch.Success)
        {
            config.ResolutionWidth = int.Parse(resolutionMatch.Groups[1].Value);
            config.ResolutionHeight = int.Parse(resolutionMatch.Groups[2].Value);
            return;
        }
        
        // FRAMERATE コマンド
        var frameRateMatch = FrameRatePattern.Match(line);
        if (frameRateMatch.Success)
        {
            config.FrameRate = int.Parse(frameRateMatch.Groups[1].Value);
            return;
        }
        
        // DEFAULT_FONT コマンド
        var defaultFontMatch = DefaultFontPattern.Match(line);
        if (defaultFontMatch.Success)
        {
            config.DefaultFont = defaultFontMatch.Groups[1].Value;
            return;
        }
        
        // DEFAULT_FONT_SIZE コマンド
        var defaultFontSizeMatch = DefaultFontSizePattern.Match(line);
        if (defaultFontSizeMatch.Success)
        {
            config.DefaultFontSize = int.Parse(defaultFontSizeMatch.Groups[1].Value);
            return;
        }
        
        // DEFAULT_TITLE_COLOR コマンド
        var defaultTitleColorMatch = DefaultTitleColorPattern.Match(line);
        if (defaultTitleColorMatch.Success)
        {
            config.DefaultTitleColor = "#" + defaultTitleColorMatch.Groups[1].Value.ToUpper();
            return;
        }
        
        // DEFAULT_SUBTITLE_COLOR コマンド
        var defaultSubtitleColorMatch = DefaultSubtitleColorPattern.Match(line);
        if (defaultSubtitleColorMatch.Success)
        {
            config.DefaultSubtitleColor = "#" + defaultSubtitleColorMatch.Groups[1].Value.ToUpper();
            return;
        }
        
        // DEFAULT_BACKGROUND_ALPHA コマンド
        var defaultBackgroundAlphaMatch = DefaultBackgroundAlphaPattern.Match(line);
        if (defaultBackgroundAlphaMatch.Success)
        {
            config.DefaultBackgroundAlpha = double.Parse(defaultBackgroundAlphaMatch.Groups[1].Value);
            return;
        }
    }
}
```

### 1.6 EditorViewModel への統合

```csharp
public class EditorViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IHeaderParser _headerParser;
    private readonly ICommandParser _commandParser;
    private readonly ISceneParser _sceneParser;
    private readonly ICommandExecutor _commandExecutor;
    private readonly IVideoSegmentManager _segmentManager;
    
    /// <summary>
    /// プロジェクト設定（Header から生成）
    /// </summary>
    public ReactiveProperty<ProjectConfig> ProjectConfig { get; }
    
    public EditorViewModel(
        IHeaderParser headerParser,
        ICommandParser commandParser,
        ISceneParser sceneParser,
        ICommandExecutor commandExecutor,
        IVideoSegmentManager segmentManager)
    {
        _headerParser = headerParser ?? throw new ArgumentNullException(nameof(headerParser));
        _commandParser = commandParser ?? throw new ArgumentNullException(nameof(commandParser));
        _sceneParser = sceneParser ?? throw new ArgumentNullException(nameof(sceneParser));
        _commandExecutor = commandExecutor ?? throw new ArgumentNullException(nameof(commandExecutor));
        _segmentManager = segmentManager ?? throw new ArgumentNullException(nameof(segmentManager));
        
        // プロジェクト設定の初期化
        ProjectConfig = new ReactiveProperty<ProjectConfig>(new ProjectConfig())
            .AddTo(_disposables);
        
        // テキスト変更を監視
        Text
            .Throttle(TimeSpan.FromMilliseconds(500))
            .ObserveOn(SynchronizationContext.Current)
            .Subscribe(text => OnTextChanged(text))
            .AddTo(_disposables);
    }
    
    /// <summary>
    /// テキストが変更された時の処理
    /// </summary>
    private void OnTextChanged(string text)
    {
        if (_isParsing) return;
        
        try
        {
            _isParsing = true;
            
            LogHelper.WriteLog(
                "EditorViewModel:OnTextChanged",
                "Text changed, starting parse",
                new { textLength = text.Length });
            
            // ステップ1: Header をパース
            var (projectConfig, bodyStartLine) = _headerParser.ParseHeader(text);
            
            // プロジェクト設定を更新
            ProjectConfig.Value = projectConfig;
            
            LogHelper.WriteLog(
                "EditorViewModel:OnTextChanged",
                "Header parsed",
                new { 
                    projectName = projectConfig.ProjectName,
                    bodyStartLine = bodyStartLine 
                });
            
            // ステップ2: Body 部分のテキストを抽出
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var bodyLines = lines.Skip(bodyStartLine);
            var bodyText = string.Join("\n", bodyLines);
            
            // ステップ3: Body のコマンドをパース
            var commands = _commandParser.ParseCommands(bodyText);
            
            // ステップ4: コマンドのハッシュ値を計算（差分検出）
            var currentHash = CalculateCommandsHash(commands);
            
            // ステップ5: 前回と同じならスキップ
            if (currentHash == _previousCommandsHash)
            {
                LogHelper.WriteLog(
                    "EditorViewModel:OnTextChanged",
                    "Commands unchanged, skipping execution",
                    null);
                return;
            }
            
            _previousCommandsHash = currentHash;
            
            // ステップ6: Body のシーンをパース
            var scenes = _sceneParser.ParseScenes(bodyText);
            
            // ステップ7: セグメントマネージャーをクリア
            _segmentManager.Clear();
            
            // ステップ8: コマンドを順次実行
            _commandExecutor.ExecuteCommands(commands);
            
            // ステップ9: シーンブロックを更新
            UpdateSceneBlocks(scenes);
            
            LogHelper.WriteLog(
                "EditorViewModel:OnTextChanged",
                "Parse and execution completed",
                new { commandCount = commands.Count, sceneCount = scenes.Count });
        }
        catch (Exception ex)
        {
            LogHelper.WriteLog(
                "EditorViewModel:OnTextChanged",
                "Error during parse and execution",
                new { exceptionType = ex.GetType().Name, message = ex.Message });
            
            NotifyError($"テキストの解析に失敗しました: {ex.Message}");
        }
        finally
        {
            _isParsing = false;
        }
    }
}
```

### 1.7 デフォルト値の適用

Header が存在しない場合（区切り文字 `===` がない場合）は、すべてデフォルト値が使用されます：

- プロジェクト名: "無題のプロジェクト"
- 解像度: 1920x1080
- フレームレート: 30 fps
- フォント: "メイリオ"
- フォントサイズ: 24
- 題名色: #FFFFFF（白）
- 字幕色: #FFFFFF（白）
- 背景透明度: 0.8

### 1.8 エラーハンドリング

- 不正な形式のコマンド: ログに記録してスキップ
- 区切り文字がない場合: テキスト全体を Body として扱う
- 無効な値: デフォルト値を使用

---

<a name="phase1-1"></a>
## 2. テキスト変更監視と自動実行の実装ロジック

### 2.1 概要
テキストエディタの内容が変更されたら、自動的にコマンドをパースして実行する仕組みを実装します。

### 2.2 処理フロー

```
[ユーザーがテキスト入力]
        ↓
[Text.Value が変更される（ReactiveProperty）]
        ↓
[Throttle（500ms待機）] ← 連続入力を抑制
        ↓
[OnTextChanged() が呼ばれる]
        ↓
[Header/Body 分割] ← 新規追加
        ↓
    ┌───────┴───────┐
    ↓               ↓
[HeaderParser]  [Body解析]
    ↓           ┌───┴────┐
[ProjectConfig] ↓        ↓
         [CommandParser] [SceneParser]
              ↓              ↓
         [EditCommand]  [SceneBlock]
              └──────┬───────┘
                     ↓
            [差分検出（最適化）]
                     ↓
            [コマンド実行]
                     ↓
         [VideoSegmentManager 更新]
                     ↓
         [UI に反映（ObservableCollection）]
```

### 2.3 EditorViewModel の実装ロジック

#### 2.3.1 プロパティとフィールド

```csharp
public class EditorViewModel : INotifyPropertyChanged, IDisposable
{
    // 依存性注入されるサービス
    private readonly ICommandParser _commandParser;
    private readonly ISceneParser _sceneParser;
    private readonly ICommandExecutor _commandExecutor;
    private readonly IVideoSegmentManager _segmentManager;
    private readonly CompositeDisposable _disposables = new();
    
    // テキスト内容（Source of Truth）
    public ReactiveProperty<string> Text { get; }
    
    // 前回パースしたコマンドのハッシュ（差分検出用）
    private string _previousCommandsHash = string.Empty;
    
    // パース中フラグ（再帰的な更新を防ぐ）
    private bool _isParsing = false;
}
```

#### 2.3.2 コンストラクタでのテキスト変更監視設定

```csharp
public EditorViewModel(
    ICommandParser commandParser,
    ISceneParser sceneParser,
    ICommandExecutor commandExecutor,
    IVideoSegmentManager segmentManager)
{
    _commandParser = commandParser ?? throw new ArgumentNullException(nameof(commandParser));
    _sceneParser = sceneParser ?? throw new ArgumentNullException(nameof(sceneParser));
    _commandExecutor = commandExecutor ?? throw new ArgumentNullException(nameof(commandExecutor));
    _segmentManager = segmentManager ?? throw new ArgumentNullException(nameof(segmentManager));
    
    // テキストプロパティの初期化
    Text = new ReactiveProperty<string>(string.Empty)
        .AddTo(_disposables);
    
    // テキスト変更を監視（Throttleで500ms遅延）
    Text
        .Throttle(TimeSpan.FromMilliseconds(500))
        .ObserveOn(SynchronizationContext.Current) // UIスレッドで実行
        .Subscribe(text => OnTextChanged(text))
        .AddTo(_disposables);
}
```

#### 2.3.3 テキスト変更時の処理ロジック

```csharp
/// <summary>
/// テキストが変更された時の処理
/// </summary>
private void OnTextChanged(string text)
{
    // 再帰的な更新を防ぐ
    if (_isParsing) return;
    
    try
    {
        _isParsing = true;
        
        // ログ出力（デバッグ用）
        LogHelper.WriteLog(
            "EditorViewModel:OnTextChanged",
            "Text changed, starting parse",
            new { textLength = text.Length });
        
        // ステップ1: コマンドをパース
        var commands = _commandParser.ParseCommands(text);
        
        // ステップ2: コマンドのハッシュ値を計算（差分検出）
        var currentHash = CalculateCommandsHash(commands);
        
        // ステップ3: 前回と同じならスキップ（パフォーマンス最適化）
        if (currentHash == _previousCommandsHash)
        {
            LogHelper.WriteLog(
                "EditorViewModel:OnTextChanged",
                "Commands unchanged, skipping execution",
                null);
            return;
        }
        
        // ステップ4: ハッシュ値を更新
        _previousCommandsHash = currentHash;
        
        // ステップ5: シーンをパース
        var scenes = _sceneParser.ParseScenes(text);
        
        // ステップ6: セグメントマネージャーをクリア
        _segmentManager.Clear();
        
        // ステップ7: コマンドを順次実行
        _commandExecutor.ExecuteCommands(commands);
        
        // ステップ8: シーンブロックを更新（TimelineViewModelで使用）
        UpdateSceneBlocks(scenes);
        
        LogHelper.WriteLog(
            "EditorViewModel:OnTextChanged",
            "Parse and execution completed",
            new { commandCount = commands.Count, sceneCount = scenes.Count });
    }
    catch (Exception ex)
    {
        LogHelper.WriteLog(
            "EditorViewModel:OnTextChanged",
            "Error during parse and execution",
            new { exceptionType = ex.GetType().Name, message = ex.Message });
        
        // エラーをユーザーに通知（MessageBoxやステータスバーなど）
        NotifyError($"テキストの解析に失敗しました: {ex.Message}");
    }
    finally
    {
        _isParsing = false;
    }
}
```

#### 2.3.4 コマンドハッシュ計算ロジック

```csharp
/// <summary>
/// コマンドリストからハッシュ値を計算（差分検出用）
/// </summary>
private string CalculateCommandsHash(List<EditCommand> commands)
{
    // コマンドの内容を文字列化
    var commandStrings = commands.Select(cmd => 
    {
        return cmd.Type switch
        {
            CommandType.Load => $"LOAD:{cmd.FilePath}",
            CommandType.Cut => $"CUT:{cmd.Time}",
            CommandType.Hide => $"HIDE:{cmd.StartTime}:{cmd.EndTime}",
            CommandType.Show => $"SHOW:{cmd.StartTime}:{cmd.EndTime}",
            CommandType.Delete => $"DELETE:{cmd.StartTime}:{cmd.EndTime}",
            _ => string.Empty
        };
    });
    
    // 全コマンドを結合してハッシュ化
    var combined = string.Join("|", commandStrings);
    
    using (var sha256 = SHA256.Create())
    {
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
        return Convert.ToBase64String(hashBytes);
    }
}
```

#### 2.3.5 シーンブロック更新ロジック

```csharp
/// <summary>
/// シーンブロックを更新（TimelineViewModelに通知）
/// </summary>
private void UpdateSceneBlocks(List<SceneBlock> scenes)
{
    // TimelineViewModelのSceneBlocksコレクションを更新
    // この処理は TimelineViewModel 側で実装
    
    // イベントを発火してTimelineViewModelに通知
    SceneBlocksUpdated?.Invoke(this, new SceneBlocksEventArgs(scenes));
}

// イベント定義
public event EventHandler<SceneBlocksEventArgs>? SceneBlocksUpdated;

public class SceneBlocksEventArgs : EventArgs
{
    public List<SceneBlock> SceneBlocks { get; }
    
    public SceneBlocksEventArgs(List<SceneBlock> sceneBlocks)
    {
        SceneBlocks = sceneBlocks;
    }
}
```

### 2.4 パフォーマンス最適化のポイント

#### 2.4.1 Throttleによる連続入力の抑制
- ユーザーが連続して入力している間は処理を実行しない
- 最後の入力から500ms経過後に処理を開始
- これにより、不要な再パース・再実行を防ぐ

#### 2.4.2 ハッシュ値による差分検出
- コマンド内容が前回と同じ場合は処理をスキップ
- セグメント再構築のコストを削減

#### 2.4.3 再帰的な更新の防止
- `_isParsing` フラグで再帰的な処理を防ぐ
- UI更新によるテキスト変更イベントの無限ループを防止

---

<a name="phase1-2"></a>
## 3. セパレータ形式のパース実装ロジック

### 3.1 概要
`--- [00:01:15.000 -> 00:01:20.500] ---` 形式のパラグラフセパレータをパースし、対応するコンテンツと関連付けます。

### 3.2 処理フロー

```
[テキスト全体]
    ↓
[行ごとに分割]
    ↓
[各行を順番に処理]
    ↓
[セパレータ行を検出]
    ↓
[正規表現でStartTime/EndTimeを抽出]
    ↓
[次のセパレータまでの行を収集]
    ↓
[SceneBlock オブジェクトを生成]
    ↓
[リストに追加]
    ↓
[全SceneBlockのリストを返す]
```

### 3.3 SceneParser の実装ロジック

#### 3.3.1 ISceneParser インターフェース

```csharp
/// <summary>
/// シーンパーサーのインターフェース
/// </summary>
public interface ISceneParser
{
    /// <summary>
    /// テキストからシーンブロックのリストをパースします
    /// </summary>
    List<SceneBlock> ParseScenes(string text);
}
```

#### 3.3.2 SceneParser クラス

```csharp
public class SceneParser : ISceneParser
{
    // セパレータ形式の正規表現
    // 形式: --- [00:01:15.000 -> 00:01:20.500] ---
    private static readonly Regex SeparatorPattern = new Regex(
        @"^[-]{3,}\s*\[(?<startHour>\d{2}):(?<startMin>\d{2}):(?<startSec>\d{2})\.(?<startMs>\d{3})\s*->\s*(?<endHour>\d{2}):(?<endMin>\d{2}):(?<endSec>\d{2})\.(?<endMs>\d{3})\]\s*[-]{3,}$",
        RegexOptions.Compiled);
    
    /// <summary>
    /// テキストからシーンブロックのリストをパースします
    /// </summary>
    public List<SceneBlock> ParseScenes(string text)
    {
        var scenes = new List<SceneBlock>();
        
        if (string.IsNullOrEmpty(text))
            return scenes;
        
        // 改行で分割（\r\n, \n の両方に対応）
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        
        // 各行を処理
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var match = SeparatorPattern.Match(line);
            
            if (match.Success)
            {
                // セパレータ行を発見
                var scene = ParseSceneBlock(lines, i, match);
                if (scene != null)
                {
                    scenes.Add(scene);
                }
            }
        }
        
        LogHelper.WriteLog(
            "SceneParser:ParseScenes",
            "Scenes parsed",
            new { sceneCount = scenes.Count });
        
        return scenes;
    }
    
    /// <summary>
    /// セパレータ行から SceneBlock を生成します
    /// </summary>
    private SceneBlock? ParseSceneBlock(string[] lines, int separatorIndex, Match match)
    {
        try
        {
            // StartTime/EndTime を抽出
            var startTime = ParseTimeFromMatch(
                match.Groups["startHour"].Value,
                match.Groups["startMin"].Value,
                match.Groups["startSec"].Value,
                match.Groups["startMs"].Value);
            
            var endTime = ParseTimeFromMatch(
                match.Groups["endHour"].Value,
                match.Groups["endMin"].Value,
                match.Groups["endSec"].Value,
                match.Groups["endMs"].Value);
            
            // 次のセパレータまでのコンテンツを収集
            var contentLines = new List<string>();
            int contentStartIndex = separatorIndex + 1;
            
            for (int j = contentStartIndex; j < lines.Length; j++)
            {
                // 次のセパレータに到達したら終了
                if (SeparatorPattern.IsMatch(lines[j]))
                    break;
                
                // LOADやCUTなどのコマンド行は除外
                if (IsCommandLine(lines[j]))
                    break;
                
                contentLines.Add(lines[j]);
            }
            
            // SceneBlock を生成
            var scene = new SceneBlock
            {
                StartTime = startTime,
                EndTime = endTime,
                CommandLineLineNumber = separatorIndex + 1, // 1ベースの行番号
                ContentText = string.Join("\n", contentLines).Trim()
            };
            
            return scene;
        }
        catch (Exception ex)
        {
            LogHelper.WriteLog(
                "SceneParser:ParseSceneBlock",
                "Failed to parse scene block",
                new { separatorIndex = separatorIndex, exceptionType = ex.GetType().Name, message = ex.Message });
            
            return null;
        }
    }
    
    /// <summary>
    /// 時間、分、秒、ミリ秒から TimeSpan を生成します
    /// </summary>
    private TimeSpan ParseTimeFromMatch(string hour, string minute, string second, string millisecond)
    {
        var h = int.Parse(hour);
        var m = int.Parse(minute);
        var s = int.Parse(second);
        var ms = int.Parse(millisecond);
        
        return new TimeSpan(0, h, m, s, ms);
    }
    
    /// <summary>
    /// 行がコマンド行かどうかを判定します
    /// </summary>
    private bool IsCommandLine(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith("LOAD ", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("CUT ", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("HIDE ", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("SHOW ", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("DELETE ", StringComparison.OrdinalIgnoreCase);
    }
}
```

### 3.4 SceneBlock とVideoSegment の関連付けロジック

#### 3.4.1 TimelineViewModel での関連付け

```csharp
public class TimelineViewModel : INotifyPropertyChanged, IDisposable
{
    // シーンブロックのコレクション
    public ObservableCollection<SceneBlock> SceneBlocks { get; }
    
    // EditorViewModelからのイベントを購読
    private void SubscribeToEditorViewModel()
    {
        if (_editorViewModel != null)
        {
            _editorViewModel.SceneBlocksUpdated += OnSceneBlocksUpdated;
        }
    }
    
    /// <summary>
    /// シーンブロックが更新された時の処理
    /// </summary>
    private void OnSceneBlocksUpdated(object? sender, SceneBlocksEventArgs e)
    {
        // UIスレッドで実行
        Dispatcher.InvokeAsync(() =>
        {
            // コレクションをクリアして再追加
            SceneBlocks.Clear();
            foreach (var scene in e.SceneBlocks)
            {
                SceneBlocks.Add(scene);
            }
            
            // リンクエリアの再描画をトリガー
            RequestLinkAreaRedraw();
        });
    }
    
    /// <summary>
    /// セグメントとシーンの対応関係を取得します
    /// </summary>
    public Dictionary<VideoSegment, List<SceneBlock>> GetSegmentSceneMapping()
    {
        var mapping = new Dictionary<VideoSegment, List<SceneBlock>>();
        
        foreach (var segment in VideoSegments)
        {
            // このセグメントに対応するシーンを検索
            var matchingScenes = SceneBlocks
                .Where(scene =>
                    scene.StartTime.TotalSeconds >= segment.StartTime &&
                    scene.EndTime.TotalSeconds <= segment.EndTime)
                .ToList();
            
            if (matchingScenes.Any())
            {
                mapping[segment] = matchingScenes;
            }
        }
        
        return mapping;
    }
}
```

---

<a name="phase1-3"></a>
## 4. リンクエリアの実装ロジック

### 4.1 概要
フィルムエリアのセグメントとテキストエリアのパラグラフを台形（四角形）で結び、視覚的に対応関係を示します。

### 3.2 処理フロー

```
[TimelineViewModel から描画要求]
    ↓
[LinkAreaView.DrawLinks() 呼び出し]
    ↓
[Canvas をクリア]
    ↓
[各シーンブロックについてループ]
    ↓
    ┌─────────────────┐
    │ シーンに対応する │
    │ セグメントを検索 │
    └────────┬────────┘
             ↓
    ┌─────────────────┐
    │ フィルムエリア側 │
    │ の座標を計算     │ (Y1_film, Y2_film)
    └────────┬────────┘
             ↓
    ┌─────────────────┐
    │ テキストエリア側 │
    │ の座標を計算     │ (Y1_text, Y2_text)
    └────────┬────────┘
             ↓
    ┌─────────────────┐
    │ Path オブジェクト│
    │ で台形を描画     │
    └────────┬────────┘
             ↓
    [Canvas に追加]
    ↓
[すべてのシーンを処理]
    ↓
[描画完了]
```

### 3.3 LinkAreaView の実装ロジック

#### 3.3.1 XAML定義

```xml
<UserControl x:Class="Wideor.App.Features.Timeline.LinkAreaView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Width="80">
    <Canvas x:Name="LinkCanvas" 
            Background="Transparent" 
            ClipToBounds="True">
        <!-- 台形はコードビハインドで動的に生成 -->
    </Canvas>
</UserControl>
```

#### 3.3.2 コードビハインド

```csharp
public partial class LinkAreaView : UserControl
{
    // 依存関係のプロパティ
    private TimelineViewModel? _viewModel;
    private ScrollViewer? _filmScrollViewer;
    private ScrollViewer? _textScrollViewer;
    
    public LinkAreaView()
    {
        InitializeComponent();
        
        // スクロール変更イベントを監視（再描画トリガー）
        Loaded += LinkAreaView_Loaded;
    }
    
    private void LinkAreaView_Loaded(object sender, RoutedEventArgs e)
    {
        // 親要素からScrollViewerを取得
        _filmScrollViewer = FindFilmScrollViewer();
        _textScrollViewer = FindTextScrollViewer();
        
        // スクロール変更を監視
        if (_filmScrollViewer != null)
        {
            _filmScrollViewer.ScrollChanged += (s, e) => RedrawLinks();
        }
        
        if (_textScrollViewer != null)
        {
            _textScrollViewer.ScrollChanged += (s, e) => RedrawLinks();
        }
    }
    
    /// <summary>
    /// ViewModelを設定します
    /// </summary>
    public void SetViewModel(TimelineViewModel viewModel)
    {
        _viewModel = viewModel;
        
        // VideoSegments と SceneBlocks の変更を監視
        if (_viewModel.VideoSegments is INotifyCollectionChanged videoSegments)
        {
            videoSegments.CollectionChanged += (s, e) => RedrawLinks();
        }
        
        if (_viewModel.SceneBlocks is INotifyCollectionChanged sceneBlocks)
        {
            sceneBlocks.CollectionChanged += (s, e) => RedrawLinks();
        }
    }
    
    /// <summary>
    /// リンクを再描画します（Throttleで60fps制限）
    /// </summary>
    private DateTime _lastRedrawTime = DateTime.MinValue;
    private const int MinRedrawIntervalMs = 16; // 約60fps
    
    private void RedrawLinks()
    {
        // Throttle: 連続呼び出しを制限
        var now = DateTime.Now;
        if ((now - _lastRedrawTime).TotalMilliseconds < MinRedrawIntervalMs)
        {
            return;
        }
        _lastRedrawTime = now;
        
        // UIスレッドで実行
        Dispatcher.InvokeAsync(() => DrawLinksInternal(), DispatcherPriority.Render);
    }
    
    /// <summary>
    /// リンクを描画します（内部処理）
    /// </summary>
    private void DrawLinksInternal()
    {
        if (_viewModel == null) return;
        
        // Canvasをクリア
        LinkCanvas.Children.Clear();
        
        // スクロールオフセットを取得
        double filmScrollOffset = _filmScrollViewer?.VerticalOffset ?? 0;
        double textScrollOffset = _textScrollViewer?.VerticalOffset ?? 0;
        
        // PixelsPerSecond を取得
        double pixelsPerSecond = _viewModel.PixelsPerSecond.Value;
        
        // 各シーンブロックについて台形を描画
        foreach (var scene in _viewModel.SceneBlocks)
        {
            // シーンに対応するセグメントを検索
            var matchingSegment = _viewModel.VideoSegments.FirstOrDefault(seg =>
                seg.StartTime <= scene.StartTime.TotalSeconds &&
                seg.EndTime >= scene.EndTime.TotalSeconds);
            
            if (matchingSegment != null)
            {
                DrawTrapezoid(
                    scene,
                    matchingSegment,
                    pixelsPerSecond,
                    filmScrollOffset,
                    textScrollOffset);
            }
        }
    }
    
    /// <summary>
    /// 台形を描画します
    /// </summary>
    private void DrawTrapezoid(
        SceneBlock scene,
        VideoSegment segment,
        double pixelsPerSecond,
        double filmScrollOffset,
        double textScrollOffset)
    {
        // フィルムエリア側のY座標（左端）
        double filmY1 = scene.StartTime.TotalSeconds * pixelsPerSecond - filmScrollOffset;
        double filmY2 = scene.EndTime.TotalSeconds * pixelsPerSecond - filmScrollOffset;
        
        // テキストエリア側のY座標（右端）
        double textY1 = GetTextLineY(scene.CommandLineLineNumber) - textScrollOffset;
        double textY2 = GetTextLineY(scene.CommandLineLineNumber + CountLines(scene.ContentText)) - textScrollOffset;
        
        // 表示範囲外の場合はスキップ（最適化）
        double canvasHeight = LinkCanvas.ActualHeight;
        if ((filmY2 < 0 && textY2 < 0) || (filmY1 > canvasHeight && textY1 > canvasHeight))
        {
            return; // 表示範囲外
        }
        
        // Path オブジェクトを作成
        var path = new Path
        {
            Stroke = new SolidColorBrush(Colors.Gray),
            StrokeThickness = 1,
            Fill = new SolidColorBrush(Color.FromArgb(30, 128, 128, 128)), // 半透明グレー
            StrokeLineJoin = PenLineJoin.Round
        };
        
        // PathGeometry で台形を定義
        var geometry = new PathGeometry();
        var figure = new PathFigure
        {
            StartPoint = new Point(0, filmY1), // 左上
            IsClosed = true
        };
        
        // 台形の4つの頂点を定義
        figure.Segments.Add(new LineSegment(new Point(80, textY1), true)); // 右上
        figure.Segments.Add(new LineSegment(new Point(80, textY2), true)); // 右下
        figure.Segments.Add(new LineSegment(new Point(0, filmY2), true));  // 左下
        
        geometry.Figures.Add(figure);
        path.Data = geometry;
        
        // Canvasに追加
        LinkCanvas.Children.Add(path);
    }
    
    /// <summary>
    /// テキストエディタの行番号からY座標を取得します
    /// </summary>
    private double GetTextLineY(int lineNumber)
    {
        // AvalonEdit の行番号からY座標を計算
        // 1行あたりの高さは約20px（フォントサイズによる）
        const double LineHeight = 20.0;
        
        return (lineNumber - 1) * LineHeight;
    }
    
    /// <summary>
    /// テキストの行数をカウントします
    /// </summary>
    private int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 1;
        
        return text.Split(new[] { '\n' }, StringSplitOptions.None).Length;
    }
    
    /// <summary>
    /// フィルムエリアのScrollViewerを検索します
    /// </summary>
    private ScrollViewer? FindFilmScrollViewer()
    {
        // 親要素のビジュアルツリーを検索
        // 実装は省略（VisualTreeHelperを使用）
        return null; // TODO: 実装
    }
    
    /// <summary>
    /// テキストエリアのScrollViewerを検索します
    /// </summary>
    private ScrollViewer? FindTextScrollViewer()
    {
        // 親要素のビジュアルツリーを検索
        // 実装は省略（VisualTreeHelperを使用）
        return null; // TODO: 実装
    }
}
```

### 3.4 座標計算の詳細

#### 3.4.1 フィルムエリア側の座標計算

```
filmY = 時間(秒) × PixelsPerSecond - スクロールオフセット

例:
- シーンの開始時間: 10.0秒
- PixelsPerSecond: 100
- スクロールオフセット: 500px
→ filmY1 = 10.0 × 100 - 500 = 500px
```

#### 3.4.2 テキストエリア側の座標計算

```
textY = (行番号 - 1) × 行の高さ - スクロールオフセット

例:
- セパレータの行番号: 5
- 行の高さ: 20px
- スクロールオフセット: 100px
→ textY1 = (5 - 1) × 20 - 100 = -20px
```

### 3.5 パフォーマンス最適化

#### 3.5.1 Throttleによる再描画制限
- スクロール変更時に毎回描画すると重い
- 最小16ms間隔（約60fps）で制限

#### 3.5.2 表示範囲外の台形をスキップ
- 台形の全頂点が表示範囲外の場合は描画しない
- Canvasのクリッピングで表示範囲外は自動的に切り取られる

---

<a name="phase2-1"></a>
## 5. エンターキーでの分割実装ロジック

### 5.1 概要
動画再生中にEnterキーを押すと、現在の再生位置で分割してテキストエディタにCUTコマンドを自動挿入します。

### 4.2 処理フロー

```
[ユーザーがEnterキーを押す]
    ↓
[PreviewKeyDown イベント発火]
    ↓
[現在再生中のセグメントをチェック]
    ↓
┌────────────────┐
│ 再生中？       │
└────┬───────────┘
     │No: 通常のEnter処理
     ↓Yes
[現在の再生位置を取得]
    ↓
[CUTコマンドの文字列を生成]
    例: "CUT 00:01:23.456\n"
    ↓
[カーソル位置にコマンドを挿入]
    ↓
[Enterキーの通常動作をキャンセル]
    ↓
[テキスト変更イベント発火]
    ↓
[自動パース＆実行]
    ↓
[セグメントが2つに分割される]
```

### 4.3 EditorView の実装ロジック

#### 4.3.1 キーイベントハンドラーの登録

```csharp
public partial class EditorView : UserControl
{
    private void EditorView_Loaded(object sender, RoutedEventArgs e)
    {
        // ...既存の初期化コード...
        
        // キーイベントを監視（PreviewKeyDownを使用）
        TextEditorControl.PreviewKeyDown += OnPreviewKeyDown;
    }
    
    private void EditorView_Unloaded(object sender, RoutedEventArgs e)
    {
        // イベント購読を解除
        TextEditorControl.PreviewKeyDown -= OnPreviewKeyDown;
        
        // ...既存のクリーンアップコード...
    }
}
```

#### 4.3.2 キーイベント処理ロジック

```csharp
/// <summary>
/// キーが押された時の処理（プレビュー段階）
/// </summary>
private void OnPreviewKeyDown(object sender, KeyEventArgs e)
{
    // Enterキーのチェック
    if (e.Key != Key.Enter)
        return;
    
    // ViewModelが設定されているかチェック
    if (ViewModel == null || ViewModel.TimelineViewModel == null)
        return;
    
    // 現在再生中のセグメントを取得
    var currentSegment = ViewModel.TimelineViewModel.CurrentPlayingSegment.Value;
    
    // セグメントが再生中かチェック
    if (currentSegment == null || currentSegment.State != SegmentState.Playing)
    {
        // 再生中でない場合は通常のEnter処理
        return;
    }
    
    // 現在の再生位置を取得
    var currentPosition = ViewModel.TimelineViewModel.CurrentPosition.Value;
    
    // CUTコマンドを挿入
    InsertCutCommand(currentPosition);
    
    // Enterキーの通常動作をキャンセル
    e.Handled = true;
    
    LogHelper.WriteLog(
        "EditorView:OnPreviewKeyDown",
        "CUT command inserted",
        new { currentPosition = currentPosition });
}
```

#### 4.3.3 CUTコマンド挿入ロジック

```csharp
/// <summary>
/// 現在のカーソル位置にCUTコマンドを挿入します
/// </summary>
private void InsertCutCommand(double positionInSeconds)
{
    // 時間を HH:MM:SS.mmm 形式にフォーマット
    var timeString = FormatTime(positionInSeconds);
    
    // CUTコマンドの文字列を生成
    var cutCommand = $"CUT {timeString}\n";
    
    // カーソル位置を取得
    int caretOffset = TextEditorControl.CaretOffset;
    
    // 現在の行の先頭にカーソルを移動（オプション）
    // コマンドは行の先頭から始めるべきため
    var currentLine = TextEditorControl.Document.GetLineByOffset(caretOffset);
    int insertOffset = currentLine.Offset;
    
    // テキストを挿入
    TextEditorControl.Document.Insert(insertOffset, cutCommand);
    
    // カーソルを挿入したコマンドの次の行に移動
    TextEditorControl.CaretOffset = insertOffset + cutCommand.Length;
    
    // 挿入した行を表示範囲に含める（スクロール）
    var insertedLine = TextEditorControl.Document.GetLineByOffset(insertOffset);
    TextEditorControl.ScrollToLine(insertedLine.LineNumber);
    
    LogHelper.WriteLog(
        "EditorView:InsertCutCommand",
        "CUT command inserted into document",
        new { timeString = timeString, insertOffset = insertOffset });
}
```

#### 4.3.4 時間フォーマットロジック

```csharp
/// <summary>
/// 秒数を HH:MM:SS.mmm 形式にフォーマットします
/// </summary>
private string FormatTime(double seconds)
{
    // TimeSpanに変換
    var timeSpan = TimeSpan.FromSeconds(seconds);
    
    // HH:MM:SS.mmm 形式で返す
    return $"{timeSpan.Hours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}.{timeSpan.Milliseconds:D3}";
}
```

### 4.4 ユーザーエクスペリエンスの考慮

#### 4.4.1 視覚的フィードバック
- コマンド挿入時に挿入行をハイライト表示（オプション）
- 挿入された行が表示範囲に入るように自動スクロール

#### 4.4.2 元に戻す（Undo）対応
- AvalonEdit の Undo 機能をそのまま利用可能
- Ctrl+Z で挿入したコマンドを削除できる

---

<a name="phase2-2"></a>
## 6. グレーアウト表示実装ロジック

### 6.1 概要
非表示（HIDE）セグメントを灰色の半透明オーバーレイで覆い、視覚的に非表示状態を示します。

### 5.2 処理フロー

```
[VideoSegment.Visible プロパティ変更]
    ↓
[INotifyPropertyChanged イベント発火]
    ↓
[バインディング更新]
    ↓
[InverseBooleanToVisibilityConverter 実行]
    ↓
┌────────────────────┐
│ Visible == false?  │
└────┬───────────────┘
     │No: オーバーレイ非表示
     ↓Yes
[オーバーレイ表示]
    ↓
[セグメント全体がグレーアウト]
```

### 5.3 VideoSegmentView の実装ロジック

#### 5.3.1 XAML定義

```xml
<UserControl x:Class="Wideor.App.Features.Timeline.VideoSegmentView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:converters="clr-namespace:Wideor.App.Features.Timeline.Converters"
             Height="480" MinWidth="640">
    
    <UserControl.Resources>
        <!-- Converter を定義 -->
        <converters:InverseBooleanToVisibilityConverter x:Key="InverseBoolToVis"/>
    </UserControl.Resources>
    
    <Grid>
        <!-- 既存のコンテンツ（動画プレーヤー等） -->
        <Border BorderBrush="Gray" BorderThickness="1">
            <Grid IsEnabled="{Binding Visible}">
                <!-- VideoView など -->
                <vlc:VideoView x:Name="VideoPlayer" />
                
                <!-- ヘッダー、コントロールバーなど -->
            </Grid>
        </Border>
        
        <!-- グレーアウトオーバーレイ（最前面） -->
        <Rectangle x:Name="GrayoutOverlay"
                   Fill="#80808080"
                   Visibility="{Binding Visible, Converter={StaticResource InverseBoolToVis}}"
                   IsHitTestVisible="False">
            <!-- IsHitTestVisible="False" でマウスイベントを通過させる -->
        </Rectangle>
        
        <!-- 非表示アイコン（オプション） -->
        <Viewbox Width="64" Height="64"
                 HorizontalAlignment="Center"
                 VerticalAlignment="Center"
                 Visibility="{Binding Visible, Converter={StaticResource InverseBoolToVis}}">
            <Path Data="M12,2C6.48,2 2,6.48 2,12s4.48,10 10,10 10,-4.48 10,-10S17.52,2 12,2zM4,12c0,-4.42 3.58,-8 8,-8 1.85,0 3.55,0.63 4.9,1.69L5.69,16.9C4.63,15.55 4,13.85 4,12zm8,8c-1.85,0 -3.55,-0.63 -4.9,-1.69L18.31,7.1C19.37,8.45 20,10.15 20,12c0,4.42 -3.58,8 -8,8z"
                  Fill="White"/>
        </Viewbox>
    </Grid>
</UserControl>
```

#### 5.3.2 VideoSegment クラスの実装

```csharp
public class VideoSegment : INotifyPropertyChanged
{
    private bool _visible = true;
    
    /// <summary>
    /// 表示/非表示フラグ
    /// </summary>
    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible != value)
            {
                _visible = value;
                OnPropertyChanged(nameof(Visible));
                
                // 非表示になった場合は停止状態に変更
                if (!_visible && State == SegmentState.Playing)
                {
                    State = SegmentState.Hidden;
                }
            }
        }
    }
    
    private SegmentState _state = SegmentState.Stopped;
    
    /// <summary>
    /// セグメントの状態
    /// </summary>
    public SegmentState State
    {
        get => _state;
        set
        {
            if (_state != value)
            {
                _state = value;
                OnPropertyChanged(nameof(State));
            }
        }
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
```

#### 5.3.3 InverseBooleanToVisibilityConverter

```csharp
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// bool を Visibility に変換（反転）
    /// true → Collapsed
    /// false → Visible
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }
        
        // デフォルトは非表示
        return Visibility.Collapsed;
    }
    
    /// <summary>
    /// Visibility を bool に逆変換（通常は使用しない）
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility != Visibility.Visible;
        }
        
        return true;
    }
}
```

### 5.4 CommandExecutor でのHIDE/SHOW処理

#### 5.4.1 HIDEコマンドの実装

```csharp
private void ExecuteHide(EditCommand command)
{
    if (!command.StartTime.HasValue || !command.EndTime.HasValue)
        throw new ArgumentException("StartTime and EndTime are required for HIDE command");
    
    // 指定時間範囲のセグメントを取得
    var segments = _segmentManager.GetSegmentsByTimeRange(
        command.StartTime.Value,
        command.EndTime.Value);
    
    LogHelper.WriteLog(
        "CommandExecutor:ExecuteHide",
        "Hiding segments",
        new { segmentCount = segments.Count, startTime = command.StartTime, endTime = command.EndTime });
    
    // 各セグメントを非表示に設定
    foreach (var segment in segments)
    {
        segment.Visible = false;
        segment.State = SegmentState.Hidden;
        
        // セグメント更新イベントを発火
        _segmentManager.UpdateSegment(segment);
    }
}
```

#### 5.4.2 SHOWコマンドの実装

```csharp
private void ExecuteShow(EditCommand command)
{
    if (!command.StartTime.HasValue || !command.EndTime.HasValue)
        throw new ArgumentException("StartTime and EndTime are required for SHOW command");
    
    // 指定時間範囲のセグメントを取得
    var segments = _segmentManager.GetSegmentsByTimeRange(
        command.StartTime.Value,
        command.EndTime.Value);
    
    LogHelper.WriteLog(
        "CommandExecutor:ExecuteShow",
        "Showing segments",
        new { segmentCount = segments.Count, startTime = command.StartTime, endTime = command.EndTime });
    
    // 各セグメントを表示に設定
    foreach (var segment in segments)
    {
        segment.Visible = true;
        segment.State = SegmentState.Stopped;
        
        // セグメント更新イベントを発火
        _segmentManager.UpdateSegment(segment);
    }
}
```

---

<a name="phase2-3"></a>
## 7. スナップスクロール実装ロジック

### 7.1 概要
PowerPointのスライドのように、マウスホイールでセグメント単位にスナップしてスクロールします。

### 6.2 処理フロー

```
[ユーザーがマウスホイールを回す]
    ↓
[PreviewMouseWheel イベント発火]
    ↓
[Ctrlキーが押されているかチェック]
    ↓
┌─────────────────┐
│ Ctrl押下？      │
└────┬────────────┘
     │Yes: ズーム処理（既存）
     ↓No
[イベントをハンドル（通常スクロール停止）]
    ↓
[現在表示中のセグメントインデックスを取得]
    ↓
[スクロール方向を判定]
    ↓
┌─────────────────┐
│ 上スクロール？  │
└────┬────────────┘
     │Yes: 前のセグメント
     ↓No: 次のセグメント
[対象セグメントのインデックスを計算]
    ↓
[範囲チェック（0 ～ totalClips-1）]
    ↓
[アニメーション付きでスクロール]
    ↓
[セグメントが中央に表示される]
```

### 6.3 FilmStripView の実装ロジック

#### 6.3.1 マウスホイールイベントハンドラー

```csharp
public partial class FilmStripView : UserControl
{
    // スナップアニメーション実行中フラグ
    private bool _isSnapping = false;
    
    private void FilmStripView_Loaded(object sender, RoutedEventArgs e)
    {
        // ...既存の初期化コード...
        
        // マウスホイールイベントを監視
        FilmStripScrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;
    }
    
    private void FilmStripView_Unloaded(object sender, RoutedEventArgs e)
    {
        // イベント購読を解除
        FilmStripScrollViewer.PreviewMouseWheel -= OnPreviewMouseWheel;
        
        // ...既存のクリーンアップコード...
    }
}
```

#### 6.3.2 マウスホイール処理ロジック

```csharp
/// <summary>
/// マウスホイールイベント処理
/// </summary>
private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
{
    // Ctrlキーが押されている場合はズーム処理（既存の処理に任せる）
    if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
    {
        return; // ズーム処理を継続
    }
    
    // スナップアニメーション実行中は処理しない
    if (_isSnapping)
    {
        e.Handled = true;
        return;
    }
    
    // 通常のスクロールをキャンセル
    e.Handled = true;
    
    // セグメントがない場合は何もしない
    if (_totalClips == 0)
        return;
    
    // 現在表示中のセグメントインデックスを取得
    int currentIndex = CurrentClipIndex;
    
    // スクロール方向に応じて次のセグメントインデックスを計算
    int nextIndex;
    if (e.Delta > 0)
    {
        // 上スクロール: 前のセグメント
        nextIndex = Math.Max(0, currentIndex - 1);
    }
    else
    {
        // 下スクロール: 次のセグメント
        nextIndex = Math.Min(_totalClips - 1, currentIndex + 1);
    }
    
    // 同じインデックスの場合は何もしない
    if (nextIndex == currentIndex)
        return;
    
    LogHelper.WriteLog(
        "FilmStripView:OnPreviewMouseWheel",
        "Snap scroll triggered",
        new { currentIndex = currentIndex, nextIndex = nextIndex, delta = e.Delta });
    
    // スナップスクロールを実行
    ScrollToClipIndexWithAnimation(nextIndex);
}
```

#### 6.3.3 スナップスクロールアニメーションロジック

```csharp
/// <summary>
/// 指定したインデックスのセグメントにアニメーション付きでスクロールします
/// </summary>
private void ScrollToClipIndexWithAnimation(int index)
{
    // 範囲チェック
    if (index < 0 || index >= _totalClips)
        return;
    
    // コンテナを取得
    var container = FilmStripItemsControl.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
    if (container == null)
    {
        LogHelper.WriteLog(
            "FilmStripView:ScrollToClipIndexWithAnimation",
            "Container not found",
            new { index = index });
        return;
    }
    
    try
    {
        _isSnapping = true;
        
        // コンテナの位置を取得
        var transform = container.TransformToAncestor(FilmStripScrollViewer);
        var position = transform.Transform(new Point(0, 0));
        
        // 現在のスクロール位置
        double currentOffset = FilmStripScrollViewer.VerticalOffset;
        
        // ターゲットのスクロール位置
        // セグメントをビューポートの中央に配置
        double viewportHeight = FilmStripScrollViewer.ViewportHeight;
        double containerHeight = container.ActualHeight;
        double targetOffset = currentOffset + position.Y - (viewportHeight - containerHeight) / 2;
        
        // スクロール範囲内に制限
        targetOffset = Math.Max(0, Math.Min(targetOffset, FilmStripScrollViewer.ScrollableHeight));
        
        LogHelper.WriteLog(
            "FilmStripView:ScrollToClipIndexWithAnimation",
            "Starting scroll animation",
            new { index = index, currentOffset = currentOffset, targetOffset = targetOffset });
        
        // アニメーションを作成
        var animation = new DoubleAnimation
        {
            From = currentOffset,
            To = targetOffset,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        
        // アニメーション完了時の処理
        animation.Completed += (s, e) =>
        {
            _isSnapping = false;
            
            LogHelper.WriteLog(
                "FilmStripView:ScrollToClipIndexWithAnimation",
                "Scroll animation completed",
                new { index = index, finalOffset = FilmStripScrollViewer.VerticalOffset });
        };
        
        // Storyboard を作成して開始
        var storyboard = new Storyboard();
        Storyboard.SetTarget(animation, FilmStripScrollViewer);
        Storyboard.SetTargetProperty(animation, new PropertyPath("VerticalOffset"));
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }
    catch (Exception ex)
    {
        _isSnapping = false;
        
        LogHelper.WriteLog(
            "FilmStripView:ScrollToClipIndexWithAnimation",
            "Error during scroll animation",
            new { index = index, exceptionType = ex.GetType().Name, message = ex.Message });
    }
}
```

#### 6.3.4 現在表示中のセグメントインデックス取得ロジック

```csharp
/// <summary>
/// 現在表示中の最初のセグメントインデックスを取得します
/// </summary>
public int CurrentClipIndex
{
    get
    {
        var visibleIndices = GetVisibleClipIndices();
        
        if (visibleIndices.Count == 0)
            return 0;
        
        // ビューポートの中央に最も近いセグメントを返す
        double viewportCenter = FilmStripScrollViewer.VerticalOffset + FilmStripScrollViewer.ViewportHeight / 2;
        
        int closestIndex = 0;
        double minDistance = double.MaxValue;
        
        foreach (var index in visibleIndices)
        {
            var container = FilmStripItemsControl.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
            if (container != null)
            {
                var transform = container.TransformToAncestor(FilmStripScrollViewer);
                var position = transform.Transform(new Point(0, 0));
                double containerCenter = position.Y + container.ActualHeight / 2;
                
                double distance = Math.Abs(containerCenter - viewportCenter);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestIndex = index;
                }
            }
        }
        
        return closestIndex;
    }
}
```

### 6.4 パフォーマンスとUXの考慮

#### 6.4.1 アニメーション実行中の制御
- `_isSnapping` フラグで連続スクロールを防止
- アニメーション完了まで次のスクロール要求を無視

#### 6.4.2 イージング関数
- `CubicEase.EaseInOut` で自然なアニメーション
- 開始と終了がスムーズ

#### 6.4.3 中央配置
- セグメントをビューポートの中央に配置
- 視認性が向上

---

<a name="phase3-1"></a>
## 8. シアター再生モード実装ロジック

### 8.1 概要
完成動画のプレビュー再生機能。表示されているセグメントのみを順番に再生し、非表示セグメントは自動的にスキップします。

### 7.2 処理フロー

```
[ユーザーがシアター再生ボタンをクリック]
    ↓
[TheaterPlayCommand 実行]
    ↓
[表示されているセグメントを取得]
    ↓
[開始時間順にソート]
    ↓
[最初のセグメントから順番に処理]
    ↓
    ┌──────────────────┐
    │ 各セグメントで    │
    │ 以下を実行       │
    └─────┬────────────┘
          ↓
    [動画ファイルをロード]
          ↓
    [開始位置にシーク]
          ↓
    [再生開始]
          ↓
    [セグメントの終了まで待機]
          ↓
    [停止]
          ↓
    [次のセグメントへ]
    ↓
[全セグメント再生完了]
    ↓
[シアター再生終了]
```

### 7.3 TimelineViewModel の実装ロジック

#### 7.3.1 シアター再生コマンドの定義

```csharp
public class TimelineViewModel : INotifyPropertyChanged, IDisposable
{
    /// <summary>
    /// シアター再生コマンド
    /// </summary>
    public ReactiveCommand TheaterPlayCommand { get; }
    
    /// <summary>
    /// シアター再生中フラグ
    /// </summary>
    public ReactiveProperty<bool> IsTheaterPlaying { get; }
    
    /// <summary>
    /// シアター再生の一時停止フラグ
    /// </summary>
    private bool _theaterPlayPaused = false;
    
    /// <summary>
    /// シアター再生のキャンセルトークン
    /// </summary>
    private CancellationTokenSource? _theaterCancellationTokenSource;
    
    private void InitializeTheaterPlayCommand()
    {
        IsTheaterPlaying = new ReactiveProperty<bool>(false)
            .AddTo(_disposables);
        
        // シアター再生が実行中でない場合のみ実行可能
        TheaterPlayCommand = IsTheaterPlaying
            .Select(playing => !playing)
            .ToReactiveCommand()
            .WithSubscribe(async () => await PlayTheaterModeAsync())
            .AddTo(_disposables);
    }
}
```

#### 7.3.2 シアター再生メインロジック

```csharp
/// <summary>
/// シアター再生モードを開始します
/// </summary>
private async Task PlayTheaterModeAsync()
{
    try
    {
        IsTheaterPlaying.Value = true;
        _theaterPlayPaused = false;
        _theaterCancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _theaterCancellationTokenSource.Token;
        
        LogHelper.WriteLog(
            "TimelineViewModel:PlayTheaterModeAsync",
            "Theater mode started",
            null);
        
        // 表示されているセグメントのみを取得
        var visibleSegments = VideoSegments
            .Where(s => s.Visible)
            .OrderBy(s => s.StartTime)
            .ToList();
        
        if (visibleSegments.Count == 0)
        {
            LogHelper.WriteLog(
                "TimelineViewModel:PlayTheaterModeAsync",
                "No visible segments found",
                null);
            return;
        }
        
        LogHelper.WriteLog(
            "TimelineViewModel:PlayTheaterModeAsync",
            "Playing visible segments",
            new { segmentCount = visibleSegments.Count });
        
        // 各セグメントを順番に再生
        for (int i = 0; i < visibleSegments.Count; i++)
        {
            // キャンセルチェック
            if (cancellationToken.IsCancellationRequested)
            {
                LogHelper.WriteLog(
                    "TimelineViewModel:PlayTheaterModeAsync",
                    "Theater mode cancelled",
                    new { currentSegmentIndex = i });
                break;
            }
            
            var segment = visibleSegments[i];
            
            // セグメントを再生
            await PlaySegmentAsync(segment, cancellationToken);
            
            // セグメント間の遷移待機（100ms）
            await Task.Delay(100, cancellationToken);
        }
        
        LogHelper.WriteLog(
            "TimelineViewModel:PlayTheaterModeAsync",
            "Theater mode completed",
            null);
    }
    catch (OperationCanceledException)
    {
        LogHelper.WriteLog(
            "TimelineViewModel:PlayTheaterModeAsync",
            "Theater mode was cancelled",
            null);
    }
    catch (Exception ex)
    {
        LogHelper.WriteLog(
            "TimelineViewModel:PlayTheaterModeAsync",
            "Error during theater mode",
            new { exceptionType = ex.GetType().Name, message = ex.Message });
    }
    finally
    {
        IsTheaterPlaying.Value = false;
        _theaterCancellationTokenSource?.Dispose();
        _theaterCancellationTokenSource = null;
    }
}
```

#### 7.3.3 セグメント再生ロジック

```csharp
/// <summary>
/// 指定したセグメントを再生します
/// </summary>
private async Task PlaySegmentAsync(VideoSegment segment, CancellationToken cancellationToken)
{
    LogHelper.WriteLog(
        "TimelineViewModel:PlaySegmentAsync",
        "Playing segment",
        new { segmentId = segment.Id, startTime = segment.StartTime, endTime = segment.EndTime });
    
    try
    {
        // 動画ファイルが異なる場合はロード
        if (_currentLoadedVideoPath != segment.VideoFilePath)
        {
            LogHelper.WriteLog(
                "TimelineViewModel:PlaySegmentAsync",
                "Loading video file",
                new { videoFilePath = segment.VideoFilePath });
            
            var loadResult = await _videoEngine.LoadAsync(segment.VideoFilePath);
            if (!loadResult)
            {
                LogHelper.WriteLog(
                    "TimelineViewModel:PlaySegmentAsync",
                    "Failed to load video file",
                    new { videoFilePath = segment.VideoFilePath });
                return;
            }
            
            _currentLoadedVideoPath = segment.VideoFilePath;
            MediaPlayer = _videoEngine.MediaPlayer;
            
            // ロード完了まで待機
            await Task.Delay(100, cancellationToken);
        }
        
        // セグメントの開始位置にシーク
        await _videoEngine.SeekAsync(segment.StartTime);
        
        // シーク完了まで待機
        await Task.Delay(100, cancellationToken);
        
        // 再生開始
        segment.State = SegmentState.Playing;
        CurrentPlayingSegment.Value = segment;
        _videoEngine.Play();
        
        LogHelper.WriteLog(
            "TimelineViewModel:PlaySegmentAsync",
            "Segment playback started",
            new { segmentId = segment.Id });
        
        // セグメントの終了まで待機
        var duration = segment.Duration;
        var endTime = DateTime.Now.AddSeconds(duration);
        
        while (DateTime.Now < endTime)
        {
            // キャンセルチェック
            if (cancellationToken.IsCancellationRequested)
            {
                _videoEngine.Stop();
                segment.State = SegmentState.Stopped;
                CurrentPlayingSegment.Value = null;
                return;
            }
            
            // 一時停止チェック
            if (_theaterPlayPaused)
            {
                _videoEngine.Pause();
                while (_theaterPlayPaused && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(100, cancellationToken);
                }
                if (!cancellationToken.IsCancellationRequested)
                {
                    _videoEngine.Play();
                }
            }
            
            // 現在の再生位置をチェック
            var currentPosition = _videoEngine.CurrentPosition.Value;
            if (currentPosition >= segment.EndTime)
            {
                // セグメントの終了時間に達したら終了
                break;
            }
            
            // 100ms待機
            await Task.Delay(100, cancellationToken);
        }
        
        // 停止
        _videoEngine.Stop();
        segment.State = SegmentState.Stopped;
        CurrentPlayingSegment.Value = null;
        
        LogHelper.WriteLog(
            "TimelineViewModel:PlaySegmentAsync",
            "Segment playback completed",
            new { segmentId = segment.Id });
    }
    catch (Exception ex)
    {
        LogHelper.WriteLog(
            "TimelineViewModel:PlaySegmentAsync",
            "Error during segment playback",
            new { segmentId = segment.Id, exceptionType = ex.GetType().Name, message = ex.Message });
        
        segment.State = SegmentState.Stopped;
        CurrentPlayingSegment.Value = null;
    }
}
```

#### 7.3.4 シアター再生の一時停止・再開

```csharp
/// <summary>
/// シアター再生を一時停止します
/// </summary>
public void PauseTheaterPlay()
{
    if (IsTheaterPlaying.Value)
    {
        _theaterPlayPaused = true;
        _videoEngine.Pause();
        
        LogHelper.WriteLog(
            "TimelineViewModel:PauseTheaterPlay",
            "Theater play paused",
            null);
    }
}

/// <summary>
/// シアター再生を再開します
/// </summary>
public void ResumeTheaterPlay()
{
    if (IsTheaterPlaying.Value && _theaterPlayPaused)
    {
        _theaterPlayPaused = false;
        _videoEngine.Play();
        
        LogHelper.WriteLog(
            "TimelineViewModel:ResumeTheaterPlay",
            "Theater play resumed",
            null);
    }
}

/// <summary>
/// シアター再生を停止します
/// </summary>
public void StopTheaterPlay()
{
    if (IsTheaterPlaying.Value)
    {
        _theaterCancellationTokenSource?.Cancel();
        
        LogHelper.WriteLog(
            "TimelineViewModel:StopTheaterPlay",
            "Theater play stopped",
            null);
    }
}
```

### 7.4 UI実装（ShellRibbon）

#### 7.4.1 リボンボタンの追加

```xml
<fluent:RibbonTabItem Header="確認">
    <fluent:RibbonGroupBox Header="プレビュー">
        <!-- シアター再生ボタン -->
        <fluent:Button Header="シアター再生"
                       Command="{Binding TimelineViewModel.TheaterPlayCommand}"
                       Icon="Play"
                       LargeIcon="Play"
                       Size="Large">
            <fluent:Button.Style>
                <Style TargetType="fluent:Button">
                    <Setter Property="Header" Value="シアター再生"/>
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding TimelineViewModel.IsTheaterPlaying.Value}" Value="True">
                            <Setter Property="Header" Value="再生中..."/>
                            <Setter Property="IsEnabled" Value="False"/>
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </fluent:Button.Style>
        </fluent:Button>
        
        <!-- 停止ボタン -->
        <fluent:Button Header="停止"
                       Command="{Binding StopTheaterPlayCommand}"
                       Icon="Stop"
                       IsEnabled="{Binding TimelineViewModel.IsTheaterPlaying.Value}"/>
    </fluent:RibbonGroupBox>
</fluent:RibbonTabItem>
```

### 7.5 エラーハンドリングとユーザーフィードバック

#### 7.5.1 動画ロード失敗時
- ログに記録
- 次のセグメントにスキップ
- ステータスバーにエラー表示

#### 7.5.2 キャンセル時
- 即座に再生を停止
- リソースをクリーンアップ
- UI状態をリセット

---

## まとめ

以上の実装ロジックにより、テキストファースト設計に基づいた直感的な動画編集システムが完成します。

### 実装の特徴
1. **リアクティブプログラミング**: Reactive.Bindingsによる宣言的な実装
2. **非同期処理**: async/awaitによるスムーズなUI
3. **パフォーマンス最適化**: Throttle、差分検出、仮想化
4. **エラーハンドリング**: 包括的なログとエラー処理
5. **ユーザビリティ**: アニメーション、視覚的フィードバック

### 次のステップ
1. Phase 1の実装から開始
2. 各機能の単体テスト作成
3. 統合テストとパフォーマンステスト
4. ユーザーフィードバックに基づく改善
