using System;
using System.Collections.Generic;
using UnityEngine;

namespace EnergyBall
{
    /// <summary>负责能量球碎片的缓存、爆炸、吸引力、释放爆炸与 home 位姿恢复。</summary>
    [DisallowMultipleComponent]
    public class EnergyBallFragmentPhysics : MonoBehaviour
    {
        [Header("层级")]
        [SerializeField] Transform fragmentRoot;
        [SerializeField] string fragmentRootName = "SphereRoot";
        [SerializeField] Transform constraintCenter;

        [Header("爆炸")]
        [SerializeField] float defaultExplosionForce = 8f;
        [SerializeField] float defaultExplosionRadius = 1.5f;
        [SerializeField, Range(0f, 1f)] float randomDirectionStrength = 0.15f;
        [SerializeField] float upwardModifier = 0.05f;

        [Header("碎片 Rigidbody")]
        [SerializeField] float fragmentMass = 0.02f;
        [SerializeField] CollisionDetectionMode collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        [Header("飞散阻尼（替代距离约束）")]
        [SerializeField] float dispersedLinearDamping = 3.5f;
        [SerializeField] float dispersedAngularDamping = 4f;

        [Header("吸引子场")]
        [SerializeField] Transform attractor;
        [SerializeField] float attractorForce = 10f;
        [SerializeField] float attractorRadius = 2.5f;
        [SerializeField] float attractorDamping = 1.2f;

        [Header("目标释放爆炸")]
        [SerializeField] float releaseExplosionForce = 4f;
        [SerializeField] float releaseExplosionRadius = 0.6f;
        [SerializeField, Range(0f, 1f)] float releaseExplosionRandomStrength = 0.12f;
        [SerializeField] float releaseExplosionUpward = 0.08f;

        readonly List<Transform> _fragmentBuffer = new List<Transform>(384);

        Transform[] _fragments;
        Rigidbody[] _rigidbodies;
        Collider[] _colliders;
        Vector3[] _homeLocalPositions;
        Quaternion[] _homeLocalRotations;

        public float DefaultExplosionForce => defaultExplosionForce;
        public float DefaultExplosionRadius => defaultExplosionRadius;
        public Transform ConstraintCenter => constraintCenter;
        public Transform[] Fragments => _fragments;
        public Rigidbody[] Rigidbodies => _rigidbodies;
        public Collider[] Colliders => _colliders;
        public int FragmentCount => _fragments != null ? _fragments.Length : 0;

        public void Initialize()
        {
            ResolveReferences();
            EnsureCached();
        }

        public void ResolveReferences()
        {
            if (fragmentRoot == null)
                fragmentRoot = EnergyBallTransformUtility.FindChildByName(transform, fragmentRootName);

            if (fragmentRoot == null)
                fragmentRoot = transform;

            if (constraintCenter == null)
                constraintCenter = transform;
        }

        public void EnsureCached()
        {
            if (_fragments == null || _fragments.Length == 0)
                CacheFragments();
        }

        public void CacheFragments()
        {
            ResolveReferences();
            _fragmentBuffer.Clear();
            fragmentRoot.GetComponentsInChildren(true, _fragmentBuffer);

            int fragmentCount = 0;
            for (int i = 0; i < _fragmentBuffer.Count; i++)
            {
                Transform candidate = _fragmentBuffer[i];
                if (candidate != fragmentRoot && candidate.GetComponent<MeshFilter>() != null)
                    fragmentCount++;
            }

            _fragments = new Transform[fragmentCount];
            _rigidbodies = new Rigidbody[fragmentCount];
            _colliders = new Collider[fragmentCount];
            _homeLocalPositions = new Vector3[fragmentCount];
            _homeLocalRotations = new Quaternion[fragmentCount];

            int writeIndex = 0;
            for (int i = 0; i < _fragmentBuffer.Count; i++)
            {
                Transform candidate = _fragmentBuffer[i];
                if (candidate == fragmentRoot || candidate.GetComponent<MeshFilter>() == null)
                    continue;

                _fragments[writeIndex] = candidate;
                _rigidbodies[writeIndex] = candidate.GetComponent<Rigidbody>();
                _colliders[writeIndex] = candidate.GetComponent<Collider>();
                _homeLocalPositions[writeIndex] = candidate.localPosition;
                _homeLocalRotations[writeIndex] = candidate.localRotation;
                writeIndex++;
            }
        }

