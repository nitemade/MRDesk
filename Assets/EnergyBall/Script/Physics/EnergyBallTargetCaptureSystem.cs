using System;
using System.Collections.Generic;
using UnityEngine;

namespace EnergyBall
{
    /// <summary>一次目标释放产生的碎片批次，由 FX 门面决定后续爆开和聚拢。</summary>
    public readonly struct EnergyBallReleasedFragmentBatch
    {
        public EnergyBallReleasedFragmentBatch(Vector3 center, int[] fragmentIndices, int count)
        {
            Center = center;
            FragmentIndices = fragmentIndices;
            Count = count;
        }

        public Vector3 Center { get; }
        public int[] FragmentIndices { get; }
        public int Count { get; }
        public bool IsValid => FragmentIndices != null && Count > 0;
    }

    /// <summary>负责目标槽分配、碎片捕获、目标释放检测与捕获后浮动。</summary>
    [DisallowMultipleComponent]
    public class EnergyBallTargetCaptureSystem : MonoBehaviour
    {
        [Header("目标槽")]
        [SerializeField] List<Transform> targetSlots = new List<Transform>(320);
        [SerializeField] int presetTargetSlotCount = 320;
        [SerializeField] bool rebalanceTargetsOnChange = true;
        [SerializeField] float targetScatterRadius = 0.35f;
        [SerializeField] float targetCaptureDistance = 0.08f;
        [Tooltip("捕获后从当前位置插值到圆环槽位的时长（秒）；0 表示立刻对齐。")]
        [SerializeField] float captureBlendDuration = 0.22f;
        [SerializeField] float targetFloatAmplitude = 0.05f;
        [SerializeField] float targetFloatFrequency = 2f;

        [Header("飞散时目标引导")]
        [SerializeField] bool guideToTargetsWhileDispersed = true;
        [SerializeField] float targetAttractForce = 8f;
        [SerializeField] float targetGuidanceDamping = 1.2f;

        readonly List<Transform> _targetSlotSnapshot = new List<Transform>(320);
        readonly List<int> _releaseFragmentBuffer = new List<int>(384);
        readonly EnergyBallTargetAssignment _targetAssignment = new EnergyBallTargetAssignment();

        EnergyBallFragmentPhysics _fragmentPhysics;
        bool[] _captured;
        Vector3[] _captureBaseLocal;
        Vector3[] _captureStartLocal;
        float[] _captureFloatPhase;
        float[] _captureBlendStartTime;
        bool _assignmentsDirty = true;

        public void Initialize(EnergyBallFragmentPhysics fragmentPhysics)
        {
            _fragmentPhysics = fragmentPhysics;
            EnsureCaptureBuffers();
            SyncTargetSlotSnapshot();
        }

        public void EnsureCaptureBuffers()
        {
            int fragmentCount = _fragmentPhysics != null ? _fragmentPhysics.FragmentCount : 0;
            if (_captured == null || _captured.Length != fragmentCount)
            {
                _captured = new bool[fragmentCount];
                _captureBaseLocal = new Vector3[fragmentCount];
                _captureStartLocal = new Vector3[fragmentCount];
                _captureFloatPhase = new float[fragmentCount];
                _captureBlendStartTime = new float[fragmentCount];
            }
        }

        public bool IsCaptured(int fragmentIndex)
        {
            return _captured != null &&
                   fragmentIndex >= 0 &&
                   fragmentIndex < _captured.Length &&
                   _captured[fragmentIndex];
        }

        public bool IsHeldByTargetSlot(int fragmentIndex)
        {
            if (IsCaptured(fragmentIndex))
                return true;

            Transform[] fragments = _fragmentPhysics != null ? _fragmentPhysics.Fragments : null;
            if (fragments == null || fragmentIndex < 0 || fragmentIndex >= fragments.Length)
                return false;

            Transform fragment = fragments[fragmentIndex];
            return fragment != null && IsRegisteredTargetSlot(fragment.parent);
        }

        public void MarkRestored(int fragmentIndex)
        {
            if (_captured != null && fragmentIndex >= 0 && fragmentIndex < _captured.Length)
                _captured[fragmentIndex] = false;
        }

        public void SetTargets(List<Transform> targets)
        {
            targetSlots.Clear();

            if (targets != null)
            {
                for (int i = 0; i < targets.Count; i++)
                    targetSlots.Add(targets[i]);
            }

            _assignmentsDirty = true;
        }

