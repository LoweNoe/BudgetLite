using BudgetApp.Core.Models;
using static BudgetApp.Core.Models.CategoryIds;

namespace BudgetApp.Core.Categorisation;

public static class BuiltInCatalog
{
    private const int MerchantPriority = 100;
    private const int KeywordPriority = 50;
    private const int MccPriority = 10;

    public static List<Category> CreateCategories() =>
    [
        Cat(Groceries, "Groceries", "cart", "chart-1", essential: true),
        Cat(Takeaways, "Takeaways", "utensils", "chart-5", essential: false),
        Cat(Transport, "Transport", "car", "chart-9", essential: true),
        Cat(Fuel, "Fuel", "fuel", "chart-3", essential: true),
        Cat(Subscriptions, "Subscriptions", "repeat", "chart-6", essential: false),
        Cat(AirtimeData, "Airtime & Data", "phone", "chart-12", essential: true),
        Cat(Utilities, "Utilities", "zap", "chart-7", essential: true),
        Cat(BankFees, "Bank Fees", "landmark", "chart-11", essential: true),
        Cat(Entertainment, "Entertainment", "film", "chart-10", essential: false),
        Cat(Shopping, "Shopping", "bag", "chart-2", essential: false),
        Cat(Health, "Health", "heart", "chart-4", essential: true),
        Cat(Transfers, "Transfers", "arrows", "chart-8", essential: true),
        Cat(Income, "Income", "wallet", "chart-1", essential: true),
        Cat(Uncategorised, "Uncategorised", "help", "chart-8", essential: false),
    ];

    public static List<CategoryRule> CreateRules()
    {
        var rules = new List<CategoryRule>();

        rules.AddRange(Merchants(Groceries,
            "WOOLWORTHS", "WOOLIES", "PICK N PAY", "PNP", "CHECKERS", "SHOPRITE", "SPAR", "KWIKSPAR", "SUPERSPAR",
            "USAVE", "FOOD LOVERS", "BOXER", "OK FOODS", "OK MINIMARK", "SIXTY60", "PNP ASAP", "WOOLIES DASH"));

        rules.AddRange(Merchants(Takeaways,
            "UBER EATS", "UBEREATS", "MR D", "MRD", "MR DELIVERY", "KFC", "MCDONALDS", "MCD", "NANDOS", "STEERS",
            "WIMPY", "DEBONAIRS", "ROMANS PIZZA", "BURGER KING", "FISHAWAYS", "SPUR", "OCEAN BASKET", "PIZZA HUT",
            "DOMINOS", "STARBUCKS", "VIDA E CAFFE", "SEATTLE COFFEE", "MUGG BEAN", "BOOST JUICE", "CHICKEN LICKEN"));

        rules.AddRange(Merchants(Transport,
            "UBER", "BOLT", "INDRIVER", "GAUTRAIN", "MYCITI", "PUTCO", "SANRAL", "TOLL", "PARKING", "AARTO"));

        rules.AddRange(Merchants(Fuel,
            "ENGEN", "SHELL", "BP", "SASOL", "CALTEX", "ASTRON", "TOTALENERGIES", "TOTAL SERVICE"));

        rules.AddRange(Merchants(Subscriptions,
            "NETFLIX", "SHOWMAX", "SPOTIFY", "DSTV", "MULTICHOICE", "YOUTUBE PREMIUM", "YOUTUBEPREMIUM", "APPLE.COM",
            "ICLOUD", "DISNEY PLUS", "DISNEYPLUS", "AMAZON PRIME", "PRIMEVIDEO", "PRIME VIDEO", "DEEZER", "XBOX",
            "PLAYSTATION", "MICROSOFT 365", "GOOGLE ONE", "CHATGPT", "BOX OFFICE"));

        rules.AddRange(Merchants(AirtimeData,
            "VODACOM", "MTN", "CELL C", "TELKOM", "RAIN", "AIRTIME", "PREPAID DATA", "VOD PREPAID"));

        rules.AddRange(Merchants(Utilities,
            "ESKOM", "PREPAID ELEC", "ELECTRICITY", "MUNICIPALITY", "CITY OF CAPE TOWN", "CITY OF JOHANNESBURG",
            "CITY OF TSHWANE", "ETHEKWINI", "CITY POWER", "AFRIHOST", "VUMATEL", "OPENSERVE", "COOL IDEAS",
            "WEBAFRICA", "OCTOTEL", "FROGFOOT", "HEROTEL"));

        rules.AddRange(Merchants(BankFees,
            "MONTHLY ACCOUNT FEE", "SERVICE FEE", "ADMIN FEE", "CASH WITHDRAWAL FEE", "BANK CHARGES", "SMS NOTIFICATION",
            "NOTIFIC FEE", "OVERDRAFT INTEREST", "INTEREST ON OVERDRAFT", "UNPAID", "HONOURING FEE", "MONTHLY FEE"));
        rules.Add(Rule(BankFees, "FEE", RuleMatchType.Contains, KeywordPriority, AmountDirection.Debits));
        // FNB prefixes its own fee lines with "#".
        rules.Add(Rule(BankFees, @"^\s*#", RuleMatchType.Regex, KeywordPriority, AmountDirection.Debits));

        rules.AddRange(Merchants(Entertainment,
            "STER KINEKOR", "NU METRO", "COMPUTICKET", "TICKETPRO", "QUICKET", "STEAM", "HOLLYWOODBETS", "BETWAY",
            "SUPABETS", "LOTTO", "ITHUBA", "VIRGIN ACTIVE", "PLANET FITNESS"));

        rules.AddRange(Merchants(Shopping,
            "TAKEALOT", "AMAZON", "MR PRICE", "MRP", "EDGARS", "TRUWORTHS", "FOSCHINI", "H&M", "ZARA", "SUPERBALIST",
            "GAME", "MAKRO", "CNA", "EXCLUSIVE BOOKS", "BUILDERS", "CASHBUILD", "LEROY MERLIN", "TEMU", "SHEIN", "PEP",
            "ACKERMANS", "SPORTSMANS WAREHOUSE", "TOTALSPORTS", "INCREDIBLE CONNECTION", "HIFI CORP", "WESTPACK",
            "CASH CONVERTERS", "CLOTHING JUNCTION", "BASH"));

        rules.AddRange(Merchants(Health,
            "CLICKS", "DIS-CHEM", "DISCHEM", "MEDIRITE", "PHARMACY", "DISCOVERY HEALTH", "BONITAS", "MOMENTUM HEALTH",
            "NETCARE", "MEDICLINIC", "LIFE HEALTHCARE", "PATHCARE", "LANCET", "AMPATH", "SPEC SAVERS", "SPECSAVERS"));

        rules.AddRange(Merchants(Transfers,
            "IB TRANSFER", "INTERNET TRF", "INT-BANKING TRANSFER", "TRANSFER", "SEND MONEY", "EWALLET", "CASH SEND",
            "INSTANT MONEY", "ATM CASH", "CASH WITHDRAWAL", "PAYSHAP"));

        rules.Add(Rule(Income, "SALARY", RuleMatchType.Contains, MerchantPriority, AmountDirection.Credits));
        rules.Add(Rule(Income, "WAGES", RuleMatchType.Contains, MerchantPriority, AmountDirection.Credits));
        rules.Add(Rule(Income, "PAYROLL", RuleMatchType.Contains, MerchantPriority, AmountDirection.Credits));
        rules.Add(Rule(Income, "INTEREST", RuleMatchType.Contains, KeywordPriority, AmountDirection.Credits));

        rules.AddRange(MerchantCategoryCodes());
        return rules;
    }

