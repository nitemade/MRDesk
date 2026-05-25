using DG.Tweening;
using UnityEngine;

namespace EnergyBall
{
    /// <summary>
    /// DOTween 通用工具：沿 Transform 法线（本地 up）的浮动、旋转等动画。
    /// </summary>
    public static class DOTweenFloatUtility
    {
        const float DefaultDuration = 1f;
        /// <summary>未传入 tweenId 时，各动画 Id 的前缀（后接实体 Id 保证唯一）。</summary>
        const string FloatTweenIdPrefix = "FloatAlongNormal";
        const string RotateTweenIdPrefix = "RotateAlongNormal";
        const string RotateLoopTweenIdPrefix = "RotateAlongNormalLoop";

        /// <summary>
        /// 沿 <paramref name="target"/> 法线方向，以 <paramref name="intensity"/> 为振幅上下浮动（无限循环）。
        /// </summary>
        /// <param name="target">目标 Transform</param>
        /// <param name="intensity">浮动幅度（沿法线方向的位移距离）</param>
        /// <param name="duration">单程时长（从 -intensity 到 +intensity）</param>
        /// <param name="tweenId">自定义 Tween Id；为 null 时自动生成</param>
        /// <returns>本次浮动动画的 Id；参数无效时返回 null</returns>
        public static string FloatAlongNormal(Transform target, float intensity, float duration = DefaultDuration, string tweenId = null)
        {
            if (target == null || intensity <= 0f || duration <= 0f)
                return null;

            tweenId ??= $"{FloatTweenIdPrefix}_{target.GetEntityId()}";
            DOTween.Kill(target, tweenId);

            Vector3 origin = target.localPosition;

            DOTween.To(
                    () => -intensity,
                    offset => target.localPosition = origin + Vector3.up * offset,
                    intensity,
                    duration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetTarget(target)
                .SetId(tweenId);

            return tweenId;
        }

        /// <summary>
        /// 绕 <paramref name="target"/> 法线（本地 up）以 <paramref name="angle"/> 为振幅往复旋转（无限循环）。
        /// </summary>
        /// <param name="target">目标 Transform</param>
        /// <param name="angle">旋转振幅（度，在 ±angle 之间往复）</param>
        /// <param name="duration">单程时长（从 -angle 到 +angle）</param>
        /// <param name="tweenId">自定义 Tween Id；为 null 时自动生成</param>
        /// <returns>本次旋转动画的 Id；参数无效时返回 null</returns>
        public static string RotateAlongNormal(Transform target, float angle, float duration = DefaultDuration, string tweenId = null)
        {
            if (target == null || angle <= 0f || duration <= 0f)
                return null;

            tweenId ??= $"{RotateTweenIdPrefix}_{target.GetEntityId()}";
            DOTween.Kill(target, tweenId);

            Vector3 origin = target.localEulerAngles;

            DOTween.To(
                    () => -angle,
                    offset =>
                    {
                        Vector3 euler = origin;
                        euler.y += offset;
                        target.localEulerAngles = euler;
                    },
                    angle,
                    duration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetTarget(target)
                .SetId(tweenId);

            return tweenId;
        }

        /// <summary>
        /// 绕 <paramref name="target"/> 法线（本地 up）单方向持续旋转（无限循环）。
        /// 使用 LocalAxisAdd + Incremental，避免每圈 Restart 时欧拉角 360°→0° 的回跳顿挫。
        /// </summary>
        /// <param name="target">目标 Transform</param>
        /// <param name="durationPerRevolution">转一圈所用时间（秒）</param>
        /// <param name="tweenId">自定义 Tween Id；为 null 时自动生成</param>
        /// <returns>本次旋转动画的 Id；参数无效时返回 null</returns>
        public static string RotateAlongNormalLoop(Transform target, float durationPerRevolution, string tweenId = null)
        {
            if (target == null || durationPerRevolution <= 0f)
                return null;

            tweenId ??= $"{RotateLoopTweenIdPrefix}_{target.GetEntityId()}";
            DOTween.Kill(target, tweenId);

            target.DOLocalRotate(new Vector3(0f, 360f, 0f), durationPerRevolution, RotateMode.LocalAxisAdd)
                .SetEase(Ease.Linear)
                .SetLoops(-1, LoopType.Incremental)
                .SetTarget(target)
                .SetId(tweenId);

            return tweenId;
        }

        /// <summary>
        /// 停止指定 Transform 上以它为 Target 的所有 DOTween 动画。
        /// </summary>
        public static void StopFloat(Transform target, bool complete = false)
        {
            if (target == null)
                return;

            DOTween.Kill(target, complete);
        }

        /// <summary>
        /// 仅停止指定 Transform 上匹配 <paramref name="tweenId"/> 的动画。
        /// </summary>
        public static void StopById(Transform target, object tweenId, bool complete = false)
        {
            if (target == null || tweenId == null)
                return;

            DOTween.Kill(target, tweenId, complete);
        }

        /// <summary>
        /// 暂停指定 Transform 上以它为 Target 的所有 DOTween 动画。
        /// </summary>
        public static void PauseFloat(Transform target)
        {
            if (target == null)
                return;

            DOTween.Pause(target);
        }

        /// <summary>
        /// 仅暂停指定 Transform 上匹配 <paramref name="tweenId"/> 的动画。
        /// </summary>
        public static void PauseById(Transform target, object tweenId)
        {
            if (target == null || tweenId == null)
                return;

            var tweens = DOTween.TweensById(tweenId, false);
            if (tweens == null)
                return;

            for (int i = 0; i < tweens.Count; i++)
            {
                Tween t = tweens[i];
                if (t != null && t.IsActive() && (Object)t.target == target)
                    t.Pause();
            }
        }
    }
}
