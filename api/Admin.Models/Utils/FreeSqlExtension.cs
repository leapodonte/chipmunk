using FreeSql;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Admin.Utils
{
    public static class FreeSqlExtension
    {
        private static string GetConnectionString(this FreeSqlBuilder @this)
        {
            Type type = @this.GetType();
            FieldInfo fieldInfo = type.GetField("_masterConnectionString", BindingFlags.NonPublic | BindingFlags.Instance);
            if (fieldInfo is null)
            {
                throw new ArgumentException("_masterConnectionString is null");
            }
            return fieldInfo.GetValue(@this).ToString();
        }

        /// <summary>
        /// 请在UseConnectionString配置后调用此方法
        /// </summary>
        /// <param name="this"></param>
        /// <returns></returns>
        public static FreeSqlBuilder CreateDatabaseIfNotExists(this FreeSqlBuilder @this)
        {
            FieldInfo dataTypeFieldInfo = @this.GetType().GetField("_dataType", BindingFlags.NonPublic | BindingFlags.Instance);

            if (dataTypeFieldInfo is null)
            {
                throw new ArgumentException("_dataType is null");
            }

            string connectionString = GetConnectionString(@this);
            DataType dbType = (DataType)dataTypeFieldInfo.GetValue(@this);

            switch (dbType)
            {
                case DataType.PostgreSQL:
                    return @this.CreateDatabaseIfNotExistsPgSql(connectionString);
                //case DataType.MySql:
                //    return @this.CreateDatabaseIfNotExistsMySql(connectionString);
            }

            return @this;
        }


        public static FreeSqlBuilder CreateDatabaseIfNotExistsPgSql(this FreeSqlBuilder @this, string connectionString = "")
        {
            if (connectionString == "")
            {
                connectionString = GetConnectionString(@this);
            }

            NpgsqlConnectionStringBuilder builder = new NpgsqlConnectionStringBuilder(connectionString);

            string databaseExistSql = $"SELECT COUNT(*) FROM pg_database WHERE datname = '{builder.Database}'";

            string createDatabaseSql = $"CREATE DATABASE \"{builder.Database}\" WITH OWNER = \"{builder.Username}\"";

            using NpgsqlConnection cnn = new NpgsqlConnection($"Host={builder.Host};Port={builder.Port};Username={builder.Username};Password={builder.Password};Pooling=true");
            cnn.Open();

            using (NpgsqlCommand cmd = cnn.CreateCommand())
            {
                cmd.CommandText = databaseExistSql;
                bool databaseExist = Convert.ToInt32(cmd.ExecuteScalar().ToString()) > 0;
                if (databaseExist == false)
                {
                    cmd.CommandText = createDatabaseSql;
                    cmd.ExecuteNonQuery();
                }
            }


            return @this;
        }

        /*
        public static FreeSqlBuilder CreateDatabaseIfNotExistsMySql(this FreeSqlBuilder @this, string connectionString = "")
        {
            if (connectionString == "")
            {
                connectionString = GetConnectionString(@this);
            }

            MySqlConnectionStringBuilder builder = new MySqlConnectionStringBuilder(connectionString);

            string createDatabaseSql = $"USE mysql;CREATE DATABASE IF NOT EXISTS `{builder.Database}` CHARACTER SET '{builder.CharacterSet}' COLLATE 'utf8mb4_general_ci'";

            using MySqlConnection cnn = new MySqlConnection($"Data Source={builder.Server};Port={builder.Port};User ID={builder.UserID};Password={builder.Password};Initial Catalog=mysql;Charset=utf8;SslMode=none;Max pool size=1");
            cnn.Open();
            using (MySqlCommand cmd = cnn.CreateCommand())
            {
                cmd.CommandText = createDatabaseSql;
                cmd.ExecuteNonQuery();
            }

            return @this;
        }
        */

    }
}
