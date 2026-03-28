using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PatchGUI.Core
{
    /// <summary>
    /// Patch execution engine. Currently only simulates apply (checks files, logs, progress).
    /// Extend here to plug real resource operations such as xdelta3 or custom diffing.
    /// </summary>
    public sealed class PatchEngine
    {
        private readonly IPatchLogger _logger;
        private readonly IPatchProgress _progress;

        public PatchEngine(IPatchLogger logger, IPatchProgress progress)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        }

        /// <summary>
        /// Run rules (dryRun=true only validates, no writes).
        /// </summary>
        public async Task RunAsync(
            string gameId,
            string gameRoot,
            PatchRuleSet ruleSet,
            bool dryRun = true,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(gameRoot))
                throw new ArgumentNullException(nameof(gameRoot));
            if (ruleSet == null)
                throw new ArgumentNullException(nameof(ruleSet));

            if (!Directory.Exists(gameRoot))
                throw new DirectoryNotFoundException($"游戏目录不存在：{gameRoot}");

            if (!string.Equals(ruleSet.GameId, gameId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warn($"规则的 GameId=[{ruleSet.GameId}] 与当前游戏 [{gameId}] 不一致。");
            }

            var rules = ruleSet.Rules.Where(r => r.Enabled).ToList();
            if (rules.Count == 0)
            {
                _logger.Info("没有启用的规则，补丁无需执行。");
                _progress.Report(1.0);
                return;
            }

            _logger.Info($"开始 {(dryRun ? "模拟" : "实际")} 执行补丁，规则数：{rules.Count}");

            for (int i = 0; i < rules.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var rule = rules[i];
                double progress = (i / (double)rules.Count);
                _progress.Report(progress);

                _logger.Info($"[{i + 1}/{rules.Count}] 规则 {rule.Id}: {rule.Description}");

                string srcPath = Path.Combine(gameRoot, rule.Source);
                string tgtPath = Path.Combine(gameRoot, rule.Target);

                _logger.Info($"    源: {srcPath}");
                _logger.Info($"    目标: {tgtPath}");
                _logger.Info($"    Action: {rule.Action}");

                if (!File.Exists(srcPath))
                {
                    _logger.Warn($"    源文件不存在：{srcPath}");
                    continue;
                }

                if (dryRun)
                {
                    _logger.Info("    [DryRun] 这里将执行补丁操作（暂不改动文件）。");
                }
                else
                {
                    // 这里将来可以根据 rule.Action 区分 copy/xdelta 等
                    Directory.CreateDirectory(Path.GetDirectoryName(tgtPath)!);
                    File.Copy(srcPath, tgtPath, overwrite: true);
                    _logger.Info("    已复制源文件到目标位置。");
                }

                // 小延时让 UI 有机会刷新进度条，开发调试用，可以删掉
                await Task.Delay(10, cancellationToken);
            }

            _progress.Report(1.0);
            _logger.Info($"补丁执行完成。模式：{(dryRun ? "模拟" : "实际")}。");
        }
    }
}