        public bool ClearTargetSlot(int slotIndex, out EnergyBallReleasedFragmentBatch releasedBatch)
        {
            releasedBatch = default;
            if (slotIndex < 0 || slotIndex >= targetSlots.Count)
                return false;

            Transform previous = targetSlots[slotIndex];
            targetSlots[slotIndex] = null;

            if (previous != null)
                releasedBatch = ReleaseCapturedFragmentsForTarget(previous);

            _assignmentsDirty = true;
            SyncTargetSlotSnapshot();
            return releasedBatch.IsValid;
        }

        public void DetectReleasedTargets(Action<EnergyBallReleasedFragmentBatch> onReleased)
        {
            if (_targetSlotSnapshot.Count == 0 && targetSlots.Count > 0)
            {
                SyncTargetSlotSnapshot();
                return;
            }

            int snapshotCount = _targetSlotSnapshot.Count;
            for (int i = 0; i < snapshotCount; i++)
            {
                Transform previous = _targetSlotSnapshot[i];
                if (previous == null)
                    continue;

                Transform current = i < targetSlots.Count ? targetSlots[i] : null;
                if (current == previous)
                    continue;

                EmitReleasedBatch(ReleaseCapturedFragmentsForTarget(previous), onReleased);
            }

            for (int i = targetSlots.Count; i < snapshotCount; i++)
            {
                Transform previous = _targetSlotSnapshot[i];
                if (previous != null)
                    EmitReleasedBatch(ReleaseCapturedFragmentsForTarget(previous), onReleased);
            }

            SyncTargetSlotSnapshot();
        }

        public void SyncTargetSlotSnapshot()
        {
            _targetSlotSnapshot.Clear();
            for (int i = 0; i < targetSlots.Count; i++)
                _targetSlotSnapshot.Add(targetSlots[i]);
        }

        public void UpdateAssignments()
        {
            if (_fragmentPhysics == null)
                return;

            EnsureCaptureBuffers();
            DetectReleasedTargets(null);
            _targetAssignment.SetTargets(targetSlots, _fragmentPhysics.FragmentCount, rebalanceTargetsOnChange, presetTargetSlotCount);
            _assignmentsDirty = false;
            SyncTargetSlotSnapshot();
        }

        public void ApplyTargetGuidanceForces()
        {
            if (_fragmentPhysics == null || !guideToTargetsWhileDispersed || targetAttractForce <= 0f)
                return;

            EnsureCaptureBuffers();
            if (_assignmentsDirty ||
                _targetAssignment.FragmentCount != _fragmentPhysics.FragmentCount ||
                _targetAssignment.TargetsDiffer(targetSlots, presetTargetSlotCount))
            {
                UpdateAssignments();
            }

            if (!_targetAssignment.HasTargets)
                return;

            Rigidbody[] rigidbodies = _fragmentPhysics.Rigidbodies;
            if (rigidbodies == null)
                return;

            float captureDistanceSqr = targetCaptureDistance * targetCaptureDistance;

            for (int i = 0; i < rigidbodies.Length; i++)
            {
                if (IsCaptured(i))
                    continue;

                Rigidbody body = rigidbodies[i];
                if (body == null || body.isKinematic)
                    continue;

                if (!_targetAssignment.TryGetFragmentTarget(i, out Transform target, out int groupIndex, out int groupCount))
                    continue;

                Vector3 ringWorld = EnergyBallTargetScatterUtility.GetWorldRingPosition(
                    target,
                    groupIndex,
                    groupCount,
                    targetScatterRadius);
                Vector3 toTarget = ringWorld - body.position;

                if (toTarget.sqrMagnitude <= captureDistanceSqr)
                {
                    CaptureFragment(i, target, groupIndex, groupCount);
                    continue;
                }

                Vector3 acceleration = toTarget * targetAttractForce - body.linearVelocity * targetGuidanceDamping;
                body.AddForce(acceleration, ForceMode.Acceleration);
            }
        }

