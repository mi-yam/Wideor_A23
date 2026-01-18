using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Reactive.Bindings;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Shared.Infra
{
    /// <summary>
    /// IProjectContextの実装クラス。
    /// プロジェクトファイルの読み込み・保存機能を提供します。
    /// </summary>
    public class ProjectContext : IProjectContext, IDisposable
    {
        private readonly ReactiveProperty<string?> _projectFilePath = new();
        private readonly ReactiveProperty<bool> _isProjectLoaded = new(false);
        private readonly ReactiveProperty<bool> _isDirty = new(false);
        private readonly ObservableCollection<SceneBlock> _sceneBlocks = new();
        private readonly ReadOnlyObservableCollection<SceneBlock> _readOnlySceneBlocks;
        private readonly ReactiveProperty<ReadOnlyObservableCollection<SceneBlock>> _sceneBlocksProperty;
        private readonly ReactiveProperty<SceneBlock?> _selectedSceneBlock = new();
        private readonly ReactiveProperty<double> _currentPlaybackPosition = new(0.0);
        private readonly ReactiveProperty<double> _totalDuration = new(0.0);
        private readonly ObservableCollection<MediaError> _errors = new();
        private readonly ReadOnlyObservableCollection<MediaError> _readOnlyErrors;
        private readonly ReactiveProperty<ReadOnlyObservableCollection<MediaError>> _errorsProperty;
        private readonly Subject<ProjectContextChangedEventArgs> _projectChangedSubject = new();
        private readonly CompositeDisposable _disposables = new();

        private const string ProjectFileExtension = ".wideor";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public ProjectContext()
        {
            _readOnlySceneBlocks = new ReadOnlyObservableCollection<SceneBlock>(_sceneBlocks);
            _sceneBlocksProperty = new ReactiveProperty<ReadOnlyObservableCollection<SceneBlock>>(_readOnlySceneBlocks);
            _readOnlyErrors = new ReadOnlyObservableCollection<MediaError>(_errors);
            _errorsProperty = new ReactiveProperty<ReadOnlyObservableCollection<MediaError>>(_readOnlyErrors);

            // シーンブロックの変更を監視してIsDirtyを更新
            _sceneBlocks.CollectionChanged += (s, e) =>
            {
                if (_isProjectLoaded.Value)
                {
                    _isDirty.Value = true;
                }
            };

            // プロジェクト変更イベントの購読を追加
            _disposables.Add(_projectChangedSubject);
        }

        public IReadOnlyReactiveProperty<string?> ProjectFilePath => _projectFilePath;
        public IReadOnlyReactiveProperty<bool> IsProjectLoaded => _isProjectLoaded;
        public IReadOnlyReactiveProperty<bool> IsDirty => _isDirty;
        public IReadOnlyReactiveProperty<ReadOnlyObservableCollection<SceneBlock>> SceneBlocks => _sceneBlocksProperty;
        public IReadOnlyReactiveProperty<SceneBlock?> SelectedSceneBlock => _selectedSceneBlock;
        public IReadOnlyReactiveProperty<double> CurrentPlaybackPosition => _currentPlaybackPosition;
        public IReadOnlyReactiveProperty<double> TotalDuration => _totalDuration;
        public IReadOnlyReactiveProperty<ReadOnlyObservableCollection<MediaError>> Errors => _errorsProperty;
        public IObservable<ProjectContextChangedEventArgs> ProjectChanged => _projectChangedSubject.AsObservable();

        /// <summary>
        /// プロジェクトファイルを読み込みます。
        /// </summary>
        public async Task<bool> LoadProjectAsync(string filePath, CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    AddError(new MediaError
                    {
                        Id = Guid.NewGuid().ToString(),
                        ErrorType = MediaErrorType.FileNotFound,
                        Message = "ファイルパスが指定されていません。",
                        Severity = MediaErrorSeverity.Error
                    });
                    return false;
                }

                if (!File.Exists(filePath))
                {
                    AddError(new MediaError
                    {
                        Id = Guid.NewGuid().ToString(),
                        ErrorType = MediaErrorType.FileNotFound,
                        Message = $"ファイルが見つかりません: {filePath}",
                        Severity = MediaErrorSeverity.Error
                    });
                    return false;
                }

                // ファイルを読み込む
                string jsonContent;
                try
                {
                    jsonContent = await File.ReadAllTextAsync(filePath, cancellationToken);
                }
                catch (UnauthorizedAccessException ex)
                {
                    AddError(new MediaError
                    {
                        Id = Guid.NewGuid().ToString(),
                        ErrorType = MediaErrorType.AccessDenied,
                        Message = $"ファイルへのアクセスが拒否されました: {filePath}",
                        InnerException = ex.Message,
                        Severity = MediaErrorSeverity.Error
                    });
                    return false;
                }
                catch (IOException ex)
                {
                    AddError(new MediaError
                    {
                        Id = Guid.NewGuid().ToString(),
                        ErrorType = MediaErrorType.FileNotFound,
                        Message = $"ファイルの読み込みに失敗しました: {filePath}",
                        InnerException = ex.Message,
                        Severity = MediaErrorSeverity.Error
                    });
                    return false;
                }

                // JSONをデシリアライズ
                ProjectFile? projectFile;
                try
                {
                    projectFile = JsonSerializer.Deserialize<ProjectFile>(jsonContent, JsonOptions);
                }
                catch (JsonException ex)
                {
                    AddError(new MediaError
                    {
                        Id = Guid.NewGuid().ToString(),
                        ErrorType = MediaErrorType.CorruptedFile,
                        Message = $"プロジェクトファイルの形式が正しくありません: {filePath}",
                        InnerException = ex.Message,
                        Severity = MediaErrorSeverity.Error
                    });
                    return false;
                }

                if (projectFile == null)
                {
                    AddError(new MediaError
                    {
                        Id = Guid.NewGuid().ToString(),
                        ErrorType = MediaErrorType.CorruptedFile,
                        Message = $"プロジェクトファイルの読み込みに失敗しました: {filePath}",
                        Severity = MediaErrorSeverity.Error
                    });
                    return false;
                }

                // データを読み込む
                _sceneBlocks.Clear();
                foreach (var block in projectFile.SceneBlocks)
                {
                    _sceneBlocks.Add(block);
                }

                _totalDuration.Value = projectFile.TotalDuration;
                _projectFilePath.Value = filePath;
                _isProjectLoaded.Value = true;
                _isDirty.Value = false;
                _selectedSceneBlock.Value = null;
                _currentPlaybackPosition.Value = 0.0;

                // イベントを発火
                _projectChangedSubject.OnNext(new ProjectContextChangedEventArgs
                {
                    ChangeType = ProjectContextChangeType.ProjectLoaded,
                    ChangedAt = DateTime.UtcNow
                });

                return true;
            }
            catch (Exception ex)
            {
                AddError(new MediaError
                {
                    Id = Guid.NewGuid().ToString(),
                    ErrorType = MediaErrorType.Unknown,
                    Message = $"予期しないエラーが発生しました: {ex.Message}",
                    InnerException = ex.ToString(),
                    Severity = MediaErrorSeverity.Fatal
                });
                return false;
            }
        }

        /// <summary>
        /// プロジェクトファイルを保存します。
        /// </summary>
        public async Task<bool> SaveProjectAsync(string? filePath = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var savePath = filePath ?? _projectFilePath.Value;
                if (string.IsNullOrWhiteSpace(savePath))
                {
                    AddError(new MediaError
                    {
                        Id = Guid.NewGuid().ToString(),
                        ErrorType = MediaErrorType.FileNotFound,
                        Message = "保存先のファイルパスが指定されていません。",
                        Severity = MediaErrorSeverity.Error
                    });
                    return false;
                }

                // 拡張子を追加（ない場合）
                if (!Path.GetExtension(savePath).Equals(ProjectFileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    savePath = Path.ChangeExtension(savePath, ProjectFileExtension);
                }

                // プロジェクトファイルオブジェクトを作成
                var projectFile = new ProjectFile
                {
                    Version = "1.0",
                    CreatedAt = _projectFilePath.Value == null ? DateTime.UtcNow : File.Exists(_projectFilePath.Value)
                        ? File.GetCreationTimeUtc(_projectFilePath.Value)
                        : DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    ProjectName = Path.GetFileNameWithoutExtension(savePath),
                    SceneBlocks = _sceneBlocks.ToList(),
                    TotalDuration = _totalDuration.Value
                };

                // JSONにシリアライズ
                string jsonContent;
                try
                {
                    jsonContent = JsonSerializer.Serialize(projectFile, JsonOptions);
                }
                catch (Exception ex)
                {
                    AddError(new MediaError
                    {
                        Id = Guid.NewGuid().ToString(),
                        ErrorType = MediaErrorType.Unknown,
                        Message = $"プロジェクトファイルのシリアライズに失敗しました: {ex.Message}",
                        InnerException = ex.ToString(),
                        Severity = MediaErrorSeverity.Error
                    });
                    return false;
                }

                // ファイルに書き込む
                try
                {
                    // ディレクトリが存在しない場合は作成
                    var directory = Path.GetDirectoryName(savePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    await File.WriteAllTextAsync(savePath, jsonContent, cancellationToken);
                }
                catch (UnauthorizedAccessException ex)
                {
                    AddError(new MediaError
                    {
                        Id = Guid.NewGuid().ToString(),
                        ErrorType = MediaErrorType.AccessDenied,
                        Message = $"ファイルへの書き込みアクセスが拒否されました: {savePath}",
                        InnerException = ex.Message,
                        Severity = MediaErrorSeverity.Error
                    });
                    return false;
                }
                catch (IOException ex)
                {
                    AddError(new MediaError
                    {
                        Id = Guid.NewGuid().ToString(),
                        ErrorType = MediaErrorType.FileNotFound,
                        Message = $"ファイルの書き込みに失敗しました: {savePath}",
                        InnerException = ex.Message,
                        Severity = MediaErrorSeverity.Error
                    });
                    return false;
                }

                _projectFilePath.Value = savePath;
                _isDirty.Value = false;

                // イベントを発火
                _projectChangedSubject.OnNext(new ProjectContextChangedEventArgs
                {
                    ChangeType = ProjectContextChangeType.ProjectSaved,
                    ChangedAt = DateTime.UtcNow
                });

                return true;
            }
            catch (Exception ex)
            {
                AddError(new MediaError
                {
                    Id = Guid.NewGuid().ToString(),
                    ErrorType = MediaErrorType.Unknown,
                    Message = $"予期しないエラーが発生しました: {ex.Message}",
                    InnerException = ex.ToString(),
                    Severity = MediaErrorSeverity.Fatal
                });
                return false;
            }
        }

        public void CreateNewProject()
        {
            _sceneBlocks.Clear();
            _errors.Clear();
            _projectFilePath.Value = null;
            _isProjectLoaded.Value = true;
            _isDirty.Value = false;
            _selectedSceneBlock.Value = null;
            _currentPlaybackPosition.Value = 0.0;
            _totalDuration.Value = 0.0;

            // イベントを発火
            _projectChangedSubject.OnNext(new ProjectContextChangedEventArgs
            {
                ChangeType = ProjectContextChangeType.ProjectCreated,
                ChangedAt = DateTime.UtcNow
            });
        }

        public void AddSceneBlock(SceneBlock sceneBlock)
        {
            if (sceneBlock == null)
                return;

            _sceneBlocks.Add(sceneBlock);
            UpdateTotalDuration();

            // イベントを発火
            _projectChangedSubject.OnNext(new ProjectContextChangedEventArgs
            {
                ChangeType = ProjectContextChangeType.SceneBlockAdded,
                SceneBlock = sceneBlock,
                ChangedAt = DateTime.UtcNow
            });
        }

        public bool RemoveSceneBlock(string sceneBlockId)
        {
            var block = _sceneBlocks.FirstOrDefault(b => b.Id == sceneBlockId);
            if (block != null)
            {
                _sceneBlocks.Remove(block);
                UpdateTotalDuration();

                // 選択中のブロックが削除された場合は選択を解除
                if (_selectedSceneBlock.Value?.Id == sceneBlockId)
                {
                    _selectedSceneBlock.Value = null;
                }

                // イベントを発火
                _projectChangedSubject.OnNext(new ProjectContextChangedEventArgs
                {
                    ChangeType = ProjectContextChangeType.SceneBlockRemoved,
                    SceneBlock = block,
                    ChangedAt = DateTime.UtcNow
                });

                return true;
            }
            return false;
        }

        public bool UpdateSceneBlock(SceneBlock sceneBlock)
        {
            if (sceneBlock == null)
                return false;

            var index = _sceneBlocks.ToList().FindIndex(b => b.Id == sceneBlock.Id);
            if (index >= 0)
            {
                _sceneBlocks[index] = sceneBlock;
                UpdateTotalDuration();

                // イベントを発火
                _projectChangedSubject.OnNext(new ProjectContextChangedEventArgs
                {
                    ChangeType = ProjectContextChangeType.SceneBlockUpdated,
                    SceneBlock = sceneBlock,
                    ChangedAt = DateTime.UtcNow
                });

                return true;
            }
            return false;
        }

        public void SetSelectedSceneBlock(SceneBlock? sceneBlock)
        {
            _selectedSceneBlock.Value = sceneBlock;

            // イベントを発火
            _projectChangedSubject.OnNext(new ProjectContextChangedEventArgs
            {
                ChangeType = ProjectContextChangeType.SceneBlockSelected,
                SceneBlock = sceneBlock,
                ChangedAt = DateTime.UtcNow
            });
        }

        public void SetPlaybackPosition(double position)
        {
            _currentPlaybackPosition.Value = Math.Max(0.0, position);

            // イベントを発火
            _projectChangedSubject.OnNext(new ProjectContextChangedEventArgs
            {
                ChangeType = ProjectContextChangeType.PlaybackPositionChanged,
                ChangedAt = DateTime.UtcNow
            });
        }

        public void AddError(MediaError error)
        {
            if (error == null)
                return;

            _errors.Add(error);

            // イベントを発火
            _projectChangedSubject.OnNext(new ProjectContextChangedEventArgs
            {
                ChangeType = ProjectContextChangeType.ErrorAdded,
                Error = error,
                ChangedAt = DateTime.UtcNow
            });
        }

        public void ClearError(string? errorId = null)
        {
            if (errorId == null)
            {
                _errors.Clear();

                // イベントを発火
                _projectChangedSubject.OnNext(new ProjectContextChangedEventArgs
                {
                    ChangeType = ProjectContextChangeType.ErrorCleared,
                    ChangedAt = DateTime.UtcNow
                });
            }
            else
            {
                var error = _errors.FirstOrDefault(e => e.Id == errorId);
                if (error != null)
                {
                    _errors.Remove(error);

                    // イベントを発火
                    _projectChangedSubject.OnNext(new ProjectContextChangedEventArgs
                    {
                        ChangeType = ProjectContextChangeType.ErrorCleared,
                        Error = error,
                        ChangedAt = DateTime.UtcNow
                    });
                }
            }
        }

        /// <summary>
        /// 総時間を更新します（シーンブロックの最大終了時間から計算）。
        /// </summary>
        private void UpdateTotalDuration()
        {
            if (_sceneBlocks.Count == 0)
            {
                _totalDuration.Value = 0.0;
                return;
            }

            var maxEndTime = _sceneBlocks.Max(b => b.EndTime);
            _totalDuration.Value = maxEndTime;
        }

        public void Dispose()
        {
            // _projectChangedSubjectは_disposablesに追加されているため、
            // _disposables.Dispose()で自動的にDisposeされる
            _disposables?.Dispose();
        }
    }
}
