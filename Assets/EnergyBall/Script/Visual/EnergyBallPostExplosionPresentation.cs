using DG.Tweening;
using UnityEngine;

namespace EnergyBall
{
    /// <summary>爆炸后的内层缩放和轨道菜单显隐、布局。</summary>
    [DisallowMultipleComponent]
    public class EnergyBallPostExplosionPresentation : MonoBehaviour
    {
        [Header("爆炸后内层 / 轨道菜单")]
        [SerializeField] Transform innerLayer;
        [SerializeField] string innerLayerObjectName = "EnergyLayer";
        [SerializeField] Transform orbitalMenuRoot;
        [SerializeField] string orbitalMenuRootName = "OrbitalMenuRoot";
        [SerializeField, Range(0.01f, 1f)] float innerLayerExplodedScale = 0.6f;
        [SerializeField] float innerLayerShrinkDuration = 0.35f;
        [SerializeField] int orbitItemCount = 5;
        [SerializeField] string orbitItemNamePrefix = "OrbitItem_";
        [SerializeField] float orbitRadiusPadding = 0.08f;
        [SerializeField] float orbitItemGapDegrees = 12f;

        [Header("轨道菜单朝向")]
        [SerializeField] Camera referenceCamera;
        [Tooltip("轨道根绕世界竖直轴的水平方位角变化超过该值（度）时才更新，避免微小抖动。")]
        [SerializeField] float orbitalMenuAlignThresholdDegrees = 10f;

        [Header("OrbitItem Billboarding")]
        [Tooltip("OrbitItem 使用 Quad 时，是否需要反向朝向（有些 Quad/材质可能看起来是背面）。")]
        [SerializeField] bool orbitItemFlipFacing;
        [SerializeField, Min(0f)] float orbitTransitionSmoothTime = 0.14f;
        [SerializeField, Min(0f)] float mainItemLineAngleThresholdDeg = 12f;

        Transform[] _orbitItems;
        Vector3 _innerLayerHomeLocalScale = Vector3.one;
        bool _innerLayerHomeLocalScaleCached;
        bool _postExplodePresentationActive;
        bool _orbitalMenuRotationInitialized;
        float _cachedOrbitalMenuYawDegrees;
        int _mainItemIndex = -1;
        int _targetMainItemIndex = -1;
        float _orbitRotationOffsetDeg;
        float _targetOrbitRotationOffsetDeg;
        float _orbitRotationOffsetVelocityDeg;
        bool _isOrbitTransitioning;

        const string InnerLayerShrinkTweenId = "EnergyBallInnerShrink";

        /// <summary>解析引用、启动时缓存 home scale 一次，并在未爆炸时隐藏轨道菜单。</summary>
        public void Initialize()
        {
            CacheReferences();
            CacheInnerLayerHomeLocalScaleOnce();
        }

        /// <summary>Inspector 中校验角间隙，避免超过 360° 均分上限。</summary>
        void OnValidate()
        {
            float maxGap = orbitItemCount > 0 ? 360f / orbitItemCount - 0.01f : 0f;
            if (orbitItemGapDegrees > maxGap)
                orbitItemGapDegrees = maxGap;

            if (orbitalMenuAlignThresholdDegrees < 0f)
                orbitalMenuAlignThresholdDegrees = 0f;

            if (mainItemLineAngleThresholdDeg < 0f)
                mainItemLineAngleThresholdDeg = 0f;

            if (orbitTransitionSmoothTime < 0f)
                orbitTransitionSmoothTime = 0f;

            if (orbitalMenuRoot != null)
                CollectOrbitItems();
        }

        void LateUpdate()
        {
            if (!_postExplodePresentationActive || orbitalMenuRoot == null || !orbitalMenuRoot.gameObject.activeSelf)
                return;

            Camera cam = referenceCamera != null ? referenceCamera : Camera.main;
            TryAlignOrbitalMenuRootToCamera(cam, force: false);
            UpdateOrbitTransition(Time.deltaTime);
            RefreshMainItemByCamera(cam, immediate: false);
            LayoutOrbitItems();
            AlignOrbitItemsToCamera(cam);
        }

        /// <summary>刷新内层/轨道根引用，收集 orbit items，并按状态隐藏菜单。</summary>
        public void CacheReferences()
        {
            ResolveReferences();
            CollectOrbitItems();

            if (!_postExplodePresentationActive && orbitalMenuRoot != null)
                orbitalMenuRoot.gameObject.SetActive(false);
        }

