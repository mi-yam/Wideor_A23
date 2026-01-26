using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Shared.Infra
{
    /// <summary>
    /// コマンド実行エンジン
    /// LOAD, CUT, HIDE, SHOW, DELETE, MERGE, SPEED コマンドを実行
    /// </summary>
    public class CommandExecutor : ICommandExecutor
    {
        private readonly IVideoSegmentManager _segmentManager;
        private readonly IVideoEngine _videoEngine;

        public CommandExecutor(IVideoSegmentManager segmentManager, IVideoEngine videoEngine)
        {
            _segmentManager = segmentManager ?? throw new ArgumentNullException(nameof(segmentManager));
            _videoEngine = videoEngine ?? throw new ArgumentNullException(nameof(videoEngine));
        }

        /// <summary>
        /// 単一のコマンドを実行
        /// </summary>
        public void ExecuteCommand(EditCommand command)
        {
            var result = ExecuteCommandWithResult(command);
            if (!result.Success)
            {
                LogHelper.WriteLog(
                    "CommandExecutor:ExecuteCommand",
                    "Command failed",
                    new { command = command.ToString(), error = result.ErrorMessage });
            }
        }

        /// <summary>
        /// 複数のコマンドを順次実行
        /// </summary>
        public void ExecuteCommands(IEnumerable<EditCommand> commands)
        {
            foreach (var command in commands)
            {
                ExecuteCommand(command);
            }
        }

        /// <summary>
        /// 単一のコマンドを実行し、結果を返す
        /// </summary>
        public CommandResult ExecuteCommandWithResult(EditCommand command)
        {
            try
            {
                switch (command.Type)
                {
                    case CommandType.Load:
                        return ExecuteLoadSync(command);
                    case CommandType.Cut:
                        return ExecuteCutWithResult(command);
                    case CommandType.Hide:
                        return ExecuteHideWithResult(command);
                    case CommandType.Show:
                        return ExecuteShowWithResult(command);
                    case CommandType.Delete:
                        return ExecuteDeleteWithResult(command);
                    case CommandType.Merge:
                        return ExecuteMergeWithResult(command);
                    case CommandType.Speed:
                        return ExecuteSpeedWithResult(command);
                    default:
                        return CommandResult.Fail(command, $"未知のコマンドタイプ: {command.Type}");
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(
                    "CommandExecutor:ExecuteCommandWithResult",
                    "Command execution failed",
                    new { command = command.ToString(), exceptionType = ex.GetType().Name, message = ex.Message });

                return CommandResult.Fail(command, ex.Message);
            }
        }

        /// <summary>
        /// 複数のコマンドを実行し、レポートを返す
        /// </summary>
        public CommandExecutionReport ExecuteCommandsWithReport(IEnumerable<EditCommand> commands)
        {
            var report = new CommandExecutionReport();

            foreach (var command in commands)
            {
                var result = ExecuteCommandWithResult(command);
                report.Results.Add(result);
                report.TotalCommands++;

                if (result.Success)
                {
                    report.SuccessCount++;
                }
                else
                {
                    report.FailureCount++;
                }
            }

            LogHelper.WriteLog(
                "CommandExecutor:ExecuteCommandsWithReport",
                "Commands execution completed",
                new { total = report.TotalCommands, success = report.SuccessCount, failure = report.FailureCount });

            return report;
        }

        /// <summary>
        /// LOADコマンドを非同期で実行
        /// </summary>
        public async Task<CommandResult> ExecuteLoadAsync(EditCommand command)
        {
            try
            {
                return await ExecuteLoadAsyncInternal(command);
            }
            catch (Exception ex)
            {
                return CommandResult.Fail(command, ex.Message);
            }
        }

        // ============================================
        // LOAD コマンド
        // ============================================

        /// <summary>
        /// LOADコマンドを同期的に実行（結果を返す）
        /// </summary>
        private CommandResult ExecuteLoadSync(EditCommand command)
        {
            LogHelper.WriteLog(
                "CommandExecutor:ExecuteLoadSync",
                "Executing LOAD command",
                new { filePath = command.FilePath });

            if (string.IsNullOrEmpty(command.FilePath))
            {
                return CommandResult.Fail(command, "LOADコマンドにはファイルパスが必要です");
            }

            if (!File.Exists(command.FilePath))
            {
                return CommandResult.Fail(command, $"動画ファイルが見つかりません: {command.FilePath}");
            }

            // 動画の長さを取得
            double duration = _videoEngine.CurrentTotalDuration;
            if (duration <= 0)
            {
                duration = 60.0; // デフォルト値（後で更新される）
            }

            // 既存セグメントの最後の終了時間を取得
            double startTime = 0.0;
            var existingSegments = _segmentManager.Segments;
            if (existingSegments.Count > 0)
            {
                startTime = existingSegments.Max(s => s.EndTime);
            }

            // 新しいセグメントを作成
            var segment = new VideoSegment
            {
                StartTime = startTime,
                EndTime = startTime + duration,
                Visible = true,
                State = SegmentState.Stopped,
                VideoFilePath = command.FilePath
            };

            _segmentManager.AddSegment(segment);

            LogHelper.WriteLog(
                "CommandExecutor:ExecuteLoadSync",
                "LOAD command completed",
                new { segmentId = segment.Id, duration = duration });

            return CommandResult.Ok(command, segment.Id);
        }

        /// <summary>
        /// LOADコマンドを非同期で実行（内部実装）
        /// </summary>
        private async Task<CommandResult> ExecuteLoadAsyncInternal(EditCommand command)
        {
            LogHelper.WriteLog(
                "CommandExecutor:ExecuteLoadAsync",
                "Executing LOAD command async",
                new { filePath = command.FilePath });

            if (string.IsNullOrEmpty(command.FilePath))
            {
                return CommandResult.Fail(command, "LOADコマンドにはファイルパスが必要です");
            }

            if (!File.Exists(command.FilePath))
            {
                return CommandResult.Fail(command, $"動画ファイルが見つかりません: {command.FilePath}");
            }

            // 動画情報を非同期で取得
            double duration = 0.0;
            try
            {
                var videoInfo = await _videoEngine.GetVideoInfoAsync();
                if (videoInfo != null && videoInfo.Duration > 0)
                {
                    duration = videoInfo.Duration;
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(
                    "CommandExecutor:ExecuteLoadAsync",
                    "Failed to get video info",
                    new { error = ex.Message });
            }

            // durationが0の場合はCurrentTotalDurationから取得
            if (duration <= 0)
            {
                duration = _videoEngine.CurrentTotalDuration;

                // まだ0の場合は待機
                if (duration <= 0)
                {
                    var timeout = DateTime.Now.AddSeconds(3);
                    while (duration <= 0 && DateTime.Now < timeout)
                    {
                        await Task.Delay(100);
                        duration = _videoEngine.CurrentTotalDuration;
                    }
                }
            }

            // デフォルト値
            if (duration <= 0)
            {
                duration = 60.0;
            }

            // 既存セグメントの最後の終了時間を取得
            double startTime = 0.0;
            var existingSegments = _segmentManager.Segments;
            if (existingSegments.Count > 0)
            {
                startTime = existingSegments.Max(s => s.EndTime);
            }

            // 新しいセグメントを作成
            var segment = new VideoSegment
            {
                StartTime = startTime,
                EndTime = startTime + duration,
                Visible = true,
                State = SegmentState.Stopped,
                VideoFilePath = command.FilePath
            };

            _segmentManager.AddSegment(segment);

            return CommandResult.Ok(command, segment.Id);
        }

        // ============================================
        // CUT コマンド
        // ============================================

        /// <summary>
        /// CUTコマンドを実行（指定位置でセグメントを分割）
        /// </summary>
        private CommandResult ExecuteCutWithResult(EditCommand command)
        {
            if (!command.Time.HasValue)
            {
                return CommandResult.Fail(command, "CUTコマンドには時間が必要です");
            }

            var cutTime = command.Time.Value;
            var segment = _segmentManager.GetSegmentAtTime(cutTime);

            if (segment == null)
            {
                return CommandResult.Fail(command, $"時間 {cutTime:F3} 秒にセグメントが見つかりません");
            }

            // 分割位置がセグメントの境界に近い場合はスキップ
            const double MinSegmentDuration = 0.1; // 最小セグメント長（秒）
            if (cutTime - segment.StartTime < MinSegmentDuration ||
                segment.EndTime - cutTime < MinSegmentDuration)
            {
                return CommandResult.Fail(command, $"分割位置がセグメントの境界に近すぎます");
            }

            // 元のセグメントを削除
            var originalId = segment.Id;
            _segmentManager.RemoveSegment(segment.Id);

            // 前半のセグメント
            var segment1 = new VideoSegment
            {
                StartTime = segment.StartTime,
                EndTime = cutTime,
                Visible = segment.Visible,
                State = SegmentState.Stopped,
                VideoFilePath = segment.VideoFilePath,
                Thumbnail = segment.Thumbnail
            };

            // 後半のセグメント
            var segment2 = new VideoSegment
            {
                StartTime = cutTime,
                EndTime = segment.EndTime,
                Visible = segment.Visible,
                State = SegmentState.Stopped,
                VideoFilePath = segment.VideoFilePath
            };

            _segmentManager.AddSegment(segment1);
            _segmentManager.AddSegment(segment2);

            LogHelper.WriteLog(
                "CommandExecutor:ExecuteCut",
                "CUT command completed",
                new { cutTime = cutTime, segment1Id = segment1.Id, segment2Id = segment2.Id });

            return CommandResult.Ok(command, segment1.Id, segment2.Id);
        }

        // ============================================
        // HIDE コマンド
        // ============================================

        /// <summary>
        /// HIDEコマンドを実行（指定範囲を非表示）
        /// </summary>
        private CommandResult ExecuteHideWithResult(EditCommand command)
        {
            if (!command.StartTime.HasValue || !command.EndTime.HasValue)
            {
                return CommandResult.Fail(command, "HIDEコマンドには開始時間と終了時間が必要です");
            }

            var segments = _segmentManager.GetSegmentsByTimeRange(command.StartTime.Value, command.EndTime.Value);

            if (segments.Count == 0)
            {
                return CommandResult.Fail(command, "指定範囲にセグメントが見つかりません");
            }

            var affectedIds = new List<int>();
            foreach (var segment in segments)
            {
                segment.Visible = false;
                segment.State = SegmentState.Hidden;
                _segmentManager.UpdateSegment(segment);
                affectedIds.Add(segment.Id);
            }

            LogHelper.WriteLog(
                "CommandExecutor:ExecuteHide",
                "HIDE command completed",
                new { startTime = command.StartTime, endTime = command.EndTime, affectedCount = affectedIds.Count });

            return CommandResult.Ok(command, affectedIds.ToArray());
        }

        // ============================================
        // SHOW コマンド
        // ============================================

        /// <summary>
        /// SHOWコマンドを実行（指定範囲を表示）
        /// </summary>
        private CommandResult ExecuteShowWithResult(EditCommand command)
        {
            if (!command.StartTime.HasValue || !command.EndTime.HasValue)
            {
                return CommandResult.Fail(command, "SHOWコマンドには開始時間と終了時間が必要です");
            }

            var segments = _segmentManager.GetSegmentsByTimeRange(command.StartTime.Value, command.EndTime.Value);

            if (segments.Count == 0)
            {
                return CommandResult.Fail(command, "指定範囲にセグメントが見つかりません");
            }

            var affectedIds = new List<int>();
            foreach (var segment in segments)
            {
                segment.Visible = true;
                segment.State = SegmentState.Stopped;
                _segmentManager.UpdateSegment(segment);
                affectedIds.Add(segment.Id);
            }

            LogHelper.WriteLog(
                "CommandExecutor:ExecuteShow",
                "SHOW command completed",
                new { startTime = command.StartTime, endTime = command.EndTime, affectedCount = affectedIds.Count });

            return CommandResult.Ok(command, affectedIds.ToArray());
        }

        // ============================================
        // DELETE コマンド
        // ============================================

        /// <summary>
        /// DELETEコマンドを実行（指定範囲を削除）
        /// </summary>
        private CommandResult ExecuteDeleteWithResult(EditCommand command)
        {
            if (!command.StartTime.HasValue || !command.EndTime.HasValue)
            {
                return CommandResult.Fail(command, "DELETEコマンドには開始時間と終了時間が必要です");
            }

            var segments = _segmentManager.GetSegmentsByTimeRange(command.StartTime.Value, command.EndTime.Value);

            if (segments.Count == 0)
            {
                return CommandResult.Fail(command, "指定範囲にセグメントが見つかりません");
            }

            var affectedIds = new List<int>();
            foreach (var segment in segments)
            {
                affectedIds.Add(segment.Id);
                _segmentManager.RemoveSegment(segment.Id);
            }

            LogHelper.WriteLog(
                "CommandExecutor:ExecuteDelete",
                "DELETE command completed",
                new { startTime = command.StartTime, endTime = command.EndTime, deletedCount = affectedIds.Count });

            return CommandResult.Ok(command, affectedIds.ToArray());
        }

        // ============================================
        // MERGE コマンド
        // ============================================

        /// <summary>
        /// MERGEコマンドを実行（隣接するセグメントを結合）
        /// </summary>
        private CommandResult ExecuteMergeWithResult(EditCommand command)
        {
            if (!command.StartTime.HasValue || !command.EndTime.HasValue)
            {
                return CommandResult.Fail(command, "MERGEコマンドには開始時間と終了時間が必要です");
            }

            var segments = _segmentManager.GetSegmentsByTimeRange(command.StartTime.Value, command.EndTime.Value)
                .OrderBy(s => s.StartTime)
                .ToList();

            if (segments.Count < 2)
            {
                return CommandResult.Fail(command, "結合するには2つ以上のセグメントが必要です");
            }

            // 同じ動画ファイルかどうかをチェック
            var firstFilePath = segments[0].VideoFilePath;
            if (segments.Any(s => s.VideoFilePath != firstFilePath))
            {
                return CommandResult.Fail(command, "異なる動画ファイルのセグメントは結合できません");
            }

            // 結合後のセグメントを作成
            var mergedSegment = new VideoSegment
            {
                StartTime = segments.First().StartTime,
                EndTime = segments.Last().EndTime,
                Visible = segments.All(s => s.Visible),
                State = SegmentState.Stopped,
                VideoFilePath = firstFilePath,
                Thumbnail = segments.First().Thumbnail
            };

            // 古いセグメントを削除
            var deletedIds = new List<int>();
            foreach (var segment in segments)
            {
                deletedIds.Add(segment.Id);
                _segmentManager.RemoveSegment(segment.Id);
            }

            // 新しいセグメントを追加
            _segmentManager.AddSegment(mergedSegment);

            LogHelper.WriteLog(
                "CommandExecutor:ExecuteMerge",
                "MERGE command completed",
                new { mergedSegmentId = mergedSegment.Id, deletedCount = deletedIds.Count });

            return CommandResult.Ok(command, mergedSegment.Id);
        }

        // ============================================
        // SPEED コマンド
        // ============================================

        /// <summary>
        /// SPEEDコマンドを実行（再生速度を変更）
        /// </summary>
        private CommandResult ExecuteSpeedWithResult(EditCommand command)
        {
            if (!command.SpeedRate.HasValue)
            {
                return CommandResult.Fail(command, "SPEEDコマンドには速度倍率が必要です");
            }

            if (!command.StartTime.HasValue || !command.EndTime.HasValue)
            {
                return CommandResult.Fail(command, "SPEEDコマンドには開始時間と終了時間が必要です");
            }

            var speedRate = command.SpeedRate.Value;
            if (speedRate < 0.1 || speedRate > 10.0)
            {
                return CommandResult.Fail(command, "速度倍率は0.1x〜10.0xの範囲で指定してください");
            }

            var segments = _segmentManager.GetSegmentsByTimeRange(command.StartTime.Value, command.EndTime.Value);

            if (segments.Count == 0)
            {
                return CommandResult.Fail(command, "指定範囲にセグメントが見つかりません");
            }

            // 各セグメントのSpeedRateを更新
            var affectedIds = new List<int>();
            foreach (var segment in segments)
            {
                segment.SpeedRate = speedRate;
                _segmentManager.UpdateSegment(segment);
                affectedIds.Add(segment.Id);
            }

            LogHelper.WriteLog(
                "CommandExecutor:ExecuteSpeed",
                "SPEED command completed",
                new { speedRate = speedRate, startTime = command.StartTime, endTime = command.EndTime, affectedCount = affectedIds.Count });

            return CommandResult.Ok(command, affectedIds.ToArray());
        }
    }
}
