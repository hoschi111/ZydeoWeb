﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Text;
using System.Data;
using MySql.Data.MySqlClient;

using ZD.Common;

namespace ZDO.CHSite.Logic
{
    partial class SqlDict
    {
        private CedictEntry loadFromBlob(int blobId, MySqlCommand cmdSelBinaryEntry, byte[] buf)
        {
            cmdSelBinaryEntry.Parameters["@blob_id"].Value = blobId;
            using (MySqlDataReader rdr = cmdSelBinaryEntry.ExecuteReader())
            {
                while (rdr.Read())
                {
                    int len = (int)rdr.GetBytes(0, 0, buf, 0, buf.Length);
                    using (BinReader br = new BinReader(buf, len))
                    {
                        return new CedictEntry(br);
                    }
                }
            }
            return null;
        }

        private static bool hasHanzi(string query)
        {
            foreach (char c in query)
                if (Utils.IsHanzi(c))
                    return true;
            return false;
        }

        private void verifyHanzi(CedictEntry entry, string query, List<CedictResult> res)
        {
            if (entry == null) return;

            // Figure out position/length of query string in simplified and traditional headwords
            int hiliteStart = -1;
            int hiliteLength = 0;
            hiliteStart = entry.ChSimpl.IndexOf(query);
            if (hiliteStart != -1) hiliteLength = query.Length;
            // If not found in simplified, check in traditional
            if (hiliteLength == 0)
            {
                hiliteStart = entry.ChTrad.IndexOf(query);
                if (hiliteStart != -1) hiliteLength = query.Length;
            }
            // Entry is a keeper if either source or target headword contains query
            if (hiliteLength != 0)
            {
                CedictResult cr = new CedictResult(CedictResult.SimpTradWarning.None,
                    entry, entry.HanziPinyinMap,
                    hiliteStart, hiliteLength);
                res.Add(cr);
            }
        }

        private void retrieveBatch(List<int> batch, byte[] buf, List<CedictEntry> entries, MySqlCommand cmdSelBinary10)
        {
            entries.Clear();
            cmdSelBinary10.Parameters["@id0"].Value = batch.Count > 0 ? batch[0] : -1;
            cmdSelBinary10.Parameters["@id1"].Value = batch.Count > 1 ? batch[1] : -1;
            cmdSelBinary10.Parameters["@id2"].Value = batch.Count > 2 ? batch[2] : -1;
            cmdSelBinary10.Parameters["@id3"].Value = batch.Count > 3 ? batch[3] : -1;
            cmdSelBinary10.Parameters["@id4"].Value = batch.Count > 4 ? batch[4] : -1;
            cmdSelBinary10.Parameters["@id5"].Value = batch.Count > 5 ? batch[5] : -1;
            cmdSelBinary10.Parameters["@id6"].Value = batch.Count > 6 ? batch[6] : -1;
            cmdSelBinary10.Parameters["@id7"].Value = batch.Count > 7 ? batch[7] : -1;
            cmdSelBinary10.Parameters["@id8"].Value = batch.Count > 8 ? batch[8] : -1;
            cmdSelBinary10.Parameters["@id9"].Value = batch.Count > 9 ? batch[9] : -1;
            for (int i = 0; i != batch.Count; ++i) entries.Add(null);
            using (MySqlDataReader rdr = cmdSelBinary10.ExecuteReader())
            {
                while (rdr.Read())
                {
                    int len = (int)rdr.GetBytes(0, 0, buf, 0, buf.Length);
                    int entryId = rdr.GetInt32(1);
                    using (BinReader br = new BinReader(buf, len))
                    {
                        CedictEntry entry = new CedictEntry(br);
                        int ix = batch.IndexOf(entryId);
                        entries[ix] = entry;
                    }
                }
            }
        }

        private void retrieveVerifyHanzi(HashSet<int> cands, string query, List<CedictResult> res)
        {
            byte[] buf = new byte[32768];
            List<CedictEntry> entries = new List<CedictEntry>(10);
            using (MySqlConnection conn = DB.GetConn())
            using (MySqlCommand cmdSelBinary10 = DB.GetCmd(conn, "SelBinaryEntry10"))
            {
                List<int> batch = new List<int>(10);
                foreach (int blobId in cands)
                {
                    if (batch.Count < 10) { batch.Add(blobId); continue; }
                    retrieveBatch(batch, buf, entries, cmdSelBinary10);
                    foreach (var entry in entries) verifyHanzi(entry, query, res);
                    batch.Clear();
                }
                if (batch.Count != 0)
                {
                    retrieveBatch(batch, buf, entries, cmdSelBinary10);
                    foreach (var entry in entries) verifyHanzi(entry, query, res);
                }
            }
        }

