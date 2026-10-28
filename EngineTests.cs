using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace FileNameNormalizer.Tests
{
    // Runs entirely in a fresh TEMP folder. User files and drives are never scanned.
    internal static class EngineTests
    {
        private static int passed;
        private static int failed;
        private static string testRoot;

        private static int Main()
        {
            // Sandboxed TEMP is deliberately retained to cover a long, but valid,
            // journal path without requiring the atomic-write sibling to exceed MAX_PATH.
            testRoot = Path.Combine(Path.GetTempPath(), "FileNameNormalizerTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testRoot);
            try
            {
                Run("Normalization examples, extension handling and strict matching", TestNormalization);
                Run("Case-only rename, preserved contents and durable undo journal", TestApplyAndUndo);
                Run("Existing file and directory targets receive the next free number", TestExistingTargets);
                Run("Duplicate targets get deterministic names and undo independently", TestDuplicateTargets);
                Run("Applying a subset preserves its previewed numbered name", TestSelectedSubset);
                Run("Late numbered collisions preserve other selected preview names", TestLateNumberedCollision);
                Run("Numbered names are idempotent with extensions and numeric code prefixes", TestNumberedNames);
                Run("Mixed letter-number prefixes preserve collision numbering and undo", TestMixedPrefixesWithCollisionsAndUndo);
                Run("Recursion opt-in and overlapping root de-duplication", TestRecursionAndDeduplication);
                Run("Unrelated and already-normalized files remain unchanged", TestSkippedAndUnchanged);
                Run("Target created after preview is preserved and the rename gets a number", TestLateCollision);
                Run("Source removed after preview is handled safely", TestRemovedSource);
                Run("Undo refuses to overwrite a newly-created original name", TestUndoCollision);
                Run("Undo refuses a replacement file with different metadata", TestUndoReplacement);
                Run("Gather is opt-in and moves supported files with preserved metadata and undo", TestGatherOptInAndUndo);
                Run("Gather maps multiple and overlapping roots to the closest dropped root", TestGatherRootSelection);
                Run("Gather numbering preserves subset previews and late arrivals", TestGatherNumbering);
                Run("Gather undo recreates missing child folders and protects new original names", TestGatherUndoFolders);
                Run("Gather preserves unselected, unsupported and hidden files in their folders", TestGatherCleanupKeepsRemainingFiles);
                Run("Gather removes only source ancestors and protects explicit roots", TestGatherCleanupScope);
                Run("Gather cleans successful moves without removing a failed source folder", TestGatherCleanupMixedResult);
                Run("Gather-off renames leave the source folder structure in place", TestGatherOffKeepsFolders);
                Run("Gather rejects changed roots, destinations and sibling-prefix escapes", TestGatherTampering);
                Run("Legacy version-one same-folder journals remain undoable", TestLegacyJournal);
            }
            finally
            {
                // testRoot is an exact, freshly-created child of the OS TEMP directory.
                // Never delete any path supplied by the user or found in a rename journal.
                try { Directory.Delete(testRoot, true); }
                catch (Exception ex) { Console.WriteLine("Temporary test cleanup: " + ex.Message); }
            }
            Console.WriteLine("RESULT: {0} passed, {1} failed", passed, failed);
            return failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                passed++;
                Console.WriteLine("PASS: " + name);
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine("FAIL: " + name + " | " + ex);
            }
        }

        private static string NewDirectory(string label)
        {
            string path = Path.Combine(testRoot, label);
            Directory.CreateDirectory(path);
            return path;
        }

        private static string Write(string directory, string name, string contents)
        {
            string path = Path.Combine(directory, name);
            File.WriteAllText(path, contents);
            return path;
        }

        private static List<RenameEntry> Ready(ScanResult result)
        {
            List<RenameEntry> ready = result.Entries.Where(e => e.Status == EntryStatus.Ready).ToList();
            foreach (RenameEntry entry in ready) entry.Selected = true;
            return ready;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(message + " Expected: " + expected + "; actual: " + actual);
        }

        private static void TestNormalization()
        {
            string[,] examples = new string[,]
            {
                { "source.example@NOTE-444", "NOTE-444" },
                { "archive.example@PHOTO-992", "PHOTO-992" },
                { "DOC-208", "DOC-208" },
                { "doc-209", "DOC-209" },
                { "source.example@note-444.mp4", "NOTE-444.mp4" },
                { "archive.example@photo-992.MP4", "PHOTO-992.MP4" },
                { "doc-0009.srt", "DOC-0009.srt" },
                { "a@b@doc-209.mp4", "DOC-209.mp4" },
                { "a-1.txt", "A-1.txt" },
                { "source.example@259DATA-1899", "259DATA-1899" },
                { "PLAN-617ch", "PLAN-617ch" },
                { "259data-1899", "259DATA-1899" },
                { "source.example@259data-1899.mp4", "259DATA-1899.mp4" },
                { "plan-617ch.mp4", "PLAN-617ch.mp4" },
                { "plan-617CH.MP4", "PLAN-617CH.MP4" },
                { "site@000259data-001899cH.MP4", "000259DATA-001899cH.MP4" },
                { "doc-209 (1)", "DOC-209 (1)" },
                { "site@259data-1899ch (2).MP4", "259DATA-1899ch (2).MP4" },
                { "REPORT-001 (12).txt", "REPORT-001 (12).txt" },
                { "source.example@T38-073", "T38-073" },
                { "source.example@t38-073.PDF", "T38-073.PDF" },
                { "2scan3b-0007final.docx", "2SCAN3B-0007final.docx" },
                { "scan2-004FINAL.TXT", "SCAN2-004FINAL.TXT" },
                { "source.example@t38-073ch (2).pdf", "T38-073ch (2).pdf" }
            };
            for (int index = 0; index < examples.GetLength(0); index++)
                Equal(examples[index, 1], RenameEngine.NormalizeName(examples[index, 0]), examples[index, 0]);

            string[] invalid = { "readme.txt", "DOC-208 extra.mp4", "DOC-208 PHOTO-992.mp4", "DOC_208.mp4", "DOC-２０８.mp4", "DOC–208.mp4", "209.mp4", "@.mp4", "", "259-1899.mp4", "PLAN-617 ch.mp4", "PLAN-617ch!.mp4", "PLAN-617-ch.mp4", "38-073.pdf", "T_38-073.pdf", "T 38-073.pdf", "T３８-073.pdf" };
            foreach (string name in invalid)
                Assert(RenameEngine.NormalizeName(name) == null, "Ambiguous or non-matching name must be skipped: " + name);
        }

        private static void TestApplyAndUndo()
        {
            string directory = NewDirectory("apply");
            string history = NewDirectory("apply-history");
            Write(directory, "source.example@NOTE-444", "extensionless content");
            string lower = Write(directory, "doc-209.MP4", "case-only content");
            Write(directory, "archive.example@photo-992.mp4", "prefix content");
            DateTime timestamp = new DateTime(2025, 1, 2, 3, 4, 6, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(lower, timestamp);
            Assert(!RenameEngine.HasUndo(history), "Fresh history should have no undo.");

            ScanResult scan = RenameEngine.Scan(new string[] { directory }, false);
            Equal(3, Ready(scan).Count, "Three files should be ready.");
            BatchResult result = RenameEngine.Apply(Ready(scan), history);
            Equal(3, result.Changed, "Three renames should succeed. " + Describe(result));
            Equal(0, result.Failed, "No rename should fail.");
            Equal("extensionless content", File.ReadAllText(Path.Combine(directory, "NOTE-444")), "No-extension content.");
            Equal("prefix content", File.ReadAllText(Path.Combine(directory, "PHOTO-992.mp4")), "Prefixed content.");
            string casePath = Directory.GetFiles(directory).Single(p => Path.GetFileName(p).Equals("DOC-209.MP4", StringComparison.Ordinal));
            Equal("case-only content", File.ReadAllText(casePath), "Case-only rename content.");
            Equal(timestamp, File.GetLastWriteTimeUtc(casePath), "Renames must preserve last-write time.");
            Assert(RenameEngine.HasUndo(history), "Successful changes must be undoable.");
            Assert(Directory.GetFiles(history, "*", SearchOption.AllDirectories).Length > 0, "History must exist on disk.");
            Assert(!String.IsNullOrEmpty(result.HistoryPath) && File.Exists(result.HistoryPath), "Reported history path must exist.");
            Equal(3, Directory.GetFiles(directory).Length, "Only the three payload files should remain.");
            Assert(!Directory.GetFiles(directory).Any(p => Path.GetFileName(p).StartsWith(".__filename_normalizer_", StringComparison.OrdinalIgnoreCase)), "No temporary rename artifact should remain.");

            BatchResult undo = RenameEngine.UndoLast(history);
            Equal(3, undo.Changed, "All three changes should undo.");
            Equal(0, undo.Failed, "Undo should not fail.");
            Equal("extensionless content", File.ReadAllText(Path.Combine(directory, "source.example@NOTE-444")), "Undo extensionless original.");
            Equal("prefix content", File.ReadAllText(Path.Combine(directory, "archive.example@photo-992.mp4")), "Undo prefixed original.");
            Assert(Directory.GetFiles(directory).Any(p => Path.GetFileName(p).Equals("doc-209.MP4", StringComparison.Ordinal)), "Undo must restore original case.");
            Assert(!RenameEngine.HasUndo(history), "An entirely undone batch should not remain pending.");
        }

        private static void TestExistingTargets()
        {
            string directory = NewDirectory("existing");
            string history = NewDirectory("existing-history");
            Write(directory, "site@DOC-208.mp4", "source");
            Write(directory, "DOC-208.mp4", "keep existing");
            Write(directory, "DOC-208 (1).mp4", "keep numbered");
            Write(directory, "site@DOC-209.mp4", "source next");
            Directory.CreateDirectory(Path.Combine(directory, "DOC-209.mp4"));
            Directory.CreateDirectory(Path.Combine(directory, "DOC-209 (1).mp4"));
            ScanResult result = RenameEngine.Scan(new string[] { directory }, false);
            Equal(2, Ready(result).Count, "File and directory collisions should both be ready with new names.");
            Equal("DOC-208 (2).mp4", result.Entries.Single(e => e.OriginalName == "site@DOC-208.mp4").NewName, "Existing numbered file must be skipped.");
            Equal("DOC-209 (2).mp4", result.Entries.Single(e => e.OriginalName == "site@DOC-209.mp4").NewName, "Existing numbered directory must be skipped.");
            Equal("keep existing", File.ReadAllText(Path.Combine(directory, "DOC-208.mp4")), "Existing target preserved.");
            Equal(4, Directory.GetFiles(directory).Length, "Preview must not mutate any file.");

            BatchResult changed = RenameEngine.Apply(Ready(result), history);
            Equal(2, changed.Changed, "Both colliding sources should be renamed. " + Describe(changed));
            Equal(0, changed.Failed, "No numbered rename should fail.");
            Equal("source", File.ReadAllText(Path.Combine(directory, "DOC-208 (2).mp4")), "Number must precede the extension.");
            Equal("source next", File.ReadAllText(Path.Combine(directory, "DOC-209 (2).mp4")), "Directory collision should use the next name.");
            Equal("keep existing", File.ReadAllText(Path.Combine(directory, "DOC-208.mp4")), "Existing base remains intact.");
            Equal("keep numbered", File.ReadAllText(Path.Combine(directory, "DOC-208 (1).mp4")), "Existing numbered file remains intact.");
            Assert(Directory.Exists(Path.Combine(directory, "DOC-209.mp4")) && Directory.Exists(Path.Combine(directory, "DOC-209 (1).mp4")), "Existing directories must remain intact.");
            Equal(2, RenameEngine.UndoLast(history).Changed, "Numbered changes must undo to their original names.");
            Equal("source", File.ReadAllText(Path.Combine(directory, "site@DOC-208.mp4")), "Undo restores original prefixed source.");
        }

        private static string Describe(BatchResult result)
        {
            return result.Message + " [" + String.Join("; ", result.Entries.Select(e => e.OriginalName + ": " + e.Status + " " + e.Message)) + "]";
        }

        private static void TestDuplicateTargets()
        {
            string directory = NewDirectory("duplicates");
            string history = NewDirectory("duplicates-history");
            string firstSource = Write(directory, "one@DOC-208.mp4", "first");
            string secondSource = Write(directory, "two@doc-208.mp4", "second");
            ScanResult result = RenameEngine.Scan(new string[] { directory }, false);
            Equal(2, Ready(result).Count, "All duplicate targets should receive unique names.");
            Equal("DOC-208.mp4", result.Entries.Single(e => e.SourcePath == firstSource).NewName, "First deterministic source gets the base name.");
            Equal("DOC-208 (1).mp4", result.Entries.Single(e => e.SourcePath == secondSource).NewName, "Second source gets a numbered name.");
            Equal(2, result.Entries.Select(e => e.TargetPath).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "Preview targets must be unique ignoring case.");
            ScanResult repeat = RenameEngine.Scan(new string[] { directory }, false);
            foreach (RenameEntry entry in result.Entries)
                Equal(entry.TargetPath, repeat.Entries.Single(e => e.SourcePath == entry.SourcePath).TargetPath, "Repeated previews should be stable.");

            BatchResult changed = RenameEngine.Apply(Ready(result), history);
            Equal(2, changed.Changed, "Both duplicate sources should be renamed. " + Describe(changed));
            Equal("first", File.ReadAllText(Path.Combine(directory, "DOC-208.mp4")), "Base-name contents.");
            Equal("second", File.ReadAllText(Path.Combine(directory, "DOC-208 (1).mp4")), "Numbered-name contents.");
            Equal(2, RenameEngine.UndoLast(history).Changed, "Both independently named files should undo.");
            Equal("first", File.ReadAllText(firstSource), "First original content after undo.");
            Equal("second", File.ReadAllText(secondSource), "Second original content after undo.");
        }

        private static void TestSelectedSubset()
        {
            string directory = NewDirectory("subset");
            string history = NewDirectory("subset-history");
            string firstSource = Write(directory, "one@REPORT-001.txt", "unselected");
            string secondSource = Write(directory, "two@report-001.txt", "selected");
            ScanResult scan = RenameEngine.Scan(new string[] { directory }, false);
            RenameEntry selected = scan.Entries.Single(e => e.SourcePath == secondSource);
            Equal("REPORT-001 (1).txt", selected.NewName, "Second preview should carry a number.");
            string plannedTarget = selected.TargetPath;

            BatchResult changed = RenameEngine.Apply(new RenameEntry[] { selected }, history);
            Equal(1, changed.Changed, "Selected subset should apply. " + Describe(changed));
            Equal(plannedTarget, selected.TargetPath, "A free previewed name must not shift when another entry is unselected.");
            Equal("selected", File.ReadAllText(plannedTarget), "Selected payload keeps its previewed name.");
            Equal("unselected", File.ReadAllText(firstSource), "Unselected source remains untouched.");
            Assert(!File.Exists(Path.Combine(directory, "REPORT-001.txt")), "Unselected base target must not be filled opportunistically.");
            Equal(1, RenameEngine.UndoLast(history).Changed, "Selected numbered file should undo.");
            Equal("selected", File.ReadAllText(secondSource), "Subset original should be restored.");
        }

        private static void TestLateNumberedCollision()
        {
            string directory = NewDirectory("late-numbered");
            string history = NewDirectory("late-numbered-history");
            Write(directory, "REPORT-001.txt", "base exists");
            string firstSource = Write(directory, "one@REPORT-001.txt", "first source");
            string secondSource = Write(directory, "two@report-001.txt", "second source");
            ScanResult scan = RenameEngine.Scan(new string[] { directory }, false);
            RenameEntry first = scan.Entries.Single(e => e.SourcePath == firstSource);
            RenameEntry second = scan.Entries.Single(e => e.SourcePath == secondSource);
            Equal("REPORT-001 (1).txt", first.NewName, "First numbered preview.");
            Equal("REPORT-001 (2).txt", second.NewName, "Second numbered preview.");
            Write(directory, first.NewName, "arrived after preview");

            BatchResult changed = RenameEngine.Apply(Ready(scan), history);
            Equal(2, changed.Changed, "Late collision should still permit both renames. " + Describe(changed));
            Equal("REPORT-001 (3).txt", first.NewName, "First source must skip the other selected entry's reserved preview.");
            Equal("REPORT-001 (2).txt", second.NewName, "Other selected preview should remain stable.");
            Equal("arrived after preview", File.ReadAllText(Path.Combine(directory, "REPORT-001 (1).txt")), "Late arrival remains intact.");
            Equal("first source", File.ReadAllText(first.TargetPath), "First moved payload.");
            Equal("second source", File.ReadAllText(second.TargetPath), "Second moved payload.");
            Equal(2, RenameEngine.UndoLast(history).Changed, "Late-renumbered targets must also be journaled and undoable.");
            Equal("first source", File.ReadAllText(firstSource), "First late-collision original restored.");
            Equal("second source", File.ReadAllText(secondSource), "Second original restored.");
        }

        private static void TestNumberedNames()
        {
            string directory = NewDirectory("numbered");
            string history = NewDirectory("numbered-history");
            Write(directory, "259DATA-1899ch", "extensionless existing");
            Write(directory, "259DATA-1899ch (1)", "extensionless first");
            string source = Write(directory, "source.example@259data-1899ch", "extensionless source");
            Write(directory, "REPORT-001.TXT", "text existing");
            string textSource = Write(directory, "site@report-001.TXT", "text source");
            ScanResult scan = RenameEngine.Scan(new string[] { directory }, false);
            Equal("259DATA-1899ch (2)", scan.Entries.Single(e => e.SourcePath == source).NewName, "Extensionless dotted prefix and letter suffix should survive numbering.");
            Equal("REPORT-001 (1).TXT", scan.Entries.Single(e => e.SourcePath == textSource).NewName, "Number belongs before the preserved extension.");
            BatchResult changed = RenameEngine.Apply(Ready(scan), history);
            Equal(2, changed.Changed, "Numbered files should be renamed. " + Describe(changed));
            Equal("extensionless source", File.ReadAllText(Path.Combine(directory, "259DATA-1899ch (2)")), "Extensionless numbered contents.");
            Equal("text source", File.ReadAllText(Path.Combine(directory, "REPORT-001 (1).TXT")), "Numbered text contents.");

            ScanResult after = RenameEngine.Scan(new string[] { directory }, false);
            Equal(0, Ready(after).Count, "Rescanning generated numbered names must not request another rename.");
            Equal(5, after.Entries.Count(e => e.Status == EntryStatus.Unchanged), "Generated and existing numbered names must be recognized as normalized.");
            Equal(0, after.Entries.Count(e => e.Status == EntryStatus.Skipped), "Generated markers must not be treated as unsupported descriptions.");
            Equal(2, RenameEngine.UndoLast(history).Changed, "Extensionless and suffixed numbered changes must undo.");
        }

        private static void TestMixedPrefixesWithCollisionsAndUndo()
        {
            string directory = NewDirectory("mixed-prefix");
            string history = NewDirectory("mixed-prefix-history");
            string existing = Write(directory, "T38-073.PDF", "keep existing document");
            string firstSource = Write(directory, "one@t38-073.PDF", "first document");
            string secondSource = Write(directory, "two@T38-073.PDF", "second document");
            ScanResult scan = RenameEngine.Scan(new string[] { directory }, false);
            RenameEntry first = scan.Entries.Single(e => e.SourcePath == firstSource);
            RenameEntry second = scan.Entries.Single(e => e.SourcePath == secondSource);
            Equal("T38-073 (1).PDF", first.NewName, "Mixed prefixes must receive the first free number.");
            Equal("T38-073 (2).PDF", second.NewName, "Other mixed-prefix files must reserve independent names.");

            BatchResult changed = RenameEngine.Apply(Ready(scan), history);
            Equal(2, changed.Changed, "Both mixed-prefix files should be normalized. " + Describe(changed));
            Equal(0, changed.Failed, "Mixed prefixes must pass target and journal validation.");
            Equal("keep existing document", File.ReadAllText(existing), "The existing document is preserved.");
            Equal("first document", File.ReadAllText(first.TargetPath), "The first numbered document retains its payload.");
            Equal("second document", File.ReadAllText(second.TargetPath), "The second numbered document retains its payload.");
            ScanResult after = RenameEngine.Scan(new string[] { directory }, false);
            Equal(0, Ready(after).Count, "Mixed-prefix numbered names must be stable when loaded again.");
            Equal(3, after.Entries.Count(e => e.Status == EntryStatus.Unchanged), "All three mixed-prefix documents are recognized as normalized.");

            BatchResult restored = RenameEngine.UndoLast(history);
            Equal(2, restored.Changed, "Mixed-prefix journal items must remain undoable. " + Describe(restored));
            Equal(0, restored.Failed, "Undo accepts the same mixed-prefix rules.");
            Equal("first document", File.ReadAllText(firstSource), "Undo restores the first original filename.");
            Equal("second document", File.ReadAllText(secondSource), "Undo restores the second original filename.");
            Equal("keep existing document", File.ReadAllText(existing), "Undo keeps the preexisting document intact.");
        }

        private static void TestRecursionAndDeduplication()
        {
            string directory = NewDirectory("recursive");
            string child = Path.Combine(directory, "nested");
            Directory.CreateDirectory(child);
            Write(directory, "site@DOC-208.mp4", "root");
            Write(child, "site@DOC-209.mp4", "child");
            Equal(1, RenameEngine.Scan(new string[] { directory }, false).Entries.Count, "Subfolders require opt-in.");
            Equal(2, RenameEngine.Scan(new string[] { directory }, true).Entries.Count, "Recursive scan includes child.");
            ScanResult overlap = RenameEngine.Scan(new string[] { directory, child, directory }, true);
            Equal(2, overlap.Entries.Count, "Overlapping or repeated roots must be deduplicated.");
            Equal(2, overlap.Entries.Select(e => e.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "Every source appears once.");
        }

        private static void TestSkippedAndUnchanged()
        {
            string directory = NewDirectory("skip");
            Write(directory, "DOC-208.mp4", "already normalized");
            Write(directory, "readme.txt", "notes");
            Write(directory, "DOC-209 extra.mp4", "ambiguous suffix");
            ScanResult scan = RenameEngine.Scan(new string[] { directory }, false);
            Equal(1, scan.Entries.Count(e => e.Status == EntryStatus.Unchanged), "Normalized file status.");
            Equal(2, scan.Entries.Count(e => e.Status == EntryStatus.Skipped), "Unrelated or ambiguous file status.");
            Equal(0, Ready(scan).Count, "No files should be selected for mutation.");
        }

        private static void TestLateCollision()
        {
            string directory = NewDirectory("late-target");
            string history = NewDirectory("late-target-history");
            string source = Write(directory, "site@DOC-208.mp4", "source payload");
            ScanResult scan = RenameEngine.Scan(new string[] { directory }, false);
            string target = Write(directory, "DOC-208.mp4", "arrived after preview");
            BatchResult result = RenameEngine.Apply(Ready(scan), history);
            Equal(1, result.Changed, "Late collision should get a numbered name. " + Describe(result));
            Equal("source payload", File.ReadAllText(Path.Combine(directory, "DOC-208 (1).mp4")), "Source payload must move to the next free name.");
            Equal("arrived after preview", File.ReadAllText(target), "Late target must remain.");
            Assert(!File.Exists(source), "Source should have moved rather than being silently skipped.");
            Equal(1, RenameEngine.UndoLast(history).Changed, "Late-numbered rename must undo.");
            Equal("source payload", File.ReadAllText(source), "Late-numbered original is restored.");
        }

        private static void TestRemovedSource()
        {
            string directory = NewDirectory("removed-source");
            string history = NewDirectory("removed-source-history");
            string source = Write(directory, "site@DOC-208.mp4", "source payload");
            ScanResult scan = RenameEngine.Scan(new string[] { directory }, false);
            File.Delete(source);
            BatchResult result = RenameEngine.Apply(Ready(scan), history);
            Equal(0, result.Changed, "A missing source must not count as changed.");
            Assert(!File.Exists(Path.Combine(directory, "DOC-208.mp4")), "No target should be invented.");
        }

        private static void TestUndoCollision()
        {
            string directory = NewDirectory("undo-collision");
            string history = NewDirectory("undo-collision-history");
            string source = Write(directory, "site@DOC-208.mp4", "renamed payload");
            RenameEngine.Apply(Ready(RenameEngine.Scan(new string[] { directory }, false)), history);
            File.WriteAllText(source, "new original-name file");
            BatchResult undo = RenameEngine.UndoLast(history);
            Equal(0, undo.Changed, "Undo collision must be rejected.");
            Equal("new original-name file", File.ReadAllText(source), "Undo must never overwrite.");
            Equal("renamed payload", File.ReadAllText(Path.Combine(directory, "DOC-208.mp4")), "Renamed payload must remain available.");
        }

        private static void TestUndoReplacement()
        {
            string directory = NewDirectory("undo-replacement");
            string history = NewDirectory("undo-replacement-history");
            string source = Write(directory, "site@DOC-208.mp4", "old");
            RenameEngine.Apply(Ready(RenameEngine.Scan(new string[] { directory }, false)), history);
            string target = Path.Combine(directory, "DOC-208.mp4");
            File.Delete(target);
            File.WriteAllText(target, "a newly created replacement with a different length");
            File.SetLastWriteTimeUtc(target, new DateTime(2026, 5, 6, 7, 8, 10, DateTimeKind.Utc));
            BatchResult undo = RenameEngine.UndoLast(history);
            Equal(0, undo.Changed, "Undo must reject replaced file metadata.");
            Assert(!File.Exists(source), "Replacement must not move to the old name.");
            Equal("a newly created replacement with a different length", File.ReadAllText(target), "Replacement must remain intact.");
        }

        private static void TestGatherOptInAndUndo()
        {
            string root = NewDirectory("g0");
            string history = NewDirectory("g0h");
            string child = Path.Combine(root, "a");
            Directory.CreateDirectory(child);
            string canonical = Write(child, "REPORT-001.txt", "already named");
            string renamed = Write(child, "site@demo-002ch.MP4", "rename and move");
            string extensionless = Write(child, "website.com@259sample-0007ch", "no extension");
            string unrelated = Write(child, "readme.txt", "leave in child");
            Write(root, "SAMPLE-003.txt", "already at root");
            DateTime timestamp = new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(canonical, timestamp);

            ScanResult ordinary = RenameEngine.Scan(new string[] { root }, true);
            Equal(EntryStatus.Unchanged, ordinary.Entries.Single(e => e.SourcePath == canonical).Status, "Old API must leave an already-normalized child file alone.");
            Equal(Path.Combine(child, "DEMO-002ch.MP4"), ordinary.Entries.Single(e => e.SourcePath == renamed).TargetPath, "Gather-off preview stays in the source folder.");
            ScanResult explicitOff = RenameEngine.Scan(new string[] { root }, true, false);
            Assert(explicitOff.Entries.All(e => !e.MoveToRoot), "Gather must require the explicit option.");

            ScanResult scan = RenameEngine.Scan(new string[] { root }, true, true);
            Equal(3, Ready(scan).Count, "Canonical names in child folders must still be ready to move.");
            foreach (RenameEntry entry in Ready(scan))
            {
                Equal(root, entry.RootFolder, "The dropped root is recorded.");
                Equal(root, entry.TargetFolder, "Gather destination is the dropped root.");
                Equal(child, entry.Folder, "Folder continues to describe the source location.");
                Assert(entry.MoveToRoot, "Child files must visibly carry the move plan.");
            }
            Equal(EntryStatus.Skipped, scan.Entries.Single(e => e.SourcePath == unrelated).Status, "Unrecognized files are not gathered.");
            Equal(EntryStatus.Unchanged, scan.Entries.Single(e => e.OriginalName == "SAMPLE-003.txt").Status, "Root files already named correctly stay unchanged.");

            BatchResult changed = RenameEngine.Apply(Ready(scan), history);
            Equal(3, changed.Changed, "Three supported files should be gathered. " + Describe(changed));
            Equal(0, changed.Failed, "Gather should not fail.");
            Equal("already named", File.ReadAllText(Path.Combine(root, "REPORT-001.txt")), "Canonical file contents.");
            Equal(timestamp, File.GetLastWriteTimeUtc(Path.Combine(root, "REPORT-001.txt")), "Same-volume gathering preserves modification time.");
            Equal("rename and move", File.ReadAllText(Path.Combine(root, "DEMO-002ch.MP4")), "Suffix case and extension are retained.");
            Equal("no extension", File.ReadAllText(Path.Combine(root, "259SAMPLE-0007ch")), "Extensionless dotted prefixes remain supported.");
            Assert(!File.Exists(canonical) && !File.Exists(renamed) && !File.Exists(extensionless), "Gather is a move, not a copy.");
            Equal("leave in child", File.ReadAllText(unrelated), "Skipped payload remains in the child folder.");
            Assert(Directory.Exists(child), "An unsupported file keeps its child directory in place.");
            Equal(0, changed.RemovedDirectories, "A nonempty source directory must not be removed.");
            Equal(0, Ready(RenameEngine.Scan(new string[] { root }, true, true)).Count, "Gathered files are stable on the next preview.");

            Equal(3, RenameEngine.UndoLast(history).Changed, "Undo restores all gathered files.");
            Equal("already named", File.ReadAllText(canonical), "Canonical source location restored.");
            Equal("rename and move", File.ReadAllText(renamed), "Original name and child location restored.");
            Equal("no extension", File.ReadAllText(extensionless), "Extensionless original location restored.");
            Equal("already at root", File.ReadAllText(Path.Combine(root, "SAMPLE-003.txt")), "Unchanged root file remains intact.");
        }

        private static void TestGatherRootSelection()
        {
            string outer = NewDirectory("g1");
            string inner = Path.Combine(outer, "a");
            string innerChild = Path.Combine(inner, "c");
            string outerChild = Path.Combine(outer, "b");
            string sibling = NewDirectory("g1-other");
            string siblingChild = Path.Combine(sibling, "c");
            Directory.CreateDirectory(innerChild);
            Directory.CreateDirectory(outerChild);
            Directory.CreateDirectory(siblingChild);
            string innerSource = Write(innerChild, "site@report-001.txt", "inner");
            string outerSource = Write(outerChild, "site@report-001.txt", "outer");
            string siblingSource = Write(siblingChild, "site@report-001.txt", "sibling");
            string innerRootFile = Write(inner, "DEMO-002.txt", "at inner root");
            ScanResult scan = RenameEngine.Scan(new string[] { outer, inner, sibling, outer }, true, true);
            Equal(4, scan.Entries.Count, "Overlapping or repeated roots must not duplicate files.");
            Equal(inner, scan.Entries.Single(e => e.SourcePath == innerSource).TargetFolder, "Closest explicitly dropped ancestor wins.");
            Equal(outer, scan.Entries.Single(e => e.SourcePath == outerSource).TargetFolder, "Other descendants collect into the outer root.");
            Equal(sibling, scan.Entries.Single(e => e.SourcePath == siblingSource).TargetFolder, "A sibling sharing a root-name prefix stays separate.");
            Equal(EntryStatus.Unchanged, scan.Entries.Single(e => e.SourcePath == innerRootFile).Status, "A file directly inside an explicitly dropped child root is already at its destination.");
            Assert(Ready(scan).All(e => !e.CollisionNumbered), "Identical names in distinct destination roots do not collide.");

            string history = NewDirectory("g1h");
            BatchResult changed = RenameEngine.Apply(Ready(scan), history);
            Equal(3, changed.Changed, "Each dropped root should gather its own files. " + Describe(changed));
            Equal("inner", File.ReadAllText(Path.Combine(inner, "REPORT-001.txt")), "Inner root payload.");
            Equal("outer", File.ReadAllText(Path.Combine(outer, "REPORT-001.txt")), "Outer root payload.");
            Equal("sibling", File.ReadAllText(Path.Combine(sibling, "REPORT-001.txt")), "Sibling root payload.");
            Assert(!Directory.Exists(innerChild) && !Directory.Exists(outerChild) && !Directory.Exists(siblingChild), "Emptied source folders are removed after gathering.");
            Equal(3, changed.RemovedDirectories, "Each emptied source folder is counted once.");
            Assert(Directory.Exists(outer) && Directory.Exists(inner) && Directory.Exists(sibling), "Every explicitly dropped root is retained.");
            Equal(3, RenameEngine.UndoLast(history).Changed, "Cross-folder undo should restore all three roots independently.");
            Equal("inner", File.ReadAllText(innerSource), "Inner original restored.");
            Equal("outer", File.ReadAllText(outerSource), "Outer original restored.");
            Equal("sibling", File.ReadAllText(siblingSource), "Sibling original restored.");
        }

        private static void TestGatherNumbering()
        {
            string root = NewDirectory("g2");
            string history = NewDirectory("g2h");
            string firstFolder = Path.Combine(root, "a");
            string secondFolder = Path.Combine(root, "b");
            Directory.CreateDirectory(firstFolder);
            Directory.CreateDirectory(secondFolder);
            Write(root, "REPORT-001.txt", "existing root file");
            Directory.CreateDirectory(Path.Combine(root, "REPORT-001 (1).txt"));
            string firstSource = Write(firstFolder, "site@report-001.txt", "first child");
            string secondSource = Write(secondFolder, "site@report-001.txt", "second child");
            ScanResult scan = RenameEngine.Scan(new string[] { root }, true, true);
            RenameEntry first = scan.Entries.Single(e => e.SourcePath == firstSource);
            RenameEntry second = scan.Entries.Single(e => e.SourcePath == secondSource);
            Equal("REPORT-001 (2).txt", first.NewName, "Root file and directory names must be reserved.");
            Equal("REPORT-001 (3).txt", second.NewName, "Different child folders reserve the same root namespace.");
            Equal(1, RenameEngine.Apply(new RenameEntry[] { second }, history).Changed, "Selected subset should gather.");
            Equal("REPORT-001 (3).txt", second.NewName, "Gathering a subset keeps its previewed number.");
            Equal("first child", File.ReadAllText(firstSource), "Unselected child file is untouched.");
            Equal(1, RenameEngine.UndoLast(history).Changed, "Subset undo returns the file to its original child.");

            scan = RenameEngine.Scan(new string[] { root }, true, true);
            first = scan.Entries.Single(e => e.SourcePath == firstSource);
            second = scan.Entries.Single(e => e.SourcePath == secondSource);
            Write(root, first.NewName, "late arrival");
            BatchResult changed = RenameEngine.Apply(Ready(scan), history);
            Equal(2, changed.Changed, "Late collisions should not prevent gathering. " + Describe(changed));
            Equal("REPORT-001 (4).txt", first.NewName, "Late renumbering must not steal the second preview's destination.");
            Equal("REPORT-001 (3).txt", second.NewName, "Other reserved gather name remains stable.");
            Equal("late arrival", File.ReadAllText(Path.Combine(root, "REPORT-001 (2).txt")), "Late arrival remains intact.");
            Equal("existing root file", File.ReadAllText(Path.Combine(root, "REPORT-001.txt")), "Existing root content remains intact.");
            Assert(Directory.Exists(Path.Combine(root, "REPORT-001 (1).txt")), "Existing root directory remains intact.");
            Equal("first child", File.ReadAllText(first.TargetPath), "First gathered content.");
            Equal("second child", File.ReadAllText(second.TargetPath), "Second gathered content.");
            Equal(2, RenameEngine.UndoLast(history).Changed, "Both numbered root destinations undo to their child folders.");
            Equal("first child", File.ReadAllText(firstSource), "First original after numbered undo.");
            Equal("second child", File.ReadAllText(secondSource), "Second original after numbered undo.");
        }

        private static void TestGatherUndoFolders()
        {
            string root = NewDirectory("g3");
            string history = NewDirectory("g3h");
            string parent = Path.Combine(root, "a");
            string child = Path.Combine(parent, "b");
            Directory.CreateDirectory(child);
            string source = Write(child, "REPORT-001.txt", "restore folders");
            BatchResult gathered = RenameEngine.Apply(Ready(RenameEngine.Scan(new string[] { root }, true, true)), history);
            Equal(1, gathered.Changed, "Gather before directory recovery.");
            Equal(2, gathered.RemovedDirectories, "The emptied source folder and its empty ancestor are removed.");
            Equal(0, gathered.CleanupWarnings.Count, "Successful empty-folder cleanup needs no warnings.");
            Assert(!Directory.Exists(child) && !Directory.Exists(parent), "Gather automatically removes the empty source chain.");
            Assert(Directory.Exists(root), "The dragged root must remain.");
            BatchResult restored = RenameEngine.UndoLast(history);
            Equal(1, restored.Changed, "Undo should recreate missing original descendants under the still-existing root. " + Describe(restored));
            Assert(Directory.Exists(parent) && Directory.Exists(child), "Undo recreates both removed source folders.");
            Equal("restore folders", File.ReadAllText(source), "Recreated source location contains the original payload.");

            Equal(1, RenameEngine.Apply(Ready(RenameEngine.Scan(new string[] { root }, true, true)), history).Changed, "Gather again before an undo-name collision.");
            Directory.CreateDirectory(child);
            File.WriteAllText(source, "new original-name payload");
            BatchResult blocked = RenameEngine.UndoLast(history);
            Equal(0, blocked.Changed, "Gather undo must not number or overwrite a new original-name file.");
            Assert(blocked.Failed > 0, "Blocked gather undo must report a failure.");
            Equal("new original-name payload", File.ReadAllText(source), "New source-name payload is protected.");
            Equal("restore folders", File.ReadAllText(Path.Combine(root, "REPORT-001.txt")), "Gathered original remains available at the root.");
        }

        private static void TestGatherCleanupKeepsRemainingFiles()
        {
            string root = NewDirectory("g6");
            string history = NewDirectory("g6h");
            string subsetFolder = Path.Combine(root, "subset");
            string unsupportedFolder = Path.Combine(root, "unsupported");
            string hiddenFolder = Path.Combine(root, "hidden");
            Directory.CreateDirectory(subsetFolder);
            Directory.CreateDirectory(unsupportedFolder);
            Directory.CreateDirectory(hiddenFolder);
            Write(subsetFolder, "site@report-001.txt", "selected");
            string unselected = Write(subsetFolder, "site@demo-002.txt", "unselected");
            Write(unsupportedFolder, "site@sample-003.txt", "supported");
            string unsupported = Write(unsupportedFolder, "readme.txt", "unsupported notes");
            Write(hiddenFolder, "site@test-004.txt", "alongside hidden file");
            string hidden = Write(hiddenFolder, ".keep", "hidden payload");
            File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
            List<RenameEntry> selected = Ready(RenameEngine.Scan(new string[] { root }, true, true));
            selected.Single(e => e.SourcePath == unselected).Selected = false;
            selected = selected.Where(e => e.Selected).ToList();

            BatchResult changed = RenameEngine.Apply(selected, history);
            Equal(3, changed.Changed, "Only the three selected supported files are gathered. " + Describe(changed));
            Equal(0, changed.Failed, "Remaining files must not make successful moves fail.");
            Equal(0, changed.RemovedDirectories, "Every source folder still contains a file.");
            Equal(0, changed.CleanupWarnings.Count, "Keeping nonempty folders is expected and needs no warning.");
            Assert(Directory.Exists(subsetFolder) && Directory.Exists(unsupportedFolder) && Directory.Exists(hiddenFolder), "Unselected, unsupported and hidden files each preserve their parent folder.");
            Equal("unselected", File.ReadAllText(unselected), "The unselected supported file is untouched.");
            Equal("unsupported notes", File.ReadAllText(unsupported), "Unsupported contents remain in place.");
            Equal("hidden payload", File.ReadAllText(hidden), "Hidden contents remain in place.");
            Assert((File.GetAttributes(hidden) & FileAttributes.Hidden) != 0, "Cleanup preserves the hidden file's attributes.");
            Equal(3, RenameEngine.UndoLast(history).Changed, "All successful moves remain undoable when source folders were kept.");
        }

        private static void TestGatherCleanupScope()
        {
            string root = NewDirectory("g7");
            string history = NewDirectory("g7h");
            string parent = Path.Combine(root, "source");
            string child = Path.Combine(parent, "nested");
            string explicitRoot = Path.Combine(root, "empty-dropped-root");
            string unrelated = Path.Combine(root, "unrelated-empty");
            Directory.CreateDirectory(child);
            Directory.CreateDirectory(explicitRoot);
            Directory.CreateDirectory(unrelated);
            string source = Write(child, "site@report-001.txt", "source chain");
            ScanResult scan = RenameEngine.Scan(new string[] { root, explicitRoot }, true, true);

            BatchResult changed = RenameEngine.Apply(Ready(scan), history);
            Equal(1, changed.Changed, "The nested supported file is gathered. " + Describe(changed));
            Equal(2, changed.RemovedDirectories, "Only the two emptied source ancestors are removed.");
            Assert(!Directory.Exists(child) && !Directory.Exists(parent), "Empty source ancestors are removed from deepest to shallowest.");
            Assert(Directory.Exists(root), "The dragged destination root is retained.");
            Assert(Directory.Exists(explicitRoot), "An explicitly dropped empty nested root is retained.");
            Assert(Directory.Exists(unrelated), "An unrelated empty sibling is not purged.");
            Equal(1, RenameEngine.UndoLast(history).Changed, "Cleanup preserves the move's undo journal.");
            Equal("source chain", File.ReadAllText(source), "Undo recreates the source ancestor chain and restores its file.");
        }

        private static void TestGatherCleanupMixedResult()
        {
            string root = NewDirectory("g8");
            string history = NewDirectory("g8h");
            string successfulFolder = Path.Combine(root, "a-success");
            string failedFolder = Path.Combine(root, "b-missing");
            Directory.CreateDirectory(successfulFolder);
            Directory.CreateDirectory(failedFolder);
            string source = Write(successfulFolder, "site@report-001.txt", "successful move");
            string missing = Write(failedFolder, "site@demo-002.txt", "removed after preview");
            List<RenameEntry> selected = Ready(RenameEngine.Scan(new string[] { root }, true, true));
            File.Delete(missing);

            BatchResult changed = RenameEngine.Apply(selected, history);
            Equal(1, changed.Changed, "One successful move is retained despite the other missing source. " + Describe(changed));
            Equal(1, changed.Failed, "Only the missing source is a file failure.");
            Equal(1, changed.RemovedDirectories, "Only a successfully gathered source folder is removed.");
            Assert(!Directory.Exists(successfulFolder), "The successful move's emptied source folder is removed.");
            Assert(Directory.Exists(failedFolder), "An empty folder belonging only to a failed move is retained.");
            Equal(0, changed.CleanupWarnings.Count, "The file failure is not reported as a cleanup warning.");
            Equal(1, RenameEngine.UndoLast(history).Changed, "The successful file remains undoable after a partial result.");
            Equal("successful move", File.ReadAllText(source), "Partial-result undo restores the successful source.");
        }

        private static void TestGatherOffKeepsFolders()
        {
            string root = NewDirectory("g9");
            string history = NewDirectory("g9h");
            string child = Path.Combine(root, "source");
            string unrelated = Path.Combine(root, "unrelated-empty");
            Directory.CreateDirectory(child);
            Directory.CreateDirectory(unrelated);
            string source = Write(child, "site@report-001.txt", "rename in place");

            BatchResult changed = RenameEngine.Apply(Ready(RenameEngine.Scan(new string[] { root }, true, false)), history);
            Equal(1, changed.Changed, "Gather-off still normalizes supported names. " + Describe(changed));
            Equal(0, changed.RemovedDirectories, "Gather-off never schedules folder cleanup.");
            Equal(0, changed.CleanupWarnings.Count, "Gather-off needs no cleanup warnings.");
            Assert(Directory.Exists(child) && Directory.Exists(unrelated), "Gather-off leaves the folder structure intact.");
            Equal("rename in place", File.ReadAllText(Path.Combine(child, "REPORT-001.txt")), "The renamed file remains in its original child folder.");
            Assert(!File.Exists(Path.Combine(root, "REPORT-001.txt")), "Gather-off does not create a destination-root file.");
            Equal(1, RenameEngine.UndoLast(history).Changed, "The in-place rename remains undoable.");
            Equal("rename in place", File.ReadAllText(source), "Undo restores the original child filename.");
        }

        private static void TestGatherTampering()
        {
            string root = NewDirectory("g4");
            string sibling = NewDirectory("g4-other");
            string history = NewDirectory("g4h");
            string child = Path.Combine(root, "a");
            Directory.CreateDirectory(child);
            string source = Write(child, "site@report-001.txt", "protected source");
            Action<RenameEntry>[] tamper = new Action<RenameEntry>[]
            {
                delegate(RenameEntry entry) { entry.RootFolder = sibling; entry.TargetFolder = sibling; entry.TargetPath = Path.Combine(sibling, entry.NewName); },
                delegate(RenameEntry entry) { entry.TargetFolder = sibling; entry.TargetPath = Path.Combine(sibling, entry.NewName); },
                delegate(RenameEntry entry) { entry.TargetPath = Path.Combine(sibling, entry.NewName); }
            };
            foreach (Action<RenameEntry> change in tamper)
            {
                RenameEntry entry = Ready(RenameEngine.Scan(new string[] { root }, true, true)).Single();
                change(entry);
                BatchResult rejected = RenameEngine.Apply(new RenameEntry[] { entry }, history);
                Equal(0, rejected.Changed, "Changed root or preview location must not authorize a move elsewhere.");
                Assert(rejected.Failed > 0, "An invalid destination should be reported.");
                Equal("protected source", File.ReadAllText(source), "Invalid preview leaves its source intact.");
                Assert(!File.Exists(Path.Combine(sibling, "REPORT-001.txt")), "No file may escape into the sibling directory.");
            }

            RenameEntry outside = Ready(RenameEngine.Scan(new string[] { root }, true, true)).Single();
            string siblingChild = Path.Combine(sibling, "a");
            Directory.CreateDirectory(siblingChild);
            string relocated = Path.Combine(siblingChild, Path.GetFileName(source));
            File.Move(source, relocated);
            outside.SourcePath = relocated;
            outside.Folder = siblingChild;
            BatchResult prefixEscape = RenameEngine.Apply(new RenameEntry[] { outside }, history);
            Equal(0, prefixEscape.Changed, "A same-identity source moved outside root must not pass a plain string-prefix check.");
            Assert(prefixEscape.Failed > 0, "Outside-root source must be rejected.");
            Equal("protected source", File.ReadAllText(relocated), "Rejected outside-root source remains where it was found.");
            Assert(!File.Exists(Path.Combine(root, "REPORT-001.txt")), "Rejected outside source is not gathered into the old root.");
        }

        private static void TestLegacyJournal()
        {
            string root = NewDirectory("g5");
            string history = NewDirectory("g5h");
            string source = Write(root, "site@report-001.txt", "legacy journal payload");
            BatchResult changed = RenameEngine.Apply(Ready(RenameEngine.Scan(new string[] { root }, false)), history);
            Equal(1, changed.Changed, "Same-folder rename prepares a legacy-shaped record.");
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> data = (Dictionary<string, object>)serializer.DeserializeObject(File.ReadAllText(changed.HistoryPath));
            object[] items = (object[])data["Items"];
            List<Dictionary<string, object>> legacyItems = new List<Dictionary<string, object>>();
            foreach (object raw in items)
            {
                Dictionary<string, object> item = (Dictionary<string, object>)raw;
                Dictionary<string, object> legacy = new Dictionary<string, object>();
                foreach (string key in new string[] { "SourcePath", "TargetPath", "TemporaryPath", "Stamp", "State", "Error" }) legacy.Add(key, item[key]);
                legacyItems.Add(legacy);
            }
            data["Version"] = 1;
            data["Items"] = legacyItems;
            File.WriteAllText(changed.HistoryPath, serializer.Serialize(data));
            Equal(1, RenameEngine.UndoLast(history).Changed, "A genuine version-one item without gather metadata should remain undoable.");
            Equal("legacy journal payload", File.ReadAllText(source), "Legacy original name restored.");
        }
    }
}
