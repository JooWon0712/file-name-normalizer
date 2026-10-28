using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

namespace FileNameNormalizer
{
    public enum EntryStatus { Ready, Unchanged, Skipped, Conflict, Error, Done }

    public sealed class RenameEntry
    {
        public string SourcePath { get; set; }
        public string TargetPath { get; set; }
        public string OriginalName { get; set; }
        public string NewName { get; set; }
        public string Folder { get; set; }
        public string RootFolder { get; set; }
        public string TargetFolder { get; set; }
        public bool MoveToRoot { get; set; }
        public EntryStatus Status { get; set; }
        public string Message { get; set; }
        public bool Selected { get; set; }
        public bool CollisionNumbered { get; set; }
        internal FileStamp ScannedStamp { get; set; }
        internal string ScannedRootFolder { get; set; }
        internal string ScannedTargetFolder { get; set; }
        internal bool ScannedMoveToRoot { get; set; }
        internal IList<string> ScannedProtectedRoots { get; set; }
    }

    public sealed class ScanResult
    {
        public List<RenameEntry> Entries { get; set; }
        public List<string> Warnings { get; set; }
        public ScanResult() { Entries = new List<RenameEntry>(); Warnings = new List<string>(); }
    }

    public sealed class BatchResult
    {
        public List<RenameEntry> Entries { get; set; }
        public int Changed { get; set; }
        public int Failed { get; set; }
        public int RemovedDirectories { get; set; }
        public List<string> CleanupWarnings { get; set; }
        public string Message { get; set; }
        public string HistoryPath { get; set; }
        public BatchResult()
        {
            Entries = new List<RenameEntry>();
            CleanupWarnings = new List<string>();
            Message = "";
            HistoryPath = "";
        }
    }

    // These public data-only classes allow JavaScriptSerializer to restore a crash journal.
    public sealed class FileStamp
    {
        public long Length { get; set; }
        public long LastWriteUtcTicks { get; set; }
        public long CreationUtcTicks { get; set; }
        public uint VolumeSerialNumber { get; set; }
        public uint FileIndexHigh { get; set; }
        public uint FileIndexLow { get; set; }

        internal bool Matches(FileStamp other)
        {
            return other != null && Length == other.Length && LastWriteUtcTicks == other.LastWriteUtcTicks
                && CreationUtcTicks == other.CreationUtcTicks && VolumeSerialNumber == other.VolumeSerialNumber
                && FileIndexHigh == other.FileIndexHigh && FileIndexLow == other.FileIndexLow;
        }
    }

    public sealed class RenameJournalItem
    {
        public string SourcePath { get; set; }
        public string TargetPath { get; set; }
        public string TemporaryPath { get; set; }
        public string RootFolder { get; set; }
        public string TargetFolder { get; set; }
        public bool MoveToRoot { get; set; }
        public FileStamp Stamp { get; set; }
        public string State { get; set; }
        public string Error { get; set; }
    }

    public sealed class RenameJournal
    {
        public int Version { get; set; }
        public string CreatedUtc { get; set; }
        public bool Completed { get; set; }
        public List<RenameJournalItem> Items { get; set; }
        public RenameJournal() { Items = new List<RenameJournalItem>(); }
    }

    public static class RenameEngine
    {
        private static readonly Regex CodePattern = new Regex(
            "^(?<prefix>[0-9]*[A-Za-z]+)-(?<number>[0-9]+)(?<suffix>[A-Za-z]*)(?<duplicate> \\([1-9][0-9]*\\))?\\z", RegexOptions.CultureInvariant);
        private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private const string JournalPrefix = "rename-";

        public static string NormalizeName(string filename)
        {
            if (String.IsNullOrEmpty(filename) || filename.IndexOfAny(new char[] { '\\', '/' }) >= 0) return null;
            int separator = filename.LastIndexOf('@');
            string candidate = separator >= 0 ? filename.Substring(separator + 1) : filename;
            if (candidate.Length == 0) return null;
            try
            {
                string extension = Path.GetExtension(candidate);
                string stem = candidate.Substring(0, candidate.Length - extension.Length);
                Match match = CodePattern.Match(stem);
                if (!match.Success) return null;
                return match.Groups["prefix"].Value.ToUpperInvariant() + "-" + match.Groups["number"].Value
                    + match.Groups["suffix"].Value + match.Groups["duplicate"].Value + extension;
            }
            catch (ArgumentException) { return null; }
        }

        public static ScanResult Scan(IEnumerable<string> roots, bool recursive)
        {
            return Scan(roots, recursive, false);
        }

