using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;
using TheLibrary.Models;
using Npgsql;

namespace TheLibrary.Services
{

    public class Database
    {
        public DbProvider Provider { get; private set; }
        public string ConnectionString { get; private set; }

        public Database(DbProvider provider, string connectionString)
        {
            Provider = provider;
            ConnectionString = connectionString;
        }

        private bool IsPg => Provider == DbProvider.Postgres;

        public DbConnection Open()
        {
            DbConnection conn = IsPg
                ? (DbConnection)new NpgsqlConnection(ConnectionString)
                : new SqlConnection(ConnectionString);
            conn.Open();
            return conn;
        }

        private static void AddParam(DbCommand cmd, string name, object value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        private DbCommand Cmd(DbConnection conn, string sql, params object[] nameValuePairs)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            for (int i = 0; i + 1 < nameValuePairs.Length; i += 2)
                AddParam(cmd, (string)nameValuePairs[i], nameValuePairs[i + 1]);
            return cmd;
        }

        private static string S(IDataRecord r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
        private static int I(IDataRecord r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i), CultureInfo.InvariantCulture);
        private static bool B(IDataRecord r, int i) => !r.IsDBNull(i) && Convert.ToBoolean(r.GetValue(i), CultureInfo.InvariantCulture);
        private static decimal D(IDataRecord r, int i) => r.IsDBNull(i) ? 0m : Convert.ToDecimal(r.GetValue(i), CultureInfo.InvariantCulture);
        private static decimal? DN(IDataRecord r, int i) => r.IsDBNull(i) ? (decimal?)null : Convert.ToDecimal(r.GetValue(i), CultureInfo.InvariantCulture);
        private static DateTime T(IDataRecord r, int i) => r.IsDBNull(i) ? DateTime.MinValue : Convert.ToDateTime(r.GetValue(i), CultureInfo.InvariantCulture);

        public void TestConnection()
        {
            using (var conn = Open())
            using (var cmd = Cmd(conn, "SELECT 1"))
            {
                cmd.ExecuteScalar();
            }
        }

        public static void CreateDatabaseIfMissing(DbProvider provider, string adminConnectionString, string dbName)
        {
            if (string.IsNullOrWhiteSpace(dbName))
                throw new ArgumentException("Nome do banco não informado.");

            foreach (char c in dbName)
            {
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
                    throw new ArgumentException("Nome do banco inválido. Use apenas letras, números, '_' e '-'.");
            }

            if (provider == DbProvider.Postgres)
            {
                using (var conn = new NpgsqlConnection(adminConnectionString))
                {
                    conn.Open();
                    using (var check = conn.CreateCommand())
                    {
                        check.CommandText = "SELECT 1 FROM pg_database WHERE datname = @n";
                        var p = check.CreateParameter();
                        p.ParameterName = "@n";
                        p.Value = dbName.ToLowerInvariant();
                        check.Parameters.Add(p);
                        if (check.ExecuteScalar() != null) return;
                    }
                    using (var create = conn.CreateCommand())
                    {
                        create.CommandText = "CREATE DATABASE \"" + dbName.ToLowerInvariant() + "\"";
                        create.ExecuteNonQuery();
                    }
                }
            }
            else
            {
                using (var conn = new SqlConnection(adminConnectionString))
                {
                    conn.Open();
                    using (var create = conn.CreateCommand())
                    {
                        create.CommandText =
                            "IF DB_ID(@n) IS NULL EXEC('CREATE DATABASE [" + dbName.Replace("]", "") + "]')";
                        var p = create.CreateParameter();
                        p.ParameterName = "@n";
                        p.Value = dbName;
                        create.Parameters.Add(p);
                        create.ExecuteNonQuery();
                    }
                }
            }
        }

