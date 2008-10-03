// ライセンスはRuby'sとのことらしいです。 http://www.ruby-lang.org/ja/LICENSE.txt
// 変更前ソース入手先 http://yowaken.dip.jp/sixamo/
// 変更点:
//   * C#にしました。
//   * 文字コードをUTF-8にしました。
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public class SixamoCS
{
    public static bool DEBUG = false;

    public static SixamoCS.Core Create(string dirname)
    {
        return new Core(dirname);
    }

    internal static Dictionary InitDictionary(string dirname)
    {
        Dictionary dic = new Dictionary(dirname);
        dic.LoadText();
        dic.LearnFromText();
        return dic;
    }

    internal static class Util
    {
        public static string RouletteSelect(Hash<string, double> h)
        {
            if (h.IsEmpty())
                return null;

            var sum = h.Values.Sum();
            if (sum == 0.0)
                return RandomSelect(h.Keys);

            var r = Rand() * sum;
            foreach (KeyValuePair<string, double> item in h)
            {
                r -= item.Value;
                if (r <= 0)
                    return item.Key;
            }
            return RandomSelect(h.Keys);
        }

        public static string RandomSelect(ReadOnlyCollection<string> ary)
        {
            return ary[Rand(ary.Count)];
        }

        public static string MessageNormalize(string str)
        {
            Hash<string, List<string>> paren_h = new Hash<string, List<string>>(() => null);

            (new string[] { "「」", "『』", "()", "（）" }).Each((paren) =>
            {
                paren.Scan(".", (ch) => {
                    paren_h[ch] = paren.Scan(".");
                });
            });

            var re = @"[「」『』()（）]";
            var ary = str.Scan(re);

            int cnt = 0;
            string paren2 = "";
            string str2 = str.Gsub(re, (ch) =>
            {
                string res;
                if (cnt == ary.Count - 1 && ary.Count % 2 == 1)
                {
                    res = "";
                }
                else if (cnt % 2 == 0)
                {
                    paren2 = paren_h[ch][1];
                    res = paren_h[ch][0];
                }
                else
                {
                    res = paren2;
                }
                cnt++;
                return res;
            });

            str2 = str2.Gsub(@"「」", "");
            str2 = str2.Gsub(@"（）", "");
            str2 = str2.Gsub(@"『』", "");
            str2 = str2.Gsub(@"\(\)", "");

            return str2;
        }

        public static string Markov(IEnumerable<string> src, Hash<string, double> keywords, Trie trie)
        {
            var mar = MarkovGenerate(src, trie);
            string result = MarkovSelect(mar, keywords);
            return result;
        }

        const int MARKOV_KEY_SIZE = 2;

        class MarkovKey
        {
            private readonly string[] m_data = new string[MARKOV_KEY_SIZE];

            public MarkovKey(IEnumerable<string> strings)
            {
                for (int i = 0; i < MARKOV_KEY_SIZE; i++)
                    m_data[i] = strings.ElementAt(i);
            }

            public override int GetHashCode()
            {
                return m_data[0].GetHashCode();
            }

            public override bool Equals(object obj)
            {
                MarkovKey other = obj as MarkovKey;

                if (obj == null)
                    return false;

                for (int i = 0; i < MARKOV_KEY_SIZE; i++)
                    if (!m_data[i].Equals(other.m_data[i]))
                        return false;

                return true;
            }

            public override string ToString()
            {
                return string.Join("", m_data);
            }

            public void Push(string str)
            {
                for (int i = 0; i < MARKOV_KEY_SIZE - 1; i++)
                    m_data[i] = m_data[i + 1];
                m_data[MARKOV_KEY_SIZE - 1] = str;
            }
        }

        public static string MarkovGenerate(IEnumerable<string> src, Trie trie)
        {
            if (src.Count() == 0)
                return "";

            var ary = trie.SplitIntoTerms(string.Join("\n", src.ToArray()) + "\n", int.MaxValue);

            var size = ary.Count();
            ary = ary.Concat(ary.Take(MARKOV_KEY_SIZE + 1));

            var table = new Hash<MarkovKey, List<string>>(() => new List<string>());
            size.Times((idx) => {
                var key = new MarkovKey(ary.Skip(idx).Take(MARKOV_KEY_SIZE));
                if (!table.ContainsKey(key))
                    table[key] = new List<string>();
                table[key].Add(ary.ElementAt(idx + MARKOV_KEY_SIZE));
            });

            var uniq = new Hash<MarkovKey, string>(() => "");
            var backup = new Hash<MarkovKey, string[]>(() => null);

            foreach(var i in table)
            {
                if (i.Value.Count == 1)
                    uniq[i.Key] = i.Value[0];
                else
                    backup[i.Key] = table[i.Key].ToArray();
            }

            var key2 = new MarkovKey(ary.Take(MARKOV_KEY_SIZE));
            var result = new StringBuilder(key2.ToString());

	    for(int i=0; i<10000; i++)
            {
                string str;

                if (uniq.ContainsKey(key2))
                {
                    str = uniq[key2];
                }
                else
                {
                    if (table[key2].Count == 0)
                        table[key2].AddRange(backup[key2]);

                    var idx = Rand(table[key2].Count);
                    str = table[key2][idx];

                    table[key2].RemoveAt(idx);
                }

                result.Append(str);
                key2.Push(str);
            }

            return result.ToString();
        }

        public static IEnumerable<string> MarkovSplit(string str)
        {
            var result = new List<string>();

            Regex re = new Regex(@"\A(.{25,}?)([。、．，]+|[?!.,]+[\s　])[ 　]*");
            while (true)
            {
                Match match = re.Match(str);
                if (!match.Success)
                    break;

                var m = match.Groups[1].Value;
                var g2 = match.Groups[2].Value;
                if (g2 != null && g2 != "")
                    m += g2.Replace("、", "。").Replace("，", "．");
                result.Add(m);
                str = str.Substring(match.Index + match.Length);
            }

            if (str.Length > 0)
                result.Add(str);

            return result;
        }

        public static string MarkovSelect(string result, Hash<string, double> keywords)
        {
            var tmp = result.Split('\n');
            if (tmp == null || tmp.Length == 0)
                tmp = new string[] { "" };

            var result_ary = tmp.SelectMany((str) => MarkovSplit(str))
                                .Distinct()
                                .Where((a) => !(a.Length == 0 || Regex.IsMatch(a, "\0")));

            var result_hash = new Hash<string, double>(() => 0.0);
            var trie = new Trie(keywords.Keys);
            result_ary.Each((str) =>
            {
                var terms = trie.SplitIntoTerms(str).Distinct();
                result_hash[str] = terms.Select((kw) => keywords[kw]).Sum();
            });

            if (DEBUG)
            {
                var sum = result_hash.Values.Sum();
                var tmp2 = from item in result_hash orderby -item.Value, item.Key select item;
                Console.WriteLine("-(候補数: {0})----", result_hash.Count);
                tmp2.Take(10).Each((i) =>
                {
                    Console.WriteLine("{0:###.##}: {1}", i.Value / sum * 100, i.Key);
                });
            }

            var result2 = Util.RouletteSelect(result_hash);
            if (result2 != null && result2 != "")
                return result2;
            return "";
        }
    }

    public class Core
    {
        private Dictionary m_dic;
        private readonly object m_lock = new object();

        public Core(string dirname)
        {
            m_dic = Dictionary.Load(dirname);
        }

        public string Talk()
        {
            return Talk(null);
        }

        public string Talk(string str)
        {
            return Talk(str, new Hash<string, double>(() => 0.0));
        }

        public string Talk(string str, Hash<string, double> weight)
        {
            Hash<string, double> keywords;

            if (str != null && str != "")
            {
                keywords = m_dic.SplitIntoKeywords(str);
            }
            else
            {
                List<string> text = m_dic.Text;
                IEnumerable<string> latest_text;
                latest_text = text.Count < 10 ? text : text.Skip(text.Count - 10);
                keywords = new Hash<string, double>(() => 0.0);
                latest_text.Each((str2) =>
                {
                    keywords.Each((i) => { keywords[i.Key] *= 0.5; });
                    m_dic.SplitIntoKeywords(str2).Each((i) => {keywords[i.Key] += i.Value; });
                });
            }

            weight.Keys.Each((kw) =>
            {
                if (keywords.ContainsKey(kw))
                {
                    if (weight[kw] == 0)
                        keywords.Remove(kw);
                    else
                        keywords[kw] *= weight[kw];
                }
            });

            string msg = MessageMarkov(keywords);

            if (DEBUG)
            {
                double sum = keywords.Values.Sum();
                var tmp = from keyword in keywords
                          orderby -keyword.Value, keyword.Key
                          select keyword;
                Console.WriteLine("-(term)----");
                tmp.Each((i) =>
                {
                    Console.Write("{0}({1:###.###}%)", i.Key, i.Value / sum * 100);
                });
                Console.WriteLine();
                Console.WriteLine("----------");
            }

            return msg;
        }

        public void Memorize(string lines)
        {
            lock (m_lock)
            {
                m_dic.StoreText(lines);
                if (m_dic.LearnFromText())
                    m_dic.SaveDictionary();
            }
        }

        public void Memorize(IEnumerable<string> lines)
        {
            lock (m_lock)
            {
                m_dic.StoreText(lines);
                if (m_dic.LearnFromText(true))
                    m_dic.SaveDictionary();
            }
        }

        public string MessageMarkov(Hash<string, double> keywords)
        {
            var lines = new List<int>();
            if (keywords.Count > 0)
            {
                if (keywords.Count > 10)
                {
                    keywords.SortBy((i) => -i.Value).Skip(keywords.Count - 10).Each((i) =>
                    {
                        keywords.Remove(i.Key);
                    });
                }
                double sum = keywords.Values.Sum();
                if (sum > 0.0)
                {
                    keywords.Each((i) => {keywords[i.Key] = i.Value / sum;});
                }

                keywords.Keys.Each((kw) =>
                {
                    var ary = m_dic.Lines(kw).SortBy((_) => Rand());
                    ary.Take(10).Each((idx) => lines.Add(idx));
                });
            }

            for(int i=0; i<10; i++)
            	    lines.Add(Rand(m_dic.Text.Count));
            lines = lines.Uniq();

            var source = lines.Collect((k) => m_dic.Text.Skip(k).Take(5))
                .SortBy((_) => Rand()).Flatten().Compact().Uniq();

            string msg = Util.Markov(source, keywords, m_dic.Trie);
            msg = Util.MessageNormalize(msg);
            return msg;
        }
    }

    internal class Dictionary
    {
        public const string TEXT_FILENAME = "sixamo.txt";
        public const string DIC_FILENAME = "sixamo.dic";

        class Rel
        {
            public int Num { get; set; }
            public int Sum { get; set; }
            public Rel()
            {
                Num = 0;
                Sum = 0;
            }
        }

        class Term
        {
            public List<int> Occur { get; set; }
            private readonly Rel m_rel = new Rel();
            public Rel Rel { get { return m_rel; } }

            public Term()
            {
                Occur = new List<int>();
            }
        }

        private readonly Hash<string, Term> m_term;
        private Trie m_trie;
        private readonly string m_dirname;
        private readonly string m_text_filename;
        private readonly string m_dic_filename;
        private readonly List<string> m_text;
        private int m_line_num;

        public static Dictionary Load(string dirname)
        {
            Dictionary dic = new Dictionary(dirname);
            dic.LoadText();
            dic.LoadDictionary();
            return dic;
        }

        public const int LTL = 3;
        public Dictionary(string dirname)
        {
            m_term = new Hash<string, Term>(() => new Term());
            m_trie = new Trie();

            m_dirname = dirname;
            m_text_filename = string.Format("{0}/{1}", m_dirname, TEXT_FILENAME);
            m_dic_filename = string.Format("{0}/{1}", m_dirname, DIC_FILENAME);
            m_text = new List<string>();

            m_line_num = 0;
        }

        public void LoadText()
        {
            if (!File.Exists(m_text_filename))
                return;

            using (StreamReader reader = new StreamReader(m_text_filename, Encoding.UTF8))
            {
                while (!reader.EndOfStream)
                {
                    m_text.Add(reader.ReadLine().TrimEnd());
                }
            }
        }

        public void LoadDictionary()
        {
            if (!File.Exists(m_dic_filename))
                return;

            using (StreamReader reader = new StreamReader(m_dic_filename, Encoding.UTF8))
            {
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine().TrimEnd();
                    if (line == "")
                        break;
                    Match match = Regex.Match(line, "line_num:\\s*(.*)\\s*$", RegexOptions.IgnoreCase);
                    if (match.Success)
                        m_line_num = int.Parse(match.Groups[1].Value);
                    else
                        Console.WriteLine("{0}:[Warning] Unknown_header {1}", m_dic_filename, line);
                }

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine().TrimEnd();
                    var temp = line.Split('\t');
                    var word = temp[0];
                    var num = temp[1];
                    var sum = temp[2];
                    var occur = temp[3];
                    if (occur != null && occur != "")
                    {
                        if (!m_term.ContainsKey(word))
                            m_term[word] = new Term();
                        m_term[word].Occur = occur.Split(',').Select((l) => int.Parse(l)).ToList();
                        AddTerm(word);
                        m_term[word].Rel.Num = int.Parse(num);
                        m_term[word].Rel.Sum = int.Parse(sum);
                    }
                }
            }
        }

        public void SaveText()
        {
            var tmp_filename = string.Format("{0}/sixamo.tmp.{1}-{2}", m_dirname, Process.GetCurrentProcess().Id, Rand(100));

            using (var writer = new StreamWriter(tmp_filename, false, Encoding.UTF8))
            {
                foreach (var line in m_text)
                    writer.WriteLine(line);
            }

            File.Copy(tmp_filename, m_text_filename, true);
            File.Delete(tmp_filename);
        }

        public void SaveDictionary()
        {
            var tmp_filename = string.Format("{0}/sixamo.tmp.{1}-{2}", m_dirname, Process.GetCurrentProcess().Id, Rand(100));

            using (var writer = new StreamWriter(tmp_filename, false, Encoding.UTF8))
            {
                writer.Write(this.ToString());
            }

            File.Copy(tmp_filename, m_dic_filename, true);
            File.Delete(tmp_filename);
        }

        public const int WindowSize = 500;
        public bool LearnFromText()
        {
            return LearnFromText(false);
        }

        public bool LearnFromText(bool progress)
        {
            bool modified = false;
            int read_size = 0;
            List<string> buf_prev = new List<string>();
            int idx = m_line_num;

            while (true)
            {
                List<string> buf = new List<string>();

                if (progress)
                {
                    int idx2 = read_size / WindowSize * WindowSize;

                    if (idx2 % 100000 == 0)
                    {
                        Console.WriteLine();
                        Console.Write("{0,5:####0}k", idx2 / 1000);
                    }
                    else if (idx2 % 20000 == 0)
                    {
                        Console.Write("*");
                    }
                    else if (idx2 % 2000 == 0)
                    {
                        Console.Write(".");
                    }
                }

                var tmp = read_size;
                var end_flag = false;

                while (tmp / WindowSize == read_size / WindowSize)
                {
                    if (idx >= m_text.Count)
                    {
                        end_flag = true;
                        break;
                    }
                    buf.Add(m_text[idx]);
                    tmp += m_text[idx].Length;
                    idx++;
                }
                read_size = tmp;

                if (end_flag)
                    break;

                if (buf_prev.Count > 0)
                {
                    Learn(buf_prev.Concat(buf), m_line_num);
                    modified = true;

                    m_line_num += buf_prev.Count;
                }

                buf_prev = buf;
            }
            if (progress)
                Console.WriteLine();

            return modified;
        }

        public void StoreText(string line)
        {
            StoreText(new string[] { line });
        }

        internal void StoreText(IEnumerable<string> lines)
        {
            var ary = new List<string>();
            foreach (var line in lines)
                ary.Add(Regex.Replace(line, "\\s+", " ").Trim());

            foreach (var line in ary)
                m_text.Add(line);

            using (StreamWriter writer = new StreamWriter(m_text_filename, true, Encoding.UTF8))
            {
                foreach (var line in ary)
                {
                    writer.WriteLine(line.TrimEnd());
                }
            }
        }

        public void Learn(IEnumerable<string> lines)
        {
            Learn(lines, null);
        }

        public void Learn(IEnumerable<string> lines, int? idx)
        {
            var new_terms = Freq.ExtractTerms(lines, 30);
            foreach (var term in new_terms)
                AddTerm(term);

            if (idx.HasValue)
            {
                IEnumerable<string> words_all = new List<string>();
                lines.EachWithIndex((line, i) =>
                {
                    var num = idx.Value + i;
                    var words = SplitIntoTerms(line);
                    words_all = words_all.Concat(words);
                    words.Each((term) =>
                    {
                        if (m_term[term].Occur.Count == 0 || num > m_term[term].Occur.Last())
                            m_term[term].Occur.Add(num);
                    });
                });

                WeightUpdate(words_all);

                Terms().Each((term) =>
                {
                    var occur = m_term[term].Occur;
                    var size = occur.Count;

                    if (size < 4 && size > 0 && occur.Last() + size * 150 < idx)
                        DelTerm(term);
                });
            }
        }

        public Hash<string, double> SplitIntoKeywords(string str)
        {
            var result = new Hash<string, double>(() => 0.0);
            var terms = SplitIntoTerms(str);

            terms.Each((w) => {result[w] += Weight(w);});

            return result;
        }

        public IEnumerable<string> SplitIntoTerms(string str)
        {
            return SplitIntoTerms(str, null);
        }

        public IEnumerable<string> SplitIntoTerms(string str, int? num)
        {
            return m_trie.SplitIntoTerms(str, num);
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.Append("line_num: ");
            result.Append(m_line_num);
            result.Append("\n\n");

            m_term.DeleteIf((i) => i.Value.Occur.Count == 0);

            m_term.Each((i) =>
            {
                if (i.Value.Occur.Count > 100)
                    m_term[i.Key].Occur = i.Value.Occur.Skip(i.Value.Occur.Count - 100).ToList();
            });

            var tmp = m_term.Keys.SortBy((k) => new IComparable[]{-m_term[k].Occur.Count, m_term[k].Rel.Num, k.Length, k});
            tmp.Each((k) =>
            {
                result.Append(string.Format("{0}\t{1}\t{2}\t{3}\n",
                                            k,
                                            m_term[k].Rel.Num,
                                            m_term[k].Rel.Sum,
                                            m_term[k].Occur.Collect((i) => i.ToString()).Join(",")));
            });
            return result.ToString();
        }

        public void WeightUpdate(IEnumerable<string> words)
        {
            var width = 20;

            words.Each((term) =>
            {
                if (!m_term.ContainsKey(term))
                    m_term[term] = new Term();
            });

            var size = words.Count();

            for(int idx1 = 0; idx1 < size - width; idx1++)
            {
                var word1 = words.ElementAt(idx1);

                for(int idx2 = idx1 + 1; idx2 <= idx1 + width; idx2++)
                {
                    if (word1 == words.ElementAt(idx2))
                        m_term[word1].Rel.Num += 1;
                    m_term[word1].Rel.Sum += 1;
                }
            }

            for(int idx1 = 0; idx1 < width + 1; idx1++)
            {
                string word1;
                if (idx1 == 0)
                    word1 = words.ElementAtOrDefault(0);
                else
                    word1 = words.ElementAtOrDefault(words.Count() - idx1);

                if (word1 != null && word1 != "")
                {
                    for(int idx2 = idx1 - 1; idx2 >= 1; idx2--)
                    {
                        if (word1 == words.ElementAt(words.Count() - idx2))
                            m_term[word1].Rel.Num += 1;
                        m_term[word1].Rel.Sum += 1;
                    }
                }
            }
        }

        public double Weight(string word)
        {
            if (!m_term.ContainsKey(word) || m_term[word].Rel.Sum == 0)
            {
                return 0;
            }
            else
            {
                var num = m_term[word].Rel.Num;
                var sum = m_term[word].Rel.Sum;
                return (double)num / (sum * (sum + 100));
            }
        }

        public IEnumerable<int> Lines(string word)
        {
            if (m_term.ContainsKey(word))
                return m_term[word].Occur;
            return new List<int>();
        }

        public IEnumerable<string> Terms()
        {
            return m_term.Keys;
        }

        public void AddTerm(string str)
        {
            if (!m_term.ContainsKey(str))
                m_term[str] = new Term();
            m_trie.Add(str);
        }

        public void DelTerm(string str)
        {
            var occur = m_term[str].Occur;
            m_term.Remove(str);
            m_trie.Delete(str);

            var tmp = SplitIntoTerms(str);
            tmp.Each((w) => 
            {
                var item = m_term[w].Occur.Concat(occur).Uniq();
                item.Sort();
                m_term[w].Occur = item;
            });
            if (tmp.Count() > 0)
                WeightUpdate(tmp);
        }

        public List<string> Text { get { return m_text; } }
        public Trie Trie { get { return m_trie; } }
    }

    internal class Freq
    {
        private string m_buf;

        public static IEnumerable<string> ExtractTerms(IEnumerable<string> buf, int limit)
        {
            return (new Freq(buf)).ExtractTerms(limit);
        }

        public Freq(IEnumerable<string> buf)
        {
            m_buf = string.Join("\0", buf.ToArray());
        }

        public IEnumerable<string> ExtractTerms(int limit)
        {
            var tmp = ExtractTermsSub(limit)
                .Select((item) => new KeyValuePair<string, int>(item.Key.Reverse().Trim(), item.Value));
            var tmp2 = from term in tmp orderby term.Key, term.Value select term;
            var terms = tmp2.ToArray();

            var terms2 = new List<KeyValuePair<string, int>>();

            (terms.Count() - 1).Times((idx) =>
            {
                if (terms[idx].Key.Length >= terms[idx + 1].Key.Length ||
                    terms[idx].Key != terms[idx + 1].Key.Substring(0, terms[idx].Key.Length))
                {
                    terms2.Add(terms[idx]);
                }
                else if (terms[idx].Value >= terms[idx + 1].Value + 2)
                {
                    terms2.Add(terms[idx]);
                }
            });
            if (terms.Length > 0)
                terms2.Add(terms.Last());

            return terms2.Select((item) => item.Key.Reverse());
        }

        public IEnumerable<KeyValuePair<string, int>> ExtractTermsSub(int limit)
        {
            return ExtractTermsSub(limit, "", 1, false);
        }

        public IEnumerable<KeyValuePair<string, int>> ExtractTermsSub(int limit, string str, int num, bool width)
        {
            var h = DoFreq(str);
            var flag = (h.Count <= 4);

            List<KeyValuePair<string, int>> result = new List<KeyValuePair<string, int>>();

            if (limit > 0)
            {
                if (h.ContainsKey(str))
                    h.Remove(str);

                var remove_list = new List<string>();
                foreach (var item in h)
                    if (item.Value < 2)
                        remove_list.Add(item.Key);

                foreach (var key in remove_list)
                    h.Remove(key);

                foreach (var item in h)
                {
                    result.AddRange(ExtractTermsSub(limit - 1, item.Key, item.Value, flag));
                }
            }

            if (result.Count == 0 && width)
                return new[] { new KeyValuePair<string, int>(str.ToLower(), num) };

            return result;
        }

        public Hash<string, int> DoFreq(string str)
        {
            var freq = new Hash<string, int>(() => 0);

            if (str.Length == 0)
            {
                Regex regexp = new Regex(@"([!-~])[!-~]*|([ァ-ヴ])[ァ-ヴー]*|([^ー\0])", RegexOptions.IgnoreCase);
                foreach (Match match in regexp.Matches(m_buf))
                {
                    string key;
                    if (match.Groups[1].Success)
                    {
                        key = match.Groups[1].Value;
                    }
                    else if (match.Groups[2].Success)
                    {
                        key = match.Groups[2].Value;
                    }
                    else
                    {
                        key = match.Groups[3].Value;
                    }
                    freq[key] += 1;
                }
            }
            else
            {
                Regex regexp = new Regex(Regex.Escape(str) + @"[^\0]?", RegexOptions.IgnoreCase);
                foreach (Match match in regexp.Matches(m_buf))
                    freq[match.Value] += 1;
            }

            return freq;
        }
    }

    internal class Trie
    {
        class Node : Hash<string, Node>
        {
            public Node()
                : base(() => new Node())
            {
            }
        }

        private Node m_root;

        public Trie()
        {
            m_root = new Node();
        }

        public Trie(IEnumerable<string> ary)
        {
            m_root = new Node();
            if (ary != null)
                foreach (string elm in ary)
                    Add(elm);
        }

        public void Add(string str)
        {
            var node = m_root;
            foreach (char b in str)
            {
                if (!node.ContainsKey(b.ToString()))
                    node[b.ToString()] = new Node();
                node = node[b.ToString()];
            }
            node["terminate"] = null;
        }

        public bool IsMember(string str)
        {
            var node = m_root;
            foreach (char b in str)
            {
                if (!node.ContainsKey(b.ToString()))
                    return false;
                node = node[b.ToString()];
            }

            return node.ContainsKey("terminate");
        }

        public IEnumerable<string> Members()
        {
            return MembersSub(m_root);
        }

        private IEnumerable<string> MembersSub(Node node)
        {
            return MembersSub(node, "");
        }

        private IEnumerable<string> MembersSub(Node node, string str)
        {
            return node.SelectMany((item) =>
            {
                if (item.Key == "terminate")
                {
                    return new string[] { str };
                }
                else
                {
                    return MembersSub(item.Value, str + item.Key);
                }
            });
        }

        public IEnumerable<string> SplitIntoTerms(string str)
        {
            return SplitIntoTerms(str, null);
        }

        public IEnumerable<string> SplitIntoTerms(string str, Nullable<int> num)
        {
            var result = new List<string>();

            if (str == null || str == "")
                return result;

            while (str.Length > 0 && (num == null || result.Count < num.Value))
            {
                var prefix = LongestPrefixSubword(str);
                if (prefix != null && prefix != "")
                {
                    result.Add(prefix);
                    str = str.Substring(prefix.Length, str.Length - prefix.Length);
                }
                else
                {
                    string chr = str[0].ToString();
                    if (num.HasValue)
                        result.Add(chr);
                    str = str.Substring(1, str.Length - 1);
                }
            }

            return result;
        }

        public string LongestPrefixSubword(string str)
        {
            var node = m_root;
            string result = null;
            int idx = 0;
            foreach (char b in str)
            {
                if (node.ContainsKey("terminate"))
                    result = str.Substring(0, idx);
                if (!node.ContainsKey(b.ToString()))
                    return result;
                node = node[b.ToString()];
                idx++;
            }

            if (node.ContainsKey("terminate"))
                result = str;

            return result;
        }

        public bool Delete(string str)
        {
            var node = m_root;
            var ary = new List<KeyValuePair<Node, string>>();

            foreach (char b in str)
            {
                if (!node.ContainsKey(b.ToString()))
                    return false;
                ary.Add(new KeyValuePair<Node, string>(node, b.ToString()));
                node = node[b.ToString()];
            }

            if (!node.ContainsKey("terminate"))
                return false;
            ary.Add(new KeyValuePair<Node, string>(node, "terminate"));

            foreach (var item in Enumerable.Reverse(ary))
            {
                item.Key.Remove(item.Value);
                if (item.Key.Count > 0)
                    break;
            }

            return true;
        }
    }

    public class Hash<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
    {
        private readonly Dictionary<TKey, TValue> m_data = new Dictionary<TKey, TValue>();
        private Func<TValue> m_create;

        public Hash(Func<TValue> create)
        {
            m_create = create;
        }

        public bool IsEmpty()
        {
            return m_data.Count == 0;
        }

        public ReadOnlyCollection<TKey> Keys
        {
            get
            {
                List<TKey> list = new List<TKey>();
                foreach (var key in m_data.Keys)
                    list.Add(key);
                return list.AsReadOnly();
            }
        }

        public ReadOnlyCollection<TValue> Values
        {
            get
            {
                List<TValue> list = new List<TValue>();
                foreach (var val in m_data.Values)
                    list.Add(val);
                return list.AsReadOnly();
            }
        }

        public TValue this[TKey key]
        {
            get
            {
                if (m_data.ContainsKey(key))
                    return m_data[key];
                return m_create();
            }
            set
            {
                m_data[key] = value;
            }
        }

        public int Count
        {
            get
            {
                return m_data.Count;
            }
        }

        public void Remove(TKey key)
        {
            m_data.Remove(key);
        }

        public bool ContainsKey(TKey key)
        {
            return m_data.ContainsKey(key);
        }

        public void DeleteIf(Predicate<KeyValuePair<TKey, TValue>> cond)
        {
            foreach (var item in this)
                if (cond(item))
                    Remove(item.Key);
        }

        #region IEnumerable<KeyValuePair<TKey,TValue>> メンバ

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            List<KeyValuePair<TKey, TValue>> list = new List<KeyValuePair<TKey, TValue>>();
            foreach (var item in m_data)
                list.Add(item);
            return list.GetEnumerator();
        }

        #endregion

        #region IEnumerable メンバ

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion
    }

    private static Random m_rand = new Random();

    public static double Rand()
    {
        return m_rand.NextDouble();
    }

    public static int Rand(int maxValue)
    {
        return m_rand.Next(maxValue);
    }
}

