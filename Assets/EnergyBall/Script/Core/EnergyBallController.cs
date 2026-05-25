using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace EnergyBall
{
    /// <summary>
    /// 能量球对外门面：供场景、UI、AR 交互等统一调用。
    /// 移动、待机视觉、碎裂物理分别由独立组件实现，本类只做转发与启动编排。
    /// </summary>
    [DisallowMultipleComponent]
    [MovedFrom(false, null, null, "EnergyBallManager")]
    public class EnergyBallController : MonoBehaviour
    {
        [Header("组件引用")]
        [SerializeField] EnergyBallIdleVisual idleVisual;
        [SerializeField] EnergyBallDragController dragController;
        [SerializeField] EnergyBallFXSystem fxSystem;

        [Header("启动")]
        [SerializeField] bool playIdleOnStart = true;
        [SerializeField] bool playIdleDuringRegroup = true;

        /// <summary>当前物理状态；无 FX 组件时视为完整态。</summary>
        public EnergyBallPhysicalState PhysicalState =>
            fxSystem != null ? fxSystem.State : EnergyBallPhysicalState.Intact;

        void Awake()
        {
            ResolveComponents();
        }

        void Start()
        {
            if (playIdleOnStart)
                PlayIdle();
        }

        /// <summary>播放待机 DOTween（内层浮动 + 外层旋转）。</summary>
        public void PlayIdle()
        {
            if (idleVisual != null)
                idleVisual.Play();
        }

        /// <summary>停止所有待机动画。</summary>
        public void StopIdle()
        {
            if (idleVisual != null)
                idleVisual.Stop();
        }

        /// <summary>开关调试拖拽（键鼠移动球体）。</summary>
        public void SetDragEnabled(bool enabled)
        {
            if (dragController != null)
                dragController.enabled = enabled;
        }

        /// <summary>在默认中心爆炸；会先停止待机动画。</summary>
        public void Explode()
        {
            StopIdle();

            if (fxSystem != null)
                fxSystem.Explode();
        }

        /// <summary>在指定中心、力度爆炸（使用默认半径）。</summary>
        public void Explode(Vector3 center, float force)
        {
            StopIdle();

            if (fxSystem != null)
                fxSystem.Explode(center, force);
        }

        /// <summary>在指定中心、力度与半径爆炸。</summary>
        public void Explode(Vector3 center, float force, float radius)
        {
            StopIdle();

            if (fxSystem != null)
                fxSystem.Explode(center, force, radius);
        }

        /// <summary><see cref="Explode"/> 的别名，便于动画事件/UI 绑定。</summary>
        public void TriggerExplosion()
        {
            Explode();
        }

        /// <summary><see cref="Explode(Vector3, float, float)"/> 的别名。</summary>
        public void TriggerExplosion(Vector3 center, float force, float radius)
        {
            Explode(center, force, radius);
        }

        /// <summary>设置碎片聚拢/飞散时的目标锚点列表。</summary>
        public void SetTargets(List<Transform> targets)
        {
            if (fxSystem != null)
                fxSystem.SetTargets(targets);
        }

        /// <summary>清空指定目标槽并释放该目标下已捕获的碎片。</summary>
        public void ClearTargetSlot(int slotIndex)
        {
            if (fxSystem != null)
                fxSystem.ClearTargetSlot(slotIndex);
        }

        /// <summary>设置飞散阶段的吸引子（如手指/光标 Transform）。</summary>
        public void SetAttractor(Transform attractor)
        {
            if (fxSystem != null)
                fxSystem.SetAttractor(attractor);
        }

        /// <summary>清除吸引子。</summary>
        public void ClearAttractor()
        {
            if (fxSystem != null)
                fxSystem.ClearAttractor();
        }

        /// <summary>逐片恢复；恢复开始即可播放待机旋转（由 playIdleDuringRegroup 控制）。</summary>
        public void Regroup()
        {
            if (fxSystem == null)
                return;

            fxSystem.Regroup();

            if (playIdleDuringRegroup)
                PlayIdle();
        }

        /// <summary>施加“压力”，累积到阈值后自动爆炸。</summary>
        public void ApplyPressure(float value)
        {
            if (fxSystem != null)
                fxSystem.ApplyPressure(value);
        }

        /// <summary>增加交互能量，达到阈值后触发爆炸。</summary>
        public void AddInteractionEnergy(float value)
        {
            if (fxSystem != null)
                fxSystem.AddInteractionEnergy(value);
        }

        /// <summary>自动查找同物体上的子组件；Idle 缺失时会自动添加。</summary>
        void ResolveComponents()
        {
            if (idleVisual == null)
                idleVisual = GetComponent<EnergyBallIdleVisual>();

            if (idleVisual == null)
                idleVisual = gameObject.AddComponent<EnergyBallIdleVisual>();

            if (dragController == null)
                dragController = GetComponent<EnergyBallDragController>();

            if (fxSystem == null)
                fxSystem = GetComponent<EnergyBallFXSystem>();
        }
    }
}