        /// <summary>仅在首次 Initialize 时记录内层 home localScale，运行期不再覆盖。</summary>
        void CacheInnerLayerHomeLocalScaleOnce()
        {
            if (_innerLayerHomeLocalScaleCached)
                return;

            if (innerLayer == null)
                ResolveReferences();

            if (innerLayer == null)
                return;

            _innerLayerHomeLocalScale = innerLayer.localScale;
            _innerLayerHomeLocalScaleCached = true;
        }

        /// <summary>爆炸后表现：内层 localScale 缩至固定值、激活轨道菜单并布局 orbit items。</summary>
        public void Apply()
        {
            if (_postExplodePresentationActive)
                return;

            CacheReferences();

            if (innerLayer != null)
            {
                Vector3 targetScale = Vector3.one * innerLayerExplodedScale;
                DOTween.Kill(InnerLayerShrinkTweenId);

                if (innerLayerShrinkDuration > 0f)
                {
                    innerLayer.DOScale(targetScale, innerLayerShrinkDuration)
                        .SetEase(Ease.InOutSine)
                        .SetTarget(innerLayer)
                        .SetId(InnerLayerShrinkTweenId);
                }
                else
                {
                    innerLayer.localScale = targetScale;
                }
            }

            if (orbitalMenuRoot != null)
            {
                orbitalMenuRoot.localPosition = Vector3.zero;
                orbitalMenuRoot.gameObject.SetActive(true);
                Camera cam = referenceCamera != null ? referenceCamera : Camera.main;
                TryAlignOrbitalMenuRootToCamera(cam, force: true);
                LayoutOrbitItems();
                RefreshMainItemByCamera(cam, immediate: true);
                AlignOrbitItemsToCamera(cam);
            }

            _postExplodePresentationActive = true;
        }

        /// <summary>恢复内层 home scale（带过渡）、停止缩放 tween，并隐藏轨道菜单。</summary>
        public void ResetPresentation()
        {
            if (orbitalMenuRoot != null)
            {
                orbitalMenuRoot.gameObject.SetActive(false);
                orbitalMenuRoot.localRotation = Quaternion.identity;
            }

            _orbitalMenuRotationInitialized = false;
            _postExplodePresentationActive = false;
            _isOrbitTransitioning = false;
            _mainItemIndex = -1;
            _targetMainItemIndex = -1;
            _orbitRotationOffsetDeg = 0f;
            _targetOrbitRotationOffsetDeg = 0f;
            _orbitRotationOffsetVelocityDeg = 0f;

            if (innerLayer == null)
                return;

            DOTween.Kill(InnerLayerShrinkTweenId);

            if (innerLayerShrinkDuration > 0f)
            {
                innerLayer.DOScale(_innerLayerHomeLocalScale, innerLayerShrinkDuration)
                    .SetEase(Ease.InOutSine)
                    .SetTarget(innerLayer)
                    .SetId(InnerLayerShrinkTweenId);
            }
            else
            {
                innerLayer.localScale = _innerLayerHomeLocalScale;
            }
        }

        /// <summary>按名称查找 <see cref="innerLayer"/> 与 <see cref="orbitalMenuRoot"/>（未手动赋值时）。</summary>
        void ResolveReferences()
        {
            if (innerLayer == null)
                innerLayer = EnergyBallTransformUtility.FindChildByName(transform, innerLayerObjectName);

            if (orbitalMenuRoot == null)
                orbitalMenuRoot = EnergyBallTransformUtility.FindChildByName(transform, orbitalMenuRootName);

            if (referenceCamera == null)
                referenceCamera = Camera.main;
        }

        /// <summary>
        /// 保持轨道环在世界水平面（绕世界 Y），仅将 OrbitalMenuRoot 绕竖直轴转向相机水平投影。
        /// 方位角变化小于阈值时跳过，避免微抖。
        /// </summary>
        /// <returns>是否更新了 OrbitalMenuRoot 朝向。</returns>
        bool TryAlignOrbitalMenuRootToCamera(Camera cam, bool force)
        {
            if (orbitalMenuRoot == null || innerLayer == null || cam == null)
                return false;

            Vector3 toCameraHorizontal = cam.transform.position - innerLayer.position;
            toCameraHorizontal.y = 0f;
            if (toCameraHorizontal.sqrMagnitude < 0.0001f)
                return false;

            float yawDeg = Mathf.Atan2(toCameraHorizontal.x, toCameraHorizontal.z) * Mathf.Rad2Deg;

            if (_orbitalMenuRotationInitialized && !force)
            {
                float yawDelta = Mathf.Abs(Mathf.DeltaAngle(_cachedOrbitalMenuYawDegrees, yawDeg));
                if (yawDelta < orbitalMenuAlignThresholdDegrees)
                    return false;
            }

            _cachedOrbitalMenuYawDegrees = yawDeg;
            _orbitalMenuRotationInitialized = true;

            // 世界 up 为环面法线；本地 XZ 为水平圆环，+Z 大致指向相机水平方向。
            orbitalMenuRoot.rotation = Quaternion.LookRotation(toCameraHorizontal.normalized, Vector3.up);
            return true;
        }

