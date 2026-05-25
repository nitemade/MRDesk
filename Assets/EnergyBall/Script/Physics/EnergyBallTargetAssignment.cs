using System.Collections.Generic;
using UnityEngine;

namespace EnergyBall
{
    /// <summary>
    /// 维护碎片到目标槽的分组关系：预设区内空槽也参与均分，超出预设区后仅非 null 为临时槽；目标增加时可稳定重平衡。
    /// </summary>
    public class EnergyBallTargetAssignment
    {
        readonly List<Transform> _targets = new();
        readonly List<List<int>> _groups = new();

        int[] _targetIndices = System.Array.Empty<int>();
        int[] _groupIndices = System.Array.Empty<int>();
        int _fragmentCount;
        int _presetSlotCount;

        public int FragmentCount => _fragmentCount;
        public int TargetCount => _targets.Count;

        public bool HasTargets => _targets.Count > 0;

        public bool TryGetFragmentTarget(int fragmentIndex, out Transform target, out int groupIndex, out int groupCount)
        {
            target = null;
            groupIndex = -1;
            groupCount = 0;

            if (fragmentIndex < 0 || fragmentIndex >= _targetIndices.Length)
                return false;

            int targetIndex = _targetIndices[fragmentIndex];
            if (targetIndex < 0 || targetIndex >= _targets.Count)
                return false;

            target = _targets[targetIndex];
            if (target == null)
                return false;

            groupIndex = _groupIndices[fragmentIndex];
            groupCount = _groups[targetIndex].Count;
            return groupIndex >= 0 && groupCount > 0;
        }

        /// <summary>收集分配到指定目标 Transform 的碎片索引（含未捕获）。</summary>
        public void CollectFragmentIndicesForTarget(Transform target, List<int> buffer)
        {
            buffer.Clear();
            if (target == null || _targetIndices.Length == 0)
                return;

            for (int fragmentIndex = 0; fragmentIndex < _targetIndices.Length; fragmentIndex++)
            {
                int targetIndex = _targetIndices[fragmentIndex];
                if (targetIndex < 0 || targetIndex >= _targets.Count)
                    continue;

                if (_targets[targetIndex] == target)
                    buffer.Add(fragmentIndex);
            }
        }

        public bool TargetsDiffer(IList<Transform> sourceTargets, int presetSlotCount)
        {
            List<Transform> nextTargets = BuildEffectiveTargets(sourceTargets, presetSlotCount);
            if (nextTargets.Count != _targets.Count)
                return true;

            for (int i = 0; i < nextTargets.Count; i++)
            {
                if (nextTargets[i] != _targets[i])
                    return true;
            }

            return false;
        }

        public void SetTargets(IList<Transform> sourceTargets, int fragmentCount, bool rebalanceFromPrevious, int presetSlotCount)
        {
            _presetSlotCount = Mathf.Max(0, presetSlotCount);
            List<Transform> nextTargets = BuildEffectiveTargets(sourceTargets, _presetSlotCount);
            int safeFragmentCount = Mathf.Max(0, fragmentCount);

            if (safeFragmentCount == 0 || nextTargets.Count == 0)
            {
                Clear(nextTargets, safeFragmentCount);
                return;
            }

            bool canRebalance =
                rebalanceFromPrevious &&
                _fragmentCount == safeFragmentCount &&
                _targets.Count > 0 &&
                nextTargets.Count > _targets.Count;

            if (canRebalance)
                RebalanceForExpandedTargets(nextTargets, safeFragmentCount);
            else
                RebuildEven(nextTargets, safeFragmentCount);
        }

