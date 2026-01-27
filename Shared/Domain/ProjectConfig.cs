using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Wideor.App.Shared.Domain
{
    /// <summary>
    /// プロジェクト設定（Header から生成）
    /// テキストファイルのHeaderセクションで定義されたプロジェクト全体の設定を保持します。
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
        
        // テロップ位置設定
        private double _titlePositionX = 0.05;  // 左から5%
        private double _titlePositionY = 0.05;  // 上から5%
        private double _subtitlePositionY = 0.85; // 下から15%（画面下部）
        private int _titleFontSize = 32;
        private int _subtitleFontSize = 24;
        private string _defaultFreeTextColor = "#FFFFFF";

        /// <summary>
        /// プロジェクト名
        /// </summary>
        public string ProjectName
        {
            get => _projectName;
            set
            {
                if (_projectName != value)
                {
                    _projectName = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 出力解像度の幅
        /// </summary>
        public int ResolutionWidth
        {
            get => _resolutionWidth;
            set
            {
                if (_resolutionWidth != value && value > 0)
                {
                    _resolutionWidth = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Resolution));
                }
            }
        }

        /// <summary>
        /// 出力解像度の高さ
        /// </summary>
        public int ResolutionHeight
        {
            get => _resolutionHeight;
            set
            {
                if (_resolutionHeight != value && value > 0)
                {
                    _resolutionHeight = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Resolution));
                }
            }
        }

        /// <summary>
        /// 解像度を文字列で取得（例: "1920x1080"）
        /// </summary>
        public string Resolution => $"{_resolutionWidth}x{_resolutionHeight}";

        /// <summary>
        /// フレームレート（fps）
        /// </summary>
        public int FrameRate
        {
            get => _frameRate;
            set
            {
                if (_frameRate != value && value > 0)
                {
                    _frameRate = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// デフォルトフォント名
        /// </summary>
        public string DefaultFont
        {
            get => _defaultFont;
            set
            {
                if (_defaultFont != value && !string.IsNullOrWhiteSpace(value))
                {
                    _defaultFont = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// デフォルトフォントサイズ
        /// </summary>
        public int DefaultFontSize
        {
            get => _defaultFontSize;
            set
            {
                if (_defaultFontSize != value && value > 0)
                {
                    _defaultFontSize = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 題名のデフォルト色（例: "#FFFFFF"）
        /// </summary>
        public string DefaultTitleColor
        {
            get => _defaultTitleColor;
            set
            {
                if (_defaultTitleColor != value && IsValidColor(value))
                {
                    _defaultTitleColor = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 字幕のデフォルト色（例: "#FFFFFF"）
        /// </summary>
        public string DefaultSubtitleColor
        {
            get => _defaultSubtitleColor;
            set
            {
                if (_defaultSubtitleColor != value && IsValidColor(value))
                {
                    _defaultSubtitleColor = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 背景の透明度（0.0～1.0）
        /// </summary>
        public double DefaultBackgroundAlpha
        {
            get => _defaultBackgroundAlpha;
            set
            {
                if (_defaultBackgroundAlpha != value && value >= 0.0 && value <= 1.0)
                {
                    _defaultBackgroundAlpha = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// タイトル（見出し1）のX位置（0.0～1.0、画面左端からの比率）
        /// </summary>
        public double TitlePositionX
        {
            get => _titlePositionX;
            set
            {
                if (_titlePositionX != value && value >= 0.0 && value <= 1.0)
                {
                    _titlePositionX = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// タイトル（見出し1）のY位置（0.0～1.0、画面上端からの比率）
        /// </summary>
        public double TitlePositionY
        {
            get => _titlePositionY;
            set
            {
                if (_titlePositionY != value && value >= 0.0 && value <= 1.0)
                {
                    _titlePositionY = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 字幕のY位置（0.0～1.0、画面上端からの比率、X位置は中央揃え）
        /// </summary>
        public double SubtitlePositionY
        {
            get => _subtitlePositionY;
            set
            {
                if (_subtitlePositionY != value && value >= 0.0 && value <= 1.0)
                {
                    _subtitlePositionY = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// タイトルのフォントサイズ
        /// </summary>
        public int TitleFontSize
        {
            get => _titleFontSize;
            set
            {
                if (_titleFontSize != value && value > 0)
                {
                    _titleFontSize = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 字幕のフォントサイズ
        /// </summary>
        public int SubtitleFontSize
        {
            get => _subtitleFontSize;
            set
            {
                if (_subtitleFontSize != value && value > 0)
                {
                    _subtitleFontSize = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 自由テキストのデフォルト色
        /// </summary>
        public string DefaultFreeTextColor
        {
            get => _defaultFreeTextColor;
            set
            {
                if (_defaultFreeTextColor != value && IsValidColor(value))
                {
                    _defaultFreeTextColor = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 色コードが有効かどうかを判定
        /// </summary>
        private static bool IsValidColor(string color)
        {
            if (string.IsNullOrWhiteSpace(color))
                return false;

            // #RRGGBB または #AARRGGBB 形式をチェック
            if (color.StartsWith("#"))
            {
                var hex = color.Substring(1);
                return (hex.Length == 6 || hex.Length == 8) &&
                       System.Text.RegularExpressions.Regex.IsMatch(hex, "^[0-9A-Fa-f]+$");
            }

            return false;
        }

        /// <summary>
        /// デフォルト値でリセット
        /// </summary>
        public void Reset()
        {
            ProjectName = "無題のプロジェクト";
            ResolutionWidth = 1920;
            ResolutionHeight = 1080;
            FrameRate = 30;
            DefaultFont = "メイリオ";
            DefaultFontSize = 24;
            DefaultTitleColor = "#FFFFFF";
            DefaultSubtitleColor = "#FFFFFF";
            DefaultBackgroundAlpha = 0.8;
            TitlePositionX = 0.05;
            TitlePositionY = 0.05;
            SubtitlePositionY = 0.85;
            TitleFontSize = 32;
            SubtitleFontSize = 24;
            DefaultFreeTextColor = "#FFFFFF";
        }

        /// <summary>
        /// 現在の設定を複製
        /// </summary>
        public ProjectConfig Clone()
        {
            return new ProjectConfig
            {
                ProjectName = this.ProjectName,
                ResolutionWidth = this.ResolutionWidth,
                ResolutionHeight = this.ResolutionHeight,
                FrameRate = this.FrameRate,
                DefaultFont = this.DefaultFont,
                DefaultFontSize = this.DefaultFontSize,
                DefaultTitleColor = this.DefaultTitleColor,
                DefaultSubtitleColor = this.DefaultSubtitleColor,
                DefaultBackgroundAlpha = this.DefaultBackgroundAlpha,
                TitlePositionX = this.TitlePositionX,
                TitlePositionY = this.TitlePositionY,
                SubtitlePositionY = this.SubtitlePositionY,
                TitleFontSize = this.TitleFontSize,
                SubtitleFontSize = this.SubtitleFontSize,
                DefaultFreeTextColor = this.DefaultFreeTextColor
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