        /// <summary>请求按方向切换主物体（-1 左，+1 右），仅计算轨道目标角。</summary>
        public void RequestSlideToDirection(int direction)
        {
            if (direction == 0 || _orbitItems == null || _orbitItems.Length == 0)
                return;

            int step = direction > 0 ? 1 : -1;
            int baseIndex = _targetMainItemIndex >= 0 ? _targetMainItemIndex : (_mainItemIndex >= 0 ? _mainItemIndex : 0);
            RequestSlideToIndex(baseIndex + step);
        }

        /// <summary>请求切换到目标主物体索引，并进入连续旋转过渡。</summary>
        public void RequestSlideToIndex(int targetIndex)
        {
            int count = _orbitItems != null ? _orbitItems.Length : 0;
            if (count <= 0)
                return;

            int normalized = NormalizeIndex(targetIndex, count);
            _targetMainItemIndex = normalized;
            _targetOrbitRotationOffsetDeg = GetClosestOffsetForMainIndex(normalized, _orbitRotationOffsetDeg);
            _isOrbitTransitioning = true;

            if (orbitTransitionSmoothTime <= 0f)
            {
                _orbitRotationOffsetDeg = _targetOrbitRotationOffsetDeg;
                _orbitRotationOffsetVelocityDeg = 0f;
                _isOrbitTransitioning = false;
                _mainItemIndex = _targetMainItemIndex;
            }
        }

        /// <summary>触发当前主面板：在其及子层级查找 <see cref="IEnergyBallMode"/> 并调用 <see cref="IEnergyBallMode.Enter"/>。</summary>
        public void TriggerCurrentMainPanel()
        {
            if (!_postExplodePresentationActive || _orbitItems == null || _orbitItems.Length == 0)
                return;

            if (!TryGetCurrentMainOrbitItem(out Transform mainItem))
                return;

            if (!TryGetEnergyBallMode(mainItem, out IEnergyBallMode mode))
                return;

            mode.Enter();
        }

        /// <summary>当前主面板 OrbitItem 索引；未初始化时为 -1。</summary>
        public int MainItemIndex => _mainItemIndex;

        /// <summary>解析当前主面板 OrbitItem；必要时按相机连线刷新主索引。</summary>
        bool TryGetCurrentMainOrbitItem(out Transform mainItem)
        {
            mainItem = null;
            if (_orbitItems == null || _orbitItems.Length == 0)
                return false;

            if (_mainItemIndex < 0)
            {
                Camera cam = referenceCamera != null ? referenceCamera : Camera.main;
                RefreshMainItemByCamera(cam, immediate: false);
            }

            int index = _mainItemIndex >= 0 ? _mainItemIndex : 0;
            index = NormalizeIndex(index, _orbitItems.Length);
            mainItem = _orbitItems[index];
            return mainItem != null;
        }

