using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace PatchGUI.Core
{
    /// <summary>
    /// Handles online patch metadata and downloads:
    /// - Parse embedded _origin.sha to get listUrl / refresh interval
    /// - Fetch/cache _list.sha
    /// - Decrypt _list.sha to build the online game list
    /// - Download .t3pp patches per gameId with local caching
    /// </summary>
    public sealed class OnlinePatchService
    {
        private static readonly HttpClient Http = new HttpClient();

        private readonly string _appDataRoot;
        private readonly string _shaDir;
        private readonly string _t3ppDir;

        private bool _initialized;
        private readonly object _initLock = new();

        private OriginConfig? _origin;
        private OnlinePatchIndex? _index;

        private OnlinePatchService()
        {
            _appDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "T3PPPath");

            _shaDir = Path.Combine(_appDataRoot, "sha");
            _t3ppDir = Path.Combine(_appDataRoot, "T3PP");

            Directory.CreateDirectory(_shaDir);
            Directory.CreateDirectory(_t3ppDir);
        }

        public static OnlinePatchService Instance { get; } = new OnlinePatchService();

        public IReadOnlyList<OnlineGameEntry> Games
        {
            get
            {
                if (_index != null && _index.Games != null)
                    return _index.Games;

                return Array.Empty<OnlineGameEntry>();
            }
        }

        /// <summary>
        /// 初始化：只跑一次。
        /// ruleKey1/2 就是你 MainWindow 里用的 RuleKey1/RuleKey2，
        /// 用同一套密钥来解 _origin.sha / _list.sha。
        /// </summary>
        public async Task EnsureInitializedAsync(string ruleKey1, string ruleKey2, Action<string>? log = null)
        {
            bool enableOnline = false;
            if (!enableOnline)
            {
                _initialized = true;
                _index = new OnlinePatchIndex();
                return;
            }
            if (_initialized) return;

            lock (_initLock)
            {
                if (_initialized) return;
                _initialized = true;
            }

            try
            {
                await LoadOriginAndListAsync(ruleKey1, ruleKey2, log).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                log?.Invoke($"[ONLINE] 初始化失败：{ex.Message}");
                // 保持 _index = null，后续会自动退回手动模式
            }
        }
        private static string MaskUrl(string url)
        {
            try
            {
                var uri = new Uri(url);

                // 把域名全打星号，保留 scheme 和路径
                var maskedHost = new string('*', uri.Host.Length);
                return $"{uri.Scheme}://{maskedHost}{uri.PathAndQuery}";
            }
            catch
            {
                // 解析失败就原样返回或直接返回 "***"
                return "***";
            }
        }

        private async Task LoadOriginAndListAsync(string ruleKey1, string ruleKey2, Action<string>? log)
        {
            // 1. 从嵌入资源加载 _origin.sha
            const string originResName = "PatchGUI.res._origin.sha"; // 自己按实际资源名改
            var asm = Assembly.GetExecutingAssembly();

            using var originStream = asm.GetManifestResourceStream(originResName)
                                   ?? throw new InvalidOperationException($"找不到嵌入资源：{originResName}");

            using var ms = new MemoryStream();
            await originStream.CopyToAsync(ms).ConfigureAwait(false);
            var originBytes = ms.ToArray();

            string originJson = RuleCrypto.DecryptRuleShaBytes(originBytes, ruleKey1, ruleKey2);
            _origin = JsonSerializer.Deserialize<OriginConfig>(originJson, _jsonOptions)
                      ?? throw new InvalidDataException("_origin.sha JSON 解析失败");

            if (string.IsNullOrWhiteSpace(_origin.ListUrl))
                throw new InvalidDataException("origin 配置中缺少 listUrl");


            log?.Invoke($"[ONLINE] listUrl = {MaskUrl(_origin.ListUrl)}");

            // 2. 处理本地 _list.sha 缓存x
            string listShaPath = Path.Combine(_shaDir, "_list.sha");
            bool needRefresh = true;

            if (File.Exists(listShaPath) && _origin.UpdateIntervalMinutes > 0)
            {
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(listShaPath);
                if (age.TotalMinutes < _origin.UpdateIntervalMinutes)
                {
                    needRefresh = false;
                }
            }

            if (needRefresh)
            {
                try
                {
                    log?.Invoke("[ONLINE] 正在从云端拉取 _list.sha ...");
                    using var resp = await Http.GetAsync(_origin.ListUrl).ConfigureAwait(false);
                    resp.EnsureSuccessStatusCode();

                    var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    File.WriteAllBytes(listShaPath, bytes);

                    log?.Invoke($"[ONLINE] 已更新本地 _list.sha：{listShaPath}");
                }
                catch (Exception ex)
                {
                    log?.Invoke($"[ONLINE] 拉取 _list.sha 失败：{ex.Message}");
                    if (!File.Exists(listShaPath))
                        throw; // 没有本地缓存就只能抛错
                }
            }
            else
            {
                log?.Invoke("[ONLINE] 本地 _list.sha 还在有效期内，跳过下载。");
            }

            // 3. 解密 _list.sha → 在线游戏列表
            string listJson = RuleCrypto.DecryptRuleShaFile(listShaPath, ruleKey1, ruleKey2);
            _index = JsonSerializer.Deserialize<OnlinePatchIndex>(listJson, _jsonOptions)
                     ?? new OnlinePatchIndex();

            log?.Invoke($"[ONLINE] 在线游戏条目数：{_index.Games.Count}");

        }


        public bool TryGetGame(string gameId, out OnlineGameEntry entry)
        {
            if (_index != null)
            {
                foreach (var g in _index.Games)
                {
                    if (string.Equals(g.Id, gameId, StringComparison.OrdinalIgnoreCase))
                    {
                        entry = g;
                        return true;
                    }
                }
            }

            entry = null!;
            return false;
        }

        /// <summary>
        /// 拿到某个 gameId 对应的补丁文件本地路径。
        /// 内部会按 patchUrl 下载并缓存，如果缓存未过期就直接用。
        /// </summary>
        /// 

        public async Task<string?> GetOrDownloadPatchAsync(string gameId, IPatchLogger? logger = null)
        {
            if (!TryGetGame(gameId, out var g))
                return null;

            if (string.IsNullOrWhiteSpace(g.PatchUrl))
                return null;

            var uri = new Uri(g.PatchUrl, UriKind.Absolute);

            string fileName = g.FileName;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = Path.GetFileName(uri.LocalPath);
            }
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = $"{g.Id}.t3pp";
            }

            string localPath = Path.Combine(_t3ppDir, fileName);

            // 简单缓存策略：存在 + 未过期 就不重下，防止把 R2 拉爆
            if (File.Exists(localPath))
            {
                if (g.MaxAgeMinutes <= 0)
                    return localPath;

                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(localPath);
                if (age.TotalMinutes < g.MaxAgeMinutes)
                {
                    logger?.Info($"[ONLINE] 使用本地缓存补丁：{localPath}");
                    return localPath;
                }
            }

            string tempPath = localPath + ".tmp";

            logger?.Info($"[ONLINE] 正在从云端下载补丁：{MaskUrl(g.PatchUrl)}");
            using var resp = await Http.GetAsync(uri).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            File.WriteAllBytes(tempPath, bytes);

            if (File.Exists(localPath))
                File.Delete(localPath);

            File.Move(tempPath, localPath);

            logger?.Info($"[ONLINE] 补丁已保存到：{localPath}");
            return localPath;
        }

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
    }

    /// <summary>
    /// _origin.sha 解密后的结构
    /// </summary>
    public sealed class OriginConfig
    {
        public int Version { get; set; } = 1;
        public string ListUrl { get; set; } = string.Empty;

        /// <summary>
        /// _list.sha 的刷新间隔（分钟），用来防止频繁打 R2。
        /// </summary>
        public int UpdateIntervalMinutes { get; set; } = 60;
    }

    /// <summary>
    /// _list.sha 解密后的根结构
    /// </summary>
    public sealed class OnlinePatchIndex
    {
        public int Version { get; set; } = 1;
        public List<OnlineGameEntry> Games { get; set; } = new();
    }

    /// <summary>
    /// 单个游戏的在线补丁配置
    /// </summary>
    public sealed class OnlineGameEntry
    {
        /// <summary>
        /// 要和菜单里的 Tag / _currentGameId 对上，例如 "th_tcl"
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 显示用名字（你可以拿来自动生成菜单）
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// R2 上的补丁直链
        /// </summary>
        public string PatchUrl { get; set; } = string.Empty;

        /// <summary>
        /// 本地缓存文件名，不填就从 PatchUrl 里取文件名
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// 本地补丁缓存多久后认为过期（分钟），0 或负数 = 永不过期
        /// </summary>
        public int MaxAgeMinutes { get; set; } = 1440;
    }
}
