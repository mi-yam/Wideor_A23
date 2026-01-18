using System;
using System.Reactive.Linq;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Wideor.App.Features.Editor
{
    /// <summary>
    /// スマートアンカーロジック（Twin Trigger Logic）
    /// クリップ作成時のステートマシン（Editor機能専用）
    /// </summary>
    internal class AnchorLogic : IDisposable
    {
        private readonly CompositeDisposable _disposables = new();

        // --- State ---

        /// <summary>
        /// 録画中かどうか
        /// </summary>
        public IReadOnlyReactiveProperty<bool> IsRecording { get; }

        private readonly ReactiveProperty<bool> _isRecording = new(false);

        /// <summary>
        /// ピボット時間（最初のクリック時の時間、nullの場合は未設定）
        /// </summary>
        public IReadOnlyReactiveProperty<double?> PivotTime { get; }

        private readonly ReactiveProperty<double?> _pivotTime = new(null);

        /// <summary>
        /// 現在のプレビュー範囲（始点・終点）
        /// </summary>
        public IReadOnlyReactiveProperty<(double Start, double End)?> PreviewRange { get; }

        private readonly ReactiveProperty<(double Start, double End)?> _previewRange = new(null);

        public AnchorLogic()
        {
            IsRecording = _isRecording.ToReadOnlyReactiveProperty()
                .AddTo(_disposables);

            PivotTime = _pivotTime.ToReadOnlyReactiveProperty()
                .AddTo(_disposables);

            PreviewRange = _previewRange.ToReadOnlyReactiveProperty()
                .AddTo(_disposables);
        }

        /// <summary>
        /// ピボットを設定します（最初のクリック）。
        /// </summary>
        /// <param name="currentTime">現在の時間（秒）</param>
        public void SetPivot(double currentTime)
        {
            if (_pivotTime.Value == null)
            {
                // 最初のクリック：ピボットを設定して録画開始
                _pivotTime.Value = currentTime;
                _isRecording.Value = true;
                _previewRange.Value = (currentTime, currentTime);
            }
            else
            {
                // 2回目のクリック：確定処理
                var (start, end) = Confirm(currentTime);
                // Confirm後は自動的にリセットされる
            }
        }

        /// <summary>
        /// 現在地に基づき、プレビュー範囲（始点・終点）を計算します。
        /// </summary>
        /// <param name="currentTime">現在の時間（秒）</param>
        /// <returns>プレビュー範囲（始点・終点）</returns>
        public (double Start, double End) CalculatePreviewRange(double currentTime)
        {
            if (_pivotTime.Value == null)
            {
                return (currentTime, currentTime);
            }

            var pivot = _pivotTime.Value.Value;
            var start = Math.Min(pivot, currentTime);
            var end = Math.Max(pivot, currentTime);

            // プレビュー範囲を更新
            _previewRange.Value = (start, end);

            return (start, end);
        }

        /// <summary>
        /// 確定処理。PivotTimeのリセットも含む。
        /// </summary>
        /// <param name="currentTime">現在の時間（秒）</param>
        /// <returns>確定された範囲（始点・終点）</returns>
        public (double Start, double End) Confirm(double currentTime)
        {
            if (_pivotTime.Value == null)
            {
                // ピボットが設定されていない場合は、現在時間を範囲として返す
                return (currentTime, currentTime);
            }

            var pivot = _pivotTime.Value.Value;
            var start = Math.Min(pivot, currentTime);
            var end = Math.Max(pivot, currentTime);

            // リセット
            _pivotTime.Value = null;
            _isRecording.Value = false;
            _previewRange.Value = null;

            return (start, end);
        }

        /// <summary>
        /// キャンセル処理。ピボットをリセットします。
        /// </summary>
        public void Cancel()
        {
            _pivotTime.Value = null;
            _isRecording.Value = false;
            _previewRange.Value = null;
        }

        public void Dispose()
        {
            _disposables?.Dispose();
        }
    }
}
