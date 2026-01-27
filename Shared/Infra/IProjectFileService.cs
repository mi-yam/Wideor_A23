using System.Threading;
using System.Threading.Tasks;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Shared.Infra
{
    /// <summary>
    /// プロジェクトファイル（.wideor）の読み書きを行うサービスのインターフェース
    /// テキストベースのHeader/Body形式を処理します。
    /// </summary>
    public interface IProjectFileService
    {
        /// <summary>
        /// プロジェクトファイルを読み込みます。
        /// </summary>
        /// <param name="filePath">ファイルパス</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>読み込んだプロジェクトデータ</returns>
        Task<ProjectFileData?> LoadAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// プロジェクトファイルを保存します。
        /// </summary>
        /// <param name="filePath">ファイルパス</param>
        /// <param name="data">保存するプロジェクトデータ</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>成功した場合true</returns>
        Task<bool> SaveAsync(string filePath, ProjectFileData data, CancellationToken cancellationToken = default);

        /// <summary>
        /// テキスト内容からプロジェクトデータを生成します。
        /// </summary>
        /// <param name="textContent">テキスト内容</param>
        /// <returns>プロジェクトデータ</returns>
        ProjectFileData ParseText(string textContent);

        /// <summary>
        /// プロジェクトデータからテキスト内容を生成します。
        /// </summary>
        /// <param name="data">プロジェクトデータ</param>
        /// <returns>テキスト内容</returns>
        string GenerateText(ProjectFileData data);
    }

    /// <summary>
    /// プロジェクトファイルのデータ構造
    /// </summary>
    public class ProjectFileData
    {
        /// <summary>
        /// プロジェクト設定（Header から生成）
        /// </summary>
        public ProjectConfig Config { get; set; } = new ProjectConfig();

        /// <summary>
        /// テキストエリアの全内容（Header + Body）
        /// </summary>
        public string TextContent { get; set; } = string.Empty;

        /// <summary>
        /// 動画ファイルパス（LOADコマンドから取得）
        /// </summary>
        public string? VideoFilePath { get; set; }

        /// <summary>
        /// ファイルパス（読み込み/保存したパス）
        /// </summary>
        public string? FilePath { get; set; }
    }
}
