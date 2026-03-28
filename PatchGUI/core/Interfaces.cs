using System;

namespace PatchGUI.Core
{
    /// <summary>
    /// 补丁过程中的日志输出接口。
    /// 由前端实现（把消息写到控制台 TextBox / 文件等）。
    /// </summary>
    public interface IPatchLogger
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
    }

    /// <summary>
    /// 补丁过程中的进度汇报接口。
    /// value 范围 0 ~ 1。
    /// </summary>
    public interface IPatchProgress
    {
        void Report(double value);
    }
}
