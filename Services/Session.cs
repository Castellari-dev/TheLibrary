using System;
using Microsoft.Data.SqlClient;
using TheLibrary.Models;
using Npgsql;

namespace TheLibrary.Services
{

    public static class ConnectionBuilder
    {
        public static string Build(DbProvider provider, string host, string port, string database,
                                   string user, string password, bool integratedSecurity, bool trustCert)
        {
            if (provider == DbProvider.Postgres)
            {
                var b = new NpgsqlConnectionStringBuilder();
                b.Host = string.IsNullOrWhiteSpace(host) ? "localhost" : host.Trim();
                int p;
                b.Port = int.TryParse(port, out p) && p > 0 ? p : 5432;
                b.Database = string.IsNullOrWhiteSpace(database) ? "postgres" : database.Trim();
                b.Username = user ?? "";
                b.Password = password ?? "";
                b.Timeout = 15;
                b.CommandTimeout = 60;
                return b.ConnectionString;
            }
            else
            {
                var b = new SqlConnectionStringBuilder();
                string h = string.IsNullOrWhiteSpace(host) ? "localhost" : host.Trim();
                int p;
                if (int.TryParse(port, out p) && p > 0 && !h.Contains(",")) h = h + "," + p;
                b.DataSource = h;
                b.InitialCatalog = string.IsNullOrWhiteSpace(database) ? "master" : database.Trim();
                b.IntegratedSecurity = integratedSecurity;
                if (!integratedSecurity)
                {
                    b.UserID = user ?? "";
                    b.Password = password ?? "";
                }
                b.TrustServerCertificate = trustCert;
                b.ConnectTimeout = 15;
                b.MultipleActiveResultSets = true;
                return b.ConnectionString;
            }
        }

        public static string ToAdmin(DbProvider provider, string connectionString)
        {
            if (provider == DbProvider.Postgres)
            {
                var b = new NpgsqlConnectionStringBuilder(connectionString);
                b.Database = "postgres";
                return b.ConnectionString;
            }
            else
            {
                var b = new SqlConnectionStringBuilder(connectionString);
                b.InitialCatalog = "master";
                return b.ConnectionString;
            }
        }

        public static string GetDatabaseName(DbProvider provider, string connectionString)
        {
            try
            {
                if (provider == DbProvider.Postgres)
                    return new NpgsqlConnectionStringBuilder(connectionString).Database;
                return new SqlConnectionStringBuilder(connectionString).InitialCatalog;
            }
            catch
            {
                return "";
            }
        }
    }

    public static class Session
    {
        public static Database Db { get; set; }
        public static AppUser User { get; set; }

        public static bool IsReady => Db != null && User != null;

        public static void Clear()
        {
            Db = null;
            User = null;
        }
    }
}