        public static ScanResult Scan(IEnumerable<string> roots, bool recursive, bool gatherToRoot)
        {
            ScanResult result = new ScanResult();
            HashSet<string> seenFolders = new HashSet<string>(PathComparer);
            HashSet<string> seenFiles = new HashSet<string>(PathComparer);
            List<string> rootList = new List<string>();
            foreach (string root in roots ?? Enumerable.Empty<string>())
            {
                try
                {
                    if (String.IsNullOrWhiteSpace(root)) continue;
                    string full = FullFolderPath(root);
                    if (!Directory.Exists(full)) { result.Warnings.Add(full + " : 폴더를 찾을 수 없습니다."); continue; }
                    EnsureOrdinaryFolder(full);
                    rootList.Add(full);
                }
                catch (Exception ex) { result.Warnings.Add((root ?? "") + " : " + FriendlyError(ex)); }
            }
            IList<string> protectedRoots = rootList.Distinct(PathComparer).OrderBy(x => x, PathComparer).ToList().AsReadOnly();
            Queue<string> folders = new Queue<string>(protectedRoots);
            while (folders.Count > 0)
            {
                string folder = folders.Dequeue();
                if (!seenFolders.Add(folder)) continue;
                try
                {
                    EnsureOrdinaryFolder(folder);
                    foreach (string file in Directory.GetFiles(folder).OrderBy(x => x, PathComparer))
                    {
                        if (!seenFiles.Add(file)) continue;
                        RenameEntry entry = NewEntry(file);
                        entry.RootFolder = rootList.Where(x => IsWithinFolder(x, entry.Folder, true))
                            .OrderByDescending(x => x.Length).ThenBy(x => x, PathComparer).First();
                        entry.ScannedRootFolder = entry.RootFolder;
                        entry.ScannedTargetFolder = entry.TargetFolder;
                        // Keep every explicitly added root, including empty/nested roots that
                        // have no selected files, protected throughout a later gather batch.
                        entry.ScannedProtectedRoots = protectedRoots;
                        result.Entries.Add(entry);
                        try
                        {
                            FileAttributes attributes = File.GetAttributes(file);
                            if ((attributes & FileAttributes.ReparsePoint) != 0)
                            {
                                SetStatus(entry, EntryStatus.Skipped, "링크 파일은 처리하지 않습니다.");
                                continue;
                            }
                            string normalized = NormalizeName(entry.OriginalName);
                            if (normalized == null)
                            {
                                SetStatus(entry, EntryStatus.Skipped, "지원하는 코드 형식이 아닙니다.");
                                continue;
                            }
                            entry.TargetFolder = gatherToRoot ? entry.RootFolder : entry.Folder;
                            entry.MoveToRoot = !PathComparer.Equals(entry.Folder, entry.TargetFolder);
                            entry.ScannedTargetFolder = entry.TargetFolder;
                            entry.ScannedMoveToRoot = entry.MoveToRoot;
                            entry.NewName = normalized;
                            entry.TargetPath = Path.Combine(entry.TargetFolder, normalized);
                            entry.ScannedStamp = CaptureStamp(file);
                            bool unchanged = String.Equals(entry.SourcePath, entry.TargetPath, StringComparison.Ordinal);
                            SetStatus(entry, unchanged
                                ? EntryStatus.Unchanged : EntryStatus.Ready,
                                unchanged ? "이미 올바른 이름입니다." : entry.MoveToRoot
                                    ? "이름을 정리하고 추가한 폴더로 이동합니다." : "변경할 수 있습니다.");
                        }
                        catch (Exception ex) { SetStatus(entry, EntryStatus.Error, FriendlyError(ex)); }
                    }
                }
                catch (Exception ex) { result.Warnings.Add(folder + " : " + FriendlyError(ex)); }
                if (!recursive) continue;
                try
                {
                    foreach (string child in Directory.GetDirectories(folder).OrderBy(x => x, PathComparer))
                    {
                        try
                        {
                            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                            {
                                result.Warnings.Add(child + " : 링크 폴더는 탐색하지 않습니다.");
                                continue;
                            }
                            folders.Enqueue(child);
                        }
                        catch (Exception ex) { result.Warnings.Add(child + " : " + FriendlyError(ex)); }
                    }
                }
                catch (Exception ex) { result.Warnings.Add(folder + " : " + FriendlyError(ex)); }
            }
            PlanUniqueTargets(result.Entries, false);
            result.Entries = result.Entries.OrderBy(x => x.SourcePath, PathComparer).ThenBy(x => x.SourcePath, StringComparer.Ordinal).ToList();
            return result;
        }

