using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data.SQLite;

namespace Octgn.Data
{
    class DatabaseHandler
    {
        private static Dictionary<string, List<string>> columnCache = new Dictionary<string, List<string>>();

        public static bool ColumnExists(string tableName, string columnName, SQLiteConnection connection)
        {
            if (!columnCache.ContainsKey(columnName) || !columnCache[tableName].Contains(columnName))
            {
                columnCache[tableName] = new List<string>();
                using (SQLiteCommand com = connection.CreateCommand())
                {
                    com.CommandText = "PRAGMA table_info(" + tableName + ")";
                    com.Prepare();
                    using (SQLiteDataReader reader = com.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int nameColumnOrdinal = reader.GetOrdinal("name");
                            string dbColumnName = reader.GetString(nameColumnOrdinal);
                            columnCache[tableName].Add(dbColumnName);
                        }
                    }
                }
            }
            return (columnCache[tableName].Contains(columnName));
        }

        public static bool AddColumn(string tableName, string columnName, PropertyType type, SQLiteConnection connection)
        {
            bool ret = false;

            using (SQLiteCommand com = connection.CreateCommand())
            {
                //YES I AM USING A STRINGBUILDER BECAUSE SQLITE WAS BEING A FUCKER. CHANGE IT IF YOU CAN MAKE IT WORK >_<
                //BLOODY PARAMETERS FUCKING UP CONSTANTLY. SQLITE IS SHIT IN MY OPINION </endrage>
                //TODO: Find out why ALTER commands do not always play nice with sqlitecommand parameters.
                StringBuilder sb = new StringBuilder("ALTER TABLE @tablename ADD [@columnname] @type DEFAULT '@default'");
                sb = sb.Replace("@tablename", tableName);
                sb = sb.Replace("@columnname", columnName);
                switch (type)
                {
                    case PropertyType.String:
                        sb = sb.Replace("@type", "TEXT");
                        sb = sb.Replace("@default", "");
                        break;
                    case PropertyType.Integer:
                        sb = sb.Replace("@type", "INTEGER");
                        sb = sb.Replace("@default", "");
                        break;
                    case PropertyType.GUID:
                        sb = sb.Replace("@type", "TEXT");
                        sb = sb.Replace("@default", "00000000-0000-0000-0000-000000000000");
                        break;
                    default:
                        sb = sb.Replace("@type", "TEXT");
                        sb = sb.Replace("@default", "");
                        break;
                }
                com.CommandText = sb.ToString();
                com.ExecuteNonQuery();
            }

            ret = ColumnExists(tableName, columnName, connection);
            return (ret);
        }
    
        public static void RebuildCardsTable(SQLiteConnection connection)
        {
            #region Find out what properties are left
            Dictionary<string, int> properties = new Dictionary<string, int>();
            using (SQLiteCommand com = connection.CreateCommand())
            {
                // must be reverse order to maintain configuration in case of conflicting property definitions.
                // i.e. [Power] INTEGER followed by [Power] TEXT
                com.CommandText = @"SELECT * FROM [custom_properties] ORDER BY [real_id] DESC";
                com.Prepare();
                using (SQLiteDataReader reader = com.ExecuteReader())
                {
                    int columnOrdinalName = reader.GetOrdinal("name");
                    int columnOrdinalType = reader.GetOrdinal("type");
                    while (reader.Read())
                    {
                        string propertyName = reader.GetString(columnOrdinalName);
                        int propertyType = reader.GetInt32(columnOrdinalType);
                        properties[propertyName] = propertyType;
                    }
                }
            }
            #endregion

            
            #region prepare SQL statements to recreate the cards table
            StringBuilder sqlCreate = new StringBuilder();
            sqlCreate.Append(@"CREATE TABLE [new_cards] (
  [real_id] INTEGER PRIMARY KEY AUTOINCREMENT, 
  [id] TEXT UNIQUE NOT NULL, 
  [game_id] TEXT NOT NULL,
  [set_real_id] INTEGER REFERENCES [sets]([real_id]) ON DELETE CASCADE ON UPDATE CASCADE, 
  [name] TEXT NOT NULL, 
  [image] TEXT NOT NULL,
  [alternate] TEXT,
  [dependent] TEXT");
            
            StringBuilder sqlCopy = new StringBuilder();
            sqlCopy.Append("INSERT INTO [new_cards] SELECT [real_id], [id], [game_id], [set_real_id], [name], [image], [alternate], [dependent]");
            
            foreach (String name in properties.Keys)
            {
                sqlCreate.Append(String.Format(@",
  [{0}] {1} DEFAULT '{2}'",
                                                  name,
                                                  properties[name] == 1 ? "INTEGER" : "TEXT",
                                                  ""));
                sqlCopy.Append(String.Format(", [{0}]", name));
            }
            sqlCreate.Append("\n);");
            sqlCopy.Append(" FROM [cards];");
            #endregion
            
            
            #region Rebuild table
            using (SQLiteCommand comCreate = connection.CreateCommand(),
                   comCopy = connection.CreateCommand(),
                   comDrop = connection.CreateCommand(),
                   comRename = connection.CreateCommand())
            {
                comCreate.CommandText = sqlCreate.ToString();
                comCreate.Prepare();
                comCopy.CommandText = sqlCopy.ToString();
                comCopy.Prepare();
                comDrop.CommandText = "DROP TABLE [cards];";
                comDrop.Prepare();
                comRename.CommandText = "ALTER TABLE [new_cards] RENAME TO [cards];";
                comRename.Prepare();
                
                comCreate.ExecuteNonQuery();
                comCopy.ExecuteNonQuery();
                comDrop.ExecuteNonQuery();
                comRename.ExecuteNonQuery();
            }
            #endregion
        }
    }
}
