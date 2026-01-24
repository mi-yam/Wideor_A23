using System;
using System.IO;

namespace Wideor.App.Shared.Infra
{
    /// <summary>
    /// デバッグログを書き込むためのヘルパークラス
    /// </summary>
    public static class LogHelper
    {
        private static readonly string LogFilePath;
        private static readonly object LockObject = new object();

        static LogHelper()
        {
            // まず一時ディレクトリを使用（確実に動作する）
            var tempLogPath = Path.Combine(Path.GetTempPath(), "Wideor_A23_debug.log");
            System.Diagnostics.Debug.WriteLine($"[LogHelper] Using temp log path: {tempLogPath}");
            
            try
            {
                // ワークスペースのルートディレクトリを取得
                var workspaceRoot = GetWorkspaceRoot();
                var logDir = Path.Combine(workspaceRoot, ".cursor");
                
                // デバッグ用: コンソールに出力（Visual Studioの出力ウィンドウに表示される）
                System.Diagnostics.Debug.WriteLine($"[LogHelper] WorkspaceRoot: {workspaceRoot}");
                System.Diagnostics.Debug.WriteLine($"[LogHelper] WorkspaceRoot exists: {Directory.Exists(workspaceRoot)}");
                System.Diagnostics.Debug.WriteLine($"[LogHelper] LogDir: {logDir}");
                
                // ワークスペースルートが存在し、.cursorディレクトリを作成できる場合のみ使用
                if (Directory.Exists(workspaceRoot))
                {
                    try
                    {
                        // .cursorディレクトリが存在しない場合は作成
                        if (!Directory.Exists(logDir))
                        {
                            Directory.CreateDirectory(logDir);
                            System.Diagnostics.Debug.WriteLine($"[LogHelper] Created directory: {logDir}");
                        }
                        
                        // ディレクトリが存在することを確認
                        if (Directory.Exists(logDir))
                        {
                            var workspaceLogPath = Path.Combine(logDir, "debug.log");
                            
                            // テスト書き込み
                            try
                            {
                                File.AppendAllText(workspaceLogPath, $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"LogHelper:StaticConstructor\",\"message\":\"LogHelper initialized\",\"data\":{{\"logFilePath\":\"{workspaceLogPath}\"}}}}\n");
                                LogFilePath = workspaceLogPath;
                                System.Diagnostics.Debug.WriteLine($"[LogHelper] Using workspace log path: {LogFilePath}");
                                System.Diagnostics.Debug.WriteLine($"[LogHelper] Test write successful");
                                return; // 成功したらここで終了
                            }
                            catch (Exception writeEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"[LogHelper] Test write to workspace failed: {writeEx.Message}");
                                System.Diagnostics.Debug.WriteLine($"[LogHelper] Falling back to temp directory");
                            }
                        }
                    }
                    catch (Exception dirEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LogHelper] Failed to create/use workspace directory: {dirEx.Message}");
                        System.Diagnostics.Debug.WriteLine($"[LogHelper] Falling back to temp directory");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[LogHelper] WorkspaceRoot does not exist, using temp directory");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LogHelper] Static constructor failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[LogHelper] Static constructor exception type: {ex.GetType().Name}");
            }
            
            // フォールバック: 一時ディレクトリを使用
            LogFilePath = tempLogPath;
            System.Diagnostics.Debug.WriteLine($"[LogHelper] Using temp log path: {LogFilePath}");
            
