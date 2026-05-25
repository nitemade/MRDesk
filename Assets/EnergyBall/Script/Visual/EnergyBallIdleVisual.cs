using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace EnergyBall
{
    /// <summary>
    /// 能量球待机视觉：内层沿法线浮动、外层持续绕 Y 轴旋转（DOTween）。
    /// 爆炸前应由 <see cref="EnergyBallController"/> 调用 <see cref="Stop"/> 停止动画。
    /// </summary>
    [DisallowMultipleComponent]
    [MovedFrom(false, null, null, "IdleState")]
    public class EnergyBallIdleVisual : MonoBehaviour
    {
        const string InnerFloatKey = "InnerFloat";
        const string OuterRotateKey = "OuterRotate";

        [Header("层级引用")]
        [SerializeField] Transform innerLayer;
        [SerializeField] Transform outerLayer;
        [SerializeField] string innerLayerObjectName = "EnergyLayer";
        [SerializeField] string outerLayerObjectName = "SphereRoot";

        [Header("待机动画参数")]
        [SerializeField] bool playOnEnable = true;
        [SerializeField] float innerFloatIntensity = 0.05f;
        [SerializeField] float innerFloatDuration = 1.5f;
        [SerializeField] float outerRotateDurationPerRevolution = 8f;

        /// <summary>逻辑键 → (目标 Transform, DOTween Id)，用于按 key 停止/替换动画。</summary>
        readonly Dictionary<string, (Transform target, string tweenId)> _tweenIds = new();

        Vector3 _innerHomeLocalPosition;
        Quaternion _outerHomeLocalRotation;
        bool _innerHomeCached;
        bool _outerHomeCached;

        void Awake()
        {
            ResolveReferences();
            CacheHomeTransforms();
        }

        void OnEnable()
        {
            if (playOnEnable)
                Play();
        }

        void OnDisable()
        {
            Stop();
        }

        /// <summary>重新解析引用并启动全部待机动画。</summary>
        public void Play()
        {
            ResolveReferences();
            Stop();
            RestoreHomeTransforms();

            PlayFloatAlongNormal(InnerFloatKey, innerLayer, innerFloatIntensity, innerFloatDuration);
            PlayRotateAlongNormalLoop(OuterRotateKey, outerLayer, outerRotateDurationPerRevolution);
        }

        /// <summary>停止并清理已注册的 Tween。</summary>
        public void Stop()
        {
            StopAllTweens();
            RestoreHomeTransforms();
        }

        /// <summary>按名称查找子节点（未手动赋值时）。</summary>
        void ResolveReferences()
        {
            if (innerLayer == null)
                innerLayer = EnergyBallTransformUtility.FindChildByName(transform, innerLayerObjectName);

            if (outerLayer == null)
                outerLayer = EnergyBallTransformUtility.FindChildByName(transform, outerLayerObjectName) ?? transform;
        }

        void CacheHomeTransforms()
        {
            if (innerLayer != null)
            {
                _innerHomeLocalPosition = innerLayer.localPosition;
                _innerHomeCached = true;
            }

            if (outerLayer != null)
            {
                _outerHomeLocalRotation = outerLayer.localRotation;
                _outerHomeCached = true;
            }
        }

        void RestoreHomeTransforms()
        {
            if (innerLayer != null && _innerHomeCached)
                innerLayer.localPosition = _innerHomeLocalPosition;

            if (outerLayer != null && _outerHomeCached)
                outerLayer.localRotation = _outerHomeLocalRotation;
        }

        void RegisterTween(string key, Transform target, string tweenId)
        {
            if (string.IsNullOrEmpty(key) || target == null || tweenId == null)
                return;

            if (_tweenIds.TryGetValue(key, out var old))
                DOTweenFloatUtility.StopById(old.target, old.tweenId);

            _tweenIds[key] = (target, tweenId);
        }

        void StopAllTweens()
        {
            foreach (var pair in _tweenIds)
                DOTweenFloatUtility.StopById(pair.Value.target, pair.Value.tweenId);

            _tweenIds.Clear();
        }

        static string BuildTweenId(Transform target, string key) => $"{key}_{target.GetEntityId()}";

        void PlayFloatAlongNormal(string key, Transform target, float intensity, float duration)
        {
            if (target == null)
                return;

            string tweenId = DOTweenFloatUtility.FloatAlongNormal(
                target,
                intensity,
                duration,
                BuildTweenId(target, key));

            RegisterTween(key, target, tweenId);
        }

        void PlayRotateAlongNormalLoop(string key, Transform target, float durationPerRevolution)
        {
            if (target == null)
                return;

            string tweenId = DOTweenFloatUtility.RotateAlongNormalLoop(
                target,
                durationPerRevolution,
                BuildTweenId(target, key));

            RegisterTween(key, target, tweenId);
        }
    }
}
