using UnityEngine;

namespace EnergyBall
{
    /// <summary>能量球层级查找等 Transform 工具。</summary>
    public static class EnergyBallTransformUtility
    {
        /// <summary>
        /// 在 <paramref name="root"/> 及其子层级（含未激活）中按名称查找第一个 Transform。
        /// 若 <paramref name="root"/> 自身名称匹配也会返回。
        /// </summary>
        public static Transform FindChildByName(Transform root, string objectName)
        {
            if (root == null || string.IsNullOrEmpty(objectName))
                return null;

            if (root.name == objectName)
                return root;

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == objectName)
                    return child;
            }

            return null;
        }
    }
}