        /// <summary>在指定 OrbitItem 及其子层级查找第一个 <see cref="IEnergyBallMode"/> 实现。</summary>
        static bool TryGetEnergyBallMode(Transform orbitItem, out IEnergyBallMode mode)
        {
            mode = null;
            if (orbitItem == null)
                return false;

            var behaviours = orbitItem.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IEnergyBallMode candidate)
                {
                    mode = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>重算主物体；过渡中允许临时切到与连线最接近项。</summary>
        void RefreshMainItemByCamera(Camera cam, bool immediate)
        {
            int resolvedIndex = ResolveMainItemIndexByCameraLine(cam);
            if (resolvedIndex < 0)
                return;

            if (_isOrbitTransitioning && !immediate)
            {
                _mainItemIndex = resolvedIndex;
                return;
            }

            if (immediate)
            {
                SetMainItem(resolvedIndex, snapToCenterLine: true);
                return;
            }

            _mainItemIndex = resolvedIndex;
        }

        /// <summary>按球心→屏幕中心连线判定主物体；未命中时回退屏幕左侧。</summary>
        int ResolveMainItemIndexByCameraLine(Camera cam)
        {
            if (cam == null || innerLayer == null || _orbitItems == null || _orbitItems.Length == 0)
                return -1;

            Vector3 sphereScreen3 = cam.WorldToScreenPoint(innerLayer.position);
            if (sphereScreen3.z <= 0f)
                return -1;

            Vector2 sphereScreen = new Vector2(sphereScreen3.x, sphereScreen3.y);
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 cameraLine = screenCenter - sphereScreen;

            float minAngle = float.MaxValue;
            int nearestByAngle = -1;
            float leftMostX = float.MaxValue;
            int leftMostIndex = -1;

            for (int i = 0; i < _orbitItems.Length; i++)
            {
                Transform item = _orbitItems[i];
                if (item == null)
                    continue;

                Vector3 itemScreen3 = cam.WorldToScreenPoint(item.position);
                if (itemScreen3.z <= 0f)
                    continue;

                Vector2 itemScreen = new Vector2(itemScreen3.x, itemScreen3.y);
                Vector2 toItem = itemScreen - sphereScreen;
                if (toItem.sqrMagnitude < 0.0001f)
                    continue;

                float angle = cameraLine.sqrMagnitude > 0.0001f ? Vector2.Angle(cameraLine, toItem) : 180f;
                if (angle < minAngle)
                {
                    minAngle = angle;
                    nearestByAngle = i;
                }

                if (itemScreen.x < leftMostX)
                {
                    leftMostX = itemScreen.x;
                    leftMostIndex = i;
                }
            }

            if (nearestByAngle >= 0 && minAngle <= mainItemLineAngleThresholdDeg)
                return nearestByAngle;

            if (leftMostIndex >= 0)
                return leftMostIndex;

            return nearestByAngle;
        }

        /// <summary>设置当前主物体，可选立即将其对齐到球-相机连线。</summary>
        void SetMainItem(int index, bool snapToCenterLine)
        {
            int count = _orbitItems != null ? _orbitItems.Length : 0;
            if (count <= 0)
                return;

            int normalized = NormalizeIndex(index, count);
            _mainItemIndex = normalized;
            _targetMainItemIndex = normalized;

            if (!snapToCenterLine)
                return;

            _targetOrbitRotationOffsetDeg = GetClosestOffsetForMainIndex(normalized, _orbitRotationOffsetDeg);
            _orbitRotationOffsetDeg = _targetOrbitRotationOffsetDeg;
            _orbitRotationOffsetVelocityDeg = 0f;
            _isOrbitTransitioning = false;
        }

        /// <summary>平滑更新轨道偏移角，驱动连续旋转过渡。</summary>
        void UpdateOrbitTransition(float deltaTime)
        {
            if (!_isOrbitTransitioning)
                return;

            _orbitRotationOffsetDeg = Mathf.SmoothDampAngle(
                _orbitRotationOffsetDeg,
                _targetOrbitRotationOffsetDeg,
                ref _orbitRotationOffsetVelocityDeg,
                orbitTransitionSmoothTime,
                Mathf.Infinity,
                deltaTime);

            float delta = Mathf.Abs(Mathf.DeltaAngle(_orbitRotationOffsetDeg, _targetOrbitRotationOffsetDeg));
            if (delta <= 0.05f && Mathf.Abs(_orbitRotationOffsetVelocityDeg) <= 0.2f)
            {
                _orbitRotationOffsetDeg = _targetOrbitRotationOffsetDeg;
                _orbitRotationOffsetVelocityDeg = 0f;
                _isOrbitTransitioning = false;
                _mainItemIndex = _targetMainItemIndex;
            }
        }

        /// <summary>返回使给定主索引对齐到连线位置的最近偏移角。</summary>
        float GetClosestOffsetForMainIndex(int mainIndex, float fromOffsetDeg)
        {
            int count = _orbitItems != null ? _orbitItems.Length : 0;
            if (count <= 0)
                return fromOffsetDeg;

            float stepDeg = 360f / count;
            float desired = -NormalizeIndex(mainIndex, count) * stepDeg;
            float delta = Mathf.DeltaAngle(fromOffsetDeg, desired);
            return fromOffsetDeg + delta;
        }

        /// <summary>索引循环到 [0, count)。</summary>
        static int NormalizeIndex(int index, int count)
        {
            if (count <= 0)
                return 0;

            int mod = index % count;
            return mod < 0 ? mod + count : mod;
        }

        /// <summary>让每个 OrbitItem 的正面（+Z）始终朝向相机（含俯仰）。</summary>
        void AlignOrbitItemsToCamera(Camera cam)
        {
            if (cam == null || _orbitItems == null)
                return;

            Vector3 cameraPosition = cam.transform.position;
            Vector3 cameraUp = cam.transform.up;

            for (int i = 0; i < _orbitItems.Length; i++)
            {
                Transform item = _orbitItems[i];
                if (item == null)
                    continue;

                Vector3 toCamera = cameraPosition - item.position;
                if (toCamera.sqrMagnitude < 0.0001f)
                    continue;

                Vector3 forward = orbitItemFlipFacing ? -toCamera : toCamera;
                item.rotation = Quaternion.LookRotation(forward.normalized, cameraUp);
            }
        }

        /// <summary>按前缀收集 <see cref="orbitItemNamePrefix"/>0~N 子物体，并修正其物理组件。</summary>
        void CollectOrbitItems()
        {
            if (orbitalMenuRoot == null)
            {
                _orbitItems = null;
                return;
            }

            var items = new System.Collections.Generic.List<Transform>(orbitItemCount);
            for (int i = 0; i < orbitItemCount; i++)
            {
                string itemName = orbitItemNamePrefix + i;
                Transform item = orbitalMenuRoot.Find(itemName);
                if (item == null)
                    item = EnergyBallTransformUtility.FindChildByName(orbitalMenuRoot, itemName);

                if (item != null)
                {
                    SanitizeOrbitItemPhysics(item);
                    items.Add(item);
                }
            }

            _orbitItems = items.ToArray();
        }

        /// <summary>
        /// 清理 OrbitItem 及其子层级（如 Quad）上的物理组件。
        /// EnergyBall 根节点有动态 Rigidbody 时，子物体上的非凸 MeshCollider 会触发 Unity 报错。
        /// </summary>
        static void SanitizeOrbitItemPhysics(Transform item)
        {
            if (item == null)
                return;

            var rigidbodies = item.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rigidbodies.Length; i++)
            {
                Rigidbody rigidbody = rigidbodies[i];
                if (rigidbody == null)
                    continue;

                if (Application.isPlaying)
                    Destroy(rigidbody);
                else
                    DestroyImmediate(rigidbody);
            }

            var meshColliders = item.GetComponentsInChildren<MeshCollider>(true);
            for (int i = 0; i < meshColliders.Length; i++)
            {
                MeshCollider meshCollider = meshColliders[i];
                if (meshCollider == null)
                    continue;

                meshCollider.convex = true;
                meshCollider.isTrigger = true;
            }
        }