        /// <summary>
        /// 预设区内每个索引都算一个槽位（含 null）；超出预设区后仅非 null 计入临时槽。
        /// </summary>
        static List<Transform> BuildEffectiveTargets(IList<Transform> sourceTargets, int presetSlotCount)
        {
            var result = new List<Transform>();
            int preset = Mathf.Max(0, presetSlotCount);
            if (preset <= 0)
            {
                if (sourceTargets == null)
                    return result;

                for (int i = 0; i < sourceTargets.Count; i++)
                {
                    if (sourceTargets[i] != null)
                        result.Add(sourceTargets[i]);
                }

                return result;
            }

            if (sourceTargets != null)
            {
                int presetRegionLength = Mathf.Min(preset, sourceTargets.Count);
                for (int i = 0; i < presetRegionLength; i++)
                    result.Add(sourceTargets[i]);

                for (int i = preset; i < sourceTargets.Count; i++)
                {
                    if (sourceTargets[i] != null)
                        result.Add(sourceTargets[i]);
                }
            }

            return result;
        }

        void Clear(List<Transform> nextTargets, int fragmentCount)
        {
            ReplaceTargets(nextTargets);
            EnsureFragmentBuffers(fragmentCount);

            for (int i = 0; i < _targetIndices.Length; i++)
            {
                _targetIndices[i] = -1;
                _groupIndices[i] = -1;
            }
        }

        void RebuildEven(List<Transform> nextTargets, int fragmentCount)
        {
            ReplaceTargets(nextTargets);
            EnsureFragmentBuffers(fragmentCount);
            ClearGroups();

            for (int fragmentIndex = 0; fragmentIndex < fragmentCount; fragmentIndex++)
            {
                int targetIndex = GetEvenTargetIndex(fragmentIndex, fragmentCount, _targets.Count);
                AddFragmentToGroup(fragmentIndex, targetIndex);
            }
        }

        void RebalanceForExpandedTargets(List<Transform> nextTargets, int fragmentCount)
        {
            var oldTargets = new List<Transform>(_targets);
            var oldGroups = new List<List<int>>(_groups.Count);
            for (int i = 0; i < _groups.Count; i++)
                oldGroups.Add(new List<int>(_groups[i]));

            ReplaceTargets(nextTargets);
            EnsureFragmentBuffers(fragmentCount);
            ClearGroups();

            for (int oldIndex = 0; oldIndex < oldTargets.Count; oldIndex++)
            {
                int nextIndex = _targets.IndexOf(oldTargets[oldIndex]);
                if (nextIndex < 0)
                    continue;

                for (int i = 0; i < oldGroups[oldIndex].Count; i++)
                    _groups[nextIndex].Add(oldGroups[oldIndex][i]);
            }

            List<int> unassigned = CollectUnassignedFragments(fragmentCount);
            int[] quotas = BuildEvenQuotas(fragmentCount, _targets.Count);

            FillUnderfullGroups(unassigned, quotas);
            MoveSurplusToUnderfullGroups(quotas);
            RebuildFragmentMaps();
        }

        void ReplaceTargets(List<Transform> nextTargets)
        {
            _targets.Clear();
            _targets.AddRange(nextTargets);

            _groups.Clear();
            for (int i = 0; i < _targets.Count; i++)
                _groups.Add(new List<int>());
        }

        void EnsureFragmentBuffers(int fragmentCount)
        {
            _fragmentCount = fragmentCount;
            if (_targetIndices.Length != fragmentCount)
            {
                _targetIndices = new int[fragmentCount];
                _groupIndices = new int[fragmentCount];
            }
        }

        void ClearGroups()
        {
            for (int i = 0; i < _groups.Count; i++)
                _groups[i].Clear();

            for (int i = 0; i < _targetIndices.Length; i++)
            {
                _targetIndices[i] = -1;
                _groupIndices[i] = -1;
            }
        }

        static int GetEvenTargetIndex(int fragmentIndex, int fragmentCount, int targetCount)
        {
            if (targetCount <= 1)
                return 0;

            int baseCount = fragmentCount / targetCount;
            int remainder = fragmentCount % targetCount;
            int cursor = 0;

            for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
            {
                int groupSize = baseCount + (targetIndex < remainder ? 1 : 0);
                if (fragmentIndex < cursor + groupSize)
                    return targetIndex;

                cursor += groupSize;
            }

            return targetCount - 1;
        }

