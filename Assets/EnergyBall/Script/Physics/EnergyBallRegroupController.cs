using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EnergyBall
{
    /// <summary>负责碎片分批聚拢，以及释放碎片后的 pending regroup 队列。</summary>
    [DisallowMultipleComponent]
    public class EnergyBallRegroupController : MonoBehaviour
    {
        [Header("恢复（分批瞬移）")]
        [Tooltip("每批之间的间隔（秒），恒定不变。")]
        [SerializeField] float regroupBatchInterval = 0.04f;
        [Tooltip("斐波那契数列 F(0)，即第一批片数。")]
        [SerializeField] int regroupFibFirst = 2;
        [Tooltip("斐波那契数列 F(1)，即第二批片数；之后每批为前两批片数之和。")]
        [SerializeField] int regroupFibSecond = 3;
        [Tooltip("单批最多恢复的碎片数，斐波那契增长到此为止（约 320 片时默认 24 较顺滑）。")]
        [SerializeField] int regroupMaxBatchSize = 24;

        readonly List<int> _regroupRestorable = new List<int>(384);
        readonly List<int> _pendingReleaseRegroup = new List<int>(384);

        Coroutine _regroupCoroutine;
        int _fragmentCount;
        Func<int, bool> _isCaptured;
        Action<int> _restoreFragment;
        Action _onStarted;
        Action _onComplete;

        public bool IsRunning => _regroupCoroutine != null;

        public void Configure(
            int fragmentCount,
            Func<int, bool> isCaptured,
            Action<int> restoreFragment,
            Action onStarted,
            Action onComplete)
        {
            _fragmentCount = Mathf.Max(0, fragmentCount);
            _isCaptured = isCaptured;
            _restoreFragment = restoreFragment;
            _onStarted = onStarted;
            _onComplete = onComplete;
        }

        public void StartRegroup(bool restoreAllNonCaptured)
        {
            StopRegroup();
            _regroupCoroutine = StartCoroutine(RegroupBatchedRoutine(restoreAllNonCaptured));
        }

        public void StopRegroup()
        {
            if (_regroupCoroutine == null)
                return;

            StopCoroutine(_regroupCoroutine);
            _regroupCoroutine = null;
            _pendingReleaseRegroup.Clear();
        }

        public void QueueFragment(int fragmentIndex)
        {
            if (!_pendingReleaseRegroup.Contains(fragmentIndex))
                _pendingReleaseRegroup.Add(fragmentIndex);

            EnsureRegroupCoroutineRunning();
        }

        void EnsureRegroupCoroutineRunning()
        {
            if (_regroupCoroutine != null)
                return;

            _regroupCoroutine = StartCoroutine(RegroupBatchedRoutine(restoreAllNonCaptured: false));
        }

        IEnumerator RegroupBatchedRoutine(bool restoreAllNonCaptured)
        {
            _onStarted?.Invoke();

            if (restoreAllNonCaptured)
            {
                _regroupRestorable.Clear();
                for (int i = 0; i < _fragmentCount; i++)
                {
                    if (_isCaptured == null || !_isCaptured(i))
                        _regroupRestorable.Add(i);
                }
            }

            int restored = 0;
            int batchIndex = 0;

            while (true)
            {
                MergePendingReleaseRegroup();

                int restorableCount = _regroupRestorable.Count;
                if (restored >= restorableCount)
                    break;

                int batchSize = GetFibonacciBatchSize(batchIndex);
                batchSize = Mathf.Min(batchSize, restorableCount - restored);

                for (int j = 0; j < batchSize; j++)
                    _restoreFragment?.Invoke(_regroupRestorable[restored + j]);

                restored += batchSize;
                batchIndex++;

                if (restored >= restorableCount)
                    break;

                if (regroupBatchInterval > 0f)
                    yield return new WaitForSeconds(regroupBatchInterval);
                else
                    yield return null;
            }

            _regroupCoroutine = null;
            _onComplete?.Invoke();
        }

        void MergePendingReleaseRegroup()
        {
            for (int i = 0; i < _pendingReleaseRegroup.Count; i++)
            {
                int fragmentIndex = _pendingReleaseRegroup[i];
                if (!_regroupRestorable.Contains(fragmentIndex))
                    _regroupRestorable.Add(fragmentIndex);
            }

            _pendingReleaseRegroup.Clear();
        }

        int GetFibonacciBatchSize(int batchIndex)
        {
            int maxBatch = Mathf.Max(1, regroupMaxBatchSize);
            int first = Mathf.Clamp(Mathf.Max(1, regroupFibFirst), 1, maxBatch);
            int second = Mathf.Clamp(Mathf.Max(1, regroupFibSecond), 1, maxBatch);

            if (batchIndex <= 0)
                return first;
            if (batchIndex == 1)
                return second;

            int prevPrev = first;
            int prev = second;
            for (int i = 2; i <= batchIndex; i++)
            {
                int current = Mathf.Min(prevPrev + prev, maxBatch);
                prevPrev = prev;
                prev = current;

                if (prev >= maxBatch)
                    return maxBatch;
            }

            return prev;
        }
    }
}
