﻿/* 
 * This file is part of Radio Downloader.
 * Copyright © 2007-2012 Matt Robinson
 * 
 * This program is free software: you can redistribute it and/or modify it under the terms of the GNU General
 * Public License as published by the Free Software Foundation, either version 3 of the License, or (at your
 * option) any later version.
 * 
 * This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the
 * implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public
 * License for more details.
 * 
 * You should have received a copy of the GNU General Public License along with this program.  If not, see
 * <http://www.gnu.org/licenses/>.
 */

namespace RadioDld.Model
{
    using System;
    using System.Collections.Generic;
    using System.Data.SQLite;
    using System.Globalization;
    using System.Threading;

    internal class Subscription : Programme
    {
        private static Dictionary<int, int> sortCache;
        private static object sortCacheLock = new object();

        static Subscription()
        {
            Programme.Updated += Programme_Updated;
        }

        public Subscription(SQLiteMonDataReader reader)
            : base(reader)
        {
        }

        public Subscription(int progid)
            : base(progid)
        {
        }

        public static event ProgrammeEventHandler Added;

        public static new event ProgrammeEventHandler Updated;

        public static event ProgrammeEventHandler Removed;

        public static List<Subscription> FetchAll()
        {
            List<Subscription> items = new List<Subscription>();

            using (SQLiteCommand command = new SQLiteCommand("select " + Columns + " from subscriptions, programmes where subscriptions.progid=programmes.progid", FetchDbConn()))
            {
                using (SQLiteMonDataReader reader = new SQLiteMonDataReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        items.Add(new Subscription(reader));
                    }
                }
            }

            return items;
        }

        public static bool IsSubscribed(int progid)
        {
            using (SQLiteCommand command = new SQLiteCommand("select count(*) from subscriptions where progid=@progid", FetchDbConn()))
            {
                command.Parameters.Add(new SQLiteParameter("@progid", progid));
                return (long)command.ExecuteScalar() != 0;
            }
        }

        public static bool Add(int progid)
        {
            Model.Programme progInfo = new Model.Programme(progid);

            if (progInfo.SingleEpisode)
            {
                using (SQLiteCommand command = new SQLiteCommand("select downloads.epid from downloads, episodes where downloads.epid=episodes.epid and progid=@progid", FetchDbConn()))
                {
                    command.Parameters.Add(new SQLiteParameter("progid", progid));

                    using (SQLiteMonDataReader reader = new SQLiteMonDataReader(command.ExecuteReader()))
                    {
                        if (reader.Read())
                        {
                            // The one download for this programme is already in the downloads list
                            return false;
                        }
                    }
                }
            }

            ThreadPool.QueueUserWorkItem(delegate { AddAsync(progid); });

            return true;
        }

        public static void Remove(int progid)
        {
            ThreadPool.QueueUserWorkItem(delegate { RemoveAsync(progid); });
        }

        public static int Compare(int progid1, int progid2)
        {
            lock (sortCacheLock)
            {
                if (sortCache == null || !sortCache.ContainsKey(progid1) || !sortCache.ContainsKey(progid2))
                {
                    // The sort cache is either empty or missing one of the values that are required, so recreate it
                    sortCache = new Dictionary<int, int>();

                    int sort = 0;

                    using (SQLiteCommand command = new SQLiteCommand("select subscriptions.progid from subscriptions, programmes where programmes.progid=subscriptions.progid order by name", FetchDbConn()))
                    {
                        using (SQLiteMonDataReader reader = new SQLiteMonDataReader(command.ExecuteReader()))
                        {
                            int progidOrdinal = reader.GetOrdinal("progid");

                            while (reader.Read())
                            {
                                sortCache.Add(reader.GetInt32(progidOrdinal), sort);
                                sort += 1;
                            }
                        }
                    }
                }

                return sortCache[progid1] - sortCache[progid2];
            }
        }

        private static void Programme_Updated(int progid)
        {
            if (IsSubscribed(progid))
            {
                lock (sortCacheLock)
                {
                    sortCache = null;
                }

                if (Updated != null)
                {
                    Updated(progid);
                }
            }
        }

        private static void AddAsync(int progid)
        {
            lock (DbUpdateLock)
            {
                using (SQLiteCommand command = new SQLiteCommand("insert into subscriptions (progid) values (@progid)", FetchDbConn()))
                {
                    command.Parameters.Add(new SQLiteParameter("@progid", progid));

                    try
                    {
                        command.ExecuteNonQuery();
                    }
                    catch (SQLiteException sqliteExp)
                    {
                        if (sqliteExp.ErrorCode == SQLiteErrorCode.Constraint)
                        {
                            // Already added while this was waiting in the threadpool
                            return;
                        }

                        throw;
                    }
                }
            }

            if (Added != null)
            {
                Added(progid);
            }

            Programme.RaiseUpdated(progid);
        }

        private static void RemoveAsync(int progid)
        {
            lock (DbUpdateLock)
            {
                using (SQLiteCommand command = new SQLiteCommand("delete from subscriptions where progid=@progid", FetchDbConn()))
                {
                    command.Parameters.Add(new SQLiteParameter("@progid", progid));

                    if (command.ExecuteNonQuery() == 0)
                    {
                        // Subscription has already been removed
                        return;
                    }
                }
            }

            Programme.RaiseUpdated(progid);

            if (Removed != null)
            {
                Removed(progid);
            }
        }
    }
}
