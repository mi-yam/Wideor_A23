using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Reactive.Bindings;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Shared.Infra
{
    /// <summary>
    /// アプリケーションの「単一の真実（Single Source of Truth）」を表す契約インターフェース。
    /// プロジェクト全体の状態を管理します。
    /// </summary>
    public interface IProjectContext
    {
        /// <summary>
        /// 現在のプロジェクトファイルのパス
        /// </summary>
        IReadOnlyReactiveProperty<string?> ProjectFilePath { get; }

        /// <summary>
        /// プロジェクトが読み込まれているかどうか
        /// </summary>
        IReadOnlyReactiveProperty<bool> IsProjectLoaded { get; }

        /// <summary>
        /// プロジェクトが変更されているかどうか（未保存の変更があるか）
        /// </summary>
        IReadOnlyReactiveProperty<bool> IsDirty { get; }

        /// <summary>
        /// シーンブロックのコレクション
        /// </summary>
        IReadOnlyReactiveProperty<ReadOnlyObservableCollection<SceneBlock>> SceneBlocks { get; }

        /// <summary>
        /// 現在選択されているシーンブロック
        /// </summary>
        IReadOnlyReactiveProperty<SceneBlock?> SelectedSceneBlock { get; }

        /// <summary>
        /// 現在の再生位置（秒）
        /// </summary>
        IReadOnlyReactiveProperty<double> CurrentPlaybackPosition { get; }

        /// <summary>
        /// プロジェクトの総時間（秒）
        /// </summary>
        IReadOnlyReactiveProperty<double> TotalDuration { get; }

        /// <summary>
        /// エラーのコレクション
        /// </summary>
        IReadOnlyReactiveProperty<ReadOnlyObservableCollection<MediaError>> Errors { get; }

        /// <summary>
        /// プロジェクトを読み込みます。
        /// </summary>
        /// <param name="filePath">プロジェクトファイルのパス</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        Task<bool> LoadProjectAsync(string filePath, System.Threading.CancellationToken cancellationToken = default);

        /// <summary>
        /// プロジェクトを保存します。
        /// </summary>
        /// <param name="filePath">保存先のファイルパス（nullの場合は現在のパスに保存）</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        Task<bool> SaveProjectAsync(string? filePath = null, System.Threading.CancellationToken cancellationToken = default);

        /// <summary>
        /// 新しいプロジェクトを作成します。
        /// </summary>
        void CreateNewProject();

        /// <summary>
        /// シーンブロックを追加します。
        /// </summary>
        /// <param name="sceneBlock">追加するシーンブロック</param>
        void AddSceneBlock(SceneBlock sceneBlock);

        /// <summary>
        /// シーンブロックを削除します。
        /// </summary>
        /// <param name="sceneBlockId">削除するシーンブロックのID</param>
        /// <returns>削除に成功した場合true</returns>
        bool RemoveSceneBlock(string sceneBlockId);

        /// <summary>
        /// シーンブロックを更新します。
        /// </summary>
        /// <param name="sceneBlock">更新するシーンブロック</param>
        /// <returns>更新に成功した場合true</returns>
        bool UpdateSceneBlock(SceneBlock sceneBlock);

        /// <summary>
        /// 選択されているシーンブロックを設定します。
        /// </summary>
        /// <param name="sceneBlock">選択するシーンブロック（nullの場合は選択解除）</param>
        void SetSelectedSceneBlock(SceneBlock? sceneBlock);

        /// <summary>
        /// 現在の再生位置を設定します。
        /// </summary>
        /// <param name="position">再生位置（秒）</param>
        void SetPlaybackPosition(double position);

        /// <summary>
        /// エラーを追加します。
        /// </summary>
        /// <param name="error">追加するエラー</param>
        void AddError(MediaError error);

        /// <summary>
        /// エラーをクリアします。
        /// </summary>
        /// <param name="errorId">クリアするエラーのID（nullの場合はすべてクリア）</param>
        void ClearError(string? errorId = null);

        /// <summary>
        /// プロジェクトの変更を通知するストリーム
        /// </summary>
        IObservable<ProjectContextChangedEventArgs> ProjectChanged { get; }
    }

    /// <summary>
    /// プロジェクトコンテキストの変更イベント引数
    /// </summary>
    public record ProjectContextChangedEventArgs
    {
        public required ProjectContextChangeType ChangeType { get; init; }
        public SceneBlock? SceneBlock { get; init; }
        public MediaError? Error { get; init; }
        public DateTime ChangedAt { get; init; } = DateTime.UtcNow;
    }

    /// <summary>
    /// プロジェクトコンテキストの変更タイプ
    /// </summary>
    public enum ProjectContextChangeType
    {
        ProjectLoaded,
        ProjectSaved,
        ProjectCreated,
        SceneBlockAdded,
        SceneBlockRemoved,
        SceneBlockUpdated,
        SceneBlockSelected,
        PlaybackPositionChanged,
        ErrorAdded,
        ErrorCleared
    }
}
