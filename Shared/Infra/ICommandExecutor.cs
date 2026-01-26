using System.Collections.Generic;
using System.Threading.Tasks;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Shared.Infra
{
    /// <summary>
    /// コマンド実行のインターフェース
    /// LOAD, CUT, HIDE, SHOW, DELETE, MERGE, SPEED コマンドを実行
    /// </summary>
    public interface ICommandExecutor
    {
        /// <summary>
        /// 単一のコマンドを実行
        /// </summary>
        void ExecuteCommand(EditCommand command);

        /// <summary>
        /// 複数のコマンドを順次実行
        /// </summary>
        void ExecuteCommands(IEnumerable<EditCommand> commands);

        /// <summary>
        /// 単一のコマンドを実行し、結果を返す
        /// </summary>
        CommandResult ExecuteCommandWithResult(EditCommand command);

        /// <summary>
        /// 複数のコマンドを実行し、レポートを返す
        /// </summary>
        CommandExecutionReport ExecuteCommandsWithReport(IEnumerable<EditCommand> commands);

        /// <summary>
        /// LOADコマンドを非同期で実行
        /// </summary>
        Task<CommandResult> ExecuteLoadAsync(EditCommand command);
    }
}