    // Card networks tag every swipe with an ISO 18245 merchant category code. These only fire
    // when a statement actually carries an MCC column; merchant names above take precedence.
    private static IEnumerable<CategoryRule> MerchantCategoryCodes()
    {
        (string Codes, string CategoryId)[] map =
        [
            ("5411", Groceries), ("5422", Groceries), ("5441", Groceries), ("5451", Groceries), ("5462", Groceries), ("5499", Groceries),
            ("5811-5814", Takeaways),
            ("4111-4131", Transport), ("4784", Transport), ("4789", Transport), ("7523", Transport),
            ("5541-5542", Fuel), ("5983", Fuel),
            ("4899", Subscriptions), ("5815", Subscriptions), ("5817-5818", Subscriptions),
            ("4812", AirtimeData), ("4814", AirtimeData),
            ("4900", Utilities), ("4816", Utilities),
            ("7832", Entertainment), ("7922", Entertainment), ("7991", Entertainment), ("7995-7997", Entertainment), ("7999", Entertainment), ("5816", Entertainment),
            ("5912", Health), ("8011-8099", Health),
            ("5200", Shopping), ("5251", Shopping), ("5310-5311", Shopping), ("5600-5699", Shopping), ("5712", Shopping),
            ("5732", Shopping), ("5941-5945", Shopping), ("5977", Shopping), ("5999", Shopping),
            ("6011", Transfers),
        ];

        return map.Select(m => Rule(m.CategoryId, m.Codes, RuleMatchType.MerchantCategoryCode, MccPriority, AmountDirection.Any));
    }

    private static IEnumerable<CategoryRule> Merchants(string categoryId, params string[] patterns) =>
        patterns.Select(p => Rule(categoryId, p, RuleMatchType.Contains, MerchantPriority, AmountDirection.Debits));

    private static CategoryRule Rule(string categoryId, string pattern, RuleMatchType type, int priority, AmountDirection direction) => new()
    {
        Pattern = pattern,
        MatchType = type,
        CategoryId = categoryId,
        Priority = priority,
        Source = RuleSource.BuiltIn,
        AppliesTo = direction,
    };

    private static Category Cat(string id, string name, string icon, string colour, bool essential) => new()
    {
        Id = id,
        Name = name,
        Icon = icon,
        Colour = colour,
        IsEssential = essential,
        IsBuiltIn = true,
    };
}
