// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualBasic;

namespace Microsoft.Data.SqlClient.ManualTesting.Tests.SystemDataInternals
{
    internal static class ConnectionPoolHelper
    {
        private static Assembly s_MicrosoftDotData = Assembly.Load(new AssemblyName(typeof(SqlConnection).GetTypeInfo().Assembly.FullName));

        #region Common
        private static Type s_dbConnectionPool = s_MicrosoftDotData.GetType("Microsoft.Data.SqlClient.ConnectionPool.IDbConnectionPool");
        private static Type s_dbConnectionPoolGroup = s_MicrosoftDotData.GetType("Microsoft.Data.SqlClient.ConnectionPool.DbConnectionPoolGroup");
        private static Type s_dbConnectionPoolIdentity = s_MicrosoftDotData.GetType("Microsoft.Data.SqlClient.ConnectionPool.DbConnectionPoolIdentity");
        private static Type s_dbConnectionFactory = s_MicrosoftDotData.GetType("Microsoft.Data.ProviderBase.DbConnectionFactory");
        private static Type s_sqlConnectionFactory = s_MicrosoftDotData.GetType("Microsoft.Data.SqlClient.SqlConnectionFactory");
        private static Type s_dbConnectionPoolKey = s_MicrosoftDotData.GetType("Microsoft.Data.SqlClient.ConnectionPool.DbConnectionPoolKey");
        private static Type s_dictStringPoolGroup = typeof(Dictionary<,>).MakeGenericType(s_dbConnectionPoolKey, s_dbConnectionPoolGroup);
        private static Type s_dictPoolIdentityPool = typeof(ConcurrentDictionary<,>).MakeGenericType(s_dbConnectionPoolIdentity, s_dbConnectionPool);
        private static PropertyInfo s_dictStringPoolGroupGetKeys = s_dictStringPoolGroup.GetProperty("Keys");
        private static PropertyInfo s_dictPoolIdentityPoolValues = s_dictPoolIdentityPool.GetProperty("Values");
        private static FieldInfo s_dbConnectionFactoryPoolGroupList = s_dbConnectionFactory.GetField("_connectionPoolGroups", BindingFlags.Instance | BindingFlags.NonPublic);
        private static FieldInfo s_dbConnectionPoolGroupPoolCollection = s_dbConnectionPoolGroup.GetField("_poolCollection", BindingFlags.Instance | BindingFlags.NonPublic);
        private static FieldInfo s_sqlConnectionFactorySingleton = s_sqlConnectionFactory.GetField("SingletonInstance", BindingFlags.Static | BindingFlags.Public);
        private static MethodInfo s_dictStringPoolGroupTryGetValue = s_dictStringPoolGroup.GetMethod("TryGetValue");
        #endregion

        #region WaitHandleDbConnectionPool
        private static Type s_waitHandleDbConnectionPool = s_MicrosoftDotData.GetType("Microsoft.Data.SqlClient.ConnectionPool.WaitHandleDbConnectionPool");
        private static PropertyInfo s_waitHandleDbConnectionPoolCount = s_waitHandleDbConnectionPool.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
        private static PropertyInfo s_waitHandleDbConnectionPoolIdleCount = s_waitHandleDbConnectionPool.GetProperty("IdleCount", BindingFlags.Instance | BindingFlags.Public);
        private static MethodInfo s_waitHandleDbConnectionPoolPruneIdle = s_waitHandleDbConnectionPool.GetMethod("PruneIdle", BindingFlags.Instance | BindingFlags.Public);
        #endregion

        #region ChannelDbConnectionPool
        private static Type s_channelDbConnectionPool = s_MicrosoftDotData.GetType("Microsoft.Data.SqlClient.ConnectionPool.WaitHandleDbConnectionPool");
        private static PropertyInfo s_channelDbConnectionPoolCount = s_channelDbConnectionPool.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
        private static PropertyInfo s_channelDbConnectionPoolIdleCount = s_channelDbConnectionPool.GetProperty("IdleCount", BindingFlags.Instance | BindingFlags.Public);
        private static MethodInfo s_channelDbConnectionPoolPruneIdle = s_channelDbConnectionPool.GetMethod("PruneIdle", BindingFlags.Instance | BindingFlags.Public);
        #endregion

