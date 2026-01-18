using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Wideor.App.Features.Timeline
{
    /// <summary>
    /// TimeRulerView.xaml の相互作用ロジック
    /// 時間目盛りを描画するビュー（Timeline機能専用）
    /// </summary>
    public partial class TimeRulerView : UserControl
    {
        /// <summary>
        /// ViewModelプロパティ
        /// </summary>
        public TimelineViewModel ViewModel
        {
            get => (TimelineViewModel)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(
                nameof(ViewModel),
                typeof(TimelineViewModel),
                typeof(TimeRulerView),
                new PropertyMetadata(null, OnViewModelChanged));

        private IDisposable? _subscription;

        public TimeRulerView()
        {
            InitializeComponent();
            Loaded += TimeRulerView_Loaded;
            Unloaded += TimeRulerView_Unloaded;
        }

        private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TimeRulerView view)
            {
                view.SubscribeToViewModel();
            }
        }

        private void TimeRulerView_Loaded(object sender, RoutedEventArgs e)
        {
            SubscribeToViewModel();
            UpdateRuler();
        }

        private void TimeRulerView_Unloaded(object sender, RoutedEventArgs e)
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        private void SubscribeToViewModel()
        {
            _subscription?.Dispose();

            if (ViewModel == null)
                return;

            // PixelsPerSecondまたはTotalDurationが変更されたら目盛りを更新
            _subscription = ViewModel.PixelsPerSecond
                .AsObservable()
                .CombineLatest(ViewModel.TotalDuration.AsObservable(), (pps, duration) => new { PPS = pps, Duration = duration })
                .Subscribe(_ => UpdateRuler());
        }

        private void UpdateRuler()
        {
            if (ViewModel == null)
                return;

            RulerCanvas.Children.Clear();

            var pixelsPerSecond = ViewModel.PixelsPerSecond.Value;
            var totalDuration = ViewModel.TotalDuration.Value;
            var canvasWidth = ActualWidth > 0 ? ActualWidth : 800;
            var canvasHeight = ActualHeight > 0 ? ActualHeight : 60;

            if (pixelsPerSecond <= 0 || totalDuration <= 0)
                return;

            var totalHeight = totalDuration * pixelsPerSecond;

            // 適切な間隔を計算（1秒、5秒、10秒、30秒、1分、5分など）
            var interval = CalculateInterval(pixelsPerSecond);
            var startTime = 0.0;

            // 目盛りを描画
            for (double time = startTime; time <= totalDuration; time += interval)
            {
                var y = ViewModel.TimeToY(time);

                // メイン目盛り（長い線）
                var isMajorTick = IsMajorTick(time, interval);
                var line = new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = canvasWidth,
                    Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
                    StrokeThickness = isMajorTick ? 1.5 : 0.5,
                    Opacity = isMajorTick ? 1.0 : 0.6
                };
                RulerCanvas.Children.Add(line);

                // 時間ラベル（メイン目盛りのみ）
                if (isMajorTick)
                {
                    var textBlock = new TextBlock
                    {
                        Text = FormatTime(time),
                        Foreground = new SolidColorBrush(Colors.White),
                        FontSize = 10,
                        Margin = new Thickness(4, y - 8, 0, 0)
                    };
                    RulerCanvas.Children.Add(textBlock);
                }
            }
        }

        /// <summary>
        /// ピクセル/秒に基づいて適切な時間間隔を計算
        /// </summary>
        private double CalculateInterval(double pixelsPerSecond)
        {
            // 目盛り間隔が50ピクセル以上になるように調整
            var targetPixelInterval = 50.0;
            var timeInterval = targetPixelInterval / pixelsPerSecond;

            // 適切な間隔に丸める（1秒、5秒、10秒、30秒、1分、5分など）
            if (timeInterval <= 1)
                return 1;
            else if (timeInterval <= 5)
                return 5;
            else if (timeInterval <= 10)
                return 10;
            else if (timeInterval <= 30)
                return 30;
            else if (timeInterval <= 60)
                return 60;
            else if (timeInterval <= 300)
                return 300;
            else
                return 600; // 10分
        }

        /// <summary>
        /// メイン目盛りかどうかを判定
        /// </summary>
        private bool IsMajorTick(double time, double interval)
        {
            // 10秒、1分、5分などの区切りでメイン目盛り
            return Math.Abs(time % 60) < 0.01 || Math.Abs(time % 300) < 0.01;
        }

        /// <summary>
        /// 時間を文字列にフォーマット
        /// </summary>
        private string FormatTime(double seconds)
        {
            var timeSpan = TimeSpan.FromSeconds(seconds);
            if (timeSpan.TotalHours >= 1)
            {
                return $"{(int)timeSpan.TotalHours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
            }
            else
            {
                return $"{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            UpdateRuler();
        }
    }
}