        /// <summary>将 orbit items 均匀分布在 OrbitalMenuRoot 本地 XZ 水平圆环上（仅位置，朝向由相机对齐）。</summary>
        void LayoutOrbitItems()
        {
            if (_orbitItems == null || _orbitItems.Length == 0)
                CollectOrbitItems();

            if (_orbitItems == null || _orbitItems.Length == 0)
                return;

            float orbitRadius = GetOrbitLayoutRadius();
            float gapRadians = orbitItemGapDegrees * Mathf.Deg2Rad;
            int count = _orbitItems.Length;

            for (int i = 0; i < count; i++)
            {
                Transform item = _orbitItems[i];
                if (item == null)
                    continue;

                Vector3 localPos = EnergyBallTargetScatterUtility.GetCircleLocalOffsetXZWithGap(
                    i,
                    count,
                    orbitRadius,
                    gapRadians,
                    Mathf.PI * 0.5f + _orbitRotationOffsetDeg * Mathf.Deg2Rad);

                item.localPosition = localPos;
                item.localRotation = Quaternion.identity;
            }
        }

        /// <summary>计算 orbit items 布局半径；挂在内层下时不重复乘爆炸缩放。</summary>
        float GetOrbitLayoutRadius()
        {
            float sphereRadius = GetInnerSphereRadius();

            if (orbitalMenuRoot != null &&
                innerLayer != null &&
                orbitalMenuRoot.IsChildOf(innerLayer))
            {
                return sphereRadius + orbitRadiusPadding;
            }

            return sphereRadius * innerLayerExplodedScale + orbitRadiusPadding;
        }

        /// <summary>读取内层 SphereCollider 半径；缺失时回退 0.5。</summary>
        float GetInnerSphereRadius()
        {
            if (innerLayer == null)
                return 0.5f;

            var sphere = innerLayer.GetComponent<SphereCollider>();
            return sphere != null ? sphere.radius : 0.5f;
        }
    }
}
