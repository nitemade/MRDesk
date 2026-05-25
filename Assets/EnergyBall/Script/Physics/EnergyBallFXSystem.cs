using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace EnergyBall
{
    /// <summary>能量球碎片的物理生命周期状态。</summary>
    public enum EnergyBallPhysicalState
    {
        /// <summary>完整球体，碎片挂回 fragmentRoot，Rigidbody 关闭。</summary>
        Intact,
        /// <summary>爆炸瞬间，施加冲量并计时。</summary>
        Exploding,
        /// <summary>飞散维持，靠阻尼减速，可选吸引子与目标引导。</summary>
        Dispersed,
        /// <summary>逐片瞬移挂回 fragmentRoot。</summary>
        Regrouping,
        /// <summary>已静止并挂回父级。</summary>
        Stable
    }

    /// <summary>
    /// 能量球 FX 门面：保留对外 API 与状态机，碎片物理、目标捕获、聚拢和爆炸后表现由独立组件负责。
    /// </summary>
    [DisallowMultipleComponent]
    [MovedFrom(false, null, null, "EnergyBallExplosionSystem")]
    public class EnergyBallFXSystem : MonoBehaviour
    {
        [Header("组件引用")]
        [SerializeField] EnergyBallFragmentPhysics fragmentPhysics;
        [SerializeField] EnergyBallTargetCaptureSystem targetCaptureSystem;
        [SerializeField] EnergyBallRegroupController regroupController;
        [SerializeField] EnergyBallPostExplosionPresentation postExplosionPresentation;

        [Header("爆炸状态")]
        [SerializeField] float explodingDuration = 0.8f;
        [SerializeField] float interactionEnergyToExplode = 1f;

        EnergyBallPhysicalState _state = EnergyBallPhysicalState.Intact;
        float _stateTimer;
        float _interactionEnergy;

        public EnergyBallPhysicalState State => _state;
        public int FragmentCount => fragmentPhysics != null ? fragmentPhysics.FragmentCount : 0;

        void Awake()
        {
            ResolveComponents();
            InitializeSubsystems();
        }

        void FixedUpdate()
        {
            switch (_state)
            {
                case EnergyBallPhysicalState.Exploding:
                    TickExploding();
                    break;
                case EnergyBallPhysicalState.Dispersed:
                    TickDispersed();
                    break;
                case EnergyBallPhysicalState.Regrouping:
                    TickRegrouping();
                    break;
            }
        }

        void OnDisable()
        {
            if (regroupController != null)
                regroupController.StopRegroup();
        }

        public void Explode(Vector3 center, float force)
        {
            Explode(center, force, fragmentPhysics != null ? fragmentPhysics.DefaultExplosionRadius : 1.5f);
        }

        public void Explode(Vector3 center, float force, float radius)
        {
            EnsureInitialized();

            if (regroupController != null)
                regroupController.StopRegroup();

            _stateTimer = 0f;
            fragmentPhysics.Explode(center, force, radius, targetCaptureSystem.IsHeldByTargetSlot);
            ChangeState(EnergyBallPhysicalState.Exploding);

            if (postExplosionPresentation != null)
                postExplosionPresentation.Apply();
        }

        public void Explode()
        {
            EnsureInitialized();
            Vector3 center = fragmentPhysics != null && fragmentPhysics.ConstraintCenter != null
                ? fragmentPhysics.ConstraintCenter.position
                : transform.position;
            float force = fragmentPhysics != null ? fragmentPhysics.DefaultExplosionForce : 8f;
            float radius = fragmentPhysics != null ? fragmentPhysics.DefaultExplosionRadius : 1.5f;
            Explode(center, force, radius);
        }

        public void TriggerExplosion(Vector3 center, float force, float radius)
        {
            Explode(center, force, radius);
        }

        public void TriggerExplosion(Vector3 center, float force)
        {
            Explode(center, force);
        }

        public void TriggerExplosion()
        {
            Explode();
        }

        public void SetTargets(List<Transform> targets)
        {
            EnsureInitialized();
            targetCaptureSystem.SetTargets(targets);
            ProcessTargetReleases();

            if (_state == EnergyBallPhysicalState.Exploding || _state == EnergyBallPhysicalState.Dispersed)
                targetCaptureSystem.UpdateAssignments();
        }

        /// <summary>清空指定槽位并释放该目标下已捕获的碎片。</summary>
        public void ClearTargetSlot(int slotIndex)
        {
            EnsureInitialized();
            if (targetCaptureSystem.ClearTargetSlot(slotIndex, out EnergyBallReleasedFragmentBatch releasedBatch))
                ProcessReleasedBatch(releasedBatch);

            if (_state == EnergyBallPhysicalState.Exploding || _state == EnergyBallPhysicalState.Dispersed)
                targetCaptureSystem.UpdateAssignments();
        }

        /// <summary>分批瞬移到当前 fragmentRoot 下的 home 位姿；批间隔恒定，每批片数按斐波那契数列增加。</summary>
        public void Regroup()
        {
            EnsureInitialized();

            if (postExplosionPresentation != null)
                postExplosionPresentation.ResetPresentation();

            ConfigureRegroupCallbacks();
            regroupController.StartRegroup(restoreAllNonCaptured: true);
        }

        /// <summary>设置飞散阶段的吸引子（如手指/光标 Transform）。</summary>
        public void SetAttractor(Transform newAttractor)
        {
            EnsureInitialized();
            fragmentPhysics.SetAttractor(newAttractor);
        }

        public void ClearAttractor()
        {
            EnsureInitialized();
            fragmentPhysics.ClearAttractor();
        }

        public void AddInteractionEnergy(float value)
        {
            _interactionEnergy += Mathf.Max(0f, value);

            if (_interactionEnergy < interactionEnergyToExplode || _state == EnergyBallPhysicalState.Exploding)
                return;

            ClearInteractionEnergy();
            Explode();
        }

        public void ApplyPressure(float value)
        {
            AddInteractionEnergy(value);
        }

        public void ClearInteractionEnergy()
        {
            _interactionEnergy = 0f;
        }

        void ResolveComponents()
        {
            if (fragmentPhysics == null)
                fragmentPhysics = GetComponent<EnergyBallFragmentPhysics>();
            if (fragmentPhysics == null)
                fragmentPhysics = gameObject.AddComponent<EnergyBallFragmentPhysics>();

            if (targetCaptureSystem == null)
                targetCaptureSystem = GetComponent<EnergyBallTargetCaptureSystem>();
            if (targetCaptureSystem == null)
                targetCaptureSystem = gameObject.AddComponent<EnergyBallTargetCaptureSystem>();

            if (regroupController == null)
                regroupController = GetComponent<EnergyBallRegroupController>();
            if (regroupController == null)
                regroupController = gameObject.AddComponent<EnergyBallRegroupController>();

            if (postExplosionPresentation == null)
                postExplosionPresentation = GetComponent<EnergyBallPostExplosionPresentation>();
            if (postExplosionPresentation == null)
                postExplosionPresentation = gameObject.AddComponent<EnergyBallPostExplosionPresentation>();
        }

        void InitializeSubsystems()
        {
            fragmentPhysics.Initialize();
            targetCaptureSystem.Initialize(fragmentPhysics);
            postExplosionPresentation.Initialize();
            fragmentPhysics.SetFragmentsSleeping();
            ConfigureRegroupCallbacks();
        }

        void EnsureInitialized()
        {
            ResolveComponents();

            if (fragmentPhysics.FragmentCount == 0)
            {
                fragmentPhysics.Initialize();
                targetCaptureSystem.Initialize(fragmentPhysics);
            }

            ConfigureRegroupCallbacks();
        }

        void ConfigureRegroupCallbacks()
        {
            regroupController.Configure(
                fragmentPhysics.FragmentCount,
                targetCaptureSystem.IsCaptured,
                RestoreFragmentToHome,
                () => ChangeState(EnergyBallPhysicalState.Regrouping),
                () =>
                {
                    if (postExplosionPresentation != null)
                        postExplosionPresentation.ResetPresentation();

                    ChangeState(EnergyBallPhysicalState.Stable);
                });
        }

        void RestoreFragmentToHome(int fragmentIndex)
        {
            targetCaptureSystem.MarkRestored(fragmentIndex);
            fragmentPhysics.RestoreFragmentToHome(fragmentIndex);
        }

        void TickExploding()
        {
            _stateTimer += Time.fixedDeltaTime;
            fragmentPhysics.ApplyAttractorForces();
            targetCaptureSystem.UpdateCapturedFragments();

            if (_stateTimer >= explodingDuration)
                ChangeState(EnergyBallPhysicalState.Dispersed);
        }

        void TickDispersed()
        {
            ProcessTargetReleases();
            fragmentPhysics.ApplyAttractorForces();
            targetCaptureSystem.ApplyTargetGuidanceForces();
            targetCaptureSystem.UpdateCapturedFragments();
        }

        void TickRegrouping()
        {
            ProcessTargetReleases();
            targetCaptureSystem.UpdateCapturedFragments();
        }

        void ProcessTargetReleases()
        {
            targetCaptureSystem.DetectReleasedTargets(ProcessReleasedBatch);
        }

        void ProcessReleasedBatch(EnergyBallReleasedFragmentBatch batch)
        {
            if (!batch.IsValid)
                return;

            if (ShouldBatchRegroupOnRelease(_state))
            {
                ConfigureRegroupCallbacks();
                for (int i = 0; i < batch.Count; i++)
                    regroupController.QueueFragment(batch.FragmentIndices[i]);
            }

            fragmentPhysics.ApplyReleaseExplosionAt(batch.Center, batch.FragmentIndices, batch.Count);
        }

        static bool ShouldBatchRegroupOnRelease(EnergyBallPhysicalState state)
        {
            return state == EnergyBallPhysicalState.Regrouping ||
                   state == EnergyBallPhysicalState.Stable ||
                   state == EnergyBallPhysicalState.Intact;
        }

        void ChangeState(EnergyBallPhysicalState nextState)
        {
            _state = nextState;
            _stateTimer = 0f;
        }
    }
}
