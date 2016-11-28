﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using ZDO.CHSite.Entities;
using ZDO.CHSite.Logic;
using ZDO.CHSite.Renderers;
using ZD.Common;
using ZD.LangUtils;

namespace ZDO.CHSite.Controllers
{
    public class EditController : Controller
    {
        private readonly LangRepo langRepo;
        private readonly SqlDict dict;
        private readonly Auth auth;

        public EditController(LangRepo langRepo, SqlDict dict, Auth auth)
        {
            this.langRepo = langRepo;
            this.dict = dict;
            this.auth = auth;
        }

        public IActionResult GetEntryPreview([FromQuery] string hw, [FromQuery] string trgTxt, [FromQuery] string lang)
        {
            if (hw == null || trgTxt == null || lang == null) return StatusCode(400, "Missing parameter(s).");

            try
            {
                // DBG
                if (trgTxt.Contains("micu-barf")) throw new Exception("barf");

                trgTxt = trgTxt.Replace('/', '\\');
                trgTxt = trgTxt.Replace('\n', '/');
                trgTxt = "/" + trgTxt + "/";
                CedictParser parser = new CedictParser();
                CedictEntry entry = parser.ParseEntry(hw + " " + trgTxt, 0, null);
                EntryRenderer er = new EntryRenderer(entry);
                er.OneLineHanziLimit = 12;
                StringBuilder sb = new StringBuilder();
                er.Render(sb);
                return new ObjectResult(sb.ToString());
            }
            catch
            {
                return new ObjectResult(null);
            }
        }

        public IActionResult GetEditEntryData([FromQuery] string entryId, [FromQuery] string lang)
        {
            if (entryId == null || lang == null) return StatusCode(400, "Missing parameter(s).");
            EditEntryData res = new EditEntryData();

            int idVal = EntryId.StringToId(entryId);
            string hw, trg;
            EntryStatus status;
            SqlDict.GetEntryById(idVal, out hw, out trg, out status);
            res.Status = status;
            res.HeadTxt = hw;
            res.TrgTxt = trg.Trim('/').Replace("/", "\n");
            res.TrgTxt = res.TrgTxt.Replace('\\', '/');

            CedictParser parser = new CedictParser();
            CedictEntry entry = parser.ParseEntry(hw + " " + trg, 0, null);
            EntryRenderer er = new EntryRenderer(entry);
            er.OneLineHanziLimit = 12;
            StringBuilder sb = new StringBuilder();
            er.Render(sb);
            res.EntryHtml = sb.ToString();
            sb.Clear();

            return new ObjectResult(res);
        }

        public IActionResult GetPastChanges([FromQuery] string entryId, [FromQuery] string lang)
        {
            if (entryId == null || lang == null) return StatusCode(400, "Missing parameter(s).");
            int idVal = EntryId.StringToId(entryId);
            var changes = SqlDict.GetPastChanges(idVal);
            StringBuilder sb = new StringBuilder();
            HistoryRenderer.RenderPastChanges(sb, changes, lang);
            return new ObjectResult(sb.ToString());
        }

        public IActionResult CommentEntry([FromForm] string entryId, [FromForm] string note)
        {
            if (entryId == null || note == null) return StatusCode(400, "Missing parameter(s).");

            // Must be authenticated user
            int userId;
            string userName;
            auth.CheckSession(HttpContext.Request.Headers, out userId, out userName);
            if (userId < 0) return StatusCode(401, "Request must not contain authentication token.");

            bool success = false;
            SqlDict.SimpleBuilder builder = null;
            try
            {
                int idVal = EntryId.StringToId(entryId);
                builder = dict.GetSimpleBuilder(userId);
                builder.CommentEntry(idVal, note);
                success = true;
            }
            catch (Exception ex)
            {
                // TO-DO: Log
                //DiagLogger.LogError(ex);
            }
            finally { if (builder != null) builder.Dispose(); }

            // Tell our caller
            return new ObjectResult(success);
        }

        public IActionResult CreateEntry([FromForm] string simp, [FromForm] string trad,
            [FromForm] string pinyin, [FromForm] string trg, [FromForm] string note)
        {
            if (simp == null) return StatusCode(400, "Missing 'simp' parameter.");
            if (trad == null) return StatusCode(400, "Missing 'trad' parameter.");
            if (pinyin == null) return StatusCode(400, "Missing 'pinyin' parameter.");
            if (trg == null) return StatusCode(400, "Missing 'trg' parameter.");
            if (note == null) return StatusCode(400, "Missing 'note' parameter.");

            // Must be authenticated user
            int userId;
            string userName;
            auth.CheckSession(HttpContext.Request.Headers, out userId, out userName);
            if (userId < 0) return StatusCode(401, "Request must not contain authentication token.");

            NewEntrySubmitResult res = new NewEntrySubmitResult { Success = true };
            SqlDict.SimpleBuilder builder = null;
            try
            {
                builder = dict.GetSimpleBuilder(userId);
                CedictEntry entry = Utils.BuildEntry(simp, trad, pinyin, trg);
                builder.NewEntry(entry, note);
            }
            catch (Exception ex)
            {
                // TO-DO: Log
                //DiagLogger.LogError(ex);
                res.Success = false;
            }
            finally { if (builder != null) builder.Dispose(); }

            // Tell our caller
            return new ObjectResult(res);
        }
    }
}