        private static int hrComp(CedictResult a, CedictResult b)
        {
            // First come those where match starts sooner
            int startCmp = a.HanziHiliteStart.CompareTo(b.HanziHiliteStart);
            if (startCmp != 0) return startCmp;
            // Then, pinyin lexical compare up to shorter's length
            int pyComp = a.Entry.PinyinCompare(b.Entry);
            if (pyComp != 0) return pyComp;
            // Pinyin is identical: shorter comes first
            int lengthCmp = a.Entry.ChSimpl.Length.CompareTo(b.Entry.ChSimpl.Length);
            return lengthCmp;
        }

        private List<CedictResult> lookupHanzi(string query)
        {
            List<CedictResult> res = new List<CedictResult>();
            // Normalize query
            query = query.ToUpperInvariant();
            query = query.Replace(" ", "");
            // Distinct Hanzi
            HashSet<char> qhanzi = new HashSet<char>();
            foreach (char c in query) qhanzi.Add(c);
            // Get candidate IDs
            Stopwatch watch = new Stopwatch();
            watch.Restart();
            HashSet<int> cands = index.GetHanziCandidates(qhanzi);
            Console.WriteLine("Candidates: " + cands.Count + " (" + watch.ElapsedMilliseconds + " msec)");

            // Retrieve all candidates; verify on the fly
            watch.Restart();
            retrieveVerifyHanzi(cands, query, res);
            Console.WriteLine("Retrieval: " + res.Count + " (" + watch.ElapsedMilliseconds + " msec)");
            // Sort Hanzi results
            res.Sort((a, b) => hrComp(a, b));
            // Done.
            return res;
        }

        private bool verifyTrg(Tokenizer tokenizer, CedictEntry entry, int senseIx, List<Token> qtoks, List<CedictResult> res)
        {
            if (entry == null) return false;

            // Tokenize indicated sense's equiv; see if it matches query
            string equiv = entry.GetSenseAt(senseIx).Equiv;
            List<Token> rtoks = tokenizer.Tokenize(equiv);
            for (int i = 0; i != rtoks.Count; ++i)
            {
                int j = 0;
                for (; j != qtoks.Count; ++j) if (rtoks[i + j].Norm != qtoks[j].Norm) break;
                if (j != qtoks.Count) continue;
                // We got a match starting at i!
                CedictTargetHighlight[] hlarr = new CedictTargetHighlight[1];
                int start = rtoks[i].Start;
                int end = rtoks[i + j - 1].Start + rtoks[i + j - 1].Surf.Length;
                hlarr[0] = new CedictTargetHighlight(senseIx, start, end - start);
                ReadOnlyCollection<CedictTargetHighlight> hlcoll = new ReadOnlyCollection<CedictTargetHighlight>(hlarr);
                CedictResult cr = new CedictResult(entry, hlcoll);
                // Stop right here
                res.Add(cr);
                return true;
            }
            // Not a match
            return false;
        }

        private void retrieveVerifyTarget(Tokenizer tokenizer, HashSet<Index.TrgCandidate> cands, List<Token> toks,
            List<CedictResult> res)
        {
            byte[] buf = new byte[32768];
            List<CedictEntry> entries = new List<CedictEntry>(10);
            HashSet<int> resIdSet = new HashSet<int>(); // Makes sure we only retrieve each entry once, even if multiple senses match
            using (MySqlConnection conn = DB.GetConn())
            using (MySqlCommand cmdSelBinary10 = DB.GetCmd(conn, "SelBinaryEntry10"))
            {
                List<int> batch = new List<int>(10);
                List<int> senseIxs = new List<int>();
                foreach (var cand in cands)
                {
                    if (batch.Count < 10)
                    {
                        if (!resIdSet.Contains(cand.EntryId))
                        {
                            batch.Add(cand.EntryId);
                            senseIxs.Add(cand.SenseIx);
                        }
                        continue;
                    }
                    retrieveBatch(batch, buf, entries, cmdSelBinary10);
                    for (int i = 0; i != entries.Count; ++i)
                    {
                        if (resIdSet.Contains(batch[i])) continue;
                        if (verifyTrg(tokenizer, entries[i], senseIxs[i], toks, res)) resIdSet.Add(batch[i]);
                    }
                    batch.Clear();
                    senseIxs.Clear();
                }
                if (batch.Count != 0)
                {
                    retrieveBatch(batch, buf, entries, cmdSelBinary10);
                    for (int i = 0; i != entries.Count; ++i)
                    {
                        if (resIdSet.Contains(batch[i])) continue;
                        verifyTrg(tokenizer, entries[i], senseIxs[i], toks, res);
                    }
                }
            }
        }