        static int[] BuildEvenQuotas(int fragmentCount, int targetCount)
        {
            int[] quotas = new int[targetCount];
            int baseCount = fragmentCount / targetCount;
            int remainder = fragmentCount % targetCount;

            for (int i = 0; i < targetCount; i++)
                quotas[i] = baseCount + (i < remainder ? 1 : 0);

            return quotas;
        }

        List<int> CollectUnassignedFragments(int fragmentCount)
        {
            var assigned = new bool[fragmentCount];
            for (int groupIndex = 0; groupIndex < _groups.Count; groupIndex++)
            {
                List<int> group = _groups[groupIndex];
                for (int i = group.Count - 1; i >= 0; i--)
                {
                    int fragmentIndex = group[i];
                    if (fragmentIndex < 0 || fragmentIndex >= fragmentCount || assigned[fragmentIndex])
                    {
                        group.RemoveAt(i);
                        continue;
                    }

                    assigned[fragmentIndex] = true;
                }
            }

            var unassigned = new List<int>();
            for (int i = 0; i < fragmentCount; i++)
            {
                if (!assigned[i])
                    unassigned.Add(i);
            }

            return unassigned;
        }

        void FillUnderfullGroups(List<int> unassigned, int[] quotas)
        {
            int readIndex = 0;
            for (int targetIndex = 0; targetIndex < _groups.Count && readIndex < unassigned.Count; targetIndex++)
            {
                while (_groups[targetIndex].Count < quotas[targetIndex] && readIndex < unassigned.Count)
                    _groups[targetIndex].Add(unassigned[readIndex++]);
            }
        }

        void MoveSurplusToUnderfullGroups(int[] quotas)
        {
            int donorIndex = 0;
            for (int receiverIndex = 0; receiverIndex < _groups.Count; receiverIndex++)
            {
                while (_groups[receiverIndex].Count < quotas[receiverIndex] &&
                       TryTakeSurplusFragment(quotas, ref donorIndex, out int fragmentIndex))
                {
                    _groups[receiverIndex].Add(fragmentIndex);
                }
            }
        }

        bool TryTakeSurplusFragment(int[] quotas, ref int donorIndex, out int fragmentIndex)
        {
            for (int checkedCount = 0; checkedCount < _groups.Count; checkedCount++)
            {
                int index = (donorIndex + checkedCount) % _groups.Count;
                List<int> donor = _groups[index];
                if (donor.Count <= quotas[index])
                    continue;

                int takeIndex = donor.Count / 2;
                fragmentIndex = donor[takeIndex];
                donor.RemoveAt(takeIndex);
                donorIndex = (index + 1) % _groups.Count;
                return true;
            }

            fragmentIndex = -1;
            return false;
        }

        void AddFragmentToGroup(int fragmentIndex, int targetIndex)
        {
            int groupIndex = _groups[targetIndex].Count;
            _groups[targetIndex].Add(fragmentIndex);
            _targetIndices[fragmentIndex] = targetIndex;
            _groupIndices[fragmentIndex] = groupIndex;
        }

        void RebuildFragmentMaps()
        {
            for (int i = 0; i < _targetIndices.Length; i++)
            {
                _targetIndices[i] = -1;
                _groupIndices[i] = -1;
            }

            for (int targetIndex = 0; targetIndex < _groups.Count; targetIndex++)
            {
                List<int> group = _groups[targetIndex];
                group.Sort();

                for (int groupIndex = 0; groupIndex < group.Count; groupIndex++)
                {
                    int fragmentIndex = group[groupIndex];
                    _targetIndices[fragmentIndex] = targetIndex;
                    _groupIndices[fragmentIndex] = groupIndex;
                }
            }
        }
    }
}
