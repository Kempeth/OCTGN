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

        /// <summary>
        /// Rebuilds the [cards] table to only include the columns specified in the [custom_properties] table.
        /// </summary>
        /// <param name="connection">The connection to use</param>
        public static void RebuildCardsTable(SQLiteConnection connection)
        {
            #region Find out what properties are registered
            Dictionary<string, int> properties = new Dictionary<string, int>();
            using (SQLiteCommand com = connection.CreateCommand())
            {
                com.CommandText = @"SELECT * FROM [custom_properties]";
                com.Prepare();
                using (SQLiteDataReader reader = com.ExecuteReader())
                {
                    int columnOrdinalName = reader.GetOrdinal("name");
                    int columnOrdinalType = reader.GetOrdinal("type");
                    while (reader.Read())
                    {
                        string propertyName = reader.GetString(columnOrdinalName);
                        int propertyType = reader.GetInt32(columnOrdinalType);
                        
                        if (!properties.ContainsKey(propertyName))
                        {
                            properties[propertyName] = propertyType;
                        }
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
            StringBuilder sqlCopy2 = new StringBuilder();
            sqlCopy.Append("INSERT INTO [new_cards] ([real_id], [id], [game_id], [set_real_id], [name], [image], [alternate], [dependent]");
            sqlCopy2.Append(") SELECT [real_id], [id], [game_id], [set_real_id], [name], [image], [alternate], [dependent]");
            
            foreach (String name in properties.Keys)
            {
                sqlCreate.Append(String.Format(@",
  [{0}] {1} DEFAULT '{2}'",
                                                  name,
                                                  properties[name] == 1 ? "INTEGER" : "TEXT",
                                                  ""));
                if (ColumnExists("cards", name, connection))
                {
                    // only copy columns that already exist
                    sqlCopy.Append(String.Format(", [{0}]", name));
                    sqlCopy2.Append(String.Format(", [{0}]", name));
                }
            }
            sqlCreate.Append("\n);");
            sqlCopy.Append(sqlCopy2.ToString());
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
        
        /// <summary>
        /// Returns a list of all column names registered with the specified game.
        /// </summary>
        /// <param name="game">The guid of the game</param>
        /// <param name="connection">the connection to use</param>
        /// <returns>A list of column names.</returns>
        public static IList<String> GetCardProperties(Guid game, SQLiteConnection connection)
        {
            List<String> properties = new List<string>();
            
            using (SQLiteCommand com = connection.CreateCommand())
            {
                com.CommandText = "SELECT * FROM [custom_properties] WHERE [game_id] LIKE @game_id;";
                com.Parameters.AddWithValue("@game_id", game.ToString());
                using (SQLiteDataReader reader = com.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        properties.Add(reader.GetString(reader.GetOrdinal("name")));
                    }
                }
            }
            
            return properties;
        }
    }
}
