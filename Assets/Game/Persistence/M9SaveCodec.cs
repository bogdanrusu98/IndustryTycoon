using System;
using UnityEngine;

namespace IndustryTycoon.Persistence
{
    public enum M9SaveDecodeStatus
    {
        Success = 0,
        Empty = 1,
        MalformedJson = 2,
        InvalidSchema = 3,
        UnsupportedVersion = 4,
        ValidationFailed = 5
    }

    public sealed class M9SaveDecodeResult
    {
        private M9SaveDecodeResult(
            M9SaveDecodeStatus status,
            M9SaveData data,
            string diagnostic)
        {
            Status = status;
            Data = data;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public M9SaveDecodeStatus Status { get; }
        public M9SaveData Data { get; }
        public string Diagnostic { get; }
        public bool IsSuccess => Status == M9SaveDecodeStatus.Success && Data != null;

        internal static M9SaveDecodeResult Succeeded(M9SaveData data)
        {
            return new M9SaveDecodeResult(M9SaveDecodeStatus.Success, data, null);
        }

        internal static M9SaveDecodeResult Failed(
            M9SaveDecodeStatus status,
            string diagnostic)
        {
            return new M9SaveDecodeResult(status, null, diagnostic);
        }
    }

    public static class M9SaveCodec
    {
        [Serializable]
        private sealed class SaveHeader
        {
            public string schema = null;
            public int version = 0;
        }

        public static M9SaveDecodeResult Decode(
            string json,
            M9SaveValidationSettings validationSettings,
            long fallbackUtcUnixSeconds)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return M9SaveDecodeResult.Failed(
                    M9SaveDecodeStatus.Empty,
                    "Save JSON is empty.");
            }

            string trimmedJson = json.Trim();
            if (trimmedJson.Length < 2
                || trimmedJson[0] != '{'
                || trimmedJson[trimmedJson.Length - 1] != '}')
            {
                return M9SaveDecodeResult.Failed(
                    M9SaveDecodeStatus.MalformedJson,
                    "Save JSON does not contain an object root.");
            }

            SaveHeader header;
            try
            {
                header = JsonUtility.FromJson<SaveHeader>(trimmedJson);
            }
            catch (Exception exception)
            {
                return M9SaveDecodeResult.Failed(
                    M9SaveDecodeStatus.MalformedJson,
                    $"Save header could not be decoded: {exception.Message}");
            }

            if (header == null
                || !string.Equals(header.schema, M9SaveSchema.Id, StringComparison.Ordinal))
            {
                return M9SaveDecodeResult.Failed(
                    M9SaveDecodeStatus.InvalidSchema,
                    "Save schema identifier is unsupported.");
            }

            // Future schema migrations enter through this explicit version switch.
            switch (header.version)
            {
                case 1:
                    return DecodeVersion1(
                        trimmedJson,
                        validationSettings,
                        fallbackUtcUnixSeconds);
                default:
                    return M9SaveDecodeResult.Failed(
                        M9SaveDecodeStatus.UnsupportedVersion,
                        $"Save version {header.version} is unsupported.");
            }
        }

        public static bool TryEncode(
            M9SaveData data,
            M9SaveValidationSettings validationSettings,
            long fallbackUtcUnixSeconds,
            out string json,
            out M9SaveData normalizedData,
            out string failureReason,
            bool prettyPrint = false)
        {
            json = null;
            normalizedData = null;
            failureReason = null;
            if (!M9SaveValidator.TryNormalize(
                    data,
                    validationSettings,
                    fallbackUtcUnixSeconds,
                    out normalizedData,
                    out failureReason))
            {
                return false;
            }

            try
            {
                json = JsonUtility.ToJson(normalizedData, prettyPrint);
            }
            catch (Exception exception)
            {
                failureReason = $"Save JSON could not be encoded: {exception.Message}";
                json = null;
                normalizedData = null;
                return false;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                failureReason = "Save JSON encoder returned no data.";
                normalizedData = null;
                return false;
            }

            return true;
        }

        private static M9SaveDecodeResult DecodeVersion1(
            string json,
            M9SaveValidationSettings validationSettings,
            long fallbackUtcUnixSeconds)
        {
            M9SaveData decoded;
            try
            {
                decoded = JsonUtility.FromJson<M9SaveData>(json);
            }
            catch (Exception exception)
            {
                return M9SaveDecodeResult.Failed(
                    M9SaveDecodeStatus.MalformedJson,
                    $"Save body could not be decoded: {exception.Message}");
            }

            if (!M9SaveValidator.TryNormalize(
                    decoded,
                    validationSettings,
                    fallbackUtcUnixSeconds,
                    out M9SaveData normalized,
                    out string validationFailure))
            {
                return M9SaveDecodeResult.Failed(
                    M9SaveDecodeStatus.ValidationFailed,
                    validationFailure);
            }

            return M9SaveDecodeResult.Succeeded(normalized);
        }
    }
}