        public void Explode(Vector3 center, float force, float radius, Func<int, bool> skipFragment)
        {
            EnsureCached();
            PrepareForPhysics(dispersedLinearDamping, dispersedAngularDamping, skipFragment);

            float safeRadius = Mathf.Max(radius, 0.001f);
            float safeRadiusSqr = safeRadius * safeRadius;

            for (int i = 0; i < _fragments.Length; i++)
            {
                if (skipFragment != null && skipFragment(i))
                    continue;

                Rigidbody body = _rigidbodies[i];
                if (body == null)
                    continue;

                Vector3 offset = body.position - center;
                float distanceSqr = offset.sqrMagnitude;
                float normalizedDistance = Mathf.Clamp01(distanceSqr / safeRadiusSqr);
                float falloff = 1f - normalizedDistance;

                Vector3 direction = offset.sqrMagnitude > 0.0001f ? offset.normalized : UnityEngine.Random.onUnitSphere;
                Vector3 randomDirection = UnityEngine.Random.onUnitSphere * randomDirectionStrength;
                Vector3 finalDirection = (direction + randomDirection).normalized;

                body.AddForce(finalDirection * (force * falloff), ForceMode.Impulse);
                body.AddExplosionForce(force * falloff, center, safeRadius, upwardModifier, ForceMode.Impulse);
            }
        }

        public void PrepareForPhysics(float linearDamping, float angularDamping, Func<int, bool> skipFragment)
        {
            EnsureCached();

            for (int i = 0; i < _fragments.Length; i++)
            {
                if (skipFragment != null && skipFragment(i))
                    continue;

                Transform fragment = _fragments[i];
                Rigidbody body = _rigidbodies[i];

                if (body == null)
                {
                    body = fragment.gameObject.AddComponent<Rigidbody>();
                    _rigidbodies[i] = body;
                }

                body.mass = fragmentMass;
                body.linearDamping = linearDamping;
                body.angularDamping = angularDamping;
                body.collisionDetectionMode = collisionDetectionMode;
                body.isKinematic = false;
                body.useGravity = false;
                body.detectCollisions = true;
                body.WakeUp();

                Collider fragmentCollider = _colliders[i];
                if (fragmentCollider != null)
                    fragmentCollider.enabled = true;

                fragment.SetParent(null, true);
            }
        }

        public void SetFragmentsSleeping()
        {
            EnsureCached();

            for (int i = 0; i < _fragments.Length; i++)
                RestoreFragmentToHome(i);
        }

        public void RestoreFragmentToHome(int index)
        {
            if (_fragments == null || index < 0 || index >= _fragments.Length)
                return;

            Transform fragment = _fragments[index];
            if (fragment == null)
                return;

            fragment.SetParent(fragmentRoot, false);
            fragment.localPosition = _homeLocalPositions[index];
            fragment.localRotation = _homeLocalRotations[index];
            fragment.localScale = Vector3.one;

            Collider fragmentCollider = _colliders[index];
            if (fragmentCollider != null)
                fragmentCollider.enabled = false;

            Rigidbody body = _rigidbodies[index];
            if (body != null)
            {
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }

                body.isKinematic = true;
                body.detectCollisions = false;
                body.Sleep();
            }
        }