        private List<CedictResult> lookupTarget(string query)
        {
            List<CedictResult> res = new List<CedictResult>();
            // Tokenize input
            Tokenizer tokenizer = new Tokenizer();
            List<Token> toks = tokenizer.Tokenize(query);
            // Distinct normalized words
            HashSet<Token> tokSet = new HashSet<Token>();
            foreach (var tok in toks) if (tok.Norm != Token.Num && tok.Norm != Token.Zho) tokSet.Add(tok);
            if (tokSet.Count == 0) return res;
            // Candidates
            Stopwatch watch = new Stopwatch();
            watch.Restart();
            var cands = index.GetTrgCandidates(tokSet);
            Console.WriteLine("Candidates: " + cands.Count + " (" + watch.ElapsedMilliseconds + " msec)");
            // Retrieve and verify
            watch.Restart();
            retrieveVerifyTarget(tokenizer, cands, toks, res);
            Console.WriteLine("Retrieval: " + res.Count + " (" + watch.ElapsedMilliseconds + " msec)");
            // Sort
            res.Sort((a, b) => b.Entry.Freq.CompareTo(a.Entry.Freq));
            // Done.
            return res;
        }

        private void verifyPinyin(CedictEntry entry, List<PinyinSyllable> sylls, List<CedictResult> res)
        {
            // Find query syllables in entry
            int syllStart = -1;
            for (int i = 0; i <= entry.PinyinCount - sylls.Count; ++i)
            {
                int j;
                for (j = 0; j != sylls.Count; ++j)
                {
                    PinyinSyllable syllEntry = entry.GetPinyinAt(i + j);
                    PinyinSyllable syllQuery = sylls[j];
                    if (syllEntry.Text.ToLowerInvariant() != syllQuery.Text) break;
                    if (syllQuery.Tone != -1 && syllEntry.Tone != syllQuery.Tone) break;
                }
                if (j == sylls.Count)
                {
                    syllStart = i;
                    break;
                }
            }
            // Entry is a keeper if query syllables found
            if (syllStart == -1) return;
            // Keeper!
            CedictResult cr = new CedictResult(entry, entry.HanziPinyinMap, syllStart, sylls.Count);
            res.Add(cr);
        }

        private void retrieveVerifyPinyin(HashSet<int> cands, List<PinyinSyllable> query, List<CedictResult> res)
        {
            byte[] buf = new byte[32768];
            List<CedictEntry> entries = new List<CedictEntry>(10);
            using (MySqlConnection conn = DB.GetConn())
            using (MySqlCommand cmdSelBinary10 = DB.GetCmd(conn, "SelBinaryEntry10"))
            {
                List<int> batch = new List<int>(10);
                foreach (int blobId in cands)
                {
                    if (batch.Count < 10) { batch.Add(blobId); continue; }
                    retrieveBatch(batch, buf, entries, cmdSelBinary10);
                    foreach (var entry in entries) verifyPinyin(entry, query, res);
                    batch.Clear();
                }
                if (batch.Count != 0)
                {
                    retrieveBatch(batch, buf, entries, cmdSelBinary10);
                    foreach (var entry in entries) verifyPinyin(entry, query, res);
                }
            }
        }

        /// <summary>
        /// Compares lookup results after pinyin lookup for sorted presentation.
        /// </summary>
        private static int pyComp(CedictResult a, CedictResult b)
        {
            // Shorter entry comes first
            int lengthCmp = a.Entry.PinyinCount.CompareTo(b.Entry.PinyinCount);
            if (lengthCmp != 0) return lengthCmp;
            // Between equally long headwords where match starts sooner comes first
            int startCmp = a.PinyinHiliteStart.CompareTo(b.PinyinHiliteStart);
            if (startCmp != 0) return startCmp;
            // Order equally long entries by pinyin lexicographical order
            return a.Entry.PinyinCompare(b.Entry);
        }