        private string[] SchemaStatements()
        {
            if (IsPg)
            {
                return new[]
                {
@"CREATE TABLE IF NOT EXISTS APP_USER (
    ID              SERIAL PRIMARY KEY,
    USERNAME        VARCHAR(64)  NOT NULL UNIQUE,
    PASSWORD_HASH   VARCHAR(256) NOT NULL,
    IS_ADMIN        BOOLEAN      NOT NULL DEFAULT FALSE,
    THEME           VARCHAR(16)  NOT NULL DEFAULT 'Claro',
    ACCENT          VARCHAR(32)  NOT NULL DEFAULT 'Verde',
    CREATED_AT      TIMESTAMP    NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc')
)",
@"CREATE TABLE IF NOT EXISTS CARD (
    ID                SERIAL PRIMARY KEY,
    SCRYFALL_ID       VARCHAR(64)  NOT NULL DEFAULT '',
    ORACLE_ID         VARCHAR(64),
    NAME_EN           VARCHAR(256) NOT NULL,
    NAME_PT           VARCHAR(256),
    SET_CODE          VARCHAR(16),
    SET_NAME          VARCHAR(256),
    COLLECTOR_NUMBER  VARCHAR(32),
    RARITY            VARCHAR(16),
    TYPE_LINE         VARCHAR(256),
    MANA_COST         VARCHAR(64),
    COLORS            VARCHAR(32),
    LANG              VARCHAR(8),
    CONDITION_CODE    VARCHAR(8),
    IS_FOIL           BOOLEAN       NOT NULL DEFAULT FALSE,
    QUANTITY          INT           NOT NULL DEFAULT 1,
    MIN_PRICE_USD     DECIMAL(18,2) NOT NULL DEFAULT 0,
    MARKET_PRICE_USD  DECIMAL(18,2),
    IMAGE_URL         VARCHAR(512),
    ART_CROP_URL      VARCHAR(512),
    SCRYFALL_URI      VARCHAR(512),
    ARTIST            VARCHAR(256),
    NOTES             VARCHAR(512),
    CREATED_AT        TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
    UPDATED_AT        TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc')
)",
"CREATE INDEX IF NOT EXISTS IX_CARD_SCRYFALL ON CARD (SCRYFALL_ID)",
"CREATE INDEX IF NOT EXISTS IX_CARD_NAME ON CARD (NAME_EN)"
                };
            }

