using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace IndustryTycoon.Persistence
{
    public enum M9SaveLoadStatus
    {
        LoadedPrimary = 0,
        RecoveredBackup = 1,
        FreshNoSave = 2,
        FreshInvalidSave = 3,
        FreshUnsupportedVersion = 4,
        FreshIoFailure = 5
    }

    public sealed class M9SaveLoadResult
    {
        internal M9SaveLoadResult(
            M9SaveLoadStatus status,
            M9SaveData data,
            string diagnostic,
            bool wasMigrated = false)
        {
            Status = status;
            Data = data;
            Diagnostic = diagnostic ?? string.Empty;
            WasMigrated = wasMigrated;
        }

        public M9SaveLoadStatus Status { get; }
        public M9SaveData Data { get; }
        public string Diagnostic { get; }
        public bool WasMigrated { get; }
        public bool LoadedExisting => Status == M9SaveLoadStatus.LoadedPrimary
                                      || Status == M9SaveLoadStatus.RecoveredBackup;
        public bool ShouldRewritePrimary => Status != M9SaveLoadStatus.LoadedPrimary
                                            || WasMigrated;
    }

    public enum M9SaveWriteStatus
    {
        Success = 0,
        ValidationFailed = 1,
        IoFailure = 2
    }

    public sealed class M9SaveWriteResult
    {
        internal M9SaveWriteResult(
            M9SaveWriteStatus status,
            M9SaveData persistedData,
            string diagnostic)
        {
            Status = status;
            PersistedData = persistedData;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public M9SaveWriteStatus Status { get; }
        public M9SaveData PersistedData { get; }
        public string Diagnostic { get; }
        public bool IsSuccess => Status == M9SaveWriteStatus.Success
                                 && PersistedData != null;
    }

    public sealed class M9LocalSaveStore
    {
        public const string DefaultFileName = "industry_tycoon_save.json";

        private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false, true);

        private readonly object _ioGate = new object();
        private readonly IUtcClock _clock;
        private readonly M9SaveValidationSettings _validationSettings;
        private readonly bool _preserveInvalidFiles;

        public M9LocalSaveStore(
            string directoryPath,
            IUtcClock clock = null,
            M9SaveValidationSettings validationSettings = null,
            string fileName = DefaultFileName,
            bool? preserveInvalidFiles = null)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                throw new ArgumentException("A save directory is required.", nameof(directoryPath));
            }

            if (string.IsNullOrWhiteSpace(fileName)
                || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
            {
                throw new ArgumentException("Save file name must be one valid file name.", nameof(fileName));
            }

            DirectoryPath = Path.GetFullPath(directoryPath);
            PrimaryPath = Path.Combine(DirectoryPath, fileName);
            TemporaryPath = PrimaryPath + ".tmp";
            BackupPath = PrimaryPath + ".bak";
            _clock = clock ?? SystemUtcClock.Instance;
            _validationSettings = validationSettings
                                  ?? M9SaveValidationSettings.CreateDefault();
            _preserveInvalidFiles = preserveInvalidFiles
                                    ?? (Application.isEditor || Debug.isDebugBuild);
        }

        public string DirectoryPath { get; }
        public string PrimaryPath { get; }
        public string TemporaryPath { get; }
        public string BackupPath { get; }

        public static M9LocalSaveStore CreateForPersistentDataPath(
            IUtcClock clock = null,
            M9SaveValidationSettings validationSettings = null,
            bool? preserveInvalidFiles = null)
        {
            return new M9LocalSaveStore(
                Application.persistentDataPath,
                clock,
                validationSettings,
                DefaultFileName,
                preserveInvalidFiles);
        }

        public M9SaveLoadResult Load()
        {
            lock (_ioGate)
            {
                long now = _clock.UtcNowUnixSeconds;
                bool hasPrimary = File.Exists(PrimaryPath);
                bool hasBackup = File.Exists(BackupPath);
                if (!hasPrimary && !hasBackup)
                {
                    return FreshResult(M9SaveLoadStatus.FreshNoSave, now, null);
                }

                M9SaveDecodeResult primaryDecode = null;
                string primaryIoFailure = null;
                if (hasPrimary)
                {
                    if (TryReadAndDecode(PrimaryPath, now, out primaryDecode, out primaryIoFailure)
                        && primaryDecode.IsSuccess)
                    {
                        return new M9SaveLoadResult(
                            M9SaveLoadStatus.LoadedPrimary,
                            primaryDecode.Data,
                            null,
                            primaryDecode.WasMigrated);
                    }
                }

                M9SaveDecodeResult backupDecode = null;
                string backupIoFailure = null;
                if (hasBackup
                    && TryReadAndDecode(BackupPath, now, out backupDecode, out backupIoFailure)
                    && backupDecode.IsSuccess)
                {
                    TryRepairPrimaryFromBackup(now, out string repairFailure);

                    return new M9SaveLoadResult(
                        M9SaveLoadStatus.RecoveredBackup,
                        backupDecode.Data,
                        JoinDiagnostics(
                            BuildDiagnostic(primaryDecode, primaryIoFailure),
                            repairFailure),
                        backupDecode.WasMigrated);
                }

                if (hasPrimary && primaryDecode != null && !primaryDecode.IsSuccess)
                {
                    TryRetireInvalidFile(PrimaryPath, now);
                }

                if (hasBackup && backupDecode != null && !backupDecode.IsSuccess)
                {
                    TryRetireInvalidFile(BackupPath, now);
                }

                bool hadIoFailure = !string.IsNullOrEmpty(primaryIoFailure)
                                    || !string.IsNullOrEmpty(backupIoFailure);
                M9SaveDecodeResult rejectedDecode = primaryDecode ?? backupDecode;
                M9SaveLoadStatus fallbackStatus;
                if (hadIoFailure)
                {
                    fallbackStatus = M9SaveLoadStatus.FreshIoFailure;
                }
                else if (rejectedDecode != null
                         && rejectedDecode.Status == M9SaveDecodeStatus.UnsupportedVersion)
                {
                    fallbackStatus = M9SaveLoadStatus.FreshUnsupportedVersion;
                }
                else
                {
                    fallbackStatus = M9SaveLoadStatus.FreshInvalidSave;
                }

                string diagnostic = JoinDiagnostics(
                    BuildDiagnostic(primaryDecode, primaryIoFailure),
                    BuildDiagnostic(backupDecode, backupIoFailure));
                return FreshResult(fallbackStatus, now, diagnostic);
            }
        }

        public M9SaveWriteResult Save(M9SaveData data, bool prettyPrint = false)
        {
            lock (_ioGate)
            {
                long now = _clock.UtcNowUnixSeconds;
                if (!M9SaveValidator.TryNormalize(
                        data,
                        _validationSettings,
                        now,
                        out M9SaveData candidate,
                        out string validationFailure))
                {
                    return new M9SaveWriteResult(
                        M9SaveWriteStatus.ValidationFailed,
                        null,
                        validationFailure);
                }

                if (M9UnixTime.IsPlausible(now))
                {
                    candidate.lastWriteUtcUnixSeconds = Math.Max(
                        candidate.lastWriteUtcUnixSeconds,
                        now);
                }

                if (!M9SaveCodec.TryEncode(
                        candidate,
                        _validationSettings,
                        now,
                        out string json,
                        out M9SaveData persistedData,
                        out string encodeFailure,
                        prettyPrint))
                {
                    return new M9SaveWriteResult(
                        M9SaveWriteStatus.ValidationFailed,
                        null,
                        encodeFailure);
                }

                try
                {
                    Directory.CreateDirectory(DirectoryPath);
                    WriteTemporaryFile(json);
                    CommitTemporaryFile();
                    return new M9SaveWriteResult(
                        M9SaveWriteStatus.Success,
                        persistedData,
                        null);
                }
                catch (Exception exception)
                {
                    TryDeleteFileIgnoringFailure(TemporaryPath);
                    return new M9SaveWriteResult(
                        M9SaveWriteStatus.IoFailure,
                        null,
                        $"Save write failed: {exception.Message}");
                }
            }
        }

        public bool TryDeleteSave(out string failureReason)
        {
            lock (_ioGate)
            {
                failureReason = null;
                try
                {
                    TryDeleteFile(TemporaryPath);
                    TryDeleteFile(PrimaryPath);
                    TryDeleteFile(BackupPath);
                    return true;
                }
                catch (Exception exception)
                {
                    failureReason = $"Save reset failed: {exception.Message}";
                    return false;
                }
            }
        }

        private M9SaveLoadResult FreshResult(
            M9SaveLoadStatus status,
            long now,
            string diagnostic)
        {
            M9SaveData fresh = M9SaveData.CreateFresh(now);
            if (!M9SaveValidator.TryNormalize(
                    fresh,
                    _validationSettings,
                    now,
                    out M9SaveData normalizedFresh,
                    out string validationFailure))
            {
                return new M9SaveLoadResult(
                    status,
                    fresh,
                    JoinDiagnostics(diagnostic, validationFailure));
            }

            return new M9SaveLoadResult(status, normalizedFresh, diagnostic);
        }

        private bool TryReadAndDecode(
            string path,
            long fallbackUtcUnixSeconds,
            out M9SaveDecodeResult decodeResult,
            out string ioFailure)
        {
            decodeResult = null;
            ioFailure = null;
            try
            {
                string json = File.ReadAllText(path, Utf8WithoutBom);
                decodeResult = M9SaveCodec.Decode(
                    json,
                    _validationSettings,
                    fallbackUtcUnixSeconds);
                return true;
            }
            catch (DecoderFallbackException exception)
            {
                decodeResult = M9SaveDecodeResult.Failed(
                    M9SaveDecodeStatus.MalformedJson,
                    $"Save file is not valid UTF-8: {exception.Message}");
                return true;
            }
            catch (Exception exception)
            {
                ioFailure = $"Could not read '{path}': {exception.Message}";
                return false;
            }
        }

        private void WriteTemporaryFile(string json)
        {
            using (var stream = new FileStream(
                       TemporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None))
            using (var writer = new StreamWriter(stream, Utf8WithoutBom))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }
        }

        private void CommitTemporaryFile()
        {
            if (!File.Exists(PrimaryPath))
            {
                File.Move(TemporaryPath, PrimaryPath);
                return;
            }

            try
            {
                File.Replace(TemporaryPath, PrimaryPath, BackupPath, true);
                return;
            }
            catch (PlatformNotSupportedException)
            {
                // Some Unity target filesystems do not implement File.Replace.
            }
            catch (IOException)
            {
                // Preserve the previous primary before using the portable fallback.
            }

            if (File.Exists(BackupPath))
            {
                File.Delete(BackupPath);
            }

            File.Move(PrimaryPath, BackupPath);
            try
            {
                File.Move(TemporaryPath, PrimaryPath);
            }
            catch
            {
                if (!File.Exists(PrimaryPath) && File.Exists(BackupPath))
                {
                    File.Move(BackupPath, PrimaryPath);
                }

                throw;
            }
        }

        private void TryQuarantineInvalidFile(string path, long utcNowUnixSeconds)
        {
            if (!_preserveInvalidFiles || !File.Exists(path))
            {
                return;
            }

            string timestamp = M9UnixTime.IsPlausible(utcNowUnixSeconds)
                ? utcNowUnixSeconds.ToString()
                : "unknown-time";
            string baseDestination = path + ".corrupt." + timestamp;
            string destination = baseDestination;
            int suffix = 1;
            while (File.Exists(destination) && suffix < 1000)
            {
                destination = baseDestination + "." + suffix;
                suffix++;
            }

            try
            {
                File.Move(path, destination);
            }
            catch
            {
                // Diagnostics preservation is best effort and must never block fallback.
            }
        }

        private void TryRetireInvalidFile(string path, long utcNowUnixSeconds)
        {
            TryQuarantineInvalidFile(path, utcNowUnixSeconds);
            if (!_preserveInvalidFiles && File.Exists(path))
            {
                TryDeleteFileIgnoringFailure(path);
            }
        }

        private bool TryRepairPrimaryFromBackup(
            long utcNowUnixSeconds,
            out string failureReason)
        {
            failureReason = null;
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                TryDeleteFile(TemporaryPath);
                File.Copy(BackupPath, TemporaryPath, true);

                if (File.Exists(PrimaryPath))
                {
                    TryQuarantineInvalidFile(PrimaryPath, utcNowUnixSeconds);
                    if (File.Exists(PrimaryPath))
                    {
                        File.Delete(PrimaryPath);
                    }
                }

                File.Move(TemporaryPath, PrimaryPath);
                return true;
            }
            catch (Exception exception)
            {
                TryDeleteFileIgnoringFailure(TemporaryPath);
                failureReason = $"Backup loaded, but primary repair failed: {exception.Message}";
                return false;
            }
        }

        private static string BuildDiagnostic(
            M9SaveDecodeResult decodeResult,
            string ioFailure)
        {
            if (!string.IsNullOrEmpty(ioFailure))
            {
                return ioFailure;
            }

            return decodeResult != null && !decodeResult.IsSuccess
                ? $"{decodeResult.Status}: {decodeResult.Diagnostic}"
                : null;
        }

        private static string JoinDiagnostics(string first, string second)
        {
            if (string.IsNullOrEmpty(first))
            {
                return second ?? string.Empty;
            }

            if (string.IsNullOrEmpty(second))
            {
                return first;
            }

            return first + " | " + second;
        }

        private static void TryDeleteFile(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static void TryDeleteFileIgnoringFailure(string path)
        {
            try
            {
                TryDeleteFile(path);
            }
            catch
            {
                // Cleanup must not hide the original load/write failure.
            }
        }
    }
}
