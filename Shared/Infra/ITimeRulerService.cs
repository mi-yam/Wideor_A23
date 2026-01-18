using System;
using Reactive.Bindings;

namespace Wideor.App.Shared.Infra
{
    /// <summary>
    /// 時間と座標の相互変換ロジックを提供する契約インターフェース。
    /// タイムラインのズームレベルと座標計算を管理します。
    /// </summary>
    public interface ITimeRulerService
    {
        /// <summary>
        /// 現在のズームレベル（1秒あたりのピクセル数）
        /// </summary>
        IReadOnlyReactiveProperty<double> PixelsPerSecond { get; }

        /// <summary>
        /// ズームレベルを設定します。
        /// </summary>
        /// <param name="pixelsPerSecond">1秒あたりのピクセル数</param>
        void SetZoomLevel(double pixelsPerSecond);

        /// <summary>
        /// 時間をY座標（ピクセル）に変換します。
        /// </summary>
        /// <param name="time">時間（秒）</param>
        /// <returns>Y座標（ピクセル）</returns>
        double TimeToY(double time);

        /// <summary>
        /// Y座標（ピクセル）を時間（秒）に変換します。
        /// </summary>
        /// <param name="y">Y座標（ピクセル）</param>
        /// <returns>時間（秒）</returns>
        double YToTime(double y);
    }
}
