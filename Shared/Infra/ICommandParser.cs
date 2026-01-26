using System.Collections.Generic;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Shared.Infra
{
    public interface ICommandParser
    {
        List<EditCommand> ParseCommands(string text);
        EditCommand? ParseLine(string line, int lineNumber);
    }
}