public static class SixamoExtends
{
    public static string Reverse(this string str)
    {
        return new string(Enumerable.Reverse(str).ToArray());
    }

    public static void Each<T>(this IEnumerable<T> enumerable, Action<T> action)
    {
        foreach (var item in enumerable)
            action(item);
    }

    public static void EachWithIndex<T>(this IEnumerable<T> e, Action<T, int> action)
    {
        int i = 0;
        foreach (var item in e)
        {
            action(item, i);
            i++;
        }
    }

    public static List<string> Scan(this string str, string re)
    {
        List<string> result = new List<string>();
        str.Scan(re, (m) => result.Add(m));
        return result;
    }

    public static void Scan(this string str, string re, Action<string> action)
    {
        foreach (Match match in Regex.Matches(str, re))
            action(match.Value);
    }

    public static string Gsub(this string str, string re, Func<string, string> func)
    {
        return Regex.Replace(str, re, (m) => func(m.Value));
    }

    public static string Gsub(this string str, string re, string replace)
    {
        return Regex.Replace(str, re, replace);
    }

    public static IEnumerable<TResult> Collect<T,TResult>(this IEnumerable<T> e, Func<T, TResult> func)
    {
        return e.Select(func);
    }

    private class SortByKey : IComparable
    {
        private IComparable[] m_data;

