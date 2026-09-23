using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ImageSearch
{
    public class MainForm : Form
    {
        private const int DefaultTopK = 8;
        private const int CameraTimeoutSeconds = 180;

        private Button btnVoice;
        private Button btnUpload;
        private Button btnCamera;
        private Button btnCancelCamera;
        private Button btnRebuildIndex;
        private Label lblImagesFolder;
        private Label lblIndexCount;

        private PictureBox picQuery;
        private Label lblQueryInfo;
        private ListView listResults;
        private ImageList thumbList;
        private Label lblStatus;
        private ProgressBar progress;

        private string imagesFolder;
        private string indexFilePath;
        private int topK;

        private ImageIndex index;
        private VoiceSearchEngine voice;
        private BackgroundWorker indexWorker;

        private HashSet<string> cameraBeforeFiles;
        private Timer cameraPollTimer;
        private DateTime cameraArmedAt;

        public MainForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "圖片辨識系統";
            MinimumSize = new Size(1100, 700);

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size((int)(wa.Width * 0.8), (int)(wa.Height * 0.85));
            StartPosition = FormStartPosition.CenterScreen;

            LoadConfig();
            BuildLayout();
            InitIndex();
            InitVoice();

            FormClosed += MainForm_FormClosed;

            RunIndexRebuild();
        }

        // ---------- configuration ----------

        private static string ResolveProjectRoot()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            DirectoryInfo dir = new DirectoryInfo(baseDir);
            if (string.Equals(dir.Name, "bin", StringComparison.OrdinalIgnoreCase) && dir.Parent != null)
            {
                return dir.Parent.FullName;
            }
            return baseDir;
        }

        private void LoadConfig()
        {
            string root = ResolveProjectRoot();
            string configPath = Path.Combine(root, "config.ini");

            string relImagesFolder = "images";
            topK = DefaultTopK;

            if (File.Exists(configPath))
            {
                foreach (string rawLine in File.ReadAllLines(configPath, System.Text.Encoding.UTF8))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#"))
                    {
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }
                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();

                    if (string.Equals(key, "ImagesFolder", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                    {
                        relImagesFolder = value;
                    }
                    else if (string.Equals(key, "TopK", StringComparison.OrdinalIgnoreCase))
                    {
                        int parsed;
                        if (int.TryParse(value, out parsed) && parsed > 0)
                        {
                            topK = parsed;
                        }
                    }
                }
            }

            imagesFolder = Path.IsPathRooted(relImagesFolder)
                ? relImagesFolder
                : Path.Combine(root, relImagesFolder);
            indexFilePath = Path.Combine(root, "image_index.json");
        }

        private void InitIndex()
        {
            index = new ImageIndex(imagesFolder, indexFilePath);
            index.Load();
            UpdateIndexCountLabel();
        }

        private void InitVoice()
        {
            voice = new VoiceSearchEngine();
            voice.RecognizedText += Voice_RecognizedText;
            voice.StatusChanged += Voice_StatusChanged;
            if (!voice.IsAvailable)
            {
                btnVoice.Enabled = false;
                btnVoice.Text = "語音查詢（本機未安裝語音辨識）";
            }
        }

        // ---------- layout ----------

        private void BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            root.Controls.Add(BuildToolbar(), 0, 0);
            root.Controls.Add(BuildContent(), 0, 1);
            root.Controls.Add(BuildStatusBar(), 0, 2);
        }

        private GroupBox BuildToolbar()
        {
            GroupBox grp = new GroupBox();
            grp.Text = "查詢方式";
            grp.Dock = DockStyle.Top;
            grp.Padding = new Padding(8);
            grp.AutoSize = true;
            grp.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Top;
            flow.AutoSize = true;
            flow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flow.WrapContents = true;

            btnVoice = new Button { Text = "🎤 語音查詢", AutoSize = true, Padding = new Padding(10, 6, 10, 6) };
            btnVoice.Click += BtnVoice_Click;

            btnUpload = new Button { Text = "📁 上傳圖片查詢", AutoSize = true, Padding = new Padding(10, 6, 10, 6) };
            btnUpload.Click += BtnUpload_Click;

            btnCamera = new Button { Text = "📷 拍照查詢", AutoSize = true, Padding = new Padding(10, 6, 10, 6) };
            btnCamera.Click += BtnCamera_Click;

            btnCancelCamera = new Button { Text = "取消拍照", AutoSize = true, Padding = new Padding(10, 6, 10, 6), Enabled = false };
            btnCancelCamera.Click += BtnCancelCamera_Click;

            btnRebuildIndex = new Button { Text = "🔄 重建圖片索引", AutoSize = true, Padding = new Padding(10, 6, 10, 6) };
            btnRebuildIndex.Click += BtnRebuildIndex_Click;

            flow.Controls.Add(btnVoice);
            flow.Controls.Add(btnUpload);
            flow.Controls.Add(btnCamera);
            flow.Controls.Add(btnCancelCamera);
            flow.Controls.Add(btnRebuildIndex);

            grp.Controls.Add(flow);

            TableLayoutPanel infoRow = new TableLayoutPanel();
            infoRow.Dock = DockStyle.Top;
            infoRow.AutoSize = true;
            infoRow.ColumnCount = 2;
            infoRow.RowCount = 1;

            lblImagesFolder = new Label { AutoSize = true, Text = "圖片庫資料夾：" + imagesFolder, Padding = new Padding(0, 4, 20, 0) };
            lblIndexCount = new Label { AutoSize = true, Text = "已索引：0 張", Padding = new Padding(0, 4, 0, 0) };
            infoRow.Controls.Add(lblImagesFolder, 0, 0);
            infoRow.Controls.Add(lblIndexCount, 1, 0);
            grp.Controls.Add(infoRow);

            return grp;
        }

        private SplitContainer BuildContent()
        {
            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Vertical;
            split.SplitterWidth = 6;

            GroupBox left = new GroupBox();
            left.Text = "查詢圖片";
            left.Dock = DockStyle.Fill;
            left.Padding = new Padding(8);

            picQuery = new PictureBox();
            picQuery.Dock = DockStyle.Top;
            picQuery.Height = 320;
            picQuery.SizeMode = PictureBoxSizeMode.Zoom;
            picQuery.BorderStyle = BorderStyle.FixedSingle;

            lblQueryInfo = new Label();
            lblQueryInfo.Dock = DockStyle.Fill;
            lblQueryInfo.AutoSize = false;
            lblQueryInfo.Padding = new Padding(0, 8, 0, 0);
            lblQueryInfo.Text = "尚未查詢。請使用上方按鈕以語音、上傳圖片或拍照方式開始搜尋。";

            left.Controls.Add(lblQueryInfo);
            left.Controls.Add(picQuery);

            GroupBox right = new GroupBox();
            right.Text = "搜尋結果（依相似度排序）";
            right.Dock = DockStyle.Fill;
            right.Padding = new Padding(8);

            thumbList = new ImageList();
            thumbList.ImageSize = new Size(128, 128);
            thumbList.ColorDepth = ColorDepth.Depth32Bit;

            listResults = new ListView();
            listResults.Dock = DockStyle.Fill;
            listResults.View = View.LargeIcon;
            listResults.LargeImageList = thumbList;
            listResults.MultiSelect = false;
            listResults.FullRowSelect = true;
            listResults.DoubleClick += ListResults_DoubleClick;

            right.Controls.Add(listResults);

            split.Panel1.Controls.Add(left);
            split.Panel2.Controls.Add(right);

            split.HandleCreated += delegate { split.SplitterDistance = (int)(split.Width * 0.35); };

            return split;
        }

        private TableLayoutPanel BuildStatusBar()
        {
            TableLayoutPanel row = new TableLayoutPanel();
            row.Dock = DockStyle.Fill;
            row.ColumnCount = 2;
            row.RowCount = 1;
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));

            lblStatus = new Label();
            lblStatus.Dock = DockStyle.Fill;
            lblStatus.AutoSize = false;
            lblStatus.Height = 28;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.Padding = new Padding(6, 0, 0, 0);
            lblStatus.Text = "就緒";

            progress = new ProgressBar();
            progress.Dock = DockStyle.Fill;
            progress.Height = 20;
            progress.Style = ProgressBarStyle.Continuous;

            row.Controls.Add(lblStatus, 0, 0);
            row.Controls.Add(progress, 1, 0);
            return row;
        }

        // ---------- index rebuild ----------

        private void RunIndexRebuild()
        {
            if (indexWorker != null && indexWorker.IsBusy)
            {
                return;
            }

            SetBusy(true, "正在掃描圖片庫...");
            indexWorker = new BackgroundWorker();
            indexWorker.WorkerReportsProgress = true;
            indexWorker.DoWork += IndexWorker_DoWork;
            indexWorker.ProgressChanged += IndexWorker_ProgressChanged;
            indexWorker.RunWorkerCompleted += IndexWorker_RunWorkerCompleted;
            indexWorker.RunWorkerAsync();
        }

        private void IndexWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = (BackgroundWorker)sender;
            IndexRebuildResult result = index.Rebuild(delegate(string fileName, int i, int total)
            {
                int percent = total > 0 ? (int)((i * 100L) / total) : 0;
                worker.ReportProgress(percent, fileName);
            });
            e.Result = result;
        }

        private void IndexWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            progress.Value = Math.Max(0, Math.Min(100, e.ProgressPercentage));
            lblStatus.Text = "正在處理：" + (e.UserState as string);
        }

        private void IndexWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            SetBusy(false, "就緒");
            UpdateIndexCountLabel();

            if (e.Error != null)
            {
                lblStatus.Text = "索引建立失敗：" + e.Error.Message;
                return;
            }

            IndexRebuildResult result = (IndexRebuildResult)e.Result;
            lblStatus.Text = string.Format(
                "索引完成，共 {0} 張圖片（新增 {1}／更新 {2}／移除 {3}／失敗 {4}）",
                result.Total, result.Added, result.Updated, result.Removed, result.Failed);
        }

        private void UpdateIndexCountLabel()
        {
            lblIndexCount.Text = "已索引：" + index.Records.Count + " 張";
        }

        // ---------- upload ----------

        private void BtnUpload_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "選擇要查詢的圖片";
                dlg.Filter = "圖片檔案|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|所有檔案|*.*";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    RunVisualSearch(dlg.FileName, "上傳圖片");
                }
            }
        }

        // ---------- camera ----------

        private void BtnCamera_Click(object sender, EventArgs e)
        {
            string error;
            if (!CameraCapture.LaunchCameraApp(out error))
            {
                MessageBox.Show(this, "無法開啟相機應用程式：" + error, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            cameraBeforeFiles = CameraCapture.SnapshotExistingFiles();
            cameraArmedAt = DateTime.Now;

            btnCamera.Enabled = false;
            btnCancelCamera.Enabled = true;
            lblStatus.Text = "請在相機視窗中拍照，拍照完成後系統會自動抓取最新照片...";

            cameraPollTimer = new Timer();
            cameraPollTimer.Interval = 700;
            cameraPollTimer.Tick += CameraPollTimer_Tick;
            cameraPollTimer.Start();
        }

        private void CameraPollTimer_Tick(object sender, EventArgs e)
        {
            if ((DateTime.Now - cameraArmedAt).TotalSeconds > CameraTimeoutSeconds)
            {
                StopCameraWatch("等待拍照逾時，已取消");
                return;
            }

            string photo = CameraCapture.CheckForNewPhoto(cameraBeforeFiles);
            if (photo != null)
            {
                StopCameraWatch(null);
                RunVisualSearch(photo, "相機拍照");
            }
        }

        private void BtnCancelCamera_Click(object sender, EventArgs e)
        {
            StopCameraWatch("已取消拍照查詢");
        }

        private void StopCameraWatch(string statusMessage)
        {
            if (cameraPollTimer != null)
            {
                cameraPollTimer.Stop();
                cameraPollTimer.Dispose();
                cameraPollTimer = null;
            }
            btnCamera.Enabled = true;
            btnCancelCamera.Enabled = false;
            if (statusMessage != null)
            {
                lblStatus.Text = statusMessage;
            }
        }

        // ---------- voice ----------

        private void BtnVoice_Click(object sender, EventArgs e)
        {
            lblStatus.Text = "請開始說話...";
            voice.ListenOnce();
        }

        private void Voice_StatusChanged(object sender, string status)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(delegate(string s) { lblStatus.Text = s; }), status);
                return;
            }
            lblStatus.Text = status;
        }

        private void Voice_RecognizedText(object sender, string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(RunKeywordSearchOnUiThread), text);
                return;
            }
            RunKeywordSearchOnUiThread(text);
        }

        private void RunKeywordSearchOnUiThread(string text)
        {
            picQuery.Image = null;
            lblQueryInfo.Text = "語音辨識結果：「" + text + "」";
            lblStatus.Text = "正在依語音關鍵字搜尋...";

            List<KeyValuePair<ImageRecord, double>> results = index.FindByKeyword(text, topK);
            ShowResults(results, "語音關鍵字比對");
        }

        // ---------- visual search ----------

        private void RunVisualSearch(string filePath, string sourceLabel)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                SetQueryPreview(filePath);

                PerceptualHash queryHash = ImageHasher.ComputeHashFromFile(filePath);
                List<KeyValuePair<ImageRecord, double>> results = index.FindMostSimilar(queryHash, topK);

                lblQueryInfo.Text = sourceLabel + "：" + Path.GetFileName(filePath);
                ShowResults(results, "視覺相似度比對");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "處理圖片時發生錯誤：" + ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblStatus.Text = "查詢失敗";
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void SetQueryPreview(string filePath)
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            using (MemoryStream ms = new MemoryStream(bytes))
            {
                using (Image img = Image.FromStream(ms))
                {
                    if (picQuery.Image != null)
                    {
                        picQuery.Image.Dispose();
                    }
                    picQuery.Image = new Bitmap(img);
                }
            }
        }

        private void ShowResults(List<KeyValuePair<ImageRecord, double>> results, string modeLabel)
        {
            listResults.BeginUpdate();
            listResults.Items.Clear();
            thumbList.Images.Clear();

            if (results.Count == 0)
            {
                lblStatus.Text = modeLabel + "：找不到相符的圖片";
                listResults.EndUpdate();
                return;
            }

            int i = 0;
            foreach (KeyValuePair<ImageRecord, double> pair in results)
            {
                ImageRecord record = pair.Key;
                double score = pair.Value;

                Image thumb = LoadThumbnailSafe(record.FullPath);
                string imageKey = "img" + i;
                thumbList.Images.Add(imageKey, thumb);

                ListViewItem item = new ListViewItem();
                item.Text = record.FileName + Environment.NewLine + string.Format("相似度 {0:P0}", score);
                item.ImageKey = imageKey;
                item.Tag = record.FullPath;
                listResults.Items.Add(item);
                i++;
            }

            listResults.EndUpdate();
            lblStatus.Text = modeLabel + "：找到 " + results.Count + " 筆相符結果";
        }

        private static Image LoadThumbnailSafe(string path)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                using (MemoryStream ms = new MemoryStream(bytes))
                {
                    using (Image img = Image.FromStream(ms))
                    {
                        return new Bitmap(img, new Size(128, 128));
                    }
                }
            }
            catch (Exception)
            {
                return new Bitmap(128, 128);
            }
        }

        private void ListResults_DoubleClick(object sender, EventArgs e)
        {
            if (listResults.SelectedItems.Count == 0)
            {
                return;
            }
            string path = listResults.SelectedItems[0].Tag as string;
            if (path != null && File.Exists(path))
            {
                try
                {
                    System.Diagnostics.Process.Start(path);
                }
                catch (Exception)
                {
                }
            }
        }

        private void BtnRebuildIndex_Click(object sender, EventArgs e)
        {
            RunIndexRebuild();
        }

        private void SetBusy(bool busy, string statusText)
        {
            btnRebuildIndex.Enabled = !busy;
            btnUpload.Enabled = !busy;
            btnCamera.Enabled = !busy;
            btnVoice.Enabled = !busy && voice != null && voice.IsAvailable;
            progress.Value = 0;
            lblStatus.Text = statusText;
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (cameraPollTimer != null)
            {
                cameraPollTimer.Stop();
                cameraPollTimer.Dispose();
            }
            if (voice != null)
            {
                voice.Dispose();
            }
        }
    }
}
