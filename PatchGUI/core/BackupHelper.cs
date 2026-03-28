using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PatchGUI.Core
{
    /// <summary>
    /// 目录备份/还原：
    /// - 全量备份：把整个游戏目录打包成 zip（扩展名 .pak）
    /// - 选择性备份：仅备份“可能被补丁修改”的文件（按补丁内容推断）
    /// - 还原：全量解压覆盖；或按 manifest 做选择性还原
    /// </summary>
    public static class BackupHelper
    {
        private const string ManifestEntryName = "__patchgui_backup_manifest.json";
        private const int MaxManifestPaths = 5000;

        /// <summary>
        /// 把 sourceDir 整个打包成 zip（扩展名 .pak），返回备份文件路径。
        /// </summary>
        public static string CreateDirectoryBackup(string sourceDir, string backupFilePath)
            => CreateDirectoryBackup(sourceDir, backupFilePath, logger: null);

        /// <summary>
        /// 把 sourceDir 整个打包成 zip（扩展名 .pak），返回备份文件路径。
        /// 会将备份过程写到 logger（如果提供）。
        /// </summary>
        public static string CreateDirectoryBackup(string sourceDir, string backupFilePath, IPatchLogger? logger)
        {
            using var _ = logger?.Step("备份：创建 .pak（zip）");

            if (string.IsNullOrWhiteSpace(sourceDir))
                throw new ArgumentNullException(nameof(sourceDir));
            if (string.IsNullOrWhiteSpace(backupFilePath))
                throw new ArgumentNullException(nameof(backupFilePath));

            sourceDir = Path.GetFullPath(sourceDir);
            backupFilePath = Path.GetFullPath(backupFilePath);

            if (!Directory.Exists(sourceDir))
                throw new DirectoryNotFoundException($"源目录不存在：{sourceDir}");

            logger?.Info($"源目录：{sourceDir}");
            logger?.Info($"备份文件：{backupFilePath}");

            string sourceDirWithSep = sourceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                    + Path.DirectorySeparatorChar;
            if (backupFilePath.StartsWith(sourceDirWithSep, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "备份文件不能创建在被备份的目录内部（否则 ZipFile.CreateFromDirectory 会把备份文件本身也打包，导致占用冲突）。" +
                    $" sourceDir={sourceDir} backupFile={backupFilePath}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(backupFilePath)!);

            if (File.Exists(backupFilePath))
                File.Delete(backupFilePath);

            try
            {
                long fileCount = 0;
                long totalBytes = 0;
                foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
                {
                    fileCount++;
                    try { totalBytes += new FileInfo(file).Length; } catch { }
                }
                logger?.Info($"待打包文件数：{fileCount}，总大小：{totalBytes} bytes");
            }
            catch (Exception ex)
            {
                logger?.Warn($"统计备份文件信息失败（不影响备份）：{ex.Message}");
            }

            ZipFile.CreateFromDirectory(
                sourceDir,
                backupFilePath,
                CompressionLevel.Optimal,
                includeBaseDirectory: false);

            logger?.Info("备份创建完成。");
            return backupFilePath;
        }

        /// <summary>
        /// 只备份“可能被补丁修改”的文件：从 patchFilePath 中提取目标文件路径，
        /// 然后仅把源目录中已存在的对应文件打包进备份（.pak=zip）。
        /// </summary>
        public static string CreateSelectiveBackup(string sourceDir, string patchFilePath, string backupFilePath, IPatchLogger? logger)
        {
            using var _ = logger?.Step("备份：选择性备份（仅备份可能被修改的文件）");

            if (string.IsNullOrWhiteSpace(sourceDir))
                throw new ArgumentNullException(nameof(sourceDir));
            if (string.IsNullOrWhiteSpace(patchFilePath))
                throw new ArgumentNullException(nameof(patchFilePath));
            if (string.IsNullOrWhiteSpace(backupFilePath))
                throw new ArgumentNullException(nameof(backupFilePath));

            sourceDir = Path.GetFullPath(sourceDir);
            patchFilePath = Path.GetFullPath(patchFilePath);
            backupFilePath = Path.GetFullPath(backupFilePath);

            if (!Directory.Exists(sourceDir))
                throw new DirectoryNotFoundException($"源目录不存在：{sourceDir}");
            if (!File.Exists(patchFilePath))
                throw new FileNotFoundException("找不到补丁文件。", patchFilePath);

            logger?.Info($"源目录：{sourceDir}");
            logger?.Info($"补丁文件：{patchFilePath}");
            logger?.Info($"备份文件：{backupFilePath}");

            string sourceDirWithSep = sourceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                    + Path.DirectorySeparatorChar;
            if (backupFilePath.StartsWith(sourceDirWithSep, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "备份文件不能创建在被备份的目录内部（否则会导致占用冲突）。" +
                    $" sourceDir={sourceDir} backupFile={backupFilePath}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(backupFilePath)!);
            if (File.Exists(backupFilePath))
                File.Delete(backupFilePath);

            var candidates = ExtractCandidatePathsFromPatch(patchFilePath, logger);
            logger?.Info($"从补丁中提取到候选路径数：{candidates.Count}");

            var normalizedCandidates = candidates
                .Select(p => p.Replace('/', '\\').Trim())
                .Select(p => p.TrimStart('\\'))
                .Where(IsSafeRelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxManifestPaths)
                .ToList();

            var existingFiles = normalizedCandidates
                .Where(rel => File.Exists(Path.Combine(sourceDir, rel)))
                .ToList();

            var missingBefore = normalizedCandidates
                .Where(rel => !File.Exists(Path.Combine(sourceDir, rel)))
                .ToList();

            long totalBytes = 0;
            foreach (var rel in existingFiles)
            {
                try { totalBytes += new FileInfo(Path.Combine(sourceDir, rel)).Length; } catch { }
            }

            logger?.Info($"将备份文件数：{existingFiles.Count}，总大小：{totalBytes} bytes");

            if (existingFiles.Count == 0 && missingBefore.Count == 0)
            {
                throw new InvalidOperationException(
                    "选择性备份未能解析出任何有效文件路径（可能补丁内容不可直接解析）。");
            }

            string sourceRootName;
            try
            {
                sourceRootName = Path.GetFileName(
                    sourceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            catch
            {
                sourceRootName = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(sourceRootName))
                sourceRootName = "<root>";

            var manifest = new SelectiveBackupManifest
            {
                Version = 1,
                BackupType = "Selective",
                CreatedAtUtc = DateTime.UtcNow,
                PatchFileName = Path.GetFileName(patchFilePath),
                SourceRoot = sourceRootName,
                BackedUpFiles = existingFiles.ToArray(),
                MissingBeforeFiles = missingBefore.ToArray(),
            };

            CreateSelectiveBackupFromLists(sourceDir, patchFilePath, backupFilePath, existingFiles, missingBefore, manifest, logger);

            logger?.Info("选择性备份创建完成。");
            return backupFilePath;
        }

        public static string CreateSelectiveBackupFromLists(
            string sourceDir,
            string patchFilePath,
            string backupFilePath,
            System.Collections.Generic.IReadOnlyCollection<string> backedUpFiles,
            System.Collections.Generic.IReadOnlyCollection<string> missingBeforeFiles,
            IPatchLogger? logger)
        {
            if (string.IsNullOrWhiteSpace(sourceDir))
                throw new ArgumentNullException(nameof(sourceDir));
            if (string.IsNullOrWhiteSpace(patchFilePath))
                throw new ArgumentNullException(nameof(patchFilePath));
            if (string.IsNullOrWhiteSpace(backupFilePath))
                throw new ArgumentNullException(nameof(backupFilePath));

            if (backedUpFiles == null)
                throw new ArgumentNullException(nameof(backedUpFiles));
            if (missingBeforeFiles == null)
                throw new ArgumentNullException(nameof(missingBeforeFiles));

            sourceDir = Path.GetFullPath(sourceDir);
            patchFilePath = Path.GetFullPath(patchFilePath);
            backupFilePath = Path.GetFullPath(backupFilePath);

            var safeBackedUp = backedUpFiles
                .Select(p => p.Replace('/', '\\').Trim())
                .Select(p => p.TrimStart('\\'))
                .Where(IsSafeRelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxManifestPaths)
                .ToArray();

            var safeMissing = missingBeforeFiles
                .Select(p => p.Replace('/', '\\').Trim())
                .Select(p => p.TrimStart('\\'))
                .Where(IsSafeRelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxManifestPaths)
                .ToArray();

            if (safeBackedUp.Length == 0 && safeMissing.Length == 0)
            {
                throw new InvalidOperationException("选择性备份列表为空。");
            }

            string sourceRootName;
            try
            {
                sourceRootName = Path.GetFileName(
                    sourceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            catch
            {
                sourceRootName = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(sourceRootName))
                sourceRootName = "<root>";

            var manifest = new SelectiveBackupManifest
            {
                Version = 1,
                BackupType = "Selective",
                CreatedAtUtc = DateTime.UtcNow,
                PatchFileName = Path.GetFileName(patchFilePath),
                SourceRoot = sourceRootName,
                BackedUpFiles = safeBackedUp,
                MissingBeforeFiles = safeMissing,
            };

            return CreateSelectiveBackupFromLists(sourceDir, patchFilePath, backupFilePath, safeBackedUp, safeMissing, manifest, logger);
        }

        private static string CreateSelectiveBackupFromLists(
            string sourceDir,
            string patchFilePath,
            string backupFilePath,
            System.Collections.Generic.IReadOnlyCollection<string> backedUpFiles,
            System.Collections.Generic.IReadOnlyCollection<string> missingBeforeFiles,
            SelectiveBackupManifest manifest,
            IPatchLogger? logger)
        {
            try
            {
                long totalBytes = 0;
                int existingCount = 0;
                int missingCount = 0;

                System.Threading.Tasks.Parallel.ForEach(backedUpFiles, rel =>
                {
                    if (string.IsNullOrWhiteSpace(rel))
                        return;

                    string src = Path.Combine(sourceDir, rel);
                    if (!File.Exists(src))
                    {
                        System.Threading.Interlocked.Increment(ref missingCount);
                        return;
                    }

                    System.Threading.Interlocked.Increment(ref existingCount);
                    try
                    {
                        long len = new FileInfo(src).Length;
                        System.Threading.Interlocked.Add(ref totalBytes, len);
                    }
                    catch
                    {
                        // ignore size failures
                    }
                });

                logger?.Info($"待备份文件数：{existingCount}，总大小：{totalBytes} bytes");
                if (missingCount > 0)
                    logger?.Warn($"备份清单中有 {missingCount} 个文件不存在，将跳过。");
            }
            catch (Exception ex)
            {
                logger?.Warn($"统计选择性备份文件信息失败（不影响备份）：{ex.Message}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(backupFilePath)!);
            if (File.Exists(backupFilePath))
                File.Delete(backupFilePath);

            int added = 0;
            using (var zip = ZipFile.Open(backupFilePath, ZipArchiveMode.Create))
            {
                var manifestEntry = zip.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                using (var s = manifestEntry.Open())
                {
                    JsonSerializer.Serialize(s, manifest, new JsonSerializerOptions { WriteIndented = true });
                }

                foreach (var rel in backedUpFiles)
                {
                    string src = Path.Combine(sourceDir, rel);
                    if (!File.Exists(src))
                        continue;

                    string entryName = rel.Replace('\\', '/');
                    zip.CreateEntryFromFile(src, entryName, CompressionLevel.Optimal);
                    added++;
                }

                if (added == 0 && missingBeforeFiles.Count == 0)
                {
                    throw new InvalidOperationException("选择性备份未写入任何文件。");
                }
            }

            logger?.Info($"选择性备份清单：备份文件数={added}，新增文件数={missingBeforeFiles.Count}");
            return backupFilePath;
        }

        /// <summary>
        /// 从备份文件解压到 targetDir，已存在文件会被覆盖。
        /// </summary>
        public static void RestoreDirectoryBackup(string backupFilePath, string targetDir)
            => RestoreDirectoryBackup(backupFilePath, targetDir, logger: null);

        /// <summary>
        /// 从备份文件解压到 targetDir，已存在文件会被覆盖。
        /// 会将还原过程写到 logger（如果提供）。
        /// </summary>
        public static void RestoreDirectoryBackup(string backupFilePath, string targetDir, IPatchLogger? logger)
        {
            using var _ = logger?.Step("备份：从 .pak（zip）还原");

            if (string.IsNullOrWhiteSpace(backupFilePath))
                throw new ArgumentNullException(nameof(backupFilePath));
            if (string.IsNullOrWhiteSpace(targetDir))
                throw new ArgumentNullException(nameof(targetDir));

            backupFilePath = Path.GetFullPath(backupFilePath);
            targetDir = Path.GetFullPath(targetDir);

            if (!File.Exists(backupFilePath))
                throw new FileNotFoundException("找不到备份文件。", backupFilePath);

            logger?.Info($"备份文件：{backupFilePath}");
            logger?.Info($"还原目标：{targetDir}");

            Directory.CreateDirectory(targetDir);

            using var archive = ZipFile.OpenRead(backupFilePath);
            logger?.Info($"压缩包条目数：{archive.Entries.Count}");

            var manifestEntry = archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, ManifestEntryName, StringComparison.OrdinalIgnoreCase));
            if (manifestEntry != null)
            {
                SelectiveBackupManifest? manifest = null;
                try
                {
                    using var s = manifestEntry.Open();
                    manifest = JsonSerializer.Deserialize<SelectiveBackupManifest>(s);
                }
                catch (Exception ex)
                {
                    logger?.Warn($"读取选择性备份清单失败，将按全量解压处理：{ex.Message}");
                }

                if (manifest != null && string.Equals(manifest.BackupType, "Selective", StringComparison.OrdinalIgnoreCase))
                {
                    logger?.Info($"检测到选择性备份：Version={manifest.Version} Patch={manifest.PatchFileName} 备份文件数={manifest.BackedUpFiles?.Length ?? 0}");

                    int extracted2 = 0;
                    var backedUpList = manifest.BackedUpFiles ?? Array.Empty<string>();
                    if (backedUpList.Length > MaxManifestPaths)
                        logger?.Warn($"备份清单过大（{backedUpList.Length}），将仅处理前 {MaxManifestPaths} 条。");

                    foreach (var rel in backedUpList.Take(MaxManifestPaths))
                    {
                        if (string.IsNullOrWhiteSpace(rel))
                            continue;

                        string normalized = rel.Replace('/', '\\').TrimStart('\\');
                        if (!IsSafeRelativePath(normalized))
                            continue;

                        var entry = archive.GetEntry(normalized.Replace('\\', '/'));
                        if (entry == null)
                        {
                            logger?.Warn($"备份条目缺失：{normalized}");
                            continue;
                        }

                        try
                        {
                            string destinationPath = Path.Combine(targetDir, normalized);
                            string? dir = Path.GetDirectoryName(destinationPath);
                            if (!string.IsNullOrEmpty(dir))
                                Directory.CreateDirectory(dir);

                            entry.ExtractToFile(destinationPath, overwrite: true);
                            extracted2++;
                        }
                        catch (Exception ex)
                        {
                            logger?.Warn($"还原文件失败（可忽略）：{normalized} - {ex.Message}");
                        }
                    }

                    int deleted = 0;
                    var missingList = manifest.MissingBeforeFiles ?? Array.Empty<string>();
                    if (missingList.Length > MaxManifestPaths)
                        logger?.Warn($"新增文件清单过大（{missingList.Length}），将仅处理前 {MaxManifestPaths} 条。");

                    foreach (var rel in missingList.Take(MaxManifestPaths))
                    {
                        if (string.IsNullOrWhiteSpace(rel))
                            continue;

                        string normalized = rel.Replace('/', '\\').TrimStart('\\');
                        if (!IsSafeRelativePath(normalized))
                            continue;

                        try
                        {
                            string path = Path.Combine(targetDir, normalized);
                            if (File.Exists(path))
                            {
                                File.Delete(path);
                                deleted++;
                            }
                        }
                        catch (Exception ex)
                        {
                            logger?.Warn($"删除补丁新增文件失败（可忽略）：{normalized} - {ex.Message}");
                        }
                    }

                    logger?.Info($"选择性还原完成：已还原文件数={extracted2}，已删除新增文件数={deleted}");
                    return;
                }
            }

            int extracted = 0;
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.FullName))
                    continue;

                if (string.Equals(entry.FullName, ManifestEntryName, StringComparison.OrdinalIgnoreCase))
                    continue;

                string destinationPath = Path.Combine(targetDir, entry.FullName);
                string? dir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                    continue;

                entry.ExtractToFile(destinationPath, overwrite: true);
                extracted++;
            }

            logger?.Info($"还原完成，已解压文件数：{extracted}");
        }

        private static HashSet<string> ExtractCandidatePathsFromPatch(string patchFilePath, IPatchLogger? logger)
        {
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var fs = new FileStream(patchFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var token = new StringBuilder(capacity: 512);
                byte[] buf = new byte[1024 * 128];
                int read;
                while ((read = fs.Read(buf, 0, buf.Length)) > 0)
                {
                    for (int i = 0; i < read; i++)
                    {
                        byte b = buf[i];
                        if (b >= 0x20 && b <= 0x7E)
                        {
                            token.Append((char)b);
                            if (token.Length > 2048)
                                token.Remove(0, token.Length - 2048);
                        }
                        else
                        {
                            FlushToken(token, results);
                        }
                    }
                }
                FlushToken(token, results);
            }
            catch (Exception ex)
            {
                logger?.Warn($"解析补丁候选路径失败，将回退到全量备份：{ex.Message}");
            }

            return results;
        }

        private static void FlushToken(StringBuilder token, HashSet<string> results)
        {
            if (token.Length == 0)
                return;

            string s = token.ToString();
            token.Clear();

            if (s.Length < 4)
                return;

            foreach (var part in s.Split(new[] { ' ', '\t', '\r', '\n', '"', '\'', '<', '>', '(', ')', '[', ']', '{', '}', ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Length < 4 || part.Length > 260)
                    continue;

                if (part.Contains("://", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (part.IndexOf('.') < 0)
                    continue;

                if (!part.Contains('\\') && !part.Contains('/'))
                    continue;

                string cleaned = part.Trim().TrimEnd('.', ',', ';', ':', ')', ']', '}');
                cleaned = cleaned.TrimStart('\\', '/');

                if (cleaned.Contains(':'))
                    continue;

                cleaned = cleaned.Replace('/', '\\');

                if (!IsSafeRelativePath(cleaned))
                    continue;

                results.Add(cleaned);
            }
        }

        private static bool IsSafeRelativePath(string rel)
        {
            if (string.IsNullOrWhiteSpace(rel))
                return false;

            if (rel.Length > 260)
                return false;

            if (rel.Contains("..", StringComparison.Ordinal))
                return false;

            if (rel.StartsWith("\\", StringComparison.Ordinal) || rel.StartsWith("/", StringComparison.Ordinal))
                return false;

            if (rel.Contains(':'))
                return false;

            rel = rel.Replace('/', '\\');

            if (!Path.HasExtension(rel))
                return false;

            var invalidNameChars = Path.GetInvalidFileNameChars().Where(c => c != '\\' && c != '/').ToArray();
            foreach (var segment in rel.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment.IndexOfAny(invalidNameChars) >= 0)
                    return false;
            }

            foreach (char ch in rel)
            {
                if (ch < 0x20)
                    return false;
            }

            return true;
        }

        private sealed class SelectiveBackupManifest
        {
            public int Version { get; set; }
            public string? BackupType { get; set; }
            public DateTime CreatedAtUtc { get; set; }
            public string? PatchFileName { get; set; }
            public string? SourceRoot { get; set; }
            public string[]? BackedUpFiles { get; set; }
            public string[]? MissingBeforeFiles { get; set; }
        }
    }
}
