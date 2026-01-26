using Wideor.App.Shared.Domain;

namespace Wideor.App.Shared.Infra
{
    /// <summary>
    /// Headerパーサーのインターフェース
    /// テキストのHeaderセクションをパースしてProjectConfigを生成します。
    /// </summary>
    public interface IHeaderParser
    {
        /// <summary>
        /// テキストからHeaderをパースしてProjectConfigを生成します。
        /// </summary>
        /// <param name="text">パース対象のテキスト全体</param>
        /// <returns>ProjectConfigとBody開始行番号のタプル</returns>
        (ProjectConfig config, int bodyStartLine) ParseHeader(string text);

        /// <summary>
        /// ProjectConfigからHeaderテキストを生成します。
        /// </summary>
        /// <param name="config">プロジェクト設定</param>
        /// <returns>Header形式のテキスト</returns>
        string GenerateHeaderText(ProjectConfig config);
    }
}
