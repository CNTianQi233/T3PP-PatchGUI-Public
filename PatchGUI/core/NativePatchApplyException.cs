using System;

namespace PatchGUI.Core
{
    public sealed class NativePatchApplyException : InvalidOperationException
    {
        public int ReturnCode { get; }

        public string? ReturnCodeDescription { get; }

        public string? NativeReason { get; }

        /// <summary>
        /// 包含所有原生错误消息的详细信息（可能包含具体的文件名）
        /// </summary>
        public string? NativeDetails { get; }

        public NativePatchApplyException(int returnCode, string? returnCodeDescription, string? nativeReason, string? nativeDetails = null)
            : base(BuildMessage(returnCode, returnCodeDescription, nativeReason))
        {
            ReturnCode = returnCode;
            ReturnCodeDescription = returnCodeDescription;
            NativeReason = nativeReason;
            NativeDetails = nativeDetails;
        }

        private static string BuildMessage(int returnCode, string? returnCodeDescription, string? nativeReason)
        {
            string rcText = $"错误码：{returnCode}";
            string desc = string.IsNullOrWhiteSpace(returnCodeDescription) ? string.Empty : $"（{returnCodeDescription}）";

            if (string.IsNullOrWhiteSpace(nativeReason))
                return $"原生补丁应用失败{desc}，{rcText}。";

            return $"原生补丁应用失败{desc}，{rcText}。原因：{nativeReason}";
        }
    }
}

