using UnityEngine;

namespace EnergyBall
{
    /// <summary>目标本地 Y/Z 圆环散布与碎片本地上下浮动。</summary>
    public static class EnergyBallTargetScatterUtility
    {
        const float PhaseSpread = 1.73f;

        /// <summary>目标本地 Y/Z 平面均匀圆环偏移（绕本地 X 轴）。</summary>
        public static Vector3 GetCircleLocalOffset(int groupIndex, int groupCount, float radius)
        {
            if (groupCount <= 0 || radius <= 0f)
                return Vector3.zero;

            if (groupCount == 1)
                return new Vector3(0f, radius, 0f);

            float angle = Mathf.PI * 2f * (groupIndex + 0.5f) / groupCount;
            return new Vector3(0f, Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        }

        /// <summary>本地 Y/Z 圆环均匀分布，相邻项之间留出 <paramref name="gapRadians"/> 角间隙。</summary>
        public static Vector3 GetCircleLocalOffsetWithGap(
            int index,
            int count,
            float radius,
            float gapRadians,
            float startAngle = Mathf.PI * 0.5f)
        {
            if (count <= 0 || radius <= 0f)
                return Vector3.zero;

            if (count == 1)
                return new Vector3(0f, radius, 0f);

            float step = (Mathf.PI * 2f - count * gapRadians) / count;
            if (step <= 0f)
                return GetCircleLocalOffset(index, count, radius);

            float angle = startAngle + index * (step + gapRadians);
            return new Vector3(0f, Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        }

        /// <summary>本地 X/Z 圆环均匀分布，相邻项之间留出 <paramref name="gapRadians"/> 角间隙。</summary>
        public static Vector3 GetCircleLocalOffsetXZWithGap(
            int index,
            int count,
            float radius,
            float gapRadians,
            float startAngle = Mathf.PI * 0.5f)
        {
            if (count <= 0 || radius <= 0f)
                return Vector3.zero;

            if (count == 1)
                return new Vector3(radius, 0f, 0f);

            float step = (Mathf.PI * 2f - count * gapRadians) / count;
            if (step <= 0f)
            {
                float fallbackAngle = Mathf.PI * 2f * (index + 0.5f) / count;
                return new Vector3(Mathf.Cos(fallbackAngle) * radius, 0f, Mathf.Sin(fallbackAngle) * radius);
            }

            float angle = startAngle + index * (step + gapRadians);
            return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }

        public static Vector3 GetWorldRingPosition(Transform target, int groupIndex, int groupCount, float radius)
        {
            if (target == null)
                return Vector3.zero;

            return target.TransformPoint(GetCircleLocalOffset(groupIndex, groupCount, radius));
        }

        /// <summary>碎片本地 Y 轴正弦浮动偏移（相对父节点）。</summary>
        public static Vector3 GetFragmentFloatLocalOffset(int fragmentIndex, float time, float amplitude, float frequency)
        {
            if (amplitude <= 0f || frequency <= 0f)
                return Vector3.zero;

            float phase = fragmentIndex * PhaseSpread;
            float y = Mathf.Sin(time * frequency + phase) * amplitude;
            return new Vector3(0f, y, 0f);
        }

        public static float GetFragmentFloatPhase(int fragmentIndex) => fragmentIndex * PhaseSpread;
    }
}
