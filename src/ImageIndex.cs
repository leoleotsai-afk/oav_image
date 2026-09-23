using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace ImageSearch
{
    // One indexed picture: where it is, when it was last modified, and its
    // perceptual hash (stored as a 64-char hex string so JavaScriptSerializer,
    // which represents numbers as JS doubles, never loses precision on the
    // underlying 64-bit values).
    public class ImageRecord
    {
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public long LastWriteTicks { get; set; }
        public string HashHex { get; set; }

        [ScriptIgnore]
        public PerceptualHash Hash
        {
            get { return PerceptualHash.FromHex(HashHex); }
            set { HashHex = value.ToHex(); }
        }
    }

    public class IndexRebuildResult
    {
        public int Added;
        public int Updated;
        public int Removed;
        public int Failed;
        public int Total;
    }

    public class ImageIndex
    {
        public static readonly string[] SupportedExtensions =
            { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };

        public string ImagesFolder { get; private set; }
        public string IndexFilePath { get; private set; }
        public List<ImageRecord> Records { get; private set; }

        public ImageIndex(string imagesFolder, string indexFilePath)
        {
            ImagesFolder = imagesFolder;
            IndexFilePath = indexFilePath;
            Records = new List<ImageRecord>();
        }

        public void Load()
        {
            Records = new List<ImageRecord>();
            if (!File.Exists(IndexFilePath))
            {
                return;
            }

            string json = File.ReadAllText(IndexFilePath, System.Text.Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            List<ImageRecord> loaded = serializer.Deserialize<List<ImageRecord>>(json);
            if (loaded != null)
            {
                Records = loaded;
            }
        }

        public void Save()
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            string json = serializer.Serialize(Records);
            File.WriteAllText(IndexFilePath, json, System.Text.Encoding.UTF8);
        }

        // Rescans ImagesFolder: hashes new or modified files, drops records for
        // files that no longer exist, and leaves everything else untouched.
        // progress is called after every file that is (re)hashed.
        public IndexRebuildResult Rebuild(Action<string, int, int> progress)
        {
            IndexRebuildResult result = new IndexRebuildResult();

            if (!Directory.Exists(ImagesFolder))
            {
                Directory.CreateDirectory(ImagesFolder);
            }

            List<string> files = new List<string>();
            foreach (string ext in SupportedExtensions)
            {
                files.AddRange(Directory.GetFiles(ImagesFolder, "*" + ext, SearchOption.AllDirectories));
            }
            files = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            Dictionary<string, ImageRecord> byPath = new Dictionary<string, ImageRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (ImageRecord r in Records)
            {
                byPath[r.FullPath] = r;
            }

            List<ImageRecord> updatedRecords = new List<ImageRecord>();
            int index = 0;
            foreach (string file in files)
            {
                index++;
                if (progress != null)
                {
                    progress(Path.GetFileName(file), index, files.Count);
                }

                long ticks = File.GetLastWriteTimeUtc(file).Ticks;
                ImageRecord existing;
                if (byPath.TryGetValue(file, out existing) && existing.LastWriteTicks == ticks)
                {
                    updatedRecords.Add(existing);
                    continue;
                }

                try
                {
                    PerceptualHash hash = ImageHasher.ComputeHashFromFile(file);
                    ImageRecord record = new ImageRecord();
                    record.FileName = Path.GetFileName(file);
                    record.FullPath = file;
                    record.LastWriteTicks = ticks;
                    record.Hash = hash;
                    updatedRecords.Add(record);

                    if (existing == null)
                    {
                        result.Added++;
                    }
                    else
                    {
                        result.Updated++;
                    }
                }
                catch (Exception)
                {
                    result.Failed++;
                }
            }

            result.Removed = Records.Count(r => !files.Contains(r.FullPath, StringComparer.OrdinalIgnoreCase));
            result.Total = updatedRecords.Count;

            Records = updatedRecords;
            Save();
            return result;
        }

        public List<KeyValuePair<ImageRecord, double>> FindMostSimilar(PerceptualHash queryHash, int topK)
        {
            return Records
                .Select(r => new KeyValuePair<ImageRecord, double>(r, ImageHasher.Similarity(queryHash, r.Hash)))
                .OrderByDescending(p => p.Value)
                .Take(topK)
                .ToList();
        }

        // Simple offline keyword search used for voice queries: scores each file
        // name by how many of the recognized characters/words it contains.
        public List<KeyValuePair<ImageRecord, double>> FindByKeyword(string query, int topK)
        {
            List<KeyValuePair<ImageRecord, double>> scored = new List<KeyValuePair<ImageRecord, double>>();
            if (string.IsNullOrWhiteSpace(query))
            {
                return scored;
            }

            string[] tokens = query
                .Split(new char[] { ' ', '\t', '　', ',', '，', '。', '、' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                tokens = new string[] { query };
            }

            foreach (ImageRecord r in Records)
            {
                string name = Path.GetFileNameWithoutExtension(r.FileName);
                double score = 0;

                foreach (string token in tokens)
                {
                    if (token.Length == 0) continue;
                    if (name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        score += token.Length * 2; // whole-token match counts extra
                    }
                }

                // Character-level overlap as a fallback so partial matches still rank.
                foreach (char c in query)
                {
                    if (char.IsWhiteSpace(c)) continue;
                    if (name.IndexOf(c) >= 0)
                    {
                        score += 1;
                    }
                }

                if (score > 0)
                {
                    scored.Add(new KeyValuePair<ImageRecord, double>(r, score));
                }
            }

            double maxScore = scored.Count > 0 ? scored.Max(p => p.Value) : 1;
            if (maxScore <= 0) maxScore = 1;

            return scored
                .Select(p => new KeyValuePair<ImageRecord, double>(p.Key, p.Value / maxScore))
                .OrderByDescending(p => p.Value)
                .Take(topK)
                .ToList();
        }
    }
}
