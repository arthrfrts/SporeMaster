﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Windows.Threading;

namespace SporeMaster
{
    class DirectoryTreeWatcher
    {
        DirectoryTree target;
        int side;
        string path;
        FileSystemWatcher fswatch;
        System.Windows.Threading.Dispatcher disp;
        private bool stopped = false;

        HashSet<string> fullTextExtensions;

        public delegate void ChangeHandler();
        public event ChangeHandler Change;

        public string Path { get { return path; } }

        public DirectoryTreeWatcher(DirectoryTree target, int side, string path, System.Windows.Threading.Dispatcher disp, ChangeHandler Change,
                                    HashSet<string> fullTextExtensions,
                                    PleaseWait initProgress)
        {
            this.target = target;
            this.side = side;
            this.path = path;
            this.Change += Change;
            this.disp = disp;
            this.fullTextExtensions = fullTextExtensions;
            init(initProgress);
        }

        public void Dispose()
        {
            stopped = true;
            fswatch.Dispose();
            target.decPresent(side);
        }

        private void getAll(DirectoryInfo dir, List<string> files, List<long> sizes)
        {
            foreach (var e in dir.GetFileSystemInfos())
            {
                if ((e.Attributes & FileAttributes.Directory) == FileAttributes.Directory)
                    getAll((DirectoryInfo)e, files, sizes);
                else
                {
                    files.Add(e.FullName);
                    sizes.Add(((FileInfo)e).Length);
                }
            }
        }

        private void init(PleaseWait progress)
        {
            fswatch = new FileSystemWatcher(path);
            fswatch.Changed += thr_onFileChanged;
            fswatch.Deleted += thr_onFileChanged;
            fswatch.Renamed += thr_onFileRenamed;
            fswatch.Created += thr_onFileChanged;
            fswatch.IncludeSubdirectories = true;

            if (progress != null) progress.beginTask(0.25, 1.0);

            // Process files in random order, to make the progress bar more useful
            // Getting file sizes is very slow in .NET :-(
            var r = new Random();
            var files = (from f in Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                         orderby r.Next()
                         select f).ToArray();

            if (progress != null) progress.endTask();
            if (progress != null) progress.beginTask(0.75, files.Length);

            for(int i=0; i<files.Length; i++) {
                updateFile(files[i].Substring(path.Length + 1), +1);
                if (progress != null) progress.addProgress(1);
            }
            if (Change != null)
                thr_Change();

            fswatch.EnableRaisingEvents = true;

            if (progress != null) progress.endTask();
        }

        #region Dispatch event handlers to UI thread
        private delegate void changeDelegate();
        private delegate void onFileChangedDelegate(FileSystemEventArgs e);
        private delegate void onFileRenamedDelegate(RenamedEventArgs e);
        private void thr_Change()
        {
            disp.BeginInvoke(DispatcherPriority.Normal, new changeDelegate(onChange));
        }
        private void thr_onFileChanged(object source, FileSystemEventArgs e)
        {
            disp.BeginInvoke(DispatcherPriority.Normal, new onFileChangedDelegate(onFileChanged), e);
        }
        private void thr_onFileRenamed(object source, RenamedEventArgs e)
        {
            disp.BeginInvoke(DispatcherPriority.Normal, new onFileRenamedDelegate(onFileRenamed), e);
        }
        #endregion

        private void onChange()
        {
            if (this.stopped) return;
            Change();
        }

        private void onFileChanged(FileSystemEventArgs e)
        {
            if (this.stopped) return;
            var p = e.FullPath.Substring(path.Length + 1);
            if (e.ChangeType == WatcherChangeTypes.Created)
                updateFile(p, +1);
            else if (e.ChangeType == WatcherChangeTypes.Deleted)
                updateFile(p, -1);
            else if (e.ChangeType == WatcherChangeTypes.Changed)
                updateFile(p, 0);
            if (Change != null)
                Change();
        }
        private void onFileRenamed(RenamedEventArgs e)
        {
            if (this.stopped) return;
            var newpath = e.FullPath.Substring(path.Length + 1);
            var oldpath = e.OldFullPath.Substring(path.Length + 1);
            updateFile(oldpath, -1);
            if (Directory.Exists(e.FullPath))
                foreach (var f in Directory.GetFiles(e.FullPath, "*.*", SearchOption.AllDirectories))
                    updateFile(f.Substring(path.Length + 1), +1); 
            updateFile(newpath, +1);
            if (Change != null)
                Change();
        }
        private void updateFile(string relativePath, int isNewFile)
        {
            if (relativePath.EndsWith(".search_index")) return;
            var n = target.getFile(relativePath, isNewFile > 0);
            if (n == null) return;

            if (n.IsFolder)
            {
                if (isNewFile < 0) n.decPresent(side);
            }
            else
            {
                if (isNewFile > 0) n.incPresent(side);
                if (isNewFile < 0) n.decPresent(side);
                if (isNewFile >= 0 && !n.IsFolder && fullTextExtensions.Contains(n.FileType))
                    n.LoadFullText(side, path + "\\" + relativePath);
            }
        }
    };
}