        public static BatchResult Apply(IEnumerable<RenameEntry> selected, string historyDirectory)
        {
            BatchResult result = new BatchResult();
            result.Entries = (selected ?? Enumerable.Empty<RenameEntry>()).Where(x => x != null)
                .OrderBy(x => x.SourcePath, PathComparer).ToList();
            List<RenameEntry> ready = new List<RenameEntry>();
            foreach (RenameEntry entry in result.Entries)
            {
                if (entry.Status != EntryStatus.Ready) continue;
                try
                {
                    string source = Path.GetFullPath(entry.SourcePath);
                    string normalized = NormalizeName(Path.GetFileName(source));
                    if (normalized == null) throw new IOException("변경 가능한 파일명 형식이 아닙니다.");
                    entry.SourcePath = source;
                    entry.Folder = FullFolderPath(Path.GetDirectoryName(source));
                    entry.OriginalName = Path.GetFileName(source);
                    ValidateEntryDestination(entry);
                    string previewTarget = String.IsNullOrEmpty(entry.TargetPath)
                        ? Path.Combine(entry.TargetFolder, normalized) : Path.GetFullPath(entry.TargetPath);
                    if (!PathComparer.Equals(entry.TargetFolder, FullFolderPath(Path.GetDirectoryName(previewTarget)))
                        || !IsAllowedTargetName(entry.OriginalName, Path.GetFileName(previewTarget)))
                        throw new IOException("미리보기의 결과 이름이 올바르지 않습니다. 다시 불러와 주세요.");
                    entry.NewName = Path.GetFileName(previewTarget);
                    entry.TargetPath = previewTarget;
                    EnsureEntryFolders(entry);
                    FileStamp current = CaptureStamp(source);
                    if (entry.ScannedStamp != null && !entry.ScannedStamp.Matches(current))
                        throw new IOException("미리보기 후 파일이 변경되었습니다. 다시 불러와 주세요.");
                    entry.ScannedStamp = current;
                    if (String.Equals(source, entry.TargetPath, StringComparison.Ordinal))
                        SetStatus(entry, EntryStatus.Unchanged, "이미 올바른 이름입니다.");
                    else ready.Add(entry);
                }
                catch (Exception ex) { SetStatus(entry, EntryStatus.Error, FriendlyError(ex)); }
            }
            foreach (IGrouping<string, RenameEntry> duplicate in ready.GroupBy(x => x.SourcePath, PathComparer).Where(x => x.Count() > 1))
                foreach (RenameEntry entry in duplicate) SetStatus(entry, EntryStatus.Conflict, "같은 파일이 중복 선택되었습니다.");
            PlanUniqueTargets(ready, true);
            ready = ready.Where(x => x.Status == EntryStatus.Ready).ToList();
            if (ready.Count == 0)
            {
                result.Failed = result.Entries.Count(x => x.Status == EntryStatus.Error || x.Status == EntryStatus.Conflict);
                result.Message = result.Failed > 0 ? "충돌 또는 오류가 있는 파일을 확인해 주세요." : "변경할 파일이 없습니다.";
                return result;
            }
            bool gatheredFiles = false;
            try
            {
                string history = PrepareHistoryDirectory(historyDirectory);
                using (FileStream historyLock = AcquireHistoryLock(history))
                {
                    string journalPath = Path.Combine(history, JournalPrefix + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-" + Guid.NewGuid().ToString("N") + ".json");
                    RenameJournal journal = new RenameJournal { Version = 1, CreatedUtc = DateTime.UtcNow.ToString("o") };
                    result.HistoryPath = journalPath;
                    bool stop = false;
                    bool journalWriteFailed = false;
                    Action persistJournal = delegate
                    {
                        try { SaveJournal(journalPath, journal); }
                        catch { journalWriteFailed = true; throw; }
                    };
                    HashSet<string> protectedRoots = new HashSet<string>(PathComparer);
                    foreach (RenameEntry scanned in result.Entries)
                    {
                        foreach (string root in scanned.ScannedProtectedRoots ?? new string[0]) protectedRoots.Add(root);
                        if (scanned.ScannedRootFolder != null) protectedRoots.Add(scanned.ScannedRootFolder);
                    }
                    HashSet<string> reservedTargets = new HashSet<string>(ready.Select(x => x.TargetPath), PathComparer);
                    foreach (RenameEntry entry in ready)
                    {
                        if (stop) { SetStatus(entry, EntryStatus.Error, "변경 기록을 저장할 수 없어 작업을 중단했습니다."); continue; }
                        RenameJournalItem item = null;
                        reservedTargets.Remove(entry.TargetPath);
                        try
                        {
                            ValidateEntryDestination(entry);
                            EnsureEntryFolders(entry);
                            FileStamp current = CaptureStamp(entry.SourcePath);
                            if (!entry.ScannedStamp.Matches(current)) throw new IOException("미리보기 후 파일이 변경되었습니다. 다시 불러와 주세요.");
                            ChooseUniqueTarget(entry, reservedTargets, true);
                            item = new RenameJournalItem
                            {
                                SourcePath = entry.SourcePath,
                                TargetPath = entry.TargetPath,
                                TemporaryPath = NewTemporaryPath(entry.Folder),
                                RootFolder = entry.RootFolder,
                                TargetFolder = entry.TargetFolder,
                                MoveToRoot = entry.MoveToRoot,
                                Stamp = current,
                                State = "Pending",
                                Error = ""
                            };
                            journal.Items.Add(item);
                            // The persisted source, destination, temporary path and identity are enough
                            // to recover even if the process stops immediately after either move.
                            persistJournal();
                            File.Move(item.SourcePath, item.TemporaryPath);
                            item.State = "Staged";
                            persistJournal();
                            for (int attempt = 0; ; attempt++)
                            {
                                EnsureEntryFolders(entry);
                                if (!item.Stamp.Matches(CaptureStamp(item.TemporaryPath))) throw new IOException("작업 도중 파일이 변경되었습니다.");
                                // A file may arrive after preview or even after staging. Reserve
                                // every other selected preview name and persist the new destination
                                // before trying the non-overwriting move again.
                                ChooseUniqueTarget(entry, reservedTargets, false);
                                item.TargetPath = entry.TargetPath;
                                persistJournal();
                                try { File.Move(item.TemporaryPath, item.TargetPath); break; }
                                catch (Exception moveError)
                                {
                                    if (!(moveError is IOException) && !(moveError is UnauthorizedAccessException)) throw;
                                    if (attempt >= 999 || !PathExists(item.TargetPath) || !File.Exists(item.TemporaryPath)) throw;
                                }
                            }
                            item.State = "Applied";
                            SetStatus(entry, EntryStatus.Done, entry.MoveToRoot
                                ? (entry.CollisionNumbered ? "변경·이동 완료 · 번호 추가" : "변경·이동 완료")
                                : entry.CollisionNumbered ? "변경 완료 · 번호 추가" : "변경 완료");
                            result.Changed++;
                            if (item.MoveToRoot) gatheredFiles = true;
                            try { persistJournal(); }
                            catch (Exception ex)
                            {
                                entry.Message = "변경 완료 · 기록 상태 저장 실패: " + FriendlyError(ex);
                                stop = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            string message = FriendlyError(ex);
                            if (item != null)
                            {
                                try
                                {
                                    // Roll back only our exact file; never replace a newly created source.
                                    if (File.Exists(item.TemporaryPath))
                                    {
                                        EnsureOrdinaryFolder(entry.Folder);
                                        if (!item.Stamp.Matches(CaptureStamp(item.TemporaryPath)))
                                            throw new IOException("임시 파일이 변경·교체되어 자동 복구하지 않았습니다.");
                                        if (PathExists(item.SourcePath)) throw new IOException("원래 이름이 사용 중입니다.");
                                        File.Move(item.TemporaryPath, item.SourcePath);
                                    }
                                    item.State = "Failed";
                                }
                                catch (Exception rollbackError)
                                {
                                    item.State = "RecoveryNeeded";
                                    message += " 복구 대기: " + item.TemporaryPath + " (" + FriendlyError(rollbackError) + ")";
                                }
                                item.Error = message;
                                try { persistJournal(); }
                                catch { stop = true; }
                            }
                            SetStatus(entry, EntryStatus.Error, message);
                        }
                        finally { reservedTargets.Add(entry.TargetPath); }
                    }
                    journal.Completed = true;
                    bool finalJournalSaved = false;
                    try { persistJournal(); finalJournalSaved = true; }
                    catch (Exception ex) { result.Message = "변경 기록 저장 오류: " + FriendlyError(ex); }
                    if (gatheredFiles)
                    {
                        // Folder removal is optional post-processing. Keep all recovery paths
                        // untouched unless every journal write and the final flush succeeded.
                        if (finalJournalSaved && !journalWriteFailed && !stop
                            && !journal.Items.Any(x => x.State == "RecoveryNeeded"))
                            RemoveEmptyGatheredSourceFolders(journal.Items, protectedRoots, result);
                        else result.CleanupWarnings.Add("변경 기록 또는 복구 상태를 확인할 수 없어 빈 폴더 정리를 건너뛰었습니다.");
                    }
                }
            }
            catch (Exception ex)
            {
                foreach (RenameEntry entry in ready.Where(x => x.Status == EntryStatus.Ready))
                    SetStatus(entry, EntryStatus.Error, "변경 기록을 준비하지 못했습니다: " + FriendlyError(ex));
                result.Message = "변경 기록을 준비하지 못했습니다: " + FriendlyError(ex);
            }
            result.Failed = result.Entries.Count(x => x.Status == EntryStatus.Error || x.Status == EntryStatus.Conflict);
            if (String.IsNullOrEmpty(result.Message)) result.Message = String.Format("{0}개 변경 완료, {1}개 충돌 또는 오류", result.Changed, result.Failed);
            if (gatheredFiles) result.Message += String.Format(" · 빈 폴더 {0}개 삭제", result.RemovedDirectories);
            if (result.CleanupWarnings.Count > 0) result.Message += String.Format(" · 폴더 정리 경고 {0}개", result.CleanupWarnings.Count);
            return result;
        }

        private static void RemoveEmptyGatheredSourceFolders(IEnumerable<RenameJournalItem> items,
            HashSet<string> protectedRoots, BatchResult result)
        {
            Dictionary<string, List<RenameJournalItem>> candidates = new Dictionary<string, List<RenameJournalItem>>(PathComparer);
            foreach (RenameJournalItem item in items.Where(x => x.MoveToRoot && x.State == "Applied"))
            {
                try
                {
                    ValidateJournalItem(item);
                    string folder = FullFolderPath(Path.GetDirectoryName(item.SourcePath));
                    // Only the original file's parent chain is eligible. Do not walk an
                    // unrelated tree or remove pre-existing empty sibling/child folders.
                    while (IsWithinFolder(item.RootFolder, folder, false))
                    {
                        if (protectedRoots.Contains(folder)) break;
                        List<RenameJournalItem> proofs;
                        if (!candidates.TryGetValue(folder, out proofs))
                        {
                            proofs = new List<RenameJournalItem>();
                            candidates.Add(folder, proofs);
                        }
                        proofs.Add(item);
                        folder = FullFolderPath(Path.GetDirectoryName(folder));
                    }
                }
                catch (Exception ex)
                {
                    result.CleanupWarnings.Add(item.SourcePath + " : " + FriendlyError(ex));
                }
            }
            foreach (KeyValuePair<string, List<RenameJournalItem>> candidate in candidates
                .OrderByDescending(x => x.Key.Length).ThenBy(x => x.Key, PathComparer))
            {
                string folder = candidate.Key;
                try
                {
                    if (protectedRoots.Contains(folder)) continue;
                    foreach (RenameJournalItem item in candidate.Value)
                    {
                        ValidateJournalItem(item);
                        if (!item.MoveToRoot || item.State != "Applied"
                            || !IsWithinFolder(item.RootFolder, folder, false)
                            || !IsWithinFolder(folder, Path.GetDirectoryName(item.SourcePath), true))
                            throw new IOException("정리할 폴더가 기록된 이동 범위 안에 있지 않습니다.");
                        EnsureJournalDestination(item);
                        if (!item.Stamp.Matches(CaptureStamp(item.TargetPath)))
                            throw new IOException("이동한 파일이 변경·교체되어 빈 폴더를 정리하지 않았습니다.");
                    }
                    if (!ExistingOrdinaryFolder(folder)) continue;
                    if (Directory.EnumerateFileSystemEntries(folder).Any()) continue;
                    // Recheck this folder and every ancestor immediately before the
                    // nonrecursive delete. A remaining file/child folder always wins.
                    EnsureOrdinaryFolder(folder);
                    try
                    {
                        Directory.Delete(folder, false);
                        result.RemovedDirectories++;
                    }
                    catch (DirectoryNotFoundException) { }
                    catch (IOException ex)
                    {
                        // A file/child folder can arrive after the emptiness check.
                        if ((ex.HResult & 0xFFFF) != 145) throw;
                    }
                }
                catch (Exception ex)
                {
                    result.CleanupWarnings.Add(folder + " : " + FriendlyError(ex));
                }
            }
        }

        public static bool HasUndo(string historyDirectory)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(historyDirectory) || !Directory.Exists(historyDirectory)) return false;
                string history = Path.GetFullPath(historyDirectory);
                EnsureOrdinaryFolder(history);
                foreach (string path in JournalPaths(history))
                {
                    RenameJournal journal;
                    if (!TryLoadJournal(path, out journal)) continue;
                    if (journal.Items.Any(IsOutstanding)) return true;
                }
            }
            catch { }
            return false;
        }