            return new[]
            {
@"IF OBJECT_ID('APP_USER','U') IS NULL
CREATE TABLE APP_USER (
    ID              INT IDENTITY(1,1) PRIMARY KEY,
    USERNAME        NVARCHAR(64)  NOT NULL UNIQUE,
    PASSWORD_HASH   NVARCHAR(256) NOT NULL,
    IS_ADMIN        BIT           NOT NULL CONSTRAINT DF_APP_USER_ADMIN DEFAULT 0,
    THEME           NVARCHAR(16)  NOT NULL CONSTRAINT DF_APP_USER_THEME DEFAULT 'Claro',
    ACCENT          NVARCHAR(32)  NOT NULL CONSTRAINT DF_APP_USER_ACCENT DEFAULT 'Verde',
    CREATED_AT      DATETIME2     NOT NULL CONSTRAINT DF_APP_USER_CREATED DEFAULT SYSUTCDATETIME()
)",
@"IF OBJECT_ID('CARD','U') IS NULL
CREATE TABLE CARD (
    ID                INT IDENTITY(1,1) PRIMARY KEY,
    SCRYFALL_ID       NVARCHAR(64)  NOT NULL CONSTRAINT DF_CARD_SCRY DEFAULT '',
    ORACLE_ID         NVARCHAR(64)  NULL,
    NAME_EN           NVARCHAR(256) NOT NULL,
    NAME_PT           NVARCHAR(256) NULL,
    SET_CODE          NVARCHAR(16)  NULL,
    SET_NAME          NVARCHAR(256) NULL,
    COLLECTOR_NUMBER  NVARCHAR(32)  NULL,
    RARITY            NVARCHAR(16)  NULL,
    TYPE_LINE         NVARCHAR(256) NULL,
    MANA_COST         NVARCHAR(64)  NULL,
    COLORS            NVARCHAR(32)  NULL,
    LANG              NVARCHAR(8)   NULL,
    CONDITION_CODE    NVARCHAR(8)   NULL,
    IS_FOIL           BIT           NOT NULL CONSTRAINT DF_CARD_FOIL DEFAULT 0,
    QUANTITY          INT           NOT NULL CONSTRAINT DF_CARD_QTY DEFAULT 1,
    MIN_PRICE_USD     DECIMAL(18,2) NOT NULL CONSTRAINT DF_CARD_MIN DEFAULT 0,
    MARKET_PRICE_USD  DECIMAL(18,2) NULL,
    IMAGE_URL         NVARCHAR(512) NULL,
    ART_CROP_URL      NVARCHAR(512) NULL,
    SCRYFALL_URI      NVARCHAR(512) NULL,
    ARTIST            NVARCHAR(256) NULL,
    NOTES             NVARCHAR(512) NULL,
    CREATED_AT        DATETIME2     NOT NULL CONSTRAINT DF_CARD_CREATED DEFAULT SYSUTCDATETIME(),
    UPDATED_AT        DATETIME2     NOT NULL CONSTRAINT DF_CARD_UPDATED DEFAULT SYSUTCDATETIME()
)",
@"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CARD_SCRYFALL' AND object_id = OBJECT_ID('CARD'))
CREATE INDEX IX_CARD_SCRYFALL ON CARD (SCRYFALL_ID)",
@"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CARD_NAME' AND object_id = OBJECT_ID('CARD'))
CREATE INDEX IX_CARD_NAME ON CARD (NAME_EN)"
            };
        }

        public void EnsureSchema()
        {
            using (var conn = Open())
            {
                foreach (var sql in SchemaStatements())
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = sql;
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        public int CountUsers()
        {
            using (var conn = Open())
            using (var cmd = Cmd(conn, "SELECT COUNT(*) FROM APP_USER"))
            {
                return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        private const string UserColumns =
            "ID, USERNAME, PASSWORD_HASH, IS_ADMIN, THEME, ACCENT, CREATED_AT";

        private static AppUser ReadUser(IDataRecord r) => new AppUser
        {
            Id = I(r, 0),
            Username = S(r, 1),
            PasswordHash = S(r, 2),
            IsAdmin = B(r, 3),
            Theme = S(r, 4) ?? "Claro",
            Accent = S(r, 5) ?? "Verde",
            CreatedAt = T(r, 6)
        };

        public AppUser FindUser(string username)
        {
            using (var conn = Open())
            using (var cmd = Cmd(conn,
                "SELECT " + UserColumns + " FROM APP_USER WHERE LOWER(USERNAME) = LOWER(@u)",
                "@u", username ?? ""))
            using (var r = cmd.ExecuteReader())
            {
                return r.Read() ? ReadUser(r) : null;
            }
        }

        public List<AppUser> ListUsers()
        {
            var list = new List<AppUser>();
            using (var conn = Open())
            using (var cmd = Cmd(conn, "SELECT " + UserColumns + " FROM APP_USER ORDER BY USERNAME"))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read()) list.Add(ReadUser(r));
            }
            return list;
        }

        public int CreateUser(string username, string password, bool isAdmin, string theme, string accent)
        {
            string sql =
                "INSERT INTO APP_USER (USERNAME, PASSWORD_HASH, IS_ADMIN, THEME, ACCENT) " +
                "VALUES (@u, @h, @a, @t, @c)" +
                (IsPg ? " RETURNING ID" : "; SELECT CAST(SCOPE_IDENTITY() AS INT)");

            using (var conn = Open())
            using (var cmd = Cmd(conn, sql,
                "@u", username,
                "@h", PasswordHasher.Hash(password),
                "@a", isAdmin,
                "@t", theme ?? "Claro",
                "@c", accent ?? "Verde"))
            {
                return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        public void UpdateUserPassword(int userId, string newPassword)
        {
            using (var conn = Open())
            using (var cmd = Cmd(conn, "UPDATE APP_USER SET PASSWORD_HASH = @h WHERE ID = @i",
                "@h", PasswordHasher.Hash(newPassword), "@i", userId))
            {
                cmd.ExecuteNonQuery();
            }
        }

        public void UpdateUserPrefs(int userId, string theme, string accent)
        {
            using (var conn = Open())
            using (var cmd = Cmd(conn, "UPDATE APP_USER SET THEME = @t, ACCENT = @c WHERE ID = @i",
                "@t", theme, "@c", accent, "@i", userId))
            {
                cmd.ExecuteNonQuery();
            }
        }

        public void DeleteUser(int userId)
        {
            using (var conn = Open())
            using (var cmd = Cmd(conn, "DELETE FROM APP_USER WHERE ID = @i", "@i", userId))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private const string CardColumns =
            "ID, SCRYFALL_ID, ORACLE_ID, NAME_EN, NAME_PT, SET_CODE, SET_NAME, COLLECTOR_NUMBER, " +
            "RARITY, TYPE_LINE, MANA_COST, COLORS, LANG, CONDITION_CODE, IS_FOIL, QUANTITY, " +
            "MIN_PRICE_USD, MARKET_PRICE_USD, IMAGE_URL, ART_CROP_URL, SCRYFALL_URI, ARTIST, NOTES";

        private static CardEntry ReadCard(IDataRecord r) => new CardEntry
        {
            Id = I(r, 0),
            ScryfallId = S(r, 1) ?? "",
            OracleId = S(r, 2),
            NameEn = S(r, 3),
            NamePt = S(r, 4),
            SetCode = S(r, 5),
            SetName = S(r, 6),
            CollectorNumber = S(r, 7),
            Rarity = S(r, 8),
            TypeLine = S(r, 9),
            ManaCost = S(r, 10),
            Colors = S(r, 11),
            Lang = S(r, 12),
            Condition = S(r, 13) ?? "NM",
            IsFoil = B(r, 14),
            Quantity = I(r, 15),
            MinPriceUsd = D(r, 16),
            MarketPriceUsd = DN(r, 17),
            ImageUrl = S(r, 18),
            ArtCropUrl = S(r, 19),
            ScryfallUri = S(r, 20),
            Artist = S(r, 21),
            Notes = S(r, 22)
        };

        public List<CardEntry> ListCards(string search = null)
        {
            var list = new List<CardEntry>();
            string sql = "SELECT " + CardColumns + " FROM CARD";
            bool hasSearch = !string.IsNullOrWhiteSpace(search);
            if (hasSearch)
            {
                sql += " WHERE LOWER(NAME_EN) LIKE @q OR LOWER(COALESCE(NAME_PT,'')) LIKE @q " +
                       "OR LOWER(COALESCE(SET_CODE,'')) LIKE @q OR LOWER(COALESCE(SET_NAME,'')) LIKE @q";
            }
            sql += " ORDER BY NAME_EN, SET_CODE, COLLECTOR_NUMBER";

            using (var conn = Open())
            using (var cmd = hasSearch
                ? Cmd(conn, sql, "@q", "%" + search.Trim().ToLowerInvariant() + "%")
                : Cmd(conn, sql))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read()) list.Add(ReadCard(r));
            }
            return list;
        }

        public CardEntry FindPrinting(string scryfallId, bool isFoil, string condition)
        {
            using (var conn = Open())
            using (var cmd = Cmd(conn,
                "SELECT " + CardColumns + " FROM CARD WHERE SCRYFALL_ID = @s AND IS_FOIL = @f " +
                "AND LOWER(COALESCE(CONDITION_CODE,'')) = LOWER(@c)",
                "@s", scryfallId ?? "", "@f", isFoil, "@c", condition ?? ""))
            using (var r = cmd.ExecuteReader())
            {
                return r.Read() ? ReadCard(r) : null;
            }
        }

        public int InsertCard(CardEntry c)
        {
            string sql =
                "INSERT INTO CARD (SCRYFALL_ID, ORACLE_ID, NAME_EN, NAME_PT, SET_CODE, SET_NAME, COLLECTOR_NUMBER, " +
                "RARITY, TYPE_LINE, MANA_COST, COLORS, LANG, CONDITION_CODE, IS_FOIL, QUANTITY, MIN_PRICE_USD, " +
                "MARKET_PRICE_USD, IMAGE_URL, ART_CROP_URL, SCRYFALL_URI, ARTIST, NOTES) VALUES " +
                "(@sid, @oid, @nen, @npt, @sc, @sn, @cn, @ra, @tl, @mc, @co, @lg, @cd, @fo, @qt, @mp, @mk, @img, @art, @uri, @ar, @no)" +
                (IsPg ? " RETURNING ID" : "; SELECT CAST(SCOPE_IDENTITY() AS INT)");

            using (var conn = Open())
            using (var cmd = Cmd(conn, sql,
                "@sid", c.ScryfallId ?? "",
                "@oid", c.OracleId,
                "@nen", c.NameEn ?? "",
                "@npt", c.NamePt,
                "@sc", c.SetCode,
                "@sn", c.SetName,
                "@cn", c.CollectorNumber,
                "@ra", c.Rarity,
                "@tl", Trunc(c.TypeLine, 256),
                "@mc", Trunc(c.ManaCost, 64),
                "@co", Trunc(c.Colors, 32),
                "@lg", c.Lang,
                "@cd", c.Condition,
                "@fo", c.IsFoil,
                "@qt", c.Quantity,
                "@mp", c.MinPriceUsd,
                "@mk", (object)c.MarketPriceUsd,
                "@img", Trunc(c.ImageUrl, 512),
                "@art", Trunc(c.ArtCropUrl, 512),
                "@uri", Trunc(c.ScryfallUri, 512),
                "@ar", Trunc(c.Artist, 256),
                "@no", Trunc(c.Notes, 512)))
            {
                c.Id = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
                return c.Id;
            }
        }

        public void UpdateCard(CardEntry c)
        {
            string sql =
                "UPDATE CARD SET SCRYFALL_ID=@sid, ORACLE_ID=@oid, NAME_EN=@nen, NAME_PT=@npt, SET_CODE=@sc, " +
                "SET_NAME=@sn, COLLECTOR_NUMBER=@cn, RARITY=@ra, TYPE_LINE=@tl, MANA_COST=@mc, COLORS=@co, " +
                "LANG=@lg, CONDITION_CODE=@cd, IS_FOIL=@fo, QUANTITY=@qt, MIN_PRICE_USD=@mp, MARKET_PRICE_USD=@mk, " +
                "IMAGE_URL=@img, ART_CROP_URL=@art, SCRYFALL_URI=@uri, ARTIST=@ar, NOTES=@no, " +
                (IsPg ? "UPDATED_AT=(NOW() AT TIME ZONE 'utc') " : "UPDATED_AT=SYSUTCDATETIME() ") +
                "WHERE ID=@id";

            using (var conn = Open())
            using (var cmd = Cmd(conn, sql,
                "@sid", c.ScryfallId ?? "",
                "@oid", c.OracleId,
                "@nen", c.NameEn ?? "",
                "@npt", c.NamePt,
                "@sc", c.SetCode,
                "@sn", c.SetName,
                "@cn", c.CollectorNumber,
                "@ra", c.Rarity,
                "@tl", Trunc(c.TypeLine, 256),
                "@mc", Trunc(c.ManaCost, 64),
                "@co", Trunc(c.Colors, 32),
                "@lg", c.Lang,
                "@cd", c.Condition,
                "@fo", c.IsFoil,
                "@qt", c.Quantity,
                "@mp", c.MinPriceUsd,
                "@mk", (object)c.MarketPriceUsd,
                "@img", Trunc(c.ImageUrl, 512),
                "@art", Trunc(c.ArtCropUrl, 512),
                "@uri", Trunc(c.ScryfallUri, 512),
                "@ar", Trunc(c.Artist, 256),
                "@no", Trunc(c.Notes, 512),
                "@id", c.Id))
            {
                cmd.ExecuteNonQuery();
            }
        }

        public void SaveCard(CardEntry c)
        {
            if (c.Id > 0) UpdateCard(c);
            else InsertCard(c);
        }

        public void DeleteCard(int id)
        {
            using (var conn = Open())
            using (var cmd = Cmd(conn, "DELETE FROM CARD WHERE ID = @i", "@i", id))
            {
                cmd.ExecuteNonQuery();
            }
        }

        public void DeleteAllCards()
        {
            using (var conn = Open())
            using (var cmd = Cmd(conn, "DELETE FROM CARD"))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private static string Trunc(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : s.Substring(0, max);
        }
    }
}