        public void UpdateCapturedFragments()
        {
            Transform[] fragments = _fragmentPhysics != null ? _fragmentPhysics.Fragments : null;
            if (_captured == null || fragments == null)
                return;

            float time = Time.time;
            for (int i = 0; i < fragments.Length; i++)
            {
                if (!_captured[i])
                    continue;

                Transform fragment = fragments[i];
                if (fragment == null || fragment.parent == null)
                    continue;

                float blendT = captureBlendDuration <= 0f
                    ? 1f
                    : Mathf.Clamp01((time - _captureBlendStartTime[i]) / captureBlendDuration);
                Vector3 ringLocal = Vector3.Lerp(_captureStartLocal[i], _captureBaseLocal[i], blendT);
                float floatY = Mathf.Sin(time * targetFloatFrequency + _captureFloatPhase[i]) * targetFloatAmplitude;
                fragment.localPosition = ringLocal + new Vector3(0f, floatY, 0f);
            }
        }

        bool IsRegisteredTargetSlot(Transform target)
        {
            if (target == null)
                return false;

            for (int i = 0; i < targetSlots.Count; i++)
            {
                if (targetSlots[i] == target)
                    return true;
            }

            return false;
        }

        void CaptureFragment(int fragmentIndex, Transform target, int groupIndex, int groupCount)
        {
            Transform[] fragments = _fragmentPhysics.Fragments;
            Rigidbody[] rigidbodies = _fragmentPhysics.Rigidbodies;
            Collider[] colliders = _fragmentPhysics.Colliders;
            Transform fragment = fragments[fragmentIndex];
            if (fragment == null || target == null)
                return;

            Vector3 baseLocal = EnergyBallTargetScatterUtility.GetCircleLocalOffset(
                groupIndex,
                groupCount,
                targetScatterRadius);

            fragment.SetParent(target, true);
            fragment.localRotation = Quaternion.identity;

            _captureStartLocal[fragmentIndex] = fragment.localPosition;
            _captureBaseLocal[fragmentIndex] = baseLocal;
            _captureBlendStartTime[fragmentIndex] = Time.time;
            _captureFloatPhase[fragmentIndex] = EnergyBallTargetScatterUtility.GetFragmentFloatPhase(fragmentIndex);
            _captured[fragmentIndex] = true;

            if (captureBlendDuration <= 0f)
                fragment.localPosition = baseLocal;

            Rigidbody body = rigidbodies[fragmentIndex];
            if (body != null)
            {
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }

                body.isKinematic = true;
                body.detectCollisions = false;
            }

            Collider fragmentCollider = colliders[fragmentIndex];
            if (fragmentCollider != null)
                fragmentCollider.enabled = false;
        }

        EnergyBallReleasedFragmentBatch ReleaseCapturedFragmentsForTarget(Transform target)
        {
            _targetAssignment.CollectFragmentIndicesForTarget(target, _releaseFragmentBuffer);

            int releaseCount = 0;
            for (int i = 0; i < _releaseFragmentBuffer.Count; i++)
            {
                int fragmentIndex = _releaseFragmentBuffer[i];
                if (!IsCaptured(fragmentIndex))
                    continue;

                _releaseFragmentBuffer[releaseCount++] = fragmentIndex;
            }

            if (releaseCount == 0)
                return default;

            Vector3 explosionCenter = target != null
                ? target.position
                : _fragmentPhysics.GetFragmentIndicesCentroid(_releaseFragmentBuffer, releaseCount);

            int[] releasedIndices = new int[releaseCount];
            for (int i = 0; i < releaseCount; i++)
            {
                int fragmentIndex = _releaseFragmentBuffer[i];
                releasedIndices[i] = fragmentIndex;
                ReleaseFragment(fragmentIndex);
            }

            return new EnergyBallReleasedFragmentBatch(explosionCenter, releasedIndices, releaseCount);
        }

        void ReleaseFragment(int fragmentIndex)
        {
            if (!IsCaptured(fragmentIndex))
                return;

            _captured[fragmentIndex] = false;

            Transform[] fragments = _fragmentPhysics.Fragments;
            if (fragments == null || fragmentIndex < 0 || fragmentIndex >= fragments.Length)
                return;

            Transform fragment = fragments[fragmentIndex];
            if (fragment == null)
                return;

            fragment.SetParent(null, true);
            _fragmentPhysics.PrepareFragmentForReleasePhysics(fragmentIndex);
        }

        static void EmitReleasedBatch(EnergyBallReleasedFragmentBatch batch, Action<EnergyBallReleasedFragmentBatch> onReleased)
        {
            if (batch.IsValid && onReleased != null)
                onReleased(batch);
        }
    }
}