        public static BatchResult UndoLast(string historyDirectory)
        {
            BatchResult result = new BatchResult();
            try
            {
                if (String.IsNullOrWhiteSpace(historyDirectory) || !Directory.Exists(historyDirectory))
                {
                    result.Message = "되돌릴 작업이 없습니다.";
                    return result;
                }
                string history = Path.GetFullPath(historyDirectory);
                EnsureOrdinaryFolder(history);
                using (FileStream historyLock = AcquireHistoryLock(history))
                {
                    RenameJournal journal = null;
                    string journalPath = null;
                    foreach (string path in JournalPaths(history))
                    {
                        RenameJournal candidate;
                        if (!TryLoadJournal(path, out candidate)) continue;
                        if (!candidate.Items.Any(IsOutstanding)) continue;
                        journal = candidate;
                        journalPath = path;
                        break;
                    }
                    if (journal == null) { result.Message = "되돌릴 작업이 없습니다."; return result; }
                    result.HistoryPath = journalPath;
                    bool stop = false;
                    foreach (RenameJournalItem item in journal.Items.AsEnumerable().Reverse().Where(IsOutstanding))
                    {
                        RenameEntry entry = NewEntry(item.TargetPath);
                        entry.TargetPath = item.SourcePath;
                        entry.TargetFolder = FullFolderPath(Path.GetDirectoryName(item.SourcePath));
                        entry.RootFolder = item.RootFolder;
                        entry.NewName = Path.GetFileName(item.SourcePath);
                        result.Entries.Add(entry);
                        if (stop) { SetStatus(entry, EntryStatus.Error, "변경 기록을 저장할 수 없어 복구를 중단했습니다."); result.Failed++; continue; }
                        string undoOriginalLocation = null;
                        string stateBeforeUndo = item.State;
                        bool stagedByThisUndo = false;
                        try
                        {
                            ValidateJournalItem(item);
                            EnsureJournalDestination(item);
                            string location = FindRecordedFile(item);
                            if (location == null) throw new IOException("기록된 파일이 없거나 다른 파일로 교체·수정되었습니다.");
                            if (String.Equals(location, item.SourcePath, StringComparison.Ordinal))
                            {
                                // Pending / crash-recovery entries may already be at their original name.
                                item.State = "Undone";
                                SaveJournal(journalPath, journal);
                                SetStatus(entry, EntryStatus.Unchanged, "이미 원래 이름으로 복구되어 있습니다.");
                                continue;
                            }
                            EnsureUndoSourceFolder(item);
                            if (!PathComparer.Equals(location, item.SourcePath) && PathExists(item.SourcePath))
                                throw new IOException("원래 이름의 파일 또는 폴더가 이미 있어 되돌리지 않았습니다.");
                            item.State = "UndoPending";
                            SaveJournal(journalPath, journal);
                            undoOriginalLocation = location;
                            // Reuse the recorded temporary path; it is persisted before every move.
                            if (!String.Equals(location, item.TemporaryPath, StringComparison.Ordinal))
                            {
                                if (PathExists(item.TemporaryPath)) throw new IOException("복구용 임시 경로가 사용 중입니다.");
                                if (!item.Stamp.Matches(CaptureStamp(location))) throw new IOException("복구 도중 파일이 변경되었습니다.");
                                EnsureOrdinaryFolder(Path.GetDirectoryName(location));
                                EnsureOrdinaryFolder(Path.GetDirectoryName(item.TemporaryPath));
                                File.Move(location, item.TemporaryPath);
                                stagedByThisUndo = true;
                                location = item.TemporaryPath;
                                item.State = "UndoStaged";
                                SaveJournal(journalPath, journal);
                            }
                            if (PathExists(item.SourcePath)) throw new IOException("원래 이름의 파일 또는 폴더가 이미 있어 되돌리지 않았습니다.");
                            if (!item.Stamp.Matches(CaptureStamp(location))) throw new IOException("복구 도중 파일이 변경되었습니다.");
                            EnsureOrdinaryFolder(Path.GetDirectoryName(item.SourcePath));
                            File.Move(location, item.SourcePath);
                            item.State = "Undone";
                            item.Error = "";
                            result.Changed++;
                            SetStatus(entry, EntryStatus.Done, "원래 이름으로 복구 완료");
                            try { SaveJournal(journalPath, journal); }
                            catch (Exception ex) { entry.Message += " · 기록 상태 저장 실패: " + FriendlyError(ex); stop = true; }
                        }
                        catch (Exception ex)
                        {
                            item.Error = FriendlyError(ex);
                            if (stagedByThisUndo)
                            {
                                try
                                {
                                    if (File.Exists(item.TemporaryPath))
                                    {
                                        EnsureOrdinaryFolder(Path.GetDirectoryName(item.TemporaryPath));
                                        EnsureOrdinaryFolder(Path.GetDirectoryName(undoOriginalLocation));
                                        if (!item.Stamp.Matches(CaptureStamp(item.TemporaryPath)))
                                            throw new IOException("임시 파일이 변경·교체되어 자동 복구하지 않았습니다.");
                                        if (PathExists(undoOriginalLocation)) throw new IOException("변경된 이름이 사용 중입니다.");
                                        File.Move(item.TemporaryPath, undoOriginalLocation);
                                        item.State = stateBeforeUndo;
                                    }
                                }
                                catch (Exception rollbackError)
                                {
                                    item.State = "RecoveryNeeded";
                                    item.Error += " 복구 대기: " + item.TemporaryPath + " (" + FriendlyError(rollbackError) + ")";
                                }
                            }
                            try { SaveJournal(journalPath, journal); }
                            catch { stop = true; }
                            SetStatus(entry, EntryStatus.Error, item.Error);
                            result.Failed++;
                        }
                    }
                    result.Message = String.Format("{0}개 복구 완료, {1}개 충돌 또는 오류", result.Changed, result.Failed);
                }
            }
            catch (Exception ex) { result.Failed++; result.Message = "되돌리기 오류: " + FriendlyError(ex); }
            return result;
        }

