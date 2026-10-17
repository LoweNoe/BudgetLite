using System.Text.Json;
using System.Text.Json.Serialization;
using BudgetApp.Core.Models;

namespace BudgetApp.Data;

// Source-generated metadata keeps serialization working when the app is trimmed for single-file publishing.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(VersionedDocument<List<Transaction>>))]
[JsonSerializable(typeof(VersionedDocument<List<Category>>))]
[JsonSerializable(typeof(VersionedDocument<List<CategoryRule>>))]
[JsonSerializable(typeof(VersionedDocument<List<Account>>))]
[JsonSerializable(typeof(VersionedDocument<List<BankTip>>))]
[JsonSerializable(typeof(VersionedDocument<AppSettings>))]
[JsonSerializable(typeof(VersionedDocument<List<CategoryBudget>>))]
internal sealed partial class DataJsonContext : JsonSerializerContext
{
}