        public void ApplyAttractorForces()
        {
            if (_rigidbodies == null || attractor == null || attractorForce <= 0f || attractorRadius <= 0f)
                return;

            Vector3 attractorPosition = attractor.position;
            float radiusSqr = attractorRadius * attractorRadius;

            for (int i = 0; i < _rigidbodies.Length; i++)
            {
                Rigidbody body = _rigidbodies[i];
                if (body == null || body.isKinematic)
                    continue;

                Vector3 toAttractor = attractorPosition - body.position;
                float distanceSqr = toAttractor.sqrMagnitude;
                if (distanceSqr <= 0.0001f || distanceSqr > radiusSqr)
                    continue;

                float falloff = 1f - distanceSqr / radiusSqr;
                Vector3 acceleration = toAttractor.normalized * (attractorForce * falloff);
                acceleration -= body.linearVelocity * attractorDamping * falloff;
                body.AddForce(acceleration, ForceMode.Acceleration);
            }
        }

        public void SetAttractor(Transform newAttractor)
        {
            attractor = newAttractor;
        }

        public void ClearAttractor()
        {
            attractor = null;
        }

        public void PrepareFragmentForReleasePhysics(int fragmentIndex)
        {
            if (_rigidbodies == null || fragmentIndex < 0 || fragmentIndex >= _rigidbodies.Length)
                return;

            Rigidbody body = _rigidbodies[fragmentIndex];
            if (body != null)
            {
                body.isKinematic = false;
                body.detectCollisions = true;
                body.linearDamping = dispersedLinearDamping;
                body.angularDamping = dispersedAngularDamping;
                body.WakeUp();
            }

            Collider fragmentCollider = _colliders[fragmentIndex];
            if (fragmentCollider != null)
                fragmentCollider.enabled = true;
        }

        public Vector3 GetFragmentIndicesCentroid(IReadOnlyList<int> fragmentIndices, int count)
        {
            Vector3 sum = Vector3.zero;
            int validCount = 0;

            for (int i = 0; i < count; i++)
            {
                int fragmentIndex = fragmentIndices[i];
                if (_fragments == null || fragmentIndex < 0 || fragmentIndex >= _fragments.Length)
                    continue;

                Transform fragment = _fragments[fragmentIndex];
                if (fragment == null)
                    continue;

                sum += fragment.position;
                validCount++;
            }

            if (validCount == 0)
                return constraintCenter != null ? constraintCenter.position : transform.position;

            return sum / validCount;
        }

        public void ApplyReleaseExplosionAt(Vector3 center, IReadOnlyList<int> fragmentIndices, int count)
        {
            if (_rigidbodies == null || releaseExplosionForce <= 0f || count <= 0)
                return;

            float safeRadius = Mathf.Max(releaseExplosionRadius, 0.001f);
            float safeRadiusSqr = safeRadius * safeRadius;

            for (int n = 0; n < count; n++)
            {
                int fragmentIndex = fragmentIndices[n];
                if (fragmentIndex < 0 || fragmentIndex >= _rigidbodies.Length)
                    continue;

                Rigidbody body = _rigidbodies[fragmentIndex];
                if (body == null || body.isKinematic)
                    continue;

                Vector3 offset = body.position - center;
                float distanceSqr = offset.sqrMagnitude;
                float normalizedDistance = Mathf.Clamp01(distanceSqr / safeRadiusSqr);
                float falloff = 1f - normalizedDistance;

                Vector3 direction = offset.sqrMagnitude > 0.0001f ? offset.normalized : UnityEngine.Random.onUnitSphere;
                Vector3 randomDirection = UnityEngine.Random.onUnitSphere * releaseExplosionRandomStrength;
                Vector3 finalDirection = (direction + randomDirection).normalized;

                body.AddForce(finalDirection * (releaseExplosionForce * falloff), ForceMode.Impulse);
                body.AddExplosionForce(
                    releaseExplosionForce * falloff,
                    center,
                    safeRadius,
                    releaseExplosionUpward,
                    ForceMode.Impulse);
            }
        }
    }
}
