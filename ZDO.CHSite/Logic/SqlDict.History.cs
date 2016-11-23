﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using MySql.Data.MySqlClient;

using ZD.Common;
using ZDO.CHSite.Entities;

namespace ZDO.CHSite.Logic
{
    public partial class SqlDict
    {
        public class History : IDisposable
        {
            private MySqlConnection conn;

            // Reused commands
            private MySqlCommand cmdSelChangePage;
            private MySqlCommand cmdGetChangeCount;
            // ---------------

            public History()
            {
                conn = DB.GetConn();
                cmdSelChangePage = DB.GetCmd(conn, "SelModifPage");
                cmdGetChangeCount = DB.GetCmd(conn, "GetChangeCount");
            }

            /// <summary>
            /// Gets the total number of changes (history items).
            /// </summary>
            public int GetChangeCount()
            {
                Int64 count = (Int64)cmdGetChangeCount.ExecuteScalar();
                return (int)count;
            }

            public List<ChangeItem> GetChangePage(int pageStart, int pageLen)
            {
                List<ChangeItem> res = new List<ChangeItem>();
				cmdSelChangePage.Parameters["@page_start"].Value = pageStart;
                cmdSelChangePage.Parameters["@page_len"].Value = pageLen;
				using (MySqlDataReader rdr = cmdSelChangePage.ExecuteReader())
                {
					while (rdr.Read())
                    {
                        ChangeItem ci = new ChangeItem
                        {
                            When = rdr.GetDateTime(0), // TO-DO: UTC trick
                            User = "valaki",
                            EntryHead = rdr.IsDBNull(1) ? null : rdr.GetString(1),
                            EntryBody = rdr.IsDBNull(2) ? null : rdr.GetString(2),
                            Note = rdr.GetString(3),
                            ChangeType = (ChangeType)rdr.GetInt32(4)
                        };
                        res.Add(ci);
                    }
                }
                return res;
            }

            public void Dispose()
            {
                if (cmdGetChangeCount != null) cmdGetChangeCount.Dispose();
                if (cmdSelChangePage != null) cmdSelChangePage.Dispose();
                if (conn != null) conn.Dispose();
            }
        }
    }
}