        internal SortByKey(IComparable[] data)
        {
            m_data = data;
        }

        #region IComparable メンバ

        public int CompareTo(object obj)
        {
            SortByKey other = obj as SortByKey;

            int i = 0;
            while (true)
            {
                if (m_data.Length == i || other.m_data.Length == i)
                    return m_data.Length.CompareTo(other.m_data.Length);

                if (m_data[i] != other.m_data[i])
                    return m_data[i].CompareTo(other.m_data[i]);

                i++;
            }
        }

        #endregion
    }

    public static IOrderedEnumerable<TSource> SortBy<TSource>(this IEnumerable<TSource> source, Func<TSource, IComparable[]> func)
    {
        return source.OrderBy((i) => new SortByKey(func(i)));
    }

    public static IOrderedEnumerable<TSource> SortBy<TSource>(this IEnumerable<TSource> source, Func<TSource, IComparable> func)
    {
        return source.SortBy((i) => new IComparable[]{func(i)});
    }

    public static IEnumerable<T> Flatten<T>(this IEnumerable<IEnumerable<T>> e)
    {
        return e.SelectMany((i) => i);
    }

    public static IEnumerable<T> Compact<T>(this IEnumerable<T> e)
    {
        return e.Where((i) => i != null);
    }

    public static List<T> Uniq<T>(this IEnumerable<T> e)
    {
        return e.Distinct().ToList();
    }

    public static void Times(this int repert, Action<int> action)
    {
        for (int i = 0; i < repert; i++)
            action(i);
    }

    public static string Join(this IEnumerable<string> list, string sep)
    {
        return string.Join(sep, list.ToArray());
    }
}
