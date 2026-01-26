using System.Collections.Generic;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Shared.Infra
{
    /// <summary>
    /// シーンパーサーのインターフェース
    /// テキストからパラグラフ（セパレータ形式）をパースしてSceneBlockのリストを生成します。
    /// </summary>
    public interface ISceneParser
    {
        /// <summary>
        /// テキストからシーンブロックのリストをパースします。
        /// </summary>
        /// <param name="text">パース対象のテキスト（Body部分）</param>
        /// <returns>SceneBlockのリスト</returns>
        List<SceneBlock> ParseScenes(string text);

        /// <summary>
        /// SceneBlockからセパレータ形式のテキストを生成します。
        /// </summary>
        /// <param name="scene">シーンブロック</param>
        /// <returns>セパレータ形式のテキスト</returns>
        string GenerateSceneText(SceneBlock scene);
    }
}
