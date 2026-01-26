using System.Collections.Generic;
using System.Text.RegularExpressions;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Shared.Infra
{
    public class CommandParser : ICommandParser
    {
        // LOAD video.mp4
        private static readonly Regex LoadPattern = new Regex(@"^\s*LOAD\s+(.+)$", RegexOptions.IgnoreCase);
        
        // CUT 00:01:23.456
        private static readonly Regex CutPattern = new Regex(@"^\s*CUT\s+(\d{2}):(\d{2}):(\d{2})\.(\d{3})$");
        
        // HIDE 00:01:00.000 00:01:30.000
        private static readonly Regex HidePattern = new Regex(@"^\s*HIDE\s+(\d{2}):(\d{2}):(\d{2})\.(\d{3})\s+(\d{2}):(\d{2}):(\d{2})\.(\d{3})$");
        
        // SHOW 00:01:00.000 00:01:30.000
        private static readonly Regex ShowPattern = new Regex(@"^\s*SHOW\s+(\d{2}):(\d{2}):(\d{2})\.(\d{3})\s+(\d{2}):(\d{2}):(\d{2})\.(\d{3})$");
        
        // DELETE 00:00:30.000 00:01:00.000
        private static readonly Regex DeletePattern = new Regex(@"^\s*DELETE\s+(\d{2}):(\d{2}):(\d{2})\.(\d{3})\s+(\d{2}):(\d{2}):(\d{2})\.(\d{3})$");
        
        // --- [00:01:15.000 -> 00:01:20.500] --- (従来形式)
        private static readonly Regex SeparatorPattern = new Regex(@"^[-]{3,}\s*\[(\d{2}):(\d{2}):(\d{2})\.(\d{3})\s*->\s*(\d{2}):(\d{2}):(\d{2})\.(\d{3})\]\s*[-]{3,}$");

        public List<EditCommand> ParseCommands(string text)
        {
            var commands = new List<EditCommand>();
            var lines = text.Split('\n');
            
            for (int i = 0; i < lines.Length; i++)
            {
                var command = ParseLine(lines[i], i + 1);
                if (command != null)
                {
                    commands.Add(command);
                }
            }
            
            return commands;
        }

        public EditCommand? ParseLine(string line, int lineNumber)
        {
            // LOAD コマンド
            var loadMatch = LoadPattern.Match(line);
            if (loadMatch.Success)
            {
                return new EditCommand
                {
                    Type = CommandType.Load,
                    FilePath = loadMatch.Groups[1].Value.Trim(),
                    LineNumber = lineNumber
                };
            }

            // CUT コマンド
            var cutMatch = CutPattern.Match(line);
            if (cutMatch.Success)
            {
                var time = ParseTime(cutMatch.Groups);
                return new EditCommand
                {
                    Type = CommandType.Cut,
                    Time = time,
                    LineNumber = lineNumber
                };
            }

            // HIDE コマンド
            var hideMatch = HidePattern.Match(line);
            if (hideMatch.Success)
            {
                var (startTime, endTime) = ParseTimeRange(hideMatch.Groups);
                return new EditCommand
                {
                    Type = CommandType.Hide,
                    StartTime = startTime,
                    EndTime = endTime,
                    LineNumber = lineNumber
                };
            }

            // SHOW コマンド
            var showMatch = ShowPattern.Match(line);
            if (showMatch.Success)
            {
                var (startTime, endTime) = ParseTimeRange(showMatch.Groups);
                return new EditCommand
                {
                    Type = CommandType.Show,
                    StartTime = startTime,
                    EndTime = endTime,
                    LineNumber = lineNumber
                };
            }

            // DELETE コマンド
            var deleteMatch = DeletePattern.Match(line);
            if (deleteMatch.Success)
            {
                var (startTime, endTime) = ParseTimeRange(deleteMatch.Groups);
                return new EditCommand
                {
                    Type = CommandType.Delete,
                    StartTime = startTime,
                    EndTime = endTime,
                    LineNumber = lineNumber
                };
            }

            return null;
        }

        private double ParseTime(GroupCollection groups)
        {
            var hours = int.Parse(groups[1].Value);
            var minutes = int.Parse(groups[2].Value);
            var seconds = int.Parse(groups[3].Value);
            var milliseconds = int.Parse(groups[4].Value);
            return hours * 3600 + minutes * 60 + seconds + milliseconds / 1000.0;
        }

        private (double startTime, double endTime) ParseTimeRange(GroupCollection groups)
        {
            var startHours = int.Parse(groups[1].Value);
            var startMinutes = int.Parse(groups[2].Value);
            var startSeconds = int.Parse(groups[3].Value);
            var startMilliseconds = int.Parse(groups[4].Value);
            var startTime = startHours * 3600 + startMinutes * 60 + startSeconds + startMilliseconds / 1000.0;

            var endHours = int.Parse(groups[5].Value);
            var endMinutes = int.Parse(groups[6].Value);
            var endSeconds = int.Parse(groups[7].Value);
            var endMilliseconds = int.Parse(groups[8].Value);
            var endTime = endHours * 3600 + endMinutes * 60 + endSeconds + endMilliseconds / 1000.0;

            return (startTime, endTime);
        }
    }
}
