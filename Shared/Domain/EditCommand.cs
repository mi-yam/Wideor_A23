namespace Wideor.App.Shared.Domain
{
    public enum CommandType
    {
        Load,
        Cut,
        Hide,
        Show,
        Delete
    }

    public class EditCommand
    {
        public CommandType Type { get; set; }
        public double? Time { get; set; }          // CUT用
        public double? StartTime { get; set; }     // HIDE/SHOW/DELETE用
        public double? EndTime { get; set; }       // HIDE/SHOW/DELETE用
        public string? FilePath { get; set; }      // LOAD用
        public int LineNumber { get; set; }        // テキストエディタ上の行番号
        
        public override string ToString()
        {
            return Type switch
            {
                CommandType.Load => $"LOAD {FilePath}",
                CommandType.Cut => $"CUT {Time:00:00:00.000}",
                CommandType.Hide => $"HIDE {StartTime:00:00:00.000} {EndTime:00:00:00.000}",
                CommandType.Show => $"SHOW {StartTime:00:00:00.000} {EndTime:00:00:00.000}",
                CommandType.Delete => $"DELETE {StartTime:00:00:00.000} {EndTime:00:00:00.000}",
                _ => base.ToString()
            };
        }
    }
}