            // 一時ディレクトリへのテスト書き込み
            try
            {
                File.AppendAllText(LogFilePath, $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"LogHelper:StaticConstructor\",\"message\":\"LogHelper initialized (using temp)\",\"data\":{{\"logFilePath\":\"{LogFilePath}\"}}}}\n");
                System.Diagnostics.Debug.WriteLine($"[LogHelper] Test write to temp successful");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LogHelper] Test write to temp failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[LogHelper] Exception type: {ex.GetType().Name}");
            }
        }

        private static string GetWorkspaceRoot()
        {
            try
            {
                // AppDomain.CurrentDomain.BaseDirectory は bin/Debug/net8.0-windows を指す
                // その親の親の親（3階層上）がワークスペースルート（Wideor_A23）
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                System.Diagnostics.Debug.WriteLine($"[LogHelper] BaseDirectory: {baseDir}");
                
                var workspaceRoot = baseDir;

                // bin/Debug/net8.0-windows から プロジェクトルート（Wideor_A23）へ
                // 実際のログを見ると、Parent 4が正しいパス（Wideor_A23）になっている
                // 1階層: bin/Debug/net8.0-windows -> bin/Debug
                // 2階層: bin/Debug -> bin
                // 3階層: bin -> Wideor_A23（プロジェクトルート）
                // ただし、BaseDirectoryの末尾に\がある場合があるので、3階層で試す
                for (int i = 0; i < 3; i++)
                {
                    var parent = Directory.GetParent(workspaceRoot);
                    if (parent != null)
                    {
                        workspaceRoot = parent.FullName;
                        System.Diagnostics.Debug.WriteLine($"[LogHelper] Parent {i + 1}: {workspaceRoot}");
                    }
                    else
                    {
                        break;
                    }
                }
                
                // Wideor_A23.csprojが存在するか確認して、正しいパスか検証
                var csprojPath = Path.Combine(workspaceRoot, "Wideor_A23.csproj");
                if (!File.Exists(csprojPath))
                {
                    // もう1階層上がる
                    var parent = Directory.GetParent(workspaceRoot);
                    if (parent != null)
                    {
                        workspaceRoot = parent.FullName;
                        System.Diagnostics.Debug.WriteLine($"[LogHelper] Adjusted to Parent 4: {workspaceRoot}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[LogHelper] Calculated workspace root: {workspaceRoot}");
                System.Diagnostics.Debug.WriteLine($"[LogHelper] Workspace root exists: {Directory.Exists(workspaceRoot)}");

                // ワークスペースルートが見つからない場合、または存在しない場合は一時ディレクトリを使用
                if (!Directory.Exists(workspaceRoot))
                {
                    System.Diagnostics.Debug.WriteLine($"[LogHelper] Workspace root does not exist, using temp directory");
                    workspaceRoot = Path.GetTempPath();
                }

                return workspaceRoot;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LogHelper] GetWorkspaceRoot failed: {ex.Message}");
                return Path.GetTempPath();
            }
        }

        public static void WriteLog(string location, string message, object? data = null)
        {
            try
            {
                if (string.IsNullOrEmpty(LogFilePath))
                {
                    System.Diagnostics.Debug.WriteLine($"[LogHelper] LogFilePath is null or empty");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[LogHelper] WriteLog called: {location} - {message}");
                System.Diagnostics.Debug.WriteLine($"[LogHelper] LogFilePath: {LogFilePath}");

                // 親ディレクトリが存在することを確認
                var logDir = Path.GetDirectoryName(LogFilePath);
                System.Diagnostics.Debug.WriteLine($"[LogHelper] Log directory: {logDir}");
                
                if (!string.IsNullOrEmpty(logDir))
                {
                    if (!Directory.Exists(logDir))
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine($"[LogHelper] Creating log directory: {logDir}");
                            Directory.CreateDirectory(logDir);
                            System.Diagnostics.Debug.WriteLine($"[LogHelper] Created log directory: {logDir}");
                            
                            // 作成後に存在確認
                            if (!Directory.Exists(logDir))
                            {
                                System.Diagnostics.Debug.WriteLine($"[LogHelper] Directory creation failed - directory still does not exist");
                                return;
                            }
                        }
                        catch (Exception dirEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[LogHelper] Failed to create log directory: {dirEx.Message}");
                            System.Diagnostics.Debug.WriteLine($"[LogHelper] Exception type: {dirEx.GetType().Name}");
                            System.Diagnostics.Debug.WriteLine($"[LogHelper] StackTrace: {dirEx.StackTrace}");
                            return;
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[LogHelper] Log directory already exists: {logDir}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[LogHelper] Log directory is null or empty");
                    return;
                }

                var logEntry = new
                {
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    location = location,
                    message = message,
                    data = data
                };

                var json = System.Text.Json.JsonSerializer.Serialize(logEntry);
                var logLine = json + "\n";

                lock (LockObject)
                {
                    System.Diagnostics.Debug.WriteLine($"[LogHelper] Attempting to append to log file: {LogFilePath}");
                    File.AppendAllText(LogFilePath, logLine);
                    System.Diagnostics.Debug.WriteLine($"[LogHelper] WriteLog successful: {location} - {message}");
                }
            }
            catch (Exception ex)
            {
                // ログの書き込みに失敗した場合、イベントログやコンソールに出力
                System.Diagnostics.Debug.WriteLine($"[LogHelper] Failed to write log: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[LogHelper] Exception type: {ex.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"[LogHelper] StackTrace: {ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// ログファイルのパスを取得（デバッグ用）
        /// </summary>
        public static string GetLogFilePath() => LogFilePath ?? "Not initialized";
    }
}
