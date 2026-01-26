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
                DefaultBackgroundAlpha = this.DefaultBackgroundAlpha
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
