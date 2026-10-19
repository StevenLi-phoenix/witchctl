using System;
using System.Collections.Generic;
using System.Linq;

namespace WitchCtl
{
    /// <summary>
    /// Read-only dump of the game's own config tables (`GameConfigManager`: Enemy, EnemyCard, Card, Buff, Relic, Career, Level, ...), whole rows with every
    /// column including the script texts, filtered by a keyword. The resource bundles are compressed, so this is how a boss's moves, a card's script
    /// or a buff's rules are read: `witchctl raw GET '/config?table=EnemyCard&amp;match=镜像复制'`. No table name lists the tables.
    /// </summary>
    internal static class ConfigDump
    {
        internal static object Dump(string table, string match, int limit)
        {
            var names = Enum.GetNames(typeof(DataType));
            if (string.IsNullOrEmpty(table)) return new { tables = names };
            if (!Enum.TryParse<DataType>(table, true, out var type)) return new { error = "unknown table", tables = names };
            var data = Singleton<GameConfigManager>.Instance.GetTable(type);
            if (data == null) return new { error = "table not loaded", table };
            var rows = data.Getlines();
            var hits = rows.Where(r => string.IsNullOrEmpty(match) || r.Values.Any(v => v != null && v.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            return new { table = type.ToString(), total = rows.Count, matched = hits.Count, rows = hits.Take(limit <= 0 ? 20 : limit).ToList() };
        }
    }
}