        private static RenameEntry NewEntry(string file)
        {
            return new RenameEntry
            {
                SourcePath = file, TargetPath = file, OriginalName = Path.GetFileName(file), NewName = "",
                Folder = FullFolderPath(Path.GetDirectoryName(file)), TargetFolder = FullFolderPath(Path.GetDirectoryName(file)),
                Status = EntryStatus.Skipped, Message = "", Selected = false
            };
        }

        private static void SetStatus(RenameEntry entry, EntryStatus status, string message)
        {
            entry.Status = status;
            entry.Message = message;
            entry.Selected = status == EntryStatus.Ready;
        }

        private static void PlanUniqueTargets(IEnumerable<RenameEntry> entries, bool preservePreview)
        {
            List<RenameEntry> candidates = entries.Where(x => x.Status == EntryStatus.Ready)
                .OrderBy(x => x.SourcePath, PathComparer).ThenBy(x => x.SourcePath, StringComparer.Ordinal).ToList();
            Dictionary<string, int> pending = new Dictionary<string, int>(PathComparer);
            if (preservePreview)
                foreach (IGrouping<string, RenameEntry> group in candidates.GroupBy(x => x.TargetPath, PathComparer))
                    pending.Add(group.Key, group.Count());
            HashSet<string> assigned = new HashSet<string>(PathComparer);
            HashSet<string> reserved = new HashSet<string>(pending.Keys, PathComparer);
            foreach (RenameEntry entry in candidates)
            {
                string preview = entry.TargetPath;
                if (preservePreview)
                {
                    pending[preview]--;
                    if (pending[preview] == 0 && !assigned.Contains(preview)) reserved.Remove(preview);
                }
                try
                {
                    ChooseUniqueTarget(entry, reserved, true);
                    assigned.Add(entry.TargetPath);
                    reserved.Add(entry.TargetPath);
                }
                catch (Exception ex) { SetStatus(entry, EntryStatus.Error, FriendlyError(ex)); }
            }
        }

