using System.Text.RegularExpressions;
using BudgetApp.Core.Abstractions;
using BudgetApp.Core.Models;

namespace BudgetApp.Core.Categorisation;

public sealed class RuleBasedCategoriser : ICategoriser
{
    private IReadOnlyList<CompiledRule> _rules = [];

    // User corrections always win, then merchant names, then card MCCs as a broad fallback.
    // Within a tier, longer patterns go first so "UBER EATS" beats "UBER".
    public void SetRules(IEnumerable<CategoryRule> rules)
    {
        _rules = rules
            .OrderBy(r => r.Source == RuleSource.UserCorrection ? 0 : 1)
            .ThenBy(r => r.MatchType == RuleMatchType.MerchantCategoryCode ? 1 : 0)
            .ThenByDescending(r => r.Priority)
            .ThenByDescending(r => r.Pattern.Length)
            .Select(CompiledRule.TryCreate)
            .OfType<CompiledRule>()
            .ToList();
    }

    public CategoryMatch Categorise(CategorisationInput input)
    {
        foreach (var rule in _rules)
        {
            if (rule.IsMatch(input))
                return new CategoryMatch(rule.Rule.CategoryId, rule.Rule);
        }

        return new CategoryMatch(input.Amount > 0 ? CategoryIds.Income : CategoryIds.Uncategorised, null);
    }

    public static bool Matches(CategoryRule rule, CategorisationInput input) =>
        CompiledRule.TryCreate(rule)?.IsMatch(input) ?? false;

    private sealed class CompiledRule
    {
        private readonly Regex? _regex;
        private readonly (int From, int To)? _mccRange;

        private CompiledRule(CategoryRule rule, Regex? regex, (int, int)? mccRange)
        {
            Rule = rule;
            _regex = regex;
            _mccRange = mccRange;
        }

        public CategoryRule Rule { get; }

        public static CompiledRule? TryCreate(CategoryRule rule)
        {
            var pattern = rule.Pattern.Trim();
            if (pattern.Length == 0)
                return null;

            try
            {
                return rule.MatchType switch
                {
                    RuleMatchType.Contains => new CompiledRule(rule, BuildContains(pattern), null),
                    RuleMatchType.Regex => new CompiledRule(rule, BuildRegex(pattern), null),
                    RuleMatchType.MerchantCategoryCode => ParseMcc(pattern) is { } range ? new CompiledRule(rule, null, range) : null,
                    _ => null,
                };
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        public bool IsMatch(CategorisationInput input)
        {
            if (Rule.AppliesTo == AmountDirection.Debits && input.Amount >= 0) return false;
            if (Rule.AppliesTo == AmountDirection.Credits && input.Amount <= 0) return false;

            if (_mccRange is { } range)
                return int.TryParse(input.MerchantCategoryCode, out var mcc) && mcc >= range.From && mcc <= range.To;

            return _regex!.IsMatch(input.CleanDescription) || _regex.IsMatch(input.RawDescription);
        }

        // "Contains" means a whole-word match, so "SPAR" doesn't fire on "SPARKLE".
        private static Regex BuildContains(string pattern)
        {
            var words = pattern.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape);
            return BuildRegex($@"(?<![A-Z0-9]){string.Join(@"\s+", words)}(?![A-Z0-9])");
        }

        private static Regex BuildRegex(string pattern) =>
            new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        private static (int, int)? ParseMcc(string pattern)
        {
            var parts = pattern.Split('-', 2, StringSplitOptions.TrimEntries);
            if (!int.TryParse(parts[0], out var from)) return null;
            if (parts.Length == 1) return (from, from);
            return int.TryParse(parts[1], out var to) ? (from, to) : null;
        }
    }
}
