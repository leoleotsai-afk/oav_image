using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ImageSearch
{
    // Delegates the actual photo capture to the built-in Windows Camera app
    // (no camera driver / DirectShow code needed) and then watches the folder
    // it saves into so the newly taken picture can be picked up as the search query.
    public static class CameraCapture
    {
        private static readonly string[] PhotoExtensions = { ".jpg", ".jpeg", ".png" };

        public static string CameraRollFolder
        {
            get
            {
                string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                return Path.Combine(pictures, "Camera Roll");
            }
        }

        public static bool LaunchCameraApp(out string error)
        {
            error = null;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("microsoft.windows.camera:");
                psi.UseShellExecute = true;
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static HashSet<string> SnapshotExistingFiles()
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string folder = CameraRollFolder;
            if (Directory.Exists(folder))
            {
                foreach (string f in Directory.GetFiles(folder))
                {
                    set.Add(f);
                }
            }
            return set;
        }

        // Non-blocking single check: returns the newest photo that appeared after
        // 'before' was captured, or null if none is ready yet. Call repeatedly
        // from a UI timer while the Camera app is open.
        public static string CheckForNewPhoto(HashSet<string> before)
        {
            string folder = CameraRollFolder;
            if (!Directory.Exists(folder))
            {
                return null;
            }

            string newest = null;
            DateTime newestTime = DateTime.MinValue;

            foreach (string f in Directory.GetFiles(folder))
            {
                if (before.Contains(f))
                {
                    continue;
                }
                string ext = Path.GetExtension(f);
                bool isPhoto = false;
                foreach (string p in PhotoExtensions)
                {
                    if (string.Equals(ext, p, StringComparison.OrdinalIgnoreCase))
                    {
                        isPhoto = true;
                        break;
                    }
                }
                if (!isPhoto || !IsFileReady(f))
                {
                    continue;
                }

                DateTime t = File.GetLastWriteTimeUtc(f);
                if (t > newestTime)
                {
                    newestTime = t;
                    newest = f;
                }
            }

            return newest;
        }

        private static bool IsFileReady(string path)
        {
            try
            {
                using (FileStream fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    return fs.Length > 0;
                }
            }
            catch (IOException)
            {
                return false;
            }
        }
    }
}
