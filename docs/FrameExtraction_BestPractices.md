# LibVLCSharpで30fpsフレーム抽出のベストプラクティス

## 概要

LibVLCSharpを使用して動画から30fpsでフレームを抽出する方法について、パフォーマンスとメモリ効率を考慮したベストプラクティスを説明します。

## アプローチの比較

### 1. Video Callbacks方式（推奨）⭐

**メリット:**
- 最も効率的（メモリマップドファイルを使用）
- リアルタイム処理が可能
- メモリ使用量が少ない
- フレームレート制御が柔軟

**デメリット:**
- 実装が複雑
- スレッド安全性に注意が必要

### 2. TakeSnapshot方式（現在のThumbnailProviderで使用）

**メリット:**
- 実装が簡単
- 特定の時刻のフレームを確実に取得できる

**デメリット:**
- 30fpsで連続取得するには非効率（シークが多すぎる）
- ファイルI/Oが発生（一時ファイル作成）
- パフォーマンスが低い

### 3. シーク + TakeSnapshot方式

**メリット:**
- 実装が比較的簡単
- 任意の時刻のフレームを取得可能

**デメリット:**
- 30fpsで連続取得するには非効率
- シーク処理が重い

## 推奨実装：Video Callbacks方式

### 基本構造

```csharp
using LibVLCSharp.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

public class FrameExtractor
{
    private const uint Width = 1920;  // 動画の幅
    private const uint Height = 1080; // 動画の高さ
    private const uint BytePerPixel = 4; // RGBA32
    private const double TargetFrameRate = 30.0; // 30fps
    private const double FrameInterval = 1.0 / TargetFrameRate; // 約0.033秒

    // 32バイトアライメント（VLCのパフォーマンス要件）
    private static readonly uint Pitch = Align(Width * BytePerPixel);
    private static readonly uint Lines = Align(Height);

    private static uint Align(uint size) => (size % 32 == 0) ? size : ((size / 32) + 1) * 32;

    private MemoryMappedFile? _currentMappedFile;
    private MemoryMappedViewAccessor? _currentMappedViewAccessor;
    private readonly ConcurrentQueue<FrameData> _framesToProcess = new();
    private long _frameCounter = 0;
    private DateTime _lastFrameTime = DateTime.MinValue;
    private readonly object _lockObject = new object();
```

### フレームレート制御の実装

```csharp
private IntPtr Lock(IntPtr opaque, IntPtr planes)
{
    lock (_lockObject)
    {
        // メモリマップドファイルを作成
        _currentMappedFile = MemoryMappedFile.CreateNew(null, Pitch * Lines);
        _currentMappedViewAccessor = _currentMappedFile.CreateViewAccessor();
        
        // VLCにバッファのポインタを渡す
        Marshal.WriteIntPtr(planes, _currentMappedViewAccessor.SafeMemoryMappedViewHandle.DangerousGetHandle());
        
        return IntPtr.Zero;
    }
}

private void Display(IntPtr opaque, IntPtr picture)
{
    var now = DateTime.UtcNow;
    var timeSinceLastFrame = (now - _lastFrameTime).TotalSeconds;
    
    // 30fps制御: 前回のフレームから約0.033秒経過しているかチェック
    if (_lastFrameTime == DateTime.MinValue || timeSinceLastFrame >= FrameInterval)
    {
        lock (_lockObject)
        {
            if (_currentMappedFile != null && _currentMappedViewAccessor != null)
            {
                // フレームデータをキューに追加
                var frameData = new FrameData
                {
                    File = _currentMappedFile,
                    Accessor = _currentMappedViewAccessor,
                    FrameNumber = _frameCounter,
                    Timestamp = now
                };
                
                _framesToProcess.Enqueue(frameData);
                
                // 新しいバッファを準備（次のフレーム用）
                _currentMappedFile = null;
                _currentMappedViewAccessor = null;
                
                _lastFrameTime = now;
            }
        }
    }
    else
    {
        // 30fpsに達していない場合は、バッファを破棄
        lock (_lockObject)
        {
            _currentMappedViewAccessor?.Dispose();
            _currentMappedFile?.Dispose();
            _currentMappedFile = null;
            _currentMappedViewAccessor = null;
        }
    }
    
    _frameCounter++;
}
```

### フレーム処理の実装

```csharp
private async Task ProcessFramesAsync(string outputDirectory, CancellationToken cancellationToken)
{
    Directory.CreateDirectory(outputDirectory);
    
    while (!cancellationToken.IsCancellationRequested)
    {
        if (_framesToProcess.TryDequeue(out var frameData))
        {
            try
            {
                await ProcessFrameAsync(frameData, outputDirectory, cancellationToken);
            }
            finally
            {
                // リソースを確実に解放
                frameData.Accessor?.Dispose();
                frameData.File?.Dispose();
            }
        }
        else
        {
            // キューが空の場合は少し待機
            await Task.Delay(10, cancellationToken);
        }
    }
}

private async Task ProcessFrameAsync(FrameData frameData, string outputDirectory, CancellationToken cancellationToken)
{
    await Task.Run(() =>
    {
        using var image = new Image<Bgra32>((int)(Pitch / BytePerPixel), (int)Lines);
        using var sourceStream = frameData.File.CreateViewStream();
        
        // メモリマップドファイルから画像データを読み込む
        var mg = image.GetPixelMemoryGroup();
        for (int i = 0; i < mg.Count; i++)
        {
            sourceStream.Read(MemoryMarshal.AsBytes(mg[i].Span));
        }
        
        // 実際のサイズにクロップ
        image.Mutate(ctx => ctx.Crop((int)Width, (int)Height));
        
        // ファイルに保存
        var fileName = Path.Combine(outputDirectory, $"frame_{frameData.FrameNumber:00000}.jpg");
        using var outputFile = File.Open(fileName, FileMode.Create);
        image.SaveAsJpeg(outputFile);
        
    }, cancellationToken);
}
```

