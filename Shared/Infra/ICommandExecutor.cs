using System.Collections.Generic;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Shared.Infra
{
    public interface ICommandExecutor
    {
        void ExecuteCommand(EditCommand command);
        void ExecuteCommands(IEnumerable<EditCommand> commands);
    }
}
