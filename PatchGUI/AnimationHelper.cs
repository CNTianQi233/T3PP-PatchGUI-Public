using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PatchGUI
{
    public class AnimationHelper
    {
        // 缓存正在运行动画的画刷实例，防止外部（如 ThemeManager）重置资源字典导致实例丢失
        private static readonly Dictionary<string, SolidColorBrush> _runningBrushes = new Dictionary<string, SolidColorBrush>();

        /// <summary>
        /// 开始一个颜色渐变动画应用于资源字典中指定 <see cref="SolidColorBrush"/>
        /// </summary>
        /// <param name="resourceDict">包含目标画刷的资源字典</param>
        /// <param name="key">资源字典中画刷的键</param>
        /// <param name="toColor">动画结束时的颜色</param>
        /// <param name="durationMs">动画持续时间（毫秒）</param>
        public static void ResBrushBeginAnimation(ResourceDictionary resourceDict, String key, System.Windows.Media.Color toColor, int durationMs = 600)
        {
            SolidColorBrush? targetBrush = null;
            bool needsUpdateDict = false;

            // 1. 尝试获取可用的 Mutable 画刷实例
            if (_runningBrushes.TryGetValue(key, out var cachedBrush))
            {
                if (cachedBrush.IsFrozen)
                {
                    targetBrush = cachedBrush.Clone();
                    needsUpdateDict = true; // 实例变了，必须更新字典
                }
                else
                {
                    targetBrush = cachedBrush;
                    // 如果字典里被外部替换成了别的对象（或被清空），我们需要重新注入
                    if (!resourceDict.Contains(key) || resourceDict[key] != targetBrush)
                    {
                        needsUpdateDict = true;
                    }
                }
            }
            else if (resourceDict.Contains(key) && resourceDict[key] is SolidColorBrush existingBrush)
            {
                if (existingBrush.IsFrozen)
                {
                    targetBrush = existingBrush.Clone();
                }
                else
                {
                    targetBrush = existingBrush;
                }
                needsUpdateDict = true; // 新加入缓存，肯定需要更新/确认字典
            }

            if (targetBrush != null)
            {
                // 2. [可打断动画]
                // 关键点：From 使用“当前值”（可能是动画中的中间值），这样在切换主题中途再次切换时不会从头重播。
                // 并使用 SnapshotAndReplace，让新动画从当前值无缝接管。
                System.Windows.Media.Color fromColor = targetBrush.Color;
                ColorAnimation colorAnimation = new ColorAnimation
                {
                    From = fromColor,
                    To = toColor,
                    Duration = TimeSpan.FromMilliseconds(durationMs),
                    EasingFunction = new PowerEase { EasingMode = EasingMode.EaseInOut },
                    FillBehavior = FillBehavior.HoldEnd,
                };

                targetBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnimation, HandoffBehavior.SnapshotAndReplace);

                // 3. 动画开始后，再更新资源字典和缓存
                if (needsUpdateDict)
                {
                    resourceDict[key] = targetBrush;
                    _runningBrushes[key] = targetBrush;
                }
            }
        }

        /// <summary>
        /// 开始一个颜色渐变动画应用于应用程序级别资源字典中指定 <see cref="SolidColorBrush"/>
        /// </summary>
        /// <param name="key">资源字典中画刷的键</param>
        /// <param name="toColor">动画结束时的颜色</param>
        /// <param name="durationMs">动画持续时间（毫秒）</param>
        public static void ResBrushBeginAnimation(string key, System.Windows.Media.Color toColor, int durationMs = 600)
        {
            ResBrushBeginAnimation(System.Windows.Application.Current.Resources, key, toColor, durationMs);
        }
    }
}
