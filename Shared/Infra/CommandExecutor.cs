using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Shared.Infra
{
    public class CommandExecutor : ICommandExecutor
    {
        private readonly IVideoSegmentManager _segmentManager;
        private readonly IVideoEngine _videoEngine;

        public CommandExecutor(IVideoSegmentManager segmentManager, IVideoEngine videoEngine)
        {
            _segmentManager = segmentManager ?? throw new ArgumentNullException(nameof(segmentManager));
            _videoEngine = videoEngine ?? throw new ArgumentNullException(nameof(videoEngine));
        }

        public void ExecuteCommand(EditCommand command)
        {
            switch (command.Type)
            {
                case CommandType.Load:
                    ExecuteLoad(command);
                    break;
                case CommandType.Cut:
                    ExecuteCut(command);
                    break;
                case CommandType.Hide:
                    ExecuteHide(command);
                    break;
                case CommandType.Show:
                    ExecuteShow(command);
                    break;
                case CommandType.Delete:
                    ExecuteDelete(command);
                    break;
            }
        }

        public void ExecuteCommands(IEnumerable<EditCommand> commands)
        {
            foreach (var command in commands)
            {
                ExecuteCommand(command);
            }
        }

        private async void ExecuteLoad(EditCommand command)
        {
            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                "CommandExecutor.cs:ExecuteLoad",
                "ExecuteLoad called",
                new { filePath = command.FilePath });

            if (string.IsNullOrEmpty(command.FilePath))
                throw new ArgumentException("FilePath is required for LOAD command");

            if (!File.Exists(command.FilePath))
                throw new FileNotFoundException($"Video file not found: {command.FilePath}");

            // TotalDurationを取得（既に読み込まれている動画の情報を使用）
            double duration = 0.0;
            
            // GetVideoInfoAsyncを使用して動画の長さを取得（非同期で実行）
            try
            {
                var videoInfo = await _videoEngine.GetVideoInfoAsync();
                if (videoInfo != null && videoInfo.Duration > 0)
                {
                    duration = videoInfo.Duration;
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "CommandExecutor.cs:ExecuteLoad",
                        "Video duration obtained from GetVideoInfoAsync",
                        new { duration = duration });
                }
            }
            catch (Exception ex)
            {
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "CommandExecutor.cs:ExecuteLoad",
                    "Failed to get video info, trying TotalDuration observable",
                    new { exceptionType = ex.GetType().Name, message = ex.Message });
            }

            // durationが0の場合は、TotalDurationの現在値から取得を試みる
            if (duration <= 0)
            {
                try
                {
                    // TotalDurationの現在値を取得
                    duration = _videoEngine.CurrentTotalDuration;
                    
                    if (duration <= 0)
                    {
                        // TotalDurationが0の場合は、値が更新されるまで待つ（最大3秒）
                        var timeout = DateTime.Now.AddSeconds(3);
                        var checkCount = 0;
                        while (duration <= 0 && DateTime.Now < timeout)
                        {
                            await Task.Delay(100); // 100ms待つ（UIスレッドをブロックしない）
                            duration = _videoEngine.CurrentTotalDuration;
                            checkCount++;
                        }
                        
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "CommandExecutor.cs:ExecuteLoad",
                            "Waited for TotalDuration",
                            new { duration = duration, checkCount = checkCount, timedOut = duration <= 0 });
                    }
                    
                    if (duration > 0)
                    {
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "CommandExecutor.cs:ExecuteLoad",
                            "Video duration obtained from CurrentTotalDuration",
                            new { duration = duration });
                    }
                }
                catch (Exception ex)
                {
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "CommandExecutor.cs:ExecuteLoad",
                        "Failed to get duration from CurrentTotalDuration",
                        new { exceptionType = ex.GetType().Name, message = ex.Message });
                }
            }

            // durationがまだ0の場合は、デフォルト値を使用（後でTotalDurationが更新されたら再作成される）
            if (duration <= 0)
            {
                duration = 100.0; // デフォルト値（後でTotalDurationが更新されたら再作成される）
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "CommandExecutor.cs:ExecuteLoad",
                    "Using default duration",
                    new { defaultDuration = duration });
            }

            // 既存セグメントの最後の終了時間を取得（新しい動画は下に追加）
            double startTime = 0.0;
            var existingSegments = _segmentManager.Segments;
            if (existingSegments.Count > 0)
            {
                startTime = existingSegments.Max(s => s.EndTime);
            }

            // 新しいセグメントを既存の最後に追加
            var segment = new VideoSegment
            {
                StartTime = startTime,
                EndTime = startTime + duration,
                Visible = true,
                State = SegmentState.Stopped,
                VideoFilePath = command.FilePath
            };

            // 既存のセグメントはクリアせず、追加する
            _segmentManager.AddSegment(segment);

            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                "CommandExecutor.cs:ExecuteLoad",
                "VideoSegment created and added",
                new { segmentId = segment.Id, startTime = segment.StartTime, endTime = segment.EndTime, duration = segment.Duration, videoFilePath = command.FilePath });
        }

        private void ExecuteCut(EditCommand command)
        {
            if (!command.Time.HasValue)
                throw new ArgumentException("Time is required for CUT command");

            var cutTime = command.Time.Value;
            var segment = _segmentManager.GetSegmentAtTime(cutTime);

            if (segment == null)
                throw new InvalidOperationException($"No segment found at time {cutTime}");

            // セグメントを分割
            var segment1 = new VideoSegment
            {
                StartTime = segment.StartTime,
                EndTime = cutTime,
                Visible = segment.Visible,
                State = SegmentState.Stopped,
                VideoFilePath = segment.VideoFilePath
            };

            var segment2 = new VideoSegment
            {
                StartTime = cutTime,
                EndTime = segment.EndTime,
                Visible = segment.Visible,
                State = SegmentState.Stopped,
                VideoFilePath = segment.VideoFilePath
            };

            _segmentManager.RemoveSegment(segment.Id);
            _segmentManager.AddSegment(segment1);
            _segmentManager.AddSegment(segment2);
        }

        private void ExecuteHide(EditCommand command)
        {
            if (!command.StartTime.HasValue || !command.EndTime.HasValue)
                throw new ArgumentException("StartTime and EndTime are required for HIDE command");

            var segments = _segmentManager.GetSegmentsByTimeRange(command.StartTime.Value, command.EndTime.Value);
            
            foreach (var segment in segments)
            {
                segment.Visible = false;
                segment.State = SegmentState.Hidden;
                _segmentManager.UpdateSegment(segment);
            }
        }

        private void ExecuteShow(EditCommand command)
        {
            if (!command.StartTime.HasValue || !command.EndTime.HasValue)
                throw new ArgumentException("StartTime and EndTime are required for SHOW command");

            var segments = _segmentManager.GetSegmentsByTimeRange(command.StartTime.Value, command.EndTime.Value);
            
            foreach (var segment in segments)
            {
                segment.Visible = true;
                segment.State = SegmentState.Stopped;
                _segmentManager.UpdateSegment(segment);
            }
        }

        private void ExecuteDelete(EditCommand command)
        {
            if (!command.StartTime.HasValue || !command.EndTime.HasValue)
                throw new ArgumentException("StartTime and EndTime are required for DELETE command");

            var segments = _segmentManager.GetSegmentsByTimeRange(command.StartTime.Value, command.EndTime.Value);
            
            foreach (var segment in segments)
            {
                _segmentManager.RemoveSegment(segment.Id);
            }
        }
    }
}
