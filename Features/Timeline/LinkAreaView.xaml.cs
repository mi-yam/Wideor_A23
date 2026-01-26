using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Wideor.App.Shared.Domain;
using Wideor.App.Shared.Infra;

namespace Wideor.App.Features.Timeline
{
    /// <summary>
    /// LinkAreaView.xaml の相互作用ロジック
    /// フィルムエリアのセグメントとテキストエリアのパラグラフを台形で結ぶ
    /// </summary>
    public partial class LinkAreaView : UserControl
    {
        /// <summary>
        /// ViewModelプロパティ
        /// </summary>
        public TimelineViewModel? ViewModel
        {
            get => (TimelineViewModel?)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(
                nameof(ViewModel),
                typeof(TimelineViewModel),
                typeof(LinkAreaView),
                new PropertyMetadata(null, OnViewModelChanged));

        /// <summary>
        /// フィルムエリアのスクロールオフセット
        /// </summary>
        public double FilmScrollOffset
        {
            get => (double)GetValue(FilmScrollOffsetProperty);
            set => SetValue(FilmScrollOffsetProperty, value);
        }

        public static readonly DependencyProperty FilmScrollOffsetProperty =
            DependencyProperty.Register(
                nameof(FilmScrollOffset),
                typeof(double),
                typeof(LinkAreaView),
                new PropertyMetadata(0.0, OnScrollOffsetChanged));

        /// <summary>
        /// テキストエリアのスクロールオフセット
        /// </summary>
        public double TextScrollOffset
        {
            get => (double)GetValue(TextScrollOffsetProperty);
            set => SetValue(TextScrollOffsetProperty, value);
        }

        public static readonly DependencyProperty TextScrollOffsetProperty =
            DependencyProperty.Register(
                nameof(TextScrollOffset),
                typeof(double),
                typeof(LinkAreaView),
                new PropertyMetadata(0.0, OnScrollOffsetChanged));

        // 再描画のThrottle用
        private DateTime _lastRedrawTime = DateTime.MinValue;
        private const int MinRedrawIntervalMs = 16; // 約60fps

        public LinkAreaView()
        {
            InitializeComponent();
            Loaded += LinkAreaView_Loaded;
            SizeChanged += LinkAreaView_SizeChanged;
        }

        private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LinkAreaView view)
            {
                view.SubscribeToViewModel();
                view.RedrawLinks();
            }
        }

        private static void OnScrollOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LinkAreaView view)
            {
                view.RedrawLinks();
            }
        }

        private void LinkAreaView_Loaded(object sender, RoutedEventArgs e)
        {
            SubscribeToViewModel();
            RedrawLinks();
        }

        private void LinkAreaView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RedrawLinks();
        }

        /// <summary>
        /// ViewModelの変更を監視
        /// </summary>
        private void SubscribeToViewModel()
        {
            if (ViewModel == null)
                return;

            // VideoSegmentsの変更を監視
            if (ViewModel.VideoSegments is INotifyCollectionChanged videoSegments)
            {
                videoSegments.CollectionChanged += (s, e) => RedrawLinks();
            }
        }

        /// <summary>
        /// リンクを再描画します（Throttleで60fps制限）
        /// </summary>
        public void RedrawLinks()
        {
            // Throttle: 連続呼び出しを制限
            var now = DateTime.Now;
            if ((now - _lastRedrawTime).TotalMilliseconds < MinRedrawIntervalMs)
            {
                return;
            }
            _lastRedrawTime = now;

            // UIスレッドで実行
            Dispatcher.InvokeAsync(() => DrawLinksInternal(), System.Windows.Threading.DispatcherPriority.Render);
        }

        /// <summary>
        /// リンクを描画します（内部処理）
        /// </summary>
        private void DrawLinksInternal()
        {
            if (ViewModel == null || LinkCanvas == null)
                return;

            // Canvasをクリア
            LinkCanvas.Children.Clear();

            var segments = ViewModel.VideoSegments;
            var pixelsPerSecond = ViewModel.PixelsPerSecond?.Value ?? 100.0;

            // セグメントがない場合は終了
            if (segments == null || segments.Count == 0)
                return;

            // クリップ高さを取得（デフォルト480px）
            double clipHeight = 480.0;
            double canvasWidth = ActualWidth > 0 ? ActualWidth : 80;
            double canvasHeight = ActualHeight > 0 ? ActualHeight : 600;

            // 各セグメントに対してリンク線を描画
            double currentY = 0;
            int segmentIndex = 0;

            foreach (var segment in segments)
            {
                // フィルムエリア側の座標（左端）- セグメントの位置に基づく
                double filmY1 = currentY - FilmScrollOffset;
                double filmY2 = filmY1 + clipHeight;

                // 表示範囲内かチェック
                if (filmY2 > 0 && filmY1 < canvasHeight)
                {
                    // セグメントの対応線を描画
                    DrawSegmentLink(filmY1, filmY2, segmentIndex, canvasWidth, canvasHeight);
                }

                currentY += clipHeight;
                segmentIndex++;
            }
        }

        /// <summary>
        /// セグメントの対応線（台形）を描画
        /// </summary>
        private void DrawSegmentLink(double filmY1, double filmY2, int segmentIndex, double canvasWidth, double canvasHeight)
        {
            // テキストエリア側の座標（右端）
            // 行の高さを仮定（20px/行）
            const double LineHeight = 20.0;
            double textY1 = segmentIndex * LineHeight * 3 - TextScrollOffset; // 3行分のスペースを仮定
            double textY2 = textY1 + LineHeight * 3;

            // 表示範囲内に制限
            filmY1 = Math.Max(-10, Math.Min(canvasHeight + 10, filmY1));
            filmY2 = Math.Max(-10, Math.Min(canvasHeight + 10, filmY2));
            textY1 = Math.Max(-10, Math.Min(canvasHeight + 10, textY1));
            textY2 = Math.Max(-10, Math.Min(canvasHeight + 10, textY2));

            // Pathオブジェクトを作成
            var path = new Path
            {
                Stroke = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                StrokeThickness = 1,
                Fill = new SolidColorBrush(Color.FromArgb(30, 128, 128, 128)),
                StrokeLineJoin = PenLineJoin.Round
            };

            // PathGeometryで台形を定義
            var geometry = new PathGeometry();
            var figure = new PathFigure
            {
                StartPoint = new Point(0, filmY1), // 左上
                IsClosed = true
            };

            // 台形の4つの頂点を定義
            figure.Segments.Add(new LineSegment(new Point(canvasWidth, textY1), true)); // 右上
            figure.Segments.Add(new LineSegment(new Point(canvasWidth, textY2), true)); // 右下
            figure.Segments.Add(new LineSegment(new Point(0, filmY2), true));           // 左下

            geometry.Figures.Add(figure);
            path.Data = geometry;

            // Canvasに追加
            LinkCanvas.Children.Add(path);
        }

        /// <summary>
        /// 特定のセグメントをハイライト表示
        /// </summary>
        public void HighlightSegment(int segmentIndex)
        {
            // TODO: セグメントのハイライト表示を実装
        }
    }
}
