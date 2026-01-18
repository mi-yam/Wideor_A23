using System;
using Reactive.Bindings;

namespace Wideor.App.Shared.Infra
{
    /// <summary>
    /// 全スライスのスクロール同期を管理する契約インターフェース。
    /// 複数のUIコンポーネント間でスクロール位置を同期します。
    /// </summary>
    public interface IScrollCoordinator
    {
        /// <summary>
        /// 現在のスクロール位置（0.0～1.0の範囲）
        /// </summary>
        IReadOnlyReactiveProperty<double> ScrollPosition { get; }

        /// <summary>
        /// スクロール可能な最大値（ピクセル単位）
        /// </summary>
        IReadOnlyReactiveProperty<double> MaxScrollOffset { get; }

        /// <summary>
        /// スクロールが有効かどうか
        /// </summary>
        IReadOnlyReactiveProperty<bool> IsScrollEnabled { get; }

        /// <summary>
        /// スクロール位置を設定します。
        /// このメソッドを呼び出すと、登録されているすべてのスクロールビューが同期されます。
        /// </summary>
        /// <param name="position">スクロール位置（0.0～1.0の範囲）</param>
        void SetScrollPosition(double position);

        /// <summary>
        /// スクロール位置をオフセット（ピクセル単位）で設定します。
        /// </summary>
        /// <param name="offset">スクロールオフセット（ピクセル）</param>
        void SetScrollOffset(double offset);

        /// <summary>
        /// スクロールビューを登録します。
        /// 登録されたビューは自動的に同期されます。
        /// </summary>
        /// <param name="scrollViewer">同期するスクロールビュー</param>
        /// <returns>登録解除用のIDisposable</returns>
        IDisposable RegisterScrollViewer(System.Windows.Controls.ScrollViewer scrollViewer);

        /// <summary>
        /// スクロール位置の変更を購読します。
        /// </summary>
        /// <param name="onScrollChanged">スクロール位置が変更されたときに呼ばれるコールバック</param>
        /// <returns>購読解除用のIDisposable</returns>
        IDisposable SubscribeScrollChanged(Action<double> onScrollChanged);

        /// <summary>
        /// スクロールを有効/無効にします。
        /// </summary>
        /// <param name="enabled">有効にする場合はtrue</param>
        void SetScrollEnabled(bool enabled);

        /// <summary>
        /// スクロール可能な最大値を設定します。
        /// </summary>
        /// <param name="maxOffset">最大スクロールオフセット（ピクセル）</param>
        void SetMaxScrollOffset(double maxOffset);

        /// <summary>
        /// スクロール位置をリセットします（位置0に戻す）。
        /// </summary>
        void Reset();
    }
}
