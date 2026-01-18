using System;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using Wideor.App.Shared.Domain;
using Wideor.App.Shared.Infra;

namespace Wideor.App.Features.Player
{
    /// <summary>
    /// 動画再生機能のViewModel。
    /// IVideoEngineを使用して動画を制御し、状態をReactivePropertyで公開します。
    /// </summary>
    public class PlayerViewModel : IDisposable
    {
        private readonly IVideoEngine _videoEngine;
        private readonly CompositeDisposable _disposables = new();

        // --- Reactive Properties (Read-Only) ---

        /// <summary>
        /// 現在の再生位置（秒）
        /// </summary>
        public IReadOnlyReactiveProperty<double> CurrentPosition { get; }

        /// <summary>
        /// 動画の総時間（秒）
        /// </summary>
        public IReadOnlyReactiveProperty<double> TotalDuration { get; }

        /// <summary>
        /// 再生状態（true: 再生中, false: 停止/一時停止）
        /// </summary>
        public IReadOnlyReactiveProperty<bool> IsPlaying { get; }

        /// <summary>
        /// 動画の読み込み状態
        /// </summary>
        public IReadOnlyReactiveProperty<bool> IsLoaded { get; }

        /// <summary>
        /// 現在の音量（0.0～1.0）
        /// </summary>
        public IReadOnlyReactiveProperty<double> Volume { get; }

        /// <summary>
        /// シーク位置（スライダーとバインド、TwoWay）
        /// </summary>
        public ReactiveProperty<double> SeekPosition { get; }

        /// <summary>
        /// エラーメッセージ（エラーが発生した場合に表示）
        /// </summary>
        public IReadOnlyReactiveProperty<string?> ErrorMessage { get; }

        /// <summary>
        /// エラーが発生しているかどうか
        /// </summary>
        public IReadOnlyReactiveProperty<bool> HasError { get; }

        // --- Commands ---

        /// <summary>
        /// 再生/一時停止を切り替えるコマンド
        /// </summary>
        public ReactiveCommand TogglePlayPauseCommand { get; }

        /// <summary>
        /// 停止コマンド
        /// </summary>
        public ReactiveCommand StopCommand { get; }

        /// <summary>
        /// 動画ファイルを読み込むコマンド
        /// </summary>
        public ReactiveCommand<string> LoadVideoCommand { get; }

        /// <summary>
        /// 音量を設定するコマンド
        /// </summary>
        public ReactiveCommand<double> SetVolumeCommand { get; }

        // --- Private Reactive Properties (for internal state) ---

        private readonly ReactiveProperty<double> _volume = new(0.5);
        private bool _isSeeking = false;

        public PlayerViewModel(IVideoEngine videoEngine)
        {
            _videoEngine = videoEngine ?? throw new ArgumentNullException(nameof(videoEngine));

            // IObservableをReadOnlyReactivePropertyに変換
            CurrentPosition = _videoEngine.CurrentPosition
                .ToReadOnlyReactiveProperty(0.0)
                .AddTo(_disposables);

            TotalDuration = _videoEngine.TotalDuration
                .ToReadOnlyReactiveProperty(0.0)
                .AddTo(_disposables);

            IsPlaying = _videoEngine.IsPlaying
                .ToReadOnlyReactiveProperty(false)
                .AddTo(_disposables);

            IsLoaded = _videoEngine.IsLoaded
                .ToReadOnlyReactiveProperty(false)
                .AddTo(_disposables);

            // 音量は内部で管理（IVideoEngineに音量のObservableがないため）
            Volume = _volume.ToReadOnlyReactiveProperty()
                .AddTo(_disposables);

            // シーク位置の初期化（CurrentPositionと同期）
            SeekPosition = new ReactiveProperty<double>(0.0)
                .AddTo(_disposables);

            // CurrentPositionが変更されたらSeekPositionも更新（ただし、ユーザーがドラッグ中でない場合）
            CurrentPosition
                .Where(_ => !_isSeeking)
                .Subscribe(pos => SeekPosition.Value = pos)
                .AddTo(_disposables);

            // SeekPositionが変更されたらシーク処理を実行
            SeekPosition
                .Skip(1) // 初期値はスキップ
                .Throttle(TimeSpan.FromMilliseconds(100)) // ドラッグ中の頻繁な更新を抑制
                .Subscribe(async pos =>
                {
                    if (IsLoaded.Value && Math.Abs(pos - CurrentPosition.Value) > 0.1) // 0.1秒以上の差がある場合のみシーク
                    {
                        _isSeeking = true;
                        try
                        {
                            await SeekAsync(pos);
                        }
                        finally
                        {
                            _isSeeking = false;
                        }
                    }
                })
                .AddTo(_disposables);

            // エラー通知を購読
            var errorMessage = new ReactiveProperty<string?>(null);
            ErrorMessage = errorMessage.ToReadOnlyReactiveProperty()
                .AddTo(_disposables);

            HasError = errorMessage
                .Select(msg => !string.IsNullOrEmpty(msg))
                .ToReadOnlyReactiveProperty(false)
                .AddTo(_disposables);

            _videoEngine.Errors
                .Subscribe(error =>
                {
                    errorMessage.Value = $"[{error.ErrorType}] {error.Message}";
                })
                .AddTo(_disposables);

            // コマンドの初期化
            TogglePlayPauseCommand = IsLoaded
                .ToReactiveCommand()
                .WithSubscribe(() =>
                {
                    if (IsPlaying.Value)
                    {
                        _videoEngine.Pause();
                    }
                    else
                    {
                        _videoEngine.Play();
                    }
                })
                .AddTo(_disposables);

            StopCommand = IsLoaded
                .ToReactiveCommand()
                .WithSubscribe(() =>
                {
                    _videoEngine.Stop();
                })
                .AddTo(_disposables);

            LoadVideoCommand = new ReactiveCommand<string>()
                .WithSubscribe(async filePath =>
                {
                    if (string.IsNullOrEmpty(filePath))
                        return;

                    errorMessage.Value = null; // エラーメッセージをクリア
                    var success = await _videoEngine.LoadAsync(filePath, CancellationToken.None);
                    if (!success)
                    {
                        errorMessage.Value = "動画の読み込みに失敗しました。";
                    }
                })
                .AddTo(_disposables);

            SetVolumeCommand = new ReactiveCommand<double>()
                .WithSubscribe(volume =>
                {
                    _volume.Value = Math.Clamp(volume, 0.0, 1.0);
                    _videoEngine.SetVolume(_volume.Value);
                })
                .AddTo(_disposables);

            // 初期音量を設定
            _videoEngine.SetVolume(_volume.Value);
        }

        /// <summary>
        /// 指定された位置にシークします。
        /// </summary>
        public async Task SeekAsync(double position)
        {
            if (IsLoaded.Value)
            {
                await _videoEngine.SeekAsync(position);
            }
        }

        /// <summary>
        /// 再生速度を設定します。
        /// </summary>
        public void SetPlaybackSpeed(double speed)
        {
            if (IsLoaded.Value)
            {
                _videoEngine.SetPlaybackSpeed(speed);
            }
        }

        public void Dispose()
        {
            _disposables?.Dispose();
        }
    }
}