### 使用例

```csharp
public async Task ExtractFramesAt30fpsAsync(
    string videoFilePath, 
    string outputDirectory,
    CancellationToken cancellationToken = default)
{
    Core.Initialize();
    
    using var libvlc = new LibVLC("--intf=dummy", "--vout=dummy", "--quiet");
    using var mediaPlayer = new MediaPlayer(libvlc);
    
    // 動画を読み込み
    using var media = new Media(libvlc, videoFilePath, FromType.FromPath);
    media.AddOption(":no-audio"); // 音声なし
    
    // ビデオフォーマットを設定
    mediaPlayer.SetVideoFormat("RV32", Width, Height, Pitch);
    mediaPlayer.SetVideoCallbacks(Lock, null, Display);
    
    // フレーム処理をバックグラウンドで開始
    var processingTask = ProcessFramesAsync(outputDirectory, cancellationToken);
    
    // 再生開始
    mediaPlayer.Play(media);
    
    // 処理が完了するまで待機
    try
    {
        await processingTask;
    }
    catch (OperationCanceledException)
    {
        // キャンセル時は正常終了
    }
    
    mediaPlayer.Stop();
}
```

## 重要なポイント

### 1. メモリ管理

- **各フレームごとに新しいメモリマップドファイルを作成**する
- `Display`コールバック内で`CurrentMappedFile`を`null`に設定する前に、キューに追加する
- 処理が完了したら確実に`Dispose()`を呼ぶ

### 2. スレッド安全性

- `Lock`と`Display`は異なるスレッドから呼ばれる可能性がある
- `lock`ステートメントを使用してクリティカルセクションを保護する
- `ConcurrentQueue`を使用してスレッドセーフなキューイングを実現

### 3. フレームレート制御

- **時間ベースのサンプリング**: `DateTime`を使用してフレーム間隔を制御
- **フレームカウンター方式**: 固定間隔でフレームを取得（例: 100フレームごと）
- **ハイブリッド方式**: 時間とフレームカウンターの両方を考慮

### 4. パフォーマンス最適化

- **バッファサイズ**: PitchとLinesを32バイトアライメントにする
- **非同期処理**: フレームの処理を別スレッドで実行
- **キューサイズ制限**: メモリ使用量を制御するため、キューサイズに上限を設ける

## 30fps制御の実装パターン

### パターン1: 時間ベース（推奨）

```csharp
private DateTime _lastFrameTime = DateTime.MinValue;
private const double FrameInterval = 1.0 / 30.0; // 約0.033秒

private void Display(IntPtr opaque, IntPtr picture)
{
    var now = DateTime.UtcNow;
    if (_lastFrameTime == DateTime.MinValue || 
        (now - _lastFrameTime).TotalSeconds >= FrameInterval)
    {
        // フレームを処理
        ProcessFrame();
        _lastFrameTime = now;
    }
    else
    {
        // フレームをスキップ
        DiscardFrame();
    }
}
```

### パターン2: フレームカウンター方式

```csharp
private long _frameCounter = 0;
private const int FrameSkipCount = 1; // 元のフレームレートに応じて調整

private void Display(IntPtr opaque, IntPtr picture)
{
    if (_frameCounter % FrameSkipCount == 0)
    {
        // フレームを処理
        ProcessFrame();
    }
    else
    {
        // フレームをスキップ
        DiscardFrame();
    }
    _frameCounter++;
}
```

### パターン3: MediaPlayerのTimeChangedイベントを使用

```csharp
private double _lastProcessedTime = -1;
private const double TimeInterval = 1.0 / 30.0; // 約0.033秒

private void MediaPlayer_TimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
{
    var currentTime = e.Time / 1000.0; // ミリ秒から秒に変換
    
    if (_lastProcessedTime < 0 || 
        (currentTime - _lastProcessedTime) >= TimeInterval)
    {
        // この時点でフレームを取得
        CaptureFrameAtTime(currentTime);
        _lastProcessedTime = currentTime;
    }
}
```

## 注意事項

1. **リソースリークの防止**
   - `Display`コールバック内でリソースを確実に管理する
   - `finally`ブロックで`Dispose()`を呼ぶ

2. **エラーハンドリング**
   - メモリマップドファイルの作成に失敗した場合の処理
   - 画像処理のエラーハンドリング

3. **キャンセル処理**
   - `CancellationToken`を使用して処理を中断可能にする
   - キューに残っているフレームの処理を適切にクリーンアップする

## まとめ

30fpsでフレームを抽出する場合、**Video Callbacks方式**が最も効率的です。時間ベースのサンプリングを使用して、正確に30fpsを維持しながら、メモリ効率とパフォーマンスを最適化できます。