        private static bool ConnectionPoolV2Enabled()
        {
            Type switchesType = typeof(SqlCommand).Assembly.GetType("Microsoft.Data.SqlClient.LocalAppContextSwitches");
            PropertyInfo switchField = switchesType.GetProperty("UseConnectionPoolV2", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return (bool)switchField.GetValue(null);
        }

        public static int CountFreeConnections(object pool)
        {
            VerifyObjectIsPool(pool);

            if (ConnectionPoolV2Enabled())
            {
                return (int)s_channelDbConnectionPoolIdleCount.GetValue(pool, null);
            }
            else
            { 
                return (int)s_waitHandleDbConnectionPoolIdleCount.GetValue(pool, null);
            }
        }

        /// <summary>
        /// Finds all connection pools
        /// </summary>
        /// <returns></returns>
        public static List<Tuple<object, object>> AllConnectionPools()
        {
            List<Tuple<object, object>> connectionPools = new List<Tuple<object, object>>();
            object factorySingleton = s_sqlConnectionFactorySingleton.GetValue(null);
            object AllPoolGroups = s_dbConnectionFactoryPoolGroupList.GetValue(factorySingleton);
            ICollection connectionPoolKeys = (ICollection)s_dictStringPoolGroupGetKeys.GetValue(AllPoolGroups, null);
            foreach (var item in connectionPoolKeys)
            {
                object[] args = new object[] { item, null };
                s_dictStringPoolGroupTryGetValue.Invoke(AllPoolGroups, args);
                if (args[1] != null)
                {
                    object poolCollection = s_dbConnectionPoolGroupPoolCollection.GetValue(args[1]);
                    IEnumerable poolList = (IEnumerable)(s_dictPoolIdentityPoolValues.GetValue(poolCollection));
                    foreach (object pool in poolList)
                    {
                        connectionPools.Add(new Tuple<object, object>(pool, item));
                    }
                }
            }

            return connectionPools;
        }

        /// <summary>
        /// Finds a connection pool based on a connection string
        /// </summary>
        /// <param name="connectionString"></param>
        /// <returns></returns>
        public static object ConnectionPoolFromString(string connectionString)
        {
            if (connectionString == null)
                throw new ArgumentNullException(nameof(connectionString));

            object pool = null;
            object factorySingleton = s_sqlConnectionFactorySingleton.GetValue(null);
            object AllPoolGroups = s_dbConnectionFactoryPoolGroupList.GetValue(factorySingleton);
            object[] args = new object[] { connectionString, null };
            bool found = (bool)s_dictStringPoolGroupTryGetValue.Invoke(AllPoolGroups, args);
            if ((found) && (args[1] != null))
            {
                ICollection poolList = (ICollection)s_dictPoolIdentityPoolValues.GetValue(args[1]);
                if (poolList.Count == 1)
                {
                    poolList.Cast<object>().First();
                }
                else if (poolList.Count > 1)
                {
                    throw new NotSupportedException("Using multiple identities with SSPI is not supported");
                }
            }

            return pool;
        }

        /// <summary>
        /// Causes the cleanup timer code in the connection pool to be invoked
        /// </summary>
        /// <param name="obj">A connection pool object</param>
        internal static void CleanConnectionPool(object pool)
        {
            VerifyObjectIsPool(pool);
            if (ConnectionPoolV2Enabled())
            {
                s_channelDbConnectionPoolPruneIdle.Invoke(pool, new object[] { null });
            } else
            {
                s_waitHandleDbConnectionPoolPruneIdle.Invoke(pool, new object[] { null });
            }
        }

        /// <summary>
        /// Counts the number of connections in a connection pool
        /// </summary>
        /// <param name="pool">Pool to count connections in</param>
        /// <returns></returns>
        internal static int CountConnectionsInPool(object pool)
        {
            VerifyObjectIsPool(pool);

            if (ConnectionPoolV2Enabled())
            {
                return (int)s_channelDbConnectionPoolCount.GetValue(pool, null);
            }
            else
            {
                return (int)s_waitHandleDbConnectionPoolCount.GetValue(pool, null);
            }
        }

        private static void VerifyObjectIsPool(object pool)
        {
            if (pool == null)
                throw new ArgumentNullException(nameof(pool));
            if (!s_dbConnectionPool.IsInstanceOfType(pool))
                throw new ArgumentException("Object provided was not a DbConnectionPool", "pool");
        }
    }
}