        private List<CedictResult> lookupPinyin(string query)
        {
            List<CedictResult> res = new List<CedictResult>();
            // Interpret query (parse pinyin; normalized syllables)
            List<PinyinSyllable> sylls = pinyin.ParsePinyinQuery(query);

            // Get candidate IDs
            Stopwatch watch = new Stopwatch();
            watch.Restart();
            HashSet<int> cands = index.GetPinyinCandidates(sylls);
            Console.WriteLine("Candidates: " + cands.Count + " (" + watch.ElapsedMilliseconds + " msec)");

            // Retrieve all candidates; verify on the fly
            watch.Restart();
            retrieveVerifyPinyin(cands, sylls, res);
            Console.WriteLine("Retrieval: " + res.Count + " (" + watch.ElapsedMilliseconds + " msec)");
            // Sort Hanzi results
            res.Sort((a, b) => pyComp(a, b));
            // Done.
            return res;

        }

        /// <summary>
        /// Searches the dictionary for Hanzi, annotations, pinyin, or target-language terms.
        /// </summary>
        public CedictLookupResult Lookup(string query)
        {
            List<CedictResult> res = new List<CedictResult>();
            List<CedictAnnotation> anns = new List<CedictAnnotation>();
            bool gotLock = false;
            try
            {
                // Acquire index lock first!
                if (!index.Lock.TryEnterReadLock(5000))
                    return new CedictLookupResult(query, res, anns, SearchLang.Chinese);
                gotLock = true;

                // Hanzi / Pinyin > Annotate > Target
                if (hasHanzi(query)) res = lookupHanzi(query);
                else res = lookupPinyin(query);
                if (res.Count > 0) return new CedictLookupResult(query, res, anns, SearchLang.Chinese);
                //anns = doAnnotate(query);
                //if (anns.Count > 0) return new CedictLookupResult(query, res, anns, SearchLang.Chinese);
                res = lookupTarget(query);
                if (res.Count > 0) return new CedictLookupResult(query, res, anns, SearchLang.Target);
                return new CedictLookupResult(query, res, anns, SearchLang.Chinese);
            }
            finally
            {
                if (gotLock) index.Lock.ExitReadLock();
            }
        }

        /// <summary>
        /// One search hint for query as prefix.
        /// </summary>
        public struct PrefixHint
        {
            /// <summary>
            /// The suggestion.
            /// </summary>
            public string Suggestion;
            /// <summary>
            /// Length of matching prefix in suggestion.
            /// </summary>
            public int PrefixLength;
        }

        /// <summary>
        /// Retrieves suggested search terms for prefix; permissive with diacritics.
        /// </summary>
        /// <param name="prefix">Prefix of search term entered so far.</param>
        /// <param name="limit">Maximum number of suggestions to return.</param>
        public List<PrefixHint> GetWordsForPrefix(string prefix, int limit)
        {
            List<PrefixHint> res = new List<PrefixHint>();
            Tokenizer tokenizer = new Tokenizer();
            List<Token> toks = tokenizer.Tokenize(prefix);
            // If prefix has numbers of Chinese, no hints
            foreach (var tok in toks) if (tok.Norm == Token.Num || tok.Norm == Token.Zho) return res;
            // Single word: straight from DB
            if (toks.Count == 1)
            {
                Token tok = toks[0];
                // Shorter than 3 characters: nothing.
                if (tok.Surf.Length < 3) return res;
                List<Index.PrefixWord> cands = index.LoadWordsForPrefix(tok.Surf); // No lock needed for this; not from in-memory index.
                // Strategy A: sort by frequency, largest first
                //cands.Sort((x, y) => y.Count.CompareTo(x.Count));
                // Strategy B: alphabetically sort by naked, lower-case form
                cands.Sort((x, y) => x.NakedLo.CompareTo(y.NakedLo)); 
                // Return just ze strings
                for (int i = 0; i < cands.Count && i < limit; ++i)
                    res.Add(new PrefixHint { Suggestion = cands[i].Word, PrefixLength = tok.Surf.Length });
                return res;
            }
            // TO-DO: Multiple words
            // Nothing.
            return res;
        }
    }
}
