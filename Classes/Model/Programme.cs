﻿/* 
 * This file is part of Radio Downloader.
 * Copyright © 2007-2011 Matt Robinson
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
    using System.Data.SQLite;
    using System.Globalization;

    internal class Programme
    {
        internal const string Columns = "programmes.progid, name, description, singleepisode, pluginid, latestdownload";

        public Programme()
        {
        }

        public Programme(SQLiteMonDataReader reader)
        {
            this.FetchData(reader);
        }

        public Programme(int progid)
        {
            using (SQLiteCommand command = new SQLiteCommand("select " + Columns + " from programmes where progid=@progid", Data.FetchDbConn()))
            {
                command.Parameters.Add(new SQLiteParameter("@progid", progid));

                using (SQLiteMonDataReader reader = new SQLiteMonDataReader(command.ExecuteReader()))
                {
                    if (!reader.Read())
                    {
                        throw new DataNotFoundException(progid, "Programme does not exist");
                    }

                    this.FetchData(reader);
                }
            }
        }

        public delegate void ProgrammeEventHandler(int progid);

        public static event ProgrammeEventHandler Updated;

        public int Progid { get; private set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public bool SingleEpisode { get; set; }

        public string ProviderName { get; set; }

        public DateTime? LatestDownload { get; set; }

        public static int? Update(Guid pluginId, string progExtId)
        {
            if (!Plugins.PluginExists(pluginId))
            {
                return null;
            }

            IRadioProvider pluginInstance = Plugins.GetPluginInstance(pluginId);
            GetProgrammeInfoReturn progInfo = default(GetProgrammeInfoReturn);

            progInfo = pluginInstance.GetProgrammeInfo(progExtId);

            if (!progInfo.Success)
            {
                return null;
            }

            int? progid = null;

            lock (Data.DbUpdateLock)
            {
                using (SQLiteCommand command = new SQLiteCommand("select progid from programmes where pluginid=@pluginid and extid=@extid", Data.FetchDbConn()))
                {
                    command.Parameters.Add(new SQLiteParameter("@pluginid", pluginId.ToString()));
                    command.Parameters.Add(new SQLiteParameter("@extid", progExtId));

                    using (SQLiteMonDataReader reader = new SQLiteMonDataReader(command.ExecuteReader()))
                    {
                        if (reader.Read())
                        {
                            progid = reader.GetInt32(reader.GetOrdinal("progid"));
                        }
                    }
                }

                using (SQLiteMonTransaction transMon = new SQLiteMonTransaction(Data.FetchDbConn().BeginTransaction()))
                {
                    if (progid == null)
                    {
                        using (SQLiteCommand command = new SQLiteCommand("insert into programmes (pluginid, extid) values (@pluginid, @extid)", Data.FetchDbConn()))
                        {
                            command.Parameters.Add(new SQLiteParameter("@pluginid", pluginId.ToString()));
                            command.Parameters.Add(new SQLiteParameter("@extid", progExtId));
                            command.ExecuteNonQuery();
                        }

                        using (SQLiteCommand command = new SQLiteCommand("select last_insert_rowid()", Data.FetchDbConn()))
                        {
                            progid = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
                        }
                    }

                    using (SQLiteCommand command = new SQLiteCommand("update programmes set name=@name, description=@description, image=@image, singleepisode=@singleepisode, lastupdate=@lastupdate where progid=@progid", Data.FetchDbConn()))
                    {
                        command.Parameters.Add(new SQLiteParameter("@name", progInfo.ProgrammeInfo.Name));
                        command.Parameters.Add(new SQLiteParameter("@description", progInfo.ProgrammeInfo.Description));
                        command.Parameters.Add(new SQLiteParameter("@image", Data.GetInstance().StoreImage(progInfo.ProgrammeInfo.Image)));
                        command.Parameters.Add(new SQLiteParameter("@singleepisode", progInfo.ProgrammeInfo.SingleEpisode));
                        command.Parameters.Add(new SQLiteParameter("@lastupdate", DateTime.Now));
                        command.Parameters.Add(new SQLiteParameter("@progid", progid));
                        command.ExecuteNonQuery();
                    }

                    transMon.Trans.Commit();
                }
            }

            if (Updated != null)
            {
                Updated(progid.Value);
            }

            return progid;
        }

        public static void SetLatestDownload(int progid, DateTime downloadDate)
        {
            using (SQLiteCommand command = new SQLiteCommand("update programmes set latestdownload=@latestdownload where progid=@progid", Data.FetchDbConn()))
            {
                command.Parameters.Add(new SQLiteParameter("@latestdownload", downloadDate));
                command.Parameters.Add(new SQLiteParameter("@progid", progid));
                command.ExecuteNonQuery();
            }

            if (Updated != null)
            {
                Updated(progid);
            }
        }

        internal void FetchData(SQLiteMonDataReader reader)
        {
            int descriptionOrdinal = reader.GetOrdinal("description");
            int latestdownloadOrdinal = reader.GetOrdinal("latestdownload");

            this.Progid = reader.GetInt32(reader.GetOrdinal("progid"));
            this.Name = reader.GetString(reader.GetOrdinal("name"));

            if (!reader.IsDBNull(descriptionOrdinal))
            {
                this.Description = reader.GetString(descriptionOrdinal);
            }

            this.SingleEpisode = reader.GetBoolean(reader.GetOrdinal("singleepisode"));

            Guid pluginId = new Guid(reader.GetString(reader.GetOrdinal("pluginid")));
            IRadioProvider providerInst = Plugins.GetPluginInstance(pluginId);
            this.ProviderName = providerInst.ProviderName;

            if (!reader.IsDBNull(latestdownloadOrdinal))
            {
                this.LatestDownload = reader.GetDateTime(latestdownloadOrdinal);
            }
        }

        protected static void RaiseUpdated(int progid)
        {
            if (Updated != null)
            {
                Updated(progid);
            }
        }
    }
}