        private static void ChooseUniqueTarget(RenameEntry entry, HashSet<string> reserved, bool allowSource)
        {
            if (TargetAvailable(allowSource ? entry.SourcePath : null, entry.TargetPath, reserved, entry.ScannedStamp)) return;
            string normalized = NormalizeName(entry.OriginalName);
            if (normalized == null) throw new IOException("변경 가능한 파일명 형식이 아닙니다.");
            string baseName = WithoutCollisionSuffix(normalized);
            string extension = Path.GetExtension(baseName);
            string stem = baseName.Substring(0, baseName.Length - extension.Length);
            for (long number = 1; number <= Int32.MaxValue; number++)
            {
                string candidate = Path.Combine(entry.TargetFolder,
                    stem + " (" + number.ToString(CultureInfo.InvariantCulture) + ")" + extension);
                if (!TargetAvailable(allowSource ? entry.SourcePath : null, candidate, reserved, entry.ScannedStamp)) continue;
                entry.TargetPath = candidate;
                entry.NewName = Path.GetFileName(candidate);
                entry.CollisionNumbered = true;
                entry.Message = "이름이 겹쳐 번호를 추가합니다.";
                return;
            }
            throw new IOException("사용할 수 있는 번호 이름을 찾지 못했습니다.");
        }

        private static bool TargetAvailable(string source, string target, HashSet<string> reserved, FileStamp expectedSource)
        {
            if (reserved != null && reserved.Contains(target)) return false;
            if (!PathExists(target)) return true;
            if (source == null || !PathComparer.Equals(source, target)) return false;
            FileStamp actualSource = CaptureStamp(source);
            return (expectedSource == null || expectedSource.Matches(actualSource))
                && actualSource.Matches(CaptureStamp(target));
        }

        private static string WithoutCollisionSuffix(string normalizedName)
        {
            string extension = Path.GetExtension(normalizedName);
            string stem = normalizedName.Substring(0, normalizedName.Length - extension.Length);
            Match match = CodePattern.Match(stem);
            return match.Success ? stem.Substring(0, stem.Length - match.Groups["duplicate"].Length) + extension : normalizedName;
        }

        private static bool IsAllowedTargetName(string sourceName, string targetName)
        {
            string normalized = NormalizeName(sourceName);
            if (normalized == null || !String.Equals(NormalizeName(targetName), targetName, StringComparison.Ordinal)) return false;
            if (String.Equals(normalized, targetName, StringComparison.Ordinal)) return true;
            return !String.Equals(WithoutCollisionSuffix(targetName), targetName, StringComparison.Ordinal)
                && String.Equals(WithoutCollisionSuffix(normalized), WithoutCollisionSuffix(targetName), StringComparison.Ordinal);
        }

