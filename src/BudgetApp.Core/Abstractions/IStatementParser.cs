using BudgetApp.Core.Import;

namespace BudgetApp.Core.Abstractions;

public interface IStatementParser
{
    string Name { get; }

    // Bank-specific parsers return a higher value so they win over the generic CSV parser.
    int Specificity { get; }

    bool CanParse(StatementFile file);

    ParseResult Parse(StatementFile file, ColumnMapping? mapping = null);
}