        private static bool PathExists(string path)
        {
            try { File.GetAttributes(path); return true; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }

        private static string FullFolderPath(string path)
        {
            string full = Path.GetFullPath(path);
            string root = Path.GetPathRoot(full);
            string trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return PathComparer.Equals(trimmed, root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                ? root : trimmed;
        }

        private static bool IsWithinFolder(string root, string folder, bool allowEqual)
        {
            string fullRoot = FullFolderPath(root);
            string fullFolder = FullFolderPath(folder);
            if (PathComparer.Equals(fullRoot, fullFolder)) return allowEqual;
            string prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
            return fullFolder.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateEntryDestination(RenameEntry entry)
        {
            string sourceFolder = FullFolderPath(Path.GetDirectoryName(entry.SourcePath));
            string root = String.IsNullOrWhiteSpace(entry.RootFolder) ? sourceFolder : FullFolderPath(entry.RootFolder);
            string targetFolder = String.IsNullOrWhiteSpace(entry.TargetFolder) ? sourceFolder : FullFolderPath(entry.TargetFolder);
            bool moving = !PathComparer.Equals(sourceFolder, targetFolder);
            if (entry.ScannedRootFolder != null)
            {
                if (!PathComparer.Equals(root, entry.ScannedRootFolder)
                    || !PathComparer.Equals(targetFolder, entry.ScannedTargetFolder)
                    || entry.MoveToRoot != entry.ScannedMoveToRoot)
                    throw new IOException("미리보기의 이동 위치가 변경되었습니다. 다시 불러와 주세요.");
            }
            else if (moving || entry.MoveToRoot)
                throw new IOException("폴더 이동은 미리보기에서 승인된 위치로만 할 수 있습니다.");
            if (!IsWithinFolder(root, sourceFolder, true) || entry.MoveToRoot != moving
                || (moving && (!PathComparer.Equals(root, targetFolder) || !IsWithinFolder(root, sourceFolder, false))))
                throw new IOException("파일을 추가한 폴더 밖이나 다른 하위 폴더로 이동할 수 없습니다.");
            entry.RootFolder = root;
            entry.TargetFolder = targetFolder;
            entry.MoveToRoot = moving;
        }

        private static void EnsureEntryFolders(RenameEntry entry)
        {
            EnsureOrdinaryFolder(entry.Folder);
            EnsureOrdinaryFolder(entry.RootFolder);
            EnsureOrdinaryFolder(entry.TargetFolder);
        }

        private static bool ExistingOrdinaryFolder(string folder)
        {
            if (Directory.Exists(folder))
            {
                EnsureOrdinaryFolder(folder);
                return true;
            }
            DirectoryInfo ancestor = new DirectoryInfo(FullFolderPath(folder));
            while (ancestor != null && !ancestor.Exists)
            {
                if (PathExists(ancestor.FullName)) throw new IOException("복구 폴더 경로에 다른 파일이 있습니다.");
                ancestor = ancestor.Parent;
            }
            if (ancestor != null) EnsureOrdinaryFolder(ancestor.FullName);
            return false;
        }

        private static void EnsureJournalDestination(RenameJournalItem item)
        {
            if (item.MoveToRoot)
            {
                EnsureOrdinaryFolder(item.RootFolder);
                EnsureOrdinaryFolder(item.TargetFolder);
            }
            else EnsureOrdinaryFolder(Path.GetDirectoryName(item.SourcePath));
        }

        private static void EnsureUndoSourceFolder(RenameJournalItem item)
        {
            string sourceFolder = FullFolderPath(Path.GetDirectoryName(item.SourcePath));
            if (!item.MoveToRoot)
            {
                EnsureOrdinaryFolder(sourceFolder);
                return;
            }
            // Recreate only original descendants of the still-existing recorded root.
            // Validate each existing ancestor before creating a missing segment, and
            // leave created/empty folders in place even if a later file move fails.
            EnsureOrdinaryFolder(item.RootFolder);
            if (!IsWithinFolder(item.RootFolder, sourceFolder, false))
                throw new IOException("복구할 하위 폴더가 기록된 기준 폴더 안에 있지 않습니다.");
            Stack<DirectoryInfo> missing = new Stack<DirectoryInfo>();
            DirectoryInfo ancestor = new DirectoryInfo(sourceFolder);
            while (!ancestor.Exists)
            {
                if (!IsWithinFolder(item.RootFolder, ancestor.FullName, false) || PathExists(ancestor.FullName))
                    throw new IOException("복구 폴더 경로를 안전하게 만들 수 없습니다.");
                missing.Push(ancestor);
                ancestor = ancestor.Parent;
                if (ancestor == null) throw new IOException("복구 폴더의 상위 경로를 찾을 수 없습니다.");
            }
            EnsureOrdinaryFolder(ancestor.FullName);
            while (missing.Count > 0)
            {
                DirectoryInfo child = missing.Pop();
                EnsureOrdinaryFolder(child.Parent.FullName);
                if (PathExists(child.FullName)) EnsureOrdinaryFolder(child.FullName);
                else Directory.CreateDirectory(child.FullName);
                EnsureOrdinaryFolder(child.FullName);
            }
            EnsureOrdinaryFolder(sourceFolder);
        }

        private static void EnsureOrdinaryFolder(string folder)
        {
            DirectoryInfo current = new DirectoryInfo(Path.GetFullPath(folder));
            while (current != null)
            {
                FileAttributes attributes = File.GetAttributes(current.FullName);
                if ((attributes & FileAttributes.Directory) == 0) throw new IOException("폴더 경로가 아닙니다.");
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("링크 또는 연결된 폴더는 처리하지 않습니다.");
                current = current.Parent;
            }
        }

        private static string NewTemporaryPath(string folder)
        {
            string path;
            do { path = Path.Combine(folder, ".__filename_normalizer_" + Guid.NewGuid().ToString("N") + ".tmp"); }
            while (PathExists(path));
            return path;
        }

        private static string PrepareHistoryDirectory(string directory)
        {
            if (String.IsNullOrWhiteSpace(directory)) throw new ArgumentException("변경 기록 폴더가 지정되지 않았습니다.");
            string full = Path.GetFullPath(directory);
            // Check an existing ancestor before CreateDirectory, so a junction cannot redirect writes.
            DirectoryInfo ancestor = new DirectoryInfo(full);
            while (ancestor != null && !ancestor.Exists) ancestor = ancestor.Parent;
            if (ancestor != null) EnsureOrdinaryFolder(ancestor.FullName);
            Directory.CreateDirectory(full);
            EnsureOrdinaryFolder(full);
            return full;
        }

        private static FileStream AcquireHistoryLock(string history)
        {
            string path = Path.Combine(history, "rename-history.lock");
            if (PathExists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("변경 기록 잠금 파일이 링크입니다.");
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) { throw new IOException("다른 작업이 실행 중이거나 변경 기록을 잠글 수 없습니다.", ex); }
        }

        private static IEnumerable<string> JournalPaths(string history)
        {
            return Directory.GetFiles(history, JournalPrefix + "*.json")
                .OrderByDescending(x => Path.GetFileName(x), StringComparer.Ordinal);
        }

        private static void SaveJournal(string path, RenameJournal journal)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue, RecursionLimit = 100 };
            byte[] bytes = Utf8.GetBytes(serializer.Serialize(journal));
            if (bytes.Length > 64 * 1024 * 1024) throw new IOException("한 번에 처리할 수 있는 변경 기록 크기를 초과했습니다. 폴더를 나누어 처리해 주세요.");
            // Appending a suffix to the already long batch name can exceed Framework MAX_PATH
            // even when the actual journal path is valid. Keep this sibling name independent.
            string temporary = Path.Combine(Path.GetDirectoryName(path), ".j-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (PathExists(path))
                {
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("변경 기록 파일이 링크입니다.");
                    File.Replace(temporary, path, null);
                }
                else File.Move(temporary, path);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch { }
            }
        }

        private static bool TryLoadJournal(string path, out RenameJournal journal)
        {
            journal = null;
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
                FileInfo info = new FileInfo(path);
                if (info.Length > 64L * 1024L * 1024L) return false;
                JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024, RecursionLimit = 100 };
                journal = serializer.Deserialize<RenameJournal>(File.ReadAllText(path, Utf8));
                if (journal == null || journal.Version != 1 || journal.Items == null) return false;
                foreach (RenameJournalItem item in journal.Items) ValidateJournalItem(item);
                return true;
            }
            catch { journal = null; return false; }
        }

        private static void ValidateJournalItem(RenameJournalItem item)
        {
            if (item == null || item.Stamp == null || String.IsNullOrEmpty(item.SourcePath) || String.IsNullOrEmpty(item.TargetPath)
                || String.IsNullOrEmpty(item.TemporaryPath)) throw new IOException("변경 기록 형식이 올바르지 않습니다.");
            string folder = FullFolderPath(Path.GetDirectoryName(Path.GetFullPath(item.SourcePath)));
            string targetFolder = FullFolderPath(Path.GetDirectoryName(Path.GetFullPath(item.TargetPath)));
            if (!String.Equals(item.SourcePath, Path.GetFullPath(item.SourcePath), StringComparison.Ordinal)
                || !String.Equals(item.TargetPath, Path.GetFullPath(item.TargetPath), StringComparison.Ordinal)
                || !String.Equals(item.TemporaryPath, Path.GetFullPath(item.TemporaryPath), StringComparison.Ordinal)
                || !PathComparer.Equals(folder, FullFolderPath(Path.GetDirectoryName(item.TemporaryPath)))
                || !Path.GetFileName(item.TemporaryPath).StartsWith(".__filename_normalizer_", StringComparison.Ordinal)
                || !IsAllowedTargetName(Path.GetFileName(item.SourcePath), Path.GetFileName(item.TargetPath)))
                throw new IOException("변경 기록 경로가 올바르지 않습니다.");
            bool moving = !PathComparer.Equals(folder, targetFolder);
            if (moving != item.MoveToRoot) throw new IOException("변경 기록의 이동 정보가 올바르지 않습니다.");
            if (!String.IsNullOrEmpty(item.TargetFolder)
                && (!String.Equals(item.TargetFolder, FullFolderPath(item.TargetFolder), StringComparison.Ordinal)
                    || !PathComparer.Equals(item.TargetFolder, targetFolder)))
                throw new IOException("변경 기록의 도착 폴더가 올바르지 않습니다.");
            if (moving)
            {
                if (String.IsNullOrEmpty(item.RootFolder) || String.IsNullOrEmpty(item.TargetFolder)
                    || !String.Equals(item.RootFolder, FullFolderPath(item.RootFolder), StringComparison.Ordinal)
                    || !PathComparer.Equals(item.RootFolder, targetFolder)
                    || !IsWithinFolder(item.RootFolder, folder, false))
                    throw new IOException("변경 기록의 이동 범위가 올바르지 않습니다.");
            }
            else if (!String.IsNullOrEmpty(item.RootFolder)
                && (!String.Equals(item.RootFolder, FullFolderPath(item.RootFolder), StringComparison.Ordinal)
                    || !IsWithinFolder(item.RootFolder, folder, true)))
                throw new IOException("변경 기록의 기준 폴더가 올바르지 않습니다.");
        }

        private static bool IsOutstanding(RenameJournalItem item)
        {
            if (item == null || item.State == "Undone") return false;
            if (item.State == "Applied" || item.State == "UndoPending" || item.State == "UndoStaged" || item.State == "RecoveryNeeded") return true;
            // Pending/Staged/Failed may describe a crash between moving and persisting state.
            try
            {
                string location = FindRecordedFile(item);
                return location != null && !String.Equals(location, item.SourcePath, StringComparison.Ordinal);
            }
            catch { return item.State == "Staged"; }
        }

        private static string FindRecordedFile(RenameJournalItem item)
        {
            ValidateJournalItem(item);
            if (item.MoveToRoot) EnsureOrdinaryFolder(item.RootFolder);
            // Windows resolves differently-cased strings to the same file. Enumerating the actual
            // name is required to distinguish a completed case-only rename from its original name.
            Dictionary<string, string[]> directoryFiles = new Dictionary<string, string[]>(PathComparer);
            foreach (string expected in new string[] { item.TemporaryPath, item.TargetPath, item.SourcePath })
            {
                string folder = FullFolderPath(Path.GetDirectoryName(expected));
                string[] actualFiles;
                if (!directoryFiles.TryGetValue(folder, out actualFiles))
                {
                    actualFiles = ExistingOrdinaryFolder(folder) ? Directory.GetFiles(folder) : new string[0];
                    directoryFiles.Add(folder, actualFiles);
                }
                string actual = actualFiles.FirstOrDefault(x => String.Equals(Path.GetFileName(x), Path.GetFileName(expected), StringComparison.Ordinal));
                if (actual == null) continue;
                // The folder has already been matched separately. Keep its recorded
                // spelling while matching the actual leaf case for case-only undo.
                try { if (item.Stamp.Matches(CaptureStamp(expected))) return expected; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (Win32Exception) { }
            }
            return null;
        }

        private static string FriendlyError(Exception ex)
        {
            if (ex is UnauthorizedAccessException) return "접근 권한이 없습니다.";
            if (ex is PathTooLongException) return "파일 경로가 너무 깁니다.";
            if (ex is FileNotFoundException || ex is DirectoryNotFoundException) return "파일 또는 폴더를 찾을 수 없습니다.";
            return ex.Message;
        }

        private static FileStamp CaptureStamp(string path)
        {
            using (SafeFileHandle handle = NativeMethods.CreateFile(path, 0, 7, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero))
            {
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                NativeMethods.ByHandleFileInformation info;
                if (!NativeMethods.GetFileInformationByHandle(handle, out info)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if ((info.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0) throw new IOException("링크 파일은 처리하지 않습니다.");
                if ((info.FileAttributes & (uint)FileAttributes.Directory) != 0) throw new IOException("파일 경로가 아닙니다.");
                return new FileStamp
                {
                    Length = ((long)info.FileSizeHigh << 32) | info.FileSizeLow,
                    LastWriteUtcTicks = DateTime.FromFileTimeUtc(((long)info.LastWriteTime.dwHighDateTime << 32) | (uint)info.LastWriteTime.dwLowDateTime).Ticks,
                    CreationUtcTicks = DateTime.FromFileTimeUtc(((long)info.CreationTime.dwHighDateTime << 32) | (uint)info.CreationTime.dwLowDateTime).Ticks,
                    VolumeSerialNumber = info.VolumeSerialNumber, FileIndexHigh = info.FileIndexHigh, FileIndexLow = info.FileIndexLow
                };
            }
        }

        private static class NativeMethods
        {
            [StructLayout(LayoutKind.Sequential)]
            internal struct ByHandleFileInformation
            {
                public uint FileAttributes;
                public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
                public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
                public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
                public uint VolumeSerialNumber;
                public uint FileSizeHigh;
                public uint FileSizeLow;
                public uint NumberOfLinks;
                public uint FileIndexHigh;
                public uint FileIndexLow;
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern SafeFileHandle CreateFile(string filename, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);
        }
    }
